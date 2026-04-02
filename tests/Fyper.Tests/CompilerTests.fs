module Fyper.Tests.CompilerTests

open Expecto
open Fyper.Ast
open Fyper.CypherCompiler

[<Tests>]
let compilerTests = testList "CypherCompiler" [

    testList "compileExpr" [
        test "compiles variable" {
            Expect.equal (compileExpr (Variable "p")) "p" ""
        }
        test "compiles property access" {
            Expect.equal (compileExpr (Property("p", "age"))) "p.age" ""
        }
        test "compiles parameter" {
            Expect.equal (compileExpr (Param "p0")) "$p0" ""
        }
        test "compiles null" {
            Expect.equal (compileExpr Null) "null" ""
        }
        test "compiles string literal" {
            Expect.equal (compileExpr (Literal (box "hello"))) "'hello'" ""
        }
        test "compiles int literal" {
            Expect.equal (compileExpr (Literal (box 42))) "42" ""
        }
        test "compiles bool literal" {
            Expect.equal (compileExpr (Literal (box true))) "true" ""
        }
        test "compiles binary op" {
            let expr = BinOp(Property("p", "age"), Gt, Param "p0")
            Expect.equal (compileExpr expr) "(p.age > $p0)" ""
        }
        test "compiles nested binary ops" {
            let expr = BinOp(
                BinOp(Property("p", "age"), Gt, Param "p0"),
                And,
                BinOp(Property("p", "name"), Eq, Param "p1"))
            Expect.equal (compileExpr expr) "((p.age > $p0) AND (p.name = $p1))" ""
        }
        test "compiles NOT" {
            let expr = UnaryOp(Not, BinOp(Property("p", "age"), Gt, Param "p0"))
            Expect.equal (compileExpr expr) "NOT ((p.age > $p0))" ""
        }
        test "compiles IS NULL" {
            let expr = UnaryOp(IsNull, Property("p", "name"))
            Expect.equal (compileExpr expr) "p.name IS NULL" ""
        }
        test "compiles IS NOT NULL" {
            let expr = UnaryOp(IsNotNull, Property("p", "name"))
            Expect.equal (compileExpr expr) "p.name IS NOT NULL" ""
        }
        test "compiles function call" {
            let expr = FuncCall("count", [Variable "p"])
            Expect.equal (compileExpr expr) "count(p)" ""
        }
        test "compiles list expression" {
            let expr = ListExpr [Literal (box 1); Literal (box 2); Literal (box 3)]
            Expect.equal (compileExpr expr) "[1, 2, 3]" ""
        }
        test "compiles map expression" {
            let expr = MapExpr [("name", Param "p0"); ("age", Param "p1")]
            Expect.equal (compileExpr expr) "{name: $p0, age: $p1}" ""
        }
        test "compiles CASE expression" {
            let expr = CaseExpr(
                Some (Property("p", "age")),
                [(Literal (box 1), Literal (box "one")); (Literal (box 2), Literal (box "two"))],
                Some (Literal (box "other")))
            Expect.equal (compileExpr expr) "CASE p.age WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE 'other' END" ""
        }
        test "compiles all binary operators" {
            let ops = [
                Eq, "="; Neq, "<>"; Gt, ">"; Gte, ">="; Lt, "<"; Lte, "<="
                And, "AND"; Or, "OR"; Xor, "XOR"
                Contains, "CONTAINS"; StartsWith, "STARTS WITH"; EndsWith, "ENDS WITH"
                In, "IN"
                Add, "+"; Sub, "-"; Mul, "*"; Div, "/"; Mod, "%"
                RegexMatch, "=~"
            ]
            for (op, expected) in ops do
                let result = compileBinOp op
                Expect.equal result expected (sprintf "BinOp %A" op)
        }
    ]

    testList "compilePattern" [
        test "compiles simple node" {
            let pattern = NodePattern("p", Some "Person", Map.empty)
            Expect.equal (compilePattern pattern) "(p:Person)" ""
        }
        test "compiles node without label" {
            let pattern = NodePattern("n", None, Map.empty)
            Expect.equal (compilePattern pattern) "(n)" ""
        }
        test "compiles node with properties" {
            let pattern = NodePattern("p", Some "Person", Map.ofList [
                "name", Param "p0"
                "age", Param "p1"
            ])
            let result = compilePattern pattern
            Expect.stringContains result "(p:Person" ""
            Expect.stringContains result "name: $p0" ""
            Expect.stringContains result "age: $p1" ""
        }
        test "compiles outgoing relationship" {
            let pattern =
                RelPattern(
                    NodePattern("p", Some "Person", Map.empty),
                    Some "r", Some "ACTED_IN", Map.empty,
                    Outgoing, None,
                    NodePattern("m", Some "Movie", Map.empty))
            Expect.equal (compilePattern pattern) "(p:Person)-[r:ACTED_IN]->(m:Movie)" ""
        }
        test "compiles incoming relationship" {
            let pattern =
                RelPattern(
                    NodePattern("m", Some "Movie", Map.empty),
                    None, Some "DIRECTED", Map.empty,
                    Incoming, None,
                    NodePattern("d", Some "Person", Map.empty))
            Expect.equal (compilePattern pattern) "(m:Movie)<-[:DIRECTED]-(d:Person)" ""
        }
        test "compiles undirected relationship" {
            let pattern =
                RelPattern(
                    NodePattern("a", Some "Person", Map.empty),
                    None, Some "KNOWS", Map.empty,
                    Undirected, None,
                    NodePattern("b", Some "Person", Map.empty))
            Expect.equal (compilePattern pattern) "(a:Person)-[:KNOWS]-(b:Person)" ""
        }
        test "compiles variable-length path (exact)" {
            let pattern =
                RelPattern(
                    NodePattern("a", Some "Person", Map.empty),
                    None, Some "KNOWS", Map.empty,
                    Outgoing, Some (Exactly 3),
                    NodePattern("b", Some "Person", Map.empty))
            Expect.equal (compilePattern pattern) "(a:Person)-[:KNOWS*3]->(b:Person)" ""
        }
        test "compiles variable-length path (range)" {
            let pattern =
                RelPattern(
                    NodePattern("a", None, Map.empty),
                    None, Some "KNOWS", Map.empty,
                    Outgoing, Some (Between(1, 5)),
                    NodePattern("b", None, Map.empty))
            Expect.equal (compilePattern pattern) "(a)-[:KNOWS*1..5]->(b)" ""
        }
        test "compiles variable-length path (any)" {
            let pattern =
                RelPattern(
                    NodePattern("a", None, Map.empty),
                    None, None, Map.empty,
                    Outgoing, Some AnyLength,
                    NodePattern("b", None, Map.empty))
            Expect.equal (compilePattern pattern) "(a)-[*]->(b)" ""
        }
        test "compiles named path" {
            let inner = RelPattern(
                NodePattern("a", Some "Person", Map.empty),
                None, Some "KNOWS", Map.empty,
                Outgoing, None,
                NodePattern("b", Some "Person", Map.empty))
            let pattern = NamedPath("path", inner)
            Expect.equal (compilePattern pattern) "path = (a:Person)-[:KNOWS]->(b:Person)" ""
        }
    ]

    testList "compileClause" [
        test "compiles MATCH" {
            let clause = Match([NodePattern("p", Some "Person", Map.empty)], false)
            Expect.equal (compileClause clause) "MATCH (p:Person)" ""
        }
        test "compiles OPTIONAL MATCH" {
            let clause = Match([NodePattern("m", Some "Movie", Map.empty)], true)
            Expect.equal (compileClause clause) "OPTIONAL MATCH (m:Movie)" ""
        }
        test "compiles MATCH with multiple patterns" {
            let clause = Match([
                NodePattern("p", Some "Person", Map.empty)
                NodePattern("m", Some "Movie", Map.empty)
            ], false)
            Expect.equal (compileClause clause) "MATCH (p:Person), (m:Movie)" ""
        }
        test "compiles WHERE" {
            let clause = Where(BinOp(Property("p", "age"), Gt, Param "p0"))
            Expect.equal (compileClause clause) "WHERE (p.age > $p0)" ""
        }
        test "compiles RETURN" {
            let clause = Return([{ Expr = Variable "p"; Alias = None }], false)
            Expect.equal (compileClause clause) "RETURN p" ""
        }
        test "compiles RETURN DISTINCT" {
            let clause = Return([{ Expr = Variable "p"; Alias = None }], true)
            Expect.equal (compileClause clause) "RETURN DISTINCT p" ""
        }
        test "compiles RETURN with alias" {
            let clause = Return([{ Expr = Property("p", "name"); Alias = Some "personName" }], false)
            Expect.equal (compileClause clause) "RETURN p.name AS personName" ""
        }
        test "compiles CREATE" {
            let clause = Create [NodePattern("p", Some "Person", Map.ofList ["name", Param "p0"])]
            let result = compileClause clause
            Expect.stringContains result "CREATE (p:Person" ""
            Expect.stringContains result "name: $p0" ""
        }
        test "compiles MERGE with ON MATCH / ON CREATE" {
            let clause = Merge(
                NodePattern("p", Some "Person", Map.ofList ["name", Param "n"]),
                [SetProperty("p", "updated", Param "now")],
                [SetProperty("p", "created", Param "now")])
            let result = compileClause clause
            Expect.stringContains result "MERGE (p:Person" ""
            Expect.stringContains result "ON MATCH SET p.updated = $now" ""
            Expect.stringContains result "ON CREATE SET p.created = $now" ""
        }
        test "compiles DELETE" {
            let clause = Delete(["p"], false)
            Expect.equal (compileClause clause) "DELETE p" ""
        }
        test "compiles DETACH DELETE" {
            let clause = Delete(["p"; "m"], true)
            Expect.equal (compileClause clause) "DETACH DELETE p, m" ""
        }
        test "compiles SET" {
            let clause = Set [SetProperty("p", "age", Param "newAge")]
            Expect.equal (compileClause clause) "SET p.age = $newAge" ""
        }
        test "compiles SET with merge properties" {
            let clause = Set [MergeProperties("p", MapExpr [("age", Param "a")])]
            Expect.equal (compileClause clause) "SET p += {age: $a}" ""
        }
        test "compiles SET with add label" {
            let clause = Set [AddLabel("p", "Admin")]
            Expect.equal (compileClause clause) "SET p:Admin" ""
        }
        test "compiles REMOVE property" {
            let clause = Remove [RemoveProperty("p", "age")]
            Expect.equal (compileClause clause) "REMOVE p.age" ""
        }
        test "compiles REMOVE label" {
            let clause = Remove [RemoveLabel("p", "Admin")]
            Expect.equal (compileClause clause) "REMOVE p:Admin" ""
        }
        test "compiles ORDER BY ascending" {
            let clause = OrderBy [(Property("p", "name"), Ascending)]
            Expect.equal (compileClause clause) "ORDER BY p.name" ""
        }
        test "compiles ORDER BY descending" {
            let clause = OrderBy [(Property("p", "age"), Descending)]
            Expect.equal (compileClause clause) "ORDER BY p.age DESC" ""
        }
        test "compiles ORDER BY multiple" {
            let clause = OrderBy [
                (Property("p", "age"), Descending)
                (Property("p", "name"), Ascending)
            ]
            Expect.equal (compileClause clause) "ORDER BY p.age DESC, p.name" ""
        }
        test "compiles SKIP" {
            let clause = Skip(Param "skip_0")
            Expect.equal (compileClause clause) "SKIP $skip_0" ""
        }
        test "compiles LIMIT" {
            let clause = Limit(Param "limit_0")
            Expect.equal (compileClause clause) "LIMIT $limit_0" ""
        }
        test "compiles UNWIND" {
            let clause = Unwind(Param "names", "name")
            Expect.equal (compileClause clause) "UNWIND $names AS name" ""
        }
        test "compiles CALL" {
            let clause = Call("db.labels", [], ["label"])
            Expect.equal (compileClause clause) "CALL db.labels() YIELD label" ""
        }
        test "compiles UNION" {
            Expect.equal (compileClause (Union false)) "UNION" ""
            Expect.equal (compileClause (Union true)) "UNION ALL" ""
        }
        test "compiles WITH" {
            let clause = With([{ Expr = Variable "p"; Alias = None }], false)
            Expect.equal (compileClause clause) "WITH p" ""
        }
        test "compiles RawCypher" {
            Expect.equal (compileClause (RawCypher "CALL db.indexes()")) "CALL db.indexes()" ""
        }
    ]

    testList "compile (full query)" [
        test "compiles multi-clause query" {
            let query : CypherQuery<obj> = {
                Clauses = [
                    Match([NodePattern("p", Some "Person", Map.empty)], false)
                    Where(BinOp(Property("p", "age"), Gt, Param "p0"))
                    Return([{ Expr = Variable "p"; Alias = None }], false)
                ]
                Parameters = Map.ofList ["p0", box 30]
            }
            let result = compile query
            Expect.equal result.Cypher "MATCH (p:Person)\nWHERE (p.age > $p0)\nRETURN p" ""
            Expect.equal result.Parameters (Map.ofList ["p0", box 30]) ""
        }

        test "compiles query with ORDER BY, SKIP, LIMIT" {
            let query : CypherQuery<obj> = {
                Clauses = [
                    Match([NodePattern("p", Some "Person", Map.empty)], false)
                    Return([{ Expr = Variable "p"; Alias = None }], false)
                    OrderBy [(Property("p", "age"), Descending)]
                    Skip(Param "skip_10")
                    Limit(Param "limit_5")
                ]
                Parameters = Map.ofList ["skip_10", box 10; "limit_5", box 5]
            }
            let result = compile query
            let expected = "MATCH (p:Person)\nRETURN p\nORDER BY p.age DESC\nSKIP $skip_10\nLIMIT $limit_5"
            Expect.equal result.Cypher expected ""
        }
    ]

    testList "Query module (raw AST API)" [
        test "builds query with pipeline" {
            let query : CypherQuery<obj> =
                Query.empty
                |> Query.matchNodes [NodePattern("p", Some "Person", Map.empty)]
                |> Query.where (BinOp(Property("p", "age"), Gt, Param "minAge"))
                |> Query.return' [{ Expr = Variable "p"; Alias = None }]
                |> Query.addParam "minAge" (box 30)
            let result = compile query
            Expect.equal result.Cypher "MATCH (p:Person)\nWHERE (p.age > $minAge)\nRETURN p" ""
            Expect.isTrue (result.Parameters |> Map.containsKey "minAge") ""
        }
    ]
]
