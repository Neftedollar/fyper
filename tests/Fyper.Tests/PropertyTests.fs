module Fyper.Tests.PropertyTests

open Expecto
open FsCheck
open Fyper.Ast
open Fyper.CypherCompiler
open Fyper.Schema
module EC = Fyper.ExprCompiler

// ─── Generators ───

let genParamName = Gen.elements ["p0"; "p1"; "p2"; "p3"; "x"; "y"; "age"; "name"]
let genAlias = Gen.elements ["p"; "m"; "r"; "n"; "a"; "b"]
let genLabel = Gen.elements ["Person"; "Movie"; "ActedIn"; "Knows"; "Directed"]
let genPropName = Gen.elements ["name"; "age"; "title"; "released"; "roles"]

let genSimpleExpr =
    Gen.oneof [
        genParamName |> Gen.map Param
        genAlias |> Gen.map Variable
        Gen.map2 (fun o n -> Property(o, n)) genAlias genPropName
        Gen.constant Null
    ]

let genBinOp =
    Gen.elements [Eq; Neq; Gt; Gte; Lt; Lte; And; Or; Add; Sub; Mul; Div; Contains; StartsWith; EndsWith; In]

let genUnaryOp = Gen.elements [Not; IsNull; IsNotNull]

let genExpr =
    Gen.oneof [
        genSimpleExpr
        Gen.map3 (fun l op r -> BinOp(l, op, r)) genSimpleExpr genBinOp genSimpleExpr
        Gen.map2 (fun op e -> UnaryOp(op, e)) genUnaryOp genSimpleExpr
        Gen.map2 (fun n args -> FuncCall(n, args))
            (Gen.elements ["count"; "collect"; "sum"; "avg"; "min"; "max"])
            (Gen.listOfLength 1 genSimpleExpr)
    ]

let genReturnItem =
    Gen.map2
        (fun e a -> { Expr = e; Alias = a })
        genSimpleExpr
        (Gen.oneof [Gen.constant None; genPropName |> Gen.map Some])

let genNodePattern =
    Gen.map3
        (fun a l props -> NodePattern(a, Some l, props))
        genAlias genLabel (Gen.constant Map.empty)

let genClause =
    Gen.oneof [
        Gen.map (fun p -> Match([p], false)) genNodePattern
        Gen.map (fun p -> Match([p], true)) genNodePattern
        genExpr |> Gen.map Where
        Gen.listOfLength 2 genReturnItem |> Gen.map (fun items -> Return(items, false))
        Gen.listOfLength 1 genReturnItem |> Gen.map (fun items -> Return(items, true))
        Gen.map (fun p -> Create [p]) genNodePattern
        Gen.map2 (fun a d -> Delete([a], d)) genAlias (Gen.elements [true; false])
        Gen.constant (OrderBy [(Property("p", "age"), Ascending)])
        Gen.constant (Skip (Param "skip"))
        Gen.constant (Limit (Param "limit"))
        Gen.constant (Unwind (Param "list", "item"))
        Gen.constant (Union false)
        Gen.constant (Union true)
    ]

// ─── Property tests ───

[<Tests>]
let parameterizationTests = testList "Property: Parameterization invariant" [
    testProperty "compiled Cypher never contains raw literal values — params are $-prefixed" <|
        fun () ->
            let query : CypherQuery<obj> = {
                Clauses = [
                    Match([NodePattern("p", Some "Person", Map.empty)], false)
                    Where(BinOp(Property("p", "age"), Gt, Param "p0"))
                    Return([{ Expr = Variable "p"; Alias = None }], false)
                ]
                Parameters = Map.ofList ["p0", box 42]
            }
            let result = compile query
            // The Cypher string should use $p0, not the literal 42
            Expect.stringContains result.Cypher "$p0" "parameter reference present"
            Expect.isFalse (result.Cypher.Contains("42")) "raw literal should not appear"

    testProperty "all Param references produce dollar-prefixed output" <|
        fun () ->
            let prop = Arb.generate<string> |> Gen.filter (fun s -> s <> null && s.Length > 0 && s.Length < 20 && System.Text.RegularExpressions.Regex.IsMatch(s, "^[a-zA-Z_][a-zA-Z0-9_]*$"))
            let paramName = prop |> Gen.sample 1 1 |> List.head
            let compiled = compileExpr (Param paramName)
            Expect.isTrue (compiled.StartsWith("$")) "param must start with $"
            Expect.equal compiled (sprintf "$%s" paramName) "param format"
]

