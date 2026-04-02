namespace Fyper

module QueryTranslator =

    open Fyper.Ast
    module QP = Microsoft.FSharp.Quotations.Patterns

    /// Internal build state accumulated during translation
    type TranslateState = {
        Clauses: Clause list
        Parameters: Map<string, obj>
        VarTypes: Map<string, System.Type>
        mutable ParamCounter: int
    }

    module TranslateState =
        let empty = {
            Clauses = []
            Parameters = Map.empty
            VarTypes = Map.empty
            ParamCounter = 0
        }

    let private isMethodNamed (name: string) (mi: System.Reflection.MethodInfo) =
        mi.Name = name && mi.DeclaringType.Name = "CypherBuilder"

    let rec walkExpr (state: TranslateState) (expr: Microsoft.FSharp.Quotations.Expr) : TranslateState =
        match expr with
        // For(builder, nodeSource, fun var -> body) → MATCH (var:Label)
        | QP.Call(Some _builder, mi, [_nodeSource; QP.Lambda(var, body)]) when isMethodNamed "For" mi ->
            let nodeType = var.Type
            let label = Schema.resolveLabel nodeType
            let alias = var.Name
            let matchClause = Match([NodePattern(alias, Some label, Map.empty)], false)
            let state' = {
                state with
                    Clauses = state.Clauses @ [matchClause]
                    VarTypes = state.VarTypes |> Map.add var.Name nodeType
            }
            walkExpr state' body

        // Where(builder, source, predicateLambda)
        | QP.Call(Some _builder, mi, [source; predicateLambda]) when isMethodNamed "Where" mi ->
            let state' = walkExpr state source
            let exprState = ExprCompiler.newState()
            exprState.ParamIndex <- state'.ParamCounter
            let cypherExpr = compilePredicate exprState predicateLambda
            { state' with
                Clauses = state'.Clauses @ [Where cypherExpr]
                Parameters = Map.fold (fun acc k v -> Map.add k v acc) state'.Parameters exprState.Parameters
                ParamCounter = exprState.ParamIndex }

        // Select(builder, source, projectionLambda) → RETURN
        | QP.Call(Some _builder, mi, [source; projectionLambda]) when isMethodNamed "Select" mi ->
            let state' = walkExpr state source
            let returnItems = compileProjection projectionLambda
            { state' with Clauses = state'.Clauses @ [Return(returnItems, false)] }

        // OrderBy(builder, source, selectorLambda)
        | QP.Call(Some _builder, mi, [source; selectorLambda]) when isMethodNamed "OrderBy" mi ->
            let state' = walkExpr state source
            let exprState = ExprCompiler.newState()
            exprState.ParamIndex <- state'.ParamCounter
            let orderExpr = compileSelector exprState selectorLambda
            { state' with Clauses = state'.Clauses @ [OrderBy [(orderExpr, Ascending)]] }

        // OrderByDescending(builder, source, selectorLambda)
        | QP.Call(Some _builder, mi, [source; selectorLambda]) when isMethodNamed "OrderByDescending" mi ->
            let state' = walkExpr state source
            let exprState = ExprCompiler.newState()
            exprState.ParamIndex <- state'.ParamCounter
            let orderExpr = compileSelector exprState selectorLambda
            { state' with Clauses = state'.Clauses @ [OrderBy [(orderExpr, Descending)]] }

        // Skip(builder, source, n)
        | QP.Call(Some _builder, mi, [source; QP.Value(n, _)]) when isMethodNamed "Skip" mi ->
            let state' = walkExpr state source
            let paramName = sprintf "skip_%d" state'.ParamCounter
            { state' with
                Clauses = state'.Clauses @ [Skip(Param paramName)]
                Parameters = state'.Parameters |> Map.add paramName n
                ParamCounter = state'.ParamCounter + 1 }

        // Limit(builder, source, n)
        | QP.Call(Some _builder, mi, [source; QP.Value(n, _)]) when isMethodNamed "Limit" mi ->
            let state' = walkExpr state source
            let paramName = sprintf "limit_%d" state'.ParamCounter
            { state' with
                Clauses = state'.Clauses @ [Limit(Param paramName)]
                Parameters = state'.Parameters |> Map.add paramName n
                ParamCounter = state'.ParamCounter + 1 }

        // MatchRel(builder, source, edgePatternExpr)
        | QP.Call(Some _builder, mi, [source; patternExpr]) when isMethodNamed "MatchRel" mi ->
            let state' = walkExpr state source
            let relPattern = compileEdgePattern patternExpr state'.VarTypes
            { state' with Clauses = state'.Clauses @ [Match([relPattern], false)] }

        // Return(builder, returnExpr)
        | QP.Call(Some _builder, mi, [returnExpr]) when isMethodNamed "Return" mi ->
            let returnItems = compileReturnExpr returnExpr
            { state with Clauses = state.Clauses @ [Return(returnItems, false)] }

        // Yield / Zero (pass-through)
        | QP.Call(Some _builder, mi, _) when isMethodNamed "Yield" mi -> state
        | QP.Call(Some _builder, mi, _) when isMethodNamed "Zero" mi -> state

        // Let binding
        | QP.Let(_, _, body) -> walkExpr state body

        // Sequential
        | QP.Sequential(first, second) ->
            let state' = walkExpr state first
            walkExpr state' second

        // Fallback
        | _ -> state

    and compilePredicate (exprState: ExprCompiler.ExprCompileState) (lambda: Microsoft.FSharp.Quotations.Expr) : Ast.Expr =
        match lambda with
        | QP.Lambda(_, body) -> compilePredicate exprState body
        | _ -> ExprCompiler.compile exprState lambda

    and compileProjection (lambda: Microsoft.FSharp.Quotations.Expr) : ReturnItem list =
        match lambda with
        | QP.Lambda(_, body) -> compileProjection body
        | _ -> exprToReturnItems lambda

    and exprToReturnItems (expr: Microsoft.FSharp.Quotations.Expr) : ReturnItem list =
        match expr with
        // Single variable: select p → RETURN p
        | QP.Var v ->
            [{ Expr = Variable v.Name; Alias = None }]

        // Tuple: select (p, m) → RETURN p, m
        | QP.NewTuple items ->
            items |> List.collect exprToReturnItems

        // Property: select p.Name → RETURN p.name AS name
        | QP.PropertyGet(Some(QP.Var v), prop, []) ->
            let cypherName = Schema.toCypherName prop.Name
            [{ Expr = Property(v.Name, cypherName); Alias = Some cypherName }]

        // TupleGet — access item from CE variable-space tuple
        | QP.TupleGet(tupleExpr, _idx) ->
            exprToReturnItems tupleExpr

        // Call to NewTuple via static method (some F# versions)
        | QP.Call(None, mi, args) when mi.Name.StartsWith("MakeTuple") || mi.Name = "NewTuple" ->
            args |> List.collect exprToReturnItems

        // Let binding — unwrap
        | QP.Let(v, binding, body) ->
            // The binding might be a TupleGet extracting from variable space
            exprToReturnItems body

        // Fallback: try to interpret as a Cypher expression
        | _ ->
            try
                let exprState = ExprCompiler.newState()
                let compiled = ExprCompiler.compile exprState expr
                [{ Expr = compiled; Alias = None }]
            with _ ->
                // Last resort: dump the expression type for debugging
                failwithf "Cannot compile return expression: %A" expr

    and compileSelector (exprState: ExprCompiler.ExprCompileState) (lambda: Microsoft.FSharp.Quotations.Expr) : Ast.Expr =
        match lambda with
        | QP.Lambda(_, body) -> compileSelector exprState body
        | _ -> ExprCompiler.compile exprState lambda

    and compileReturnExpr (expr: Microsoft.FSharp.Quotations.Expr) : ReturnItem list =
        exprToReturnItems expr

    and compileEdgePattern (expr: Microsoft.FSharp.Quotations.Expr) (varTypes: Map<string, System.Type>) : Pattern =
        let rec findVars (e: Microsoft.FSharp.Quotations.Expr) : (string * System.Type) list =
            match e with
            | QP.Var v -> [(v.Name, v.Type)]
            | QP.Call(_, _, args) -> args |> List.collect findVars
            | _ -> []

        let vars = findVars expr
        match vars with
        | [(fromName, fromType); (toName, toType)] ->
            let fromLabel = Schema.resolveLabel fromType
            let toLabel = Schema.resolveLabel toType
            RelPattern(
                NodePattern(fromName, Some fromLabel, Map.empty),
                None, None, Map.empty,
                Outgoing, None,
                NodePattern(toName, Some toLabel, Map.empty))
        | _ ->
            failwithf "Could not parse edge pattern expression: %A" expr

    let translate<'T> (quotedCe: Microsoft.FSharp.Quotations.Expr<CypherQuery<'T>>) : CypherQuery<'T> =
        let state = TranslateState.empty
        let finalState = walkExpr state (quotedCe :> Microsoft.FSharp.Quotations.Expr)
        { Clauses = finalState.Clauses; Parameters = finalState.Parameters }
