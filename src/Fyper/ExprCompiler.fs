namespace Fyper

module ExprCompiler =

    open Fyper.Ast

    /// Mutable compilation state: collects parameters as it encounters literals.
    type ExprCompileState = {
        mutable ParamIndex: int
        mutable Parameters: Map<string, obj>
    }

    let newState () = { ParamIndex = 0; Parameters = Map.empty }

    let addParam (state: ExprCompileState) (value: obj) : string =
        let name = sprintf "p%d" state.ParamIndex
        state.ParamIndex <- state.ParamIndex + 1
        state.Parameters <- state.Parameters |> Map.add name value
        name

    /// Compile an F# quotation Expr into a Cypher AST Expr.
    let rec compile (state: ExprCompileState) (quotationExpr: Microsoft.FSharp.Quotations.Expr) : Ast.Expr =
        match quotationExpr with
        // Property access on a variable: p.Age → Property("p", "age")
        | Microsoft.FSharp.Quotations.Patterns.PropertyGet(
            Some(Microsoft.FSharp.Quotations.Patterns.Var v), propInfo, []) ->
            let cypherName = Schema.toCypherName propInfo.Name
            Property(v.Name, cypherName)

        // Nested property access: p.Address.City
        | Microsoft.FSharp.Quotations.Patterns.PropertyGet(Some inner, propInfo, []) ->
            let cypherName = Schema.toCypherName propInfo.Name
            match compile state inner with
            | Property(owner, prop) -> Property(owner, sprintf "%s.%s" prop cypherName)
            | Variable name -> Property(name, cypherName)
            | _ -> failwithf "Unsupported nested property access: %A" quotationExpr

        // Equality
        | Microsoft.FSharp.Quotations.Patterns.Call(None, mi, [lhs; rhs])
            when mi.Name = "op_Equality" ->
            BinOp(compile state lhs, Eq, compile state rhs)

        // Inequality
        | Microsoft.FSharp.Quotations.Patterns.Call(None, mi, [lhs; rhs])
            when mi.Name = "op_Inequality" ->
            BinOp(compile state lhs, Neq, compile state rhs)

        // Greater than
        | Microsoft.FSharp.Quotations.Patterns.Call(None, mi, [lhs; rhs])
            when mi.Name = "op_GreaterThan" ->
            BinOp(compile state lhs, Gt, compile state rhs)

        // Greater than or equal
        | Microsoft.FSharp.Quotations.Patterns.Call(None, mi, [lhs; rhs])
            when mi.Name = "op_GreaterThanOrEqual" ->
            BinOp(compile state lhs, Gte, compile state rhs)

        // Less than
        | Microsoft.FSharp.Quotations.Patterns.Call(None, mi, [lhs; rhs])
            when mi.Name = "op_LessThan" ->
            BinOp(compile state lhs, Lt, compile state rhs)

        // Less than or equal
        | Microsoft.FSharp.Quotations.Patterns.Call(None, mi, [lhs; rhs])
            when mi.Name = "op_LessThanOrEqual" ->
            BinOp(compile state lhs, Lte, compile state rhs)

        // Arithmetic
        | Microsoft.FSharp.Quotations.Patterns.Call(None, mi, [lhs; rhs])
            when mi.Name = "op_Addition" ->
            BinOp(compile state lhs, Add, compile state rhs)
        | Microsoft.FSharp.Quotations.Patterns.Call(None, mi, [lhs; rhs])
            when mi.Name = "op_Subtraction" ->
            BinOp(compile state lhs, Sub, compile state rhs)
        | Microsoft.FSharp.Quotations.Patterns.Call(None, mi, [lhs; rhs])
            when mi.Name = "op_Multiply" ->
            BinOp(compile state lhs, Mul, compile state rhs)
        | Microsoft.FSharp.Quotations.Patterns.Call(None, mi, [lhs; rhs])
            when mi.Name = "op_Division" ->
            BinOp(compile state lhs, Div, compile state rhs)
        | Microsoft.FSharp.Quotations.Patterns.Call(None, mi, [lhs; rhs])
            when mi.Name = "op_Modulus" ->
            BinOp(compile state lhs, Mod, compile state rhs)

        // Logical AND: F# quotations represent && as IfThenElse(cond, ifTrue, false)
        | Microsoft.FSharp.Quotations.Patterns.IfThenElse(
            cond, ifTrue, Microsoft.FSharp.Quotations.Patterns.Value(:? bool as b, _))
            when b = false ->
            BinOp(compile state cond, And, compile state ifTrue)

        // Logical OR: F# quotations represent || as IfThenElse(cond, true, ifFalse)
        | Microsoft.FSharp.Quotations.Patterns.IfThenElse(
            cond, Microsoft.FSharp.Quotations.Patterns.Value(:? bool as b, _), ifFalse)
            when b = true ->
            BinOp(compile state cond, Or, compile state ifFalse)

        // NOT
        | Microsoft.FSharp.Quotations.Patterns.Call(None, mi, [inner])
            when mi.Name = "Not" || mi.Name = "op_LogicalNot" ->
            UnaryOp(Not, compile state inner)

        // String.Contains
        | Microsoft.FSharp.Quotations.Patterns.Call(Some instance, mi, [arg])
            when mi.Name = "Contains" && mi.DeclaringType = typeof<string> ->
            BinOp(compile state instance, Contains, compile state arg)

        // String.StartsWith
        | Microsoft.FSharp.Quotations.Patterns.Call(Some instance, mi, [arg])
            when mi.Name = "StartsWith" && mi.DeclaringType = typeof<string> ->
            BinOp(compile state instance, StartsWith, compile state arg)

        // String.EndsWith
        | Microsoft.FSharp.Quotations.Patterns.Call(Some instance, mi, [arg])
            when mi.Name = "EndsWith" && mi.DeclaringType = typeof<string> ->
            BinOp(compile state instance, EndsWith, compile state arg)

        // ─── Cypher aggregate/scalar functions (defined in Operators module) ───
        | Microsoft.FSharp.Quotations.Patterns.Call(None, mi, [])
            when mi.Name = "count" && mi.DeclaringType.Name = "Operators" ->
            FuncCall("count", [Variable "*"])

        | Microsoft.FSharp.Quotations.Patterns.Call(None, mi, [arg])
            when (mi.Name = "countDistinct" || mi.Name = "collect" || mi.Name = "sum"
                  || mi.Name = "avg" || mi.Name = "size")
                 && mi.DeclaringType.Name = "Operators" ->
            FuncCall(mi.Name, [compile state arg])

        | Microsoft.FSharp.Quotations.Patterns.Call(None, mi, [arg])
            when mi.Name = "cypherMin" && mi.DeclaringType.Name = "Operators" ->
            FuncCall("min", [compile state arg])

        | Microsoft.FSharp.Quotations.Patterns.Call(None, mi, [arg])
            when mi.Name = "cypherMax" && mi.DeclaringType.Name = "Operators" ->
            FuncCall("max", [compile state arg])

        // CASE WHEN: caseWhen condition result elseResult
        | Microsoft.FSharp.Quotations.Patterns.Call(None, mi, [cond; result; elseResult])
            when mi.Name = "caseWhen" && mi.DeclaringType.Name = "Operators" ->
            CaseExpr(None, [(compile state cond, compile state result)], Some (compile state elseResult))

        // Variable reference
        | Microsoft.FSharp.Quotations.Patterns.Var v -> Variable v.Name

        // Non-null literal → parameterize
        | Microsoft.FSharp.Quotations.Patterns.Value(v, _) when v <> null ->
            let paramName = addParam state v
            Param paramName

        // Null literal
        | Microsoft.FSharp.Quotations.Patterns.Value(null, _) -> Ast.Null

        // Lambda body — unwrap and compile the body
        | Microsoft.FSharp.Quotations.Patterns.Lambda(_, body) -> compile state body

        // Let bindings — compile the body
        | Microsoft.FSharp.Quotations.Patterns.Let(_, _, body) -> compile state body

        | e -> failwithf "Unsupported expression in Cypher query: %A" e