[<Tests>]
let schemaNamingTests = testList "Property: Schema naming" [
    testProperty "toCypherName lowercases first character" <|
        fun () ->
            let names = ["Age"; "Name"; "FirstName"; "Released"; "Title"; "X"; "Ab"]
            for name in names do
                let result = toCypherName name
                if name.Length > 0 then
                    Expect.isTrue (System.Char.IsLower result.[0]) (sprintf "first char of '%s' should be lowercase, got '%s'" name result)

    testProperty "toCypherName preserves rest of string" <|
        fun () ->
            let names = ["Age"; "FirstName"; "MyProperty"; "Released"]
            for name in names do
                let result = toCypherName name
                if name.Length > 1 then
                    Expect.equal result.[1..] name.[1..] (sprintf "rest of '%s' preserved" name)

    testProperty "toCypherName on empty string returns empty" <|
        fun () ->
            Expect.equal (toCypherName "") "" "empty in, empty out"
            Expect.equal (toCypherName null) null "null in, null out"

    testProperty "already-lowercase names pass through unchanged" <|
        fun () ->
            let names = ["age"; "name"; "x"]
            for name in names do
                Expect.equal (toCypherName name) name (sprintf "'%s' unchanged" name)
]

[<Tests>]
let astCompilationTests = testList "Property: AST compilation exhaustiveness" [
    testProperty "every Expr variant compiles without exception" <|
        fun () ->
            let exprs = [
                Literal (box "hello")
                Param "p0"
                Variable "x"
                Null
                Property("p", "name")
                BinOp(Variable "a", Eq, Param "p0")
                BinOp(Property("p", "age"), Gt, Param "p0")
                UnaryOp(Not, Variable "x")
                UnaryOp(IsNull, Property("p", "name"))
                UnaryOp(IsNotNull, Variable "v")
                FuncCall("count", [Variable "p"])
                FuncCall("collect", [Property("p", "name")])
                ListExpr [Param "p0"; Param "p1"]
                MapExpr [("key", Param "p0")]
                CaseExpr(None, [(BinOp(Variable "x", Eq, Param "p0"), Param "p1")], Some (Param "p2"))
                CaseExpr(Some (Variable "x"), [(Param "p0", Param "p1")], None)
                ExistsSubquery [Match([NodePattern("n", Some "Node", Map.empty)], false)]
            ]
            for expr in exprs do
                let result = compileExpr expr
                Expect.isNotNull result (sprintf "expr %A compiled" expr)
                Expect.isNonEmpty result (sprintf "expr %A non-empty" expr)

    testProperty "every Clause variant compiles without exception" <|
        fun () ->
            let node = NodePattern("p", Some "Person", Map.empty)
            let item = { Expr = Variable "p"; Alias = None }
            let clauses = [
                Match([node], false)
                Match([node], true)
                Where(BinOp(Property("p", "age"), Gt, Param "p0"))
                Return([item], false)
                Return([item], true)
                With([item], false)
                With([item], true)
                Create [node]
                Merge(node, [], [])
                Merge(node, [SetProperty("p", "age", Param "p0")], [SetProperty("p", "age", Param "p1")])
                Delete(["p"], false)
                Delete(["p"], true)
                Set [SetProperty("p", "age", Param "p0")]
                Set [SetAllProperties("p", MapExpr [("age", Param "p0")])]
                Set [MergeProperties("p", MapExpr [("age", Param "p0")])]
                Set [AddLabel("p", "Admin")]
                Remove [RemoveProperty("p", "age")]
                Remove [RemoveLabel("p", "Admin")]
                OrderBy [(Property("p", "age"), Ascending); (Property("p", "name"), Descending)]
                Skip (Param "skip")
                Limit (Param "limit")
                Unwind(Param "list", "item")
                Call("db.labels", [], ["label"])
                Union false
                Union true
                RawCypher "MATCH (n) RETURN n"
            ]
            for clause in clauses do
                let result = compileClause clause
                Expect.isNotNull result (sprintf "clause compiled")
                Expect.isNonEmpty result (sprintf "clause non-empty")

    testProperty "generated clauses compile without exception" <|
        fun () ->
            let clauses = Gen.sample 5 20 genClause
            for clause in clauses do
                let result = compileClause clause
                Expect.isNotNull result "random clause compiled"
]

