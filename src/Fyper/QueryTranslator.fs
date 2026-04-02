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

    /// Unwrap Lambda/Let to get to the inner expression body
    let rec private unwrapLambda (expr: Microsoft.FSharp.Quotations.Expr) : Microsoft.FSharp.Quotations.Expr =
        match expr with
        | QP.Lambda(_, body) -> unwrapLambda body
        | QP.Let(_, _, body) -> unwrapLambda body
        | _ -> expr

    let private isMethodNamed (name: string) (mi: System.Reflection.MethodInfo) =
        mi.Name = name && mi.DeclaringType.Name = "CypherBuilder"

    /// Check if a node source expression is optionalNode<T> (for OPTIONAL MATCH)
    let private isOptionalNodeSource (expr: Microsoft.FSharp.Quotations.Expr) : bool =
        match expr with
        | QP.PropertyGet(None, pi, []) when pi.Name = "optionalNode" -> true
        | QP.Call(None, mi, _) when mi.Name = "optionalNode" -> true
        | _ -> false

    /// Resolve the user-visible alias from a For lambda.
    /// CE desugars `for p in source do body` to `For(source, fun _argN -> let p = _argN in body)`
    /// Returns (userAlias, body) — if no Let binding, falls back to the lambda var name.
    let private resolveForAlias (lambdaVar: Microsoft.FSharp.Quotations.Var) (body: Microsoft.FSharp.Quotations.Expr) : string * Microsoft.FSharp.Quotations.Expr =
        match body with
        | QP.Let(userVar, QP.Var bindVar, innerBody) when bindVar.Name = lambdaVar.Name ->
            (userVar.Name, innerBody)
        | _ ->
            (lambdaVar.Name, body)

    let rec walkExpr (state: TranslateState) (expr: Microsoft.FSharp.Quotations.Expr) : TranslateState =
        match expr with
        // For(builder, nodeSource, fun _arg -> let p = _arg in body) → MATCH (p:Label)
        // F# CE desugars `for p in source do` to `For(source, fun _argN -> let p = _argN in body)`
        // We need the user's name (p), not the internal name (_argN).
        | QP.Call(Some _builder, mi, [nodeSource; QP.Lambda(var, body)]) when isMethodNamed "For" mi ->
            let nodeType = var.Type
            let label = Schema.resolveLabel nodeType
            let alias, actualBody = resolveForAlias var body
            // Detect optionalNode<T> vs node<T> — check if the source calls "optionalNode"
            let isOptional = isOptionalNodeSource nodeSource
            let matchClause = Match([NodePattern(alias, Some label, Map.empty)], isOptional)
            let state' = {
                state with
                    Clauses = state.Clauses @ [matchClause]
                    VarTypes = state.VarTypes |> Map.add alias nodeType
            }
            walkExpr state' actualBody

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

        // MatchRel(builder, source, projectionLambda) — with ProjectionParameter
        | QP.Call(Some _builder, mi, [source; patternLambda]) when isMethodNamed "MatchRel" mi ->
            let state' = walkExpr state source
            let relPattern = compileEdgePattern patternLambda state'.VarTypes
            { state' with Clauses = state'.Clauses @ [Match([relPattern], false)] }

        // ─── Mutation operations ───

        // Delete(builder, source, selectorLambda) → DELETE alias
        | QP.Call(Some _builder, mi, [source; selectorLambda]) when isMethodNamed "Delete" mi ->
            let state' = walkExpr state source
            let aliases = extractAliasesFromProjection selectorLambda
            { state' with Clauses = state'.Clauses @ [Delete(aliases, false)] }

        // DetachDelete(builder, source, selectorLambda) → DETACH DELETE alias
        | QP.Call(Some _builder, mi, [source; selectorLambda]) when isMethodNamed "DetachDelete" mi ->
            let state' = walkExpr state source
            let aliases = extractAliasesFromProjection selectorLambda
            { state' with Clauses = state'.Clauses @ [Delete(aliases, true)] }

        // Create(builder, source, patternExpr) → CREATE pattern
        | QP.Call(Some _builder, mi, [source; patternExpr]) when isMethodNamed "Create" mi ->
            let state' = walkExpr state source
            let exprState = ExprCompiler.newState()
            exprState.ParamIndex <- state'.ParamCounter
            let pattern = compileCreatePattern exprState patternExpr state'.VarTypes
            { state' with
                Clauses = state'.Clauses @ [Create [pattern]]
                Parameters = Map.fold (fun acc k v -> Map.add k v acc) state'.Parameters exprState.Parameters
                ParamCounter = exprState.ParamIndex }

        // Set(builder, source, updaterLambda) → SET property = value
        | QP.Call(Some _builder, mi, [source; updaterLambda]) when isMethodNamed "Set" mi ->
            let state' = walkExpr state source
            let exprState = ExprCompiler.newState()
            exprState.ParamIndex <- state'.ParamCounter
            let setItems = compileSetExpression exprState updaterLambda state'.VarTypes
            { state' with
                Clauses = state'.Clauses @ [Set setItems]
                Parameters = Map.fold (fun acc k v -> Map.add k v acc) state'.Parameters exprState.Parameters
                ParamCounter = exprState.ParamIndex }

        // Merge(builder, source, patternExpr) → MERGE pattern
        | QP.Call(Some _builder, mi, [source; patternExpr]) when isMethodNamed "Merge" mi ->
            let state' = walkExpr state source
            let exprState = ExprCompiler.newState()
            exprState.ParamIndex <- state'.ParamCounter
            let pattern = compileCreatePattern exprState patternExpr state'.VarTypes
            { state' with
                Clauses = state'.Clauses @ [Merge(pattern, [], [])]
                Parameters = Map.fold (fun acc k v -> Map.add k v acc) state'.Parameters exprState.Parameters
                ParamCounter = exprState.ParamIndex }

        // OnMatch(builder, source, updaterLambda) → modifies last MERGE with ON MATCH SET
        | QP.Call(Some _builder, mi, [source; updaterLambda]) when isMethodNamed "OnMatch" mi ->
            let state' = walkExpr state source
            let exprState = ExprCompiler.newState()
            exprState.ParamIndex <- state'.ParamCounter
            let setItems = compileSetExpression exprState updaterLambda state'.VarTypes
            let clauses' = updateLastMerge state'.Clauses (fun (p, _om, oc) -> Merge(p, setItems, oc))
            { state' with
                Clauses = clauses'
                Parameters = Map.fold (fun acc k v -> Map.add k v acc) state'.Parameters exprState.Parameters
                ParamCounter = exprState.ParamIndex }

        // OnCreate(builder, source, updaterLambda) → modifies last MERGE with ON CREATE SET
        | QP.Call(Some _builder, mi, [source; updaterLambda]) when isMethodNamed "OnCreate" mi ->
            let state' = walkExpr state source
            let exprState = ExprCompiler.newState()
            exprState.ParamIndex <- state'.ParamCounter
            let setItems = compileSetExpression exprState updaterLambda state'.VarTypes
            let clauses' = updateLastMerge state'.Clauses (fun (p, om, _oc) -> Merge(p, om, setItems))
            { state' with
                Clauses = clauses'
                Parameters = Map.fold (fun acc k v -> Map.add k v acc) state'.Parameters exprState.Parameters
                ParamCounter = exprState.ParamIndex }

        // ─── Advanced operations ───

        // SelectDistinct(builder, source, projectionLambda) → RETURN DISTINCT
        | QP.Call(Some _builder, mi, [source; projectionLambda]) when isMethodNamed "SelectDistinct" mi ->
            let state' = walkExpr state source
            let returnItems = compileProjection projectionLambda
            { state' with Clauses = state'.Clauses @ [Return(returnItems, true)] }

        // Unwind(builder, source, expr, alias)
        | QP.Call(Some _builder, mi, [source; listExpr; QP.Value(alias, _)]) when isMethodNamed "Unwind" mi ->
            let state' = walkExpr state source
            let exprState = ExprCompiler.newState()
            exprState.ParamIndex <- state'.ParamCounter
            let compiled = ExprCompiler.compile exprState listExpr
            { state' with
                Clauses = state'.Clauses @ [Unwind(compiled, string alias)]
                Parameters = Map.fold (fun acc k v -> Map.add k v acc) state'.Parameters exprState.Parameters
                ParamCounter = exprState.ParamIndex }

        // WithClause(builder, source, projectionLambda) → WITH
        | QP.Call(Some _builder, mi, [source; projectionLambda]) when isMethodNamed "WithClause" mi ->
            let state' = walkExpr state source
            let returnItems = compileProjection projectionLambda
            { state' with Clauses = state'.Clauses @ [With(returnItems, false)] }

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

    /// Update the last MERGE clause in the clause list
    and updateLastMerge (clauses: Clause list) (update: Pattern * SetItem list * SetItem list -> Clause) : Clause list =
        let rec go (revClauses: Clause list) =
            match revClauses with
            | Merge(p, om, oc) :: rest -> (update (p, om, oc)) :: rest |> List.rev
            | c :: rest -> go rest |> List.rev |> fun r -> r @ [c] |> List.rev
                           // Actually, simpler approach:
            | [] -> clauses // No merge found, return unchanged
        // Find last Merge and update it
        let idx = clauses |> List.tryFindIndexBack (fun c -> match c with Merge _ -> true | _ -> false)
        match idx with
        | Some i ->
            clauses
            |> List.mapi (fun j c ->
                if j = i then
                    match c with
                    | Merge(p, om, oc) -> update (p, om, oc)
                    | _ -> c
                else c)
        | None -> clauses

    /// Extract variable alias(es) from a projection lambda for DELETE
    and extractAliasesFromProjection (lambda: Microsoft.FSharp.Quotations.Expr) : string list =
        match lambda with
        | QP.Lambda(_, body) -> extractAliasesFromProjection body
        | QP.Var v -> [v.Name]
        | QP.NewTuple items -> items |> List.collect extractAliasesFromProjection
        | QP.TupleGet(t, _) -> extractAliasesFromProjection t
        | QP.Let(_, _, body) -> extractAliasesFromProjection body
        | _ -> failwithf "Cannot extract aliases for DELETE from: %A" lambda

    /// Compile a CREATE pattern expression from quotation tree.
    /// Handles: NewRecord(Type, fields) → NodePattern with parameterized props
    and compileCreatePattern (exprState: ExprCompiler.ExprCompileState) (expr: Microsoft.FSharp.Quotations.Expr) (varTypes: Map<string, System.Type>) : Pattern =
        match expr with
        // NewRecord(Person, [name; age]) → NodePattern("", Some "Person", {name: $p0, age: $p1})
        | QP.NewRecord(recordType, fieldValues) ->
            let label = Schema.resolveLabel recordType
            let fields = Microsoft.FSharp.Reflection.FSharpType.GetRecordFields(recordType)
            let props =
                Array.zip fields (fieldValues |> Array.ofList)
                |> Array.map (fun (fi, v) ->
                    let cypherName = Schema.toCypherName fi.Name
                    let compiled = ExprCompiler.compile exprState v
                    cypherName, compiled)
                |> Map.ofArray
            let alias = string (System.Char.ToLowerInvariant label.[0])
            NodePattern(alias, Some label, props)

        // Edge pattern via operators: p -< edge<R> >- m
        // Quotation tree: Call(None, ">-", [Call(None, "-<", [Var p; edge<R>]); Var m])
        | QP.Call(None, mi, [QP.Call(None, mi2, [fromExpr; _edgeExpr]); toExpr])
            when mi.Name = "op_GreaterSubtract" || mi.Name = ">-" ->
            let fromPattern = compileCreatePattern exprState fromExpr varTypes
            let toPattern = compileCreatePattern exprState toExpr varTypes
            // Extract relationship type from edge<R> generic argument
            let relType =
                if mi2.IsGenericMethod then
                    let genArgs = mi2.GetGenericArguments()
                    if genArgs.Length > 0 then Some (Schema.resolveLabel genArgs.[0])
                    else None
                else
                    // Try to find EdgeType<R> in the edge expression
                    let rec findEdgeType (e: Microsoft.FSharp.Quotations.Expr) =
                        match e with
                        | QP.Call(None, m, _) when m.IsGenericMethod ->
                            let args = m.GetGenericArguments()
                            if args.Length > 0 then Some (Schema.resolveLabel args.[0]) else None
                        | QP.Value _ -> None
                        | _ -> None
                    findEdgeType _edgeExpr
            RelPattern(fromPattern, None, relType, Map.empty, Outgoing, None, toPattern)

        // Variable reference — already bound node, use as-is
        | QP.Var v -> NodePattern(v.Name, None, Map.empty)

        // Let binding — unwrap
        | QP.Let(_, _, body) -> compileCreatePattern exprState body varTypes

        | _ -> failwithf "Cannot compile CREATE pattern from: %A" expr

    /// Compile a SET expression from an updater lambda.
    /// In CE context with [<ProjectionParameter>], the lambda body accesses
    /// variables via Let/TupleGet chains, not direct Var references.
    /// We detect "unchanged" fields by checking if a field value is a simple
    /// property access on the same property name, and resolve the owner alias
    /// from the VarTypes map (the bound CE variables).
    and compileSetExpression (exprState: ExprCompiler.ExprCompileState) (lambda: Microsoft.FSharp.Quotations.Expr) (varTypes: Map<string, System.Type>) : SetItem list =
        // F# quotations for `{ p with Age = 51 }` produce:
        //   Let(Age, Value(51), NewRecord(Person, [p.Name, Var Age]))
        // We collect the Let bindings to substitute Var references back to their values.
        let rec findNewRecord (bindings: Map<string, Microsoft.FSharp.Quotations.Expr>) (expr: Microsoft.FSharp.Quotations.Expr) =
            match expr with
            | QP.Lambda(_, body) -> findNewRecord bindings body
            | QP.Let(v, value, body) -> findNewRecord (bindings |> Map.add v.Name value) body
            | QP.NewRecord(rt, fv) ->
                // Substitute Var references using collected bindings
                let resolved =
                    fv |> List.map (fun e ->
                        match e with
                        | QP.Var v when bindings.ContainsKey v.Name -> bindings.[v.Name]
                        | _ -> e)
                Some (rt, resolved)
            | _ -> None

        match findNewRecord Map.empty lambda with
        | Some (recordType, fieldValues) ->
            let fields = Microsoft.FSharp.Reflection.FSharpType.GetRecordFields(recordType)
            // Find the owner alias from varTypes — which CE variable has this record type?
            let owner =
                varTypes
                |> Map.tryPick (fun name ty -> if ty = recordType then Some name else None)
                |> Option.defaultValue (string (System.Char.ToLowerInvariant (Schema.resolveLabel recordType).[0]))

            Array.zip fields (fieldValues |> Array.ofList)
            |> Array.choose (fun (fi, value) ->
                // Check if this field is "unchanged" — it's a PropertyGet ending in the same property name
                let isUnchanged = isUnchangedField fi.Name value
                if isUnchanged then None
                else
                    let cypherName = Schema.toCypherName fi.Name
                    let compiled = ExprCompiler.compile exprState value
                    Some (SetProperty(owner, cypherName, compiled)))
            |> Array.toList

        | None -> failwithf "Cannot compile SET expression — expected record update, got: %A" lambda

    /// Check if a quotation expression is an unchanged field — a simple property access
    /// that just reads the same property back (p.Name in { p with Age = 51 }).
    /// Must be ONLY a property access with no other computation around it.
    and isUnchangedField (propName: string) (expr: Microsoft.FSharp.Quotations.Expr) : bool =
        match expr with
        // Direct: PropertyGet(Var v, Name)
        | QP.PropertyGet(Some(QP.Var _), pi, []) when pi.Name = propName -> true
        // CE variable space: Let(v, TupleGet/Var, PropertyGet(Var v, Name))
        | QP.Let(_, _, body) -> isUnchangedField propName body
        | _ -> false

    and compileEdgePattern (expr: Microsoft.FSharp.Quotations.Expr) (varTypes: Map<string, System.Type>) : Pattern =
        // Strategy: find all Var references (the from/to nodes) and
        // find any generic type argument that resolves to a relationship type.
        let rec findVars (e: Microsoft.FSharp.Quotations.Expr) : string list =
            match e with
            | QP.Var v -> [v.Name]
            | QP.Call(_, _, args) -> args |> List.collect findVars
            | QP.Let(_, _, body) -> findVars body
            | QP.Lambda(_, body) -> findVars body
            | _ -> []

        let rec findRelType (e: Microsoft.FSharp.Quotations.Expr) : string option =
            match e with
            | QP.Call(_, mi, args) ->
                // Check this method's generic args for a relationship type
                let fromMethod =
                    if mi.IsGenericMethod then
                        mi.GetGenericArguments()
                        |> Array.tryPick (fun t ->
                            // A relationship type is a record type that is NOT in varTypes (not a node)
                            if Microsoft.FSharp.Reflection.FSharpType.IsRecord t
                               && not (varTypes |> Map.exists (fun _ vt -> vt = t)) then
                                Some (Schema.toRelType (Schema.resolveLabel t))
                            else None)
                    else None
                match fromMethod with
                | Some _ -> fromMethod
                | None -> args |> List.tryPick findRelType
            | QP.Let(_, value, body) ->
                findRelType value |> Option.orElseWith (fun () -> findRelType body)
            | QP.Lambda(_, body) -> findRelType body
            | _ -> None

        let vars = findVars expr |> List.distinct
        let relType = findRelType expr

        match vars with
        | [fromName; toName] | [_; fromName; toName] ->
            let fromLabel = varTypes |> Map.tryFind fromName |> Option.map Schema.resolveLabel
            let toLabel = varTypes |> Map.tryFind toName |> Option.map Schema.resolveLabel
            RelPattern(
                NodePattern(fromName, fromLabel, Map.empty),
                None, relType, Map.empty,
                Outgoing, None,
                NodePattern(toName, toLabel, Map.empty))
        | _ ->
            failwithf "Could not parse edge pattern expression: found vars %A" vars

    let translate<'T> (quotedCe: Microsoft.FSharp.Quotations.Expr<CypherQuery<'T>>) : CypherQuery<'T> =
        let state = TranslateState.empty
        let finalState = walkExpr state (quotedCe :> Microsoft.FSharp.Quotations.Expr)
        { Clauses = finalState.Clauses; Parameters = finalState.Parameters }