[<Tests>]
let exprCompilerPropertyTests = testList "Property: ExprCompiler" [
    testProperty "comparison operators produce valid AST with parameterized values" <|
        fun () ->
            let testOp (quot: Microsoft.FSharp.Quotations.Expr) expectedOp =
                let state = EC.newState ()
                let result = EC.compile state quot
                match result with
                | BinOp(Param _, op, Param _) ->
                    Expect.equal op expectedOp (sprintf "operator %A" expectedOp)
                | _ -> failwithf "Expected BinOp with Params, got %A" result
            testOp <@ 1 > 2 @> Gt
            testOp <@ 1 < 2 @> Lt
            testOp <@ 1 >= 2 @> Gte
            testOp <@ 1 <= 2 @> Lte
            testOp <@ 1 = 2 @> Eq

    testProperty "arithmetic operators produce valid AST" <|
        fun () ->
            let state = EC.newState ()
            let result = EC.compile state <@ 3 + 4 @>
            match result with
            | BinOp(Param _, Add, Param _) -> ()
            | _ -> failwithf "Expected BinOp Add, got %A" result

    testProperty "all literal values become parameterized" <|
        fun () ->
            let testLit (quot: Microsoft.FSharp.Quotations.Expr) =
                let state = EC.newState ()
                let result = EC.compile state quot
                match result with
                | Param pName ->
                    Expect.isTrue (state.Parameters.ContainsKey pName) "param registered"
                | _ -> failwithf "Expected Param, got %A" result
            testLit <@ 42 @>
            testLit <@ "hello" @>
            testLit <@ true @>
            testLit <@ 3.14 @>

    testProperty "logical AND compiles to BinOp And" <|
        fun () ->
            let state = EC.newState ()
            // Use variables to avoid constant-folding edge case
            let v1 = Microsoft.FSharp.Quotations.Var("x", typeof<bool>)
            let v2 = Microsoft.FSharp.Quotations.Var("y", typeof<bool>)
            let andExpr = Microsoft.FSharp.Quotations.Expr.IfThenElse(
                Microsoft.FSharp.Quotations.Expr.Var(v1),
                Microsoft.FSharp.Quotations.Expr.Var(v2),
                Microsoft.FSharp.Quotations.Expr.Value(false))
            let result = EC.compile state andExpr
            match result with
            | BinOp(Variable "x", And, Variable "y") -> ()
            | _ -> failwithf "Expected And, got %A" result

    testProperty "logical OR compiles to BinOp Or" <|
        fun () ->
            let state = EC.newState ()
            let v1 = Microsoft.FSharp.Quotations.Var("x", typeof<bool>)
            let v2 = Microsoft.FSharp.Quotations.Var("y", typeof<bool>)
            let orExpr = Microsoft.FSharp.Quotations.Expr.IfThenElse(
                Microsoft.FSharp.Quotations.Expr.Var(v1),
                Microsoft.FSharp.Quotations.Expr.Value(true),
                Microsoft.FSharp.Quotations.Expr.Var(v2))
            let result = EC.compile state orExpr
            match result with
            | BinOp(Variable "x", Or, Variable "y") -> ()
            | _ -> failwithf "Expected Or, got %A" result
]
