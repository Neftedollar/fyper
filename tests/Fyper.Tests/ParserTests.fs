module Fyper.Tests.ParserTests

open Expecto
open Fyper.Ast
open Fyper.Parser
open Fyper.Parser.CypherParser

[<Tests>]
let lexerTests = testList "Parser: Lexer" [
    test "tokenizes simple query" {
        let tokens = Lexer.tokenize "MATCH (n:Person) RETURN n"
        Expect.contains tokens MATCH "MATCH"
        Expect.contains tokens LPAREN "("
        Expect.contains tokens (IDENTIFIER "n") "n"
        Expect.contains tokens COLON ":"
        Expect.contains tokens (IDENTIFIER "Person") "Person"
        Expect.contains tokens RPAREN ")"
        Expect.contains tokens RETURN "RETURN"
    }

    test "tokenizes string literals" {
        let tokens = Lexer.tokenize "WHERE n.name = 'Tom'"
        Expect.contains tokens (STRING "Tom") "string literal"
    }

    test "tokenizes parameters" {
        let tokens = Lexer.tokenize "WHERE n.age > $minAge"
        Expect.contains tokens (PARAMETER "minAge") "parameter"
    }

    test "tokenizes numbers" {
        let tokens = Lexer.tokenize "LIMIT 10"
        Expect.contains tokens (INTEGER 10L) "integer"
    }

    test "tokenizes float" {
        let tokens = Lexer.tokenize "WHERE n.score > 3.14"
        Expect.contains tokens (FLOAT 3.14) "float"
    }

    test "tokenizes arrows" {
        let tokens = Lexer.tokenize "(a)-[:KNOWS]->(b)"
        Expect.contains tokens ARROW_RIGHT "->"
    }

    test "tokenizes comparison operators" {
        let tokens = Lexer.tokenize "n.age >= 18 AND n.age <= 65"
        Expect.contains tokens GTE ">="
        Expect.contains tokens LTE "<="
        Expect.contains tokens AND "AND"
    }

    test "tokenizes path length star" {
        let tokens = Lexer.tokenize "-[:KNOWS*1..5]->"
        Expect.contains tokens STAR "*"
    }
]

[<Tests>]
let parserBasicTests = testList "Parser: Basic queries" [
    test "parse simple MATCH RETURN" {
        let result = parse "MATCH (p:Person) RETURN p"
        Expect.hasLength result.Clauses 2 "2 clauses"
        match result.Clauses.[0] with
        | Match(patterns, false) ->
            Expect.hasLength patterns 1 "1 pattern"
            match patterns.[0] with
            | NodePattern("p", Some "Person", props) ->
                Expect.isEmpty props "no props"
            | _ -> failtest "expected NodePattern"
        | _ -> failtest "expected Match"
        match result.Clauses.[1] with
        | Return([item], false) ->
            Expect.equal item.Expr (Variable "p") "return p"
        | _ -> failtest "expected Return"
    }

    test "parse MATCH with WHERE" {
        let result = parse "MATCH (p:Person) WHERE p.age > $minAge RETURN p"
        Expect.hasLength result.Clauses 3 "3 clauses"
        match result.Clauses.[1] with
        | Where(BinOp(Property("p", "age"), Gt, Param "minAge")) -> ()
        | _ -> failtest "expected WHERE p.age > $minAge"
    }

    test "parse MATCH with properties" {
        let result = parse "MATCH (p:Person {name: 'Tom'}) RETURN p"
        match result.Clauses.[0] with
        | Match([NodePattern(_, _, props)], _) ->
            Expect.isTrue (props.ContainsKey "name") "has name prop"
        | _ -> failtest "expected match with props"
    }

    test "parse OPTIONAL MATCH" {
        let result = parse "MATCH (p:Person) OPTIONAL MATCH (m:Movie) RETURN p, m"
        Expect.hasLength result.Clauses 3 "3 clauses"
        match result.Clauses.[0] with
        | Match(_, false) -> ()
        | _ -> failtest "first should be regular MATCH"
        match result.Clauses.[1] with
        | Match(_, true) -> ()
        | _ -> failtest "second should be OPTIONAL MATCH"
    }

    test "parse ORDER BY with DESC" {
        let result = parse "MATCH (p:Person) RETURN p ORDER BY p.age DESC"
        match result.Clauses.[2] with
        | OrderBy [(Property("p", "age"), Descending)] -> ()
        | other -> failwithf "expected ORDER BY DESC, got %A" other
    }

    test "parse SKIP and LIMIT" {
        let result = parse "MATCH (n) RETURN n SKIP 10 LIMIT 5"
        Expect.hasLength result.Clauses 4 "4 clauses"
        match result.Clauses.[2] with
        | Skip(Literal v) -> Expect.equal (v :?> int64) 10L "skip 10"
        | other -> failwithf "expected Skip, got %A" other
        match result.Clauses.[3] with
        | Limit(Literal v) -> Expect.equal (v :?> int64) 5L "limit 5"
        | other -> failwithf "expected Limit, got %A" other
    }

    test "parse RETURN DISTINCT" {
        let result = parse "MATCH (p:Person) RETURN DISTINCT p.name"
        match result.Clauses.[1] with
        | Return(_, true) -> ()
        | _ -> failtest "expected RETURN DISTINCT"
    }

    test "parse RETURN with alias" {
        let result = parse "MATCH (p:Person) RETURN p.name AS name"
        match result.Clauses.[1] with
        | Return([{ Alias = Some "name" }], _) -> ()
        | _ -> failtest "expected alias"
    }
]

[<Tests>]
let parserRelationshipTests = testList "Parser: Relationships" [
    test "parse outgoing relationship" {
        let result = parse "MATCH (p:Person)-[:ACTED_IN]->(m:Movie) RETURN p, m"
        match result.Clauses.[0] with
        | Match([RelPattern(NodePattern("p", _, _), _, Some "ACTED_IN", _, Outgoing, _, NodePattern("m", _, _))], _) -> ()
        | other -> failwithf "expected relationship pattern, got %A" other
    }

    test "parse incoming relationship" {
        let result = parse "MATCH (p:Person)<-[:DIRECTED]-(m:Movie) RETURN p"
        match result.Clauses.[0] with
        | Match([RelPattern(_, _, Some "DIRECTED", _, Incoming, _, _)], _) -> ()
        | other -> failwithf "expected incoming, got %A" other
    }

    test "parse variable-length path" {
        let result = parse "MATCH (p:Person)-[:KNOWS*1..5]->(q:Person) RETURN p, q"
        match result.Clauses.[0] with
        | Match([RelPattern(_, _, Some "KNOWS", _, _, Some (Between(1, 5)), _)], _) -> ()
        | other -> failwithf "expected path *1..5, got %A" other
    }

    test "parse any-length path" {
        let result = parse "MATCH (p)-[:KNOWS*]->(q) RETURN p"
        match result.Clauses.[0] with
        | Match([RelPattern(_, _, _, _, _, Some AnyLength, _)], _) -> ()
        | other -> failwithf "expected AnyLength, got %A" other
    }

    test "parse named relationship" {
        let result = parse "MATCH (p)-[r:KNOWS]->(q) RETURN r"
        match result.Clauses.[0] with
        | Match([RelPattern(_, Some "r", Some "KNOWS", _, _, _, _)], _) -> ()
        | other -> failwithf "expected named rel, got %A" other
    }
]

[<Tests>]
let parserMutationTests = testList "Parser: Mutations" [
    test "parse CREATE node" {
        let result = parse "CREATE (p:Person {name: 'Tom', age: 50})"
        match result.Clauses.[0] with
        | Create [NodePattern("p", Some "Person", props)] ->
            Expect.equal (Map.count props) 2 "2 properties"
        | other -> failwithf "expected CREATE, got %A" other
    }

    test "parse CREATE relationship" {
        let result = parse "MATCH (p:Person), (m:Movie) CREATE (p)-[:ACTED_IN]->(m)"
        Expect.hasLength result.Clauses 2 "match + create"
        match result.Clauses.[1] with
        | Create [RelPattern(_, _, Some "ACTED_IN", _, Outgoing, _, _)] -> ()
        | other -> failwithf "expected CREATE rel, got %A" other
    }

    test "parse DELETE" {
        let result = parse "MATCH (p:Person) DELETE p"
        match result.Clauses.[1] with
        | Delete(["p"], false) -> ()
        | other -> failwithf "expected DELETE, got %A" other
    }

    test "parse DETACH DELETE" {
        let result = parse "MATCH (p:Person) DETACH DELETE p"
        match result.Clauses.[1] with
        | Delete(["p"], true) -> ()
        | other -> failwithf "expected DETACH DELETE, got %A" other
    }

    test "parse SET property" {
        let result = parse "MATCH (p:Person) SET p.age = 30"
        match result.Clauses.[1] with
        | Set [SetProperty("p", "age", Literal _)] -> ()
        | other -> failwithf "expected SET, got %A" other
    }

    test "parse MERGE with ON MATCH SET and ON CREATE SET" {
        let result = parse "MERGE (p:Person {name: 'Tom'}) ON MATCH SET p.age = 50 ON CREATE SET p.age = 25"
        match result.Clauses.[0] with
        | Merge(NodePattern("p", Some "Person", _), [SetProperty("p", "age", _)], [SetProperty("p", "age", _)]) -> ()
        | other -> failwithf "expected MERGE with ON MATCH/CREATE, got %A" other
    }
]

[<Tests>]
let parserAdvancedTests = testList "Parser: Advanced" [
    test "parse UNWIND" {
        let result = parse "UNWIND $names AS name RETURN name"
        match result.Clauses.[0] with
        | Unwind(Param "names", "name") -> ()
        | other -> failwithf "expected UNWIND, got %A" other
    }

    test "parse WITH" {
        let result = parse "MATCH (p:Person) WITH p, count(*) AS cnt RETURN cnt"
        match result.Clauses.[1] with
        | With(items, false) ->
            Expect.hasLength items 2 "2 with items"
        | other -> failwithf "expected WITH, got %A" other
    }

    test "parse CASE expression" {
        let result = parse "MATCH (p:Person) RETURN CASE WHEN p.age > 18 THEN 'adult' ELSE 'minor' END AS status"
        match result.Clauses.[1] with
        | Return([{ Expr = CaseExpr(None, [(_, _)], Some _); Alias = Some "status" }], _) -> ()
        | other -> failwithf "expected CASE in RETURN, got %A" other
    }

    test "parse function call" {
        let result = parse "MATCH (p:Person) RETURN count(p)"
        match result.Clauses.[1] with
        | Return([{ Expr = FuncCall("count", [Variable "p"]) }], _) -> ()
        | other -> failwithf "expected count(p), got %A" other
    }

    test "parse count(*)" {
        let result = parse "MATCH (p:Person) RETURN count(*)"
        match result.Clauses.[1] with
        | Return([{ Expr = FuncCall("count", [Variable "*"]) }], _) -> ()
        | other -> failwithf "expected count(*), got %A" other
    }

    test "parse UNION ALL" {
        let result = parse "MATCH (p:Person) RETURN p UNION ALL MATCH (m:Movie) RETURN m"
        Expect.isTrue (result.Clauses |> List.exists (fun c -> match c with Union true -> true | _ -> false)) "has UNION ALL"
    }

    test "parse complex WHERE with AND/OR" {
        let result = parse "MATCH (p:Person) WHERE p.age > 18 AND p.name = 'Tom' OR p.age < 10 RETURN p"
        match result.Clauses.[1] with
        | Where(BinOp(_, _, _)) -> ()
        | other -> failwithf "expected complex WHERE, got %A" other
    }

    test "parse IS NULL and IS NOT NULL" {
        let result = parse "MATCH (p:Person) WHERE p.email IS NOT NULL RETURN p"
        match result.Clauses.[1] with
        | Where(UnaryOp(IsNotNull, Property("p", "email"))) -> ()
        | other -> failwithf "expected IS NOT NULL, got %A" other
    }

    test "parse CONTAINS" {
        let result = parse "MATCH (p:Person) WHERE p.name CONTAINS 'Tom' RETURN p"
        match result.Clauses.[1] with
        | Where(BinOp(_, Contains, _)) -> ()
        | other -> failwithf "expected CONTAINS, got %A" other
    }

    test "parse STARTS WITH" {
        let result = parse "MATCH (p:Person) WHERE p.name STARTS WITH 'T' RETURN p"
        match result.Clauses.[1] with
        | Where(BinOp(_, StartsWith, _)) -> ()
        | other -> failwithf "expected STARTS WITH, got %A" other
    }
]

[<Tests>]
let roundtripTests = testList "Parser: Roundtrip (parse → compile → compare)" [
    test "simple query roundtrip" {
        let cypher = "MATCH (p:Person) WHERE (p.age > $minAge) RETURN p"
        let parsed = parse cypher
        let compiled = Fyper.CypherCompiler.compile parsed
        Expect.stringContains compiled.Cypher "MATCH (p:Person)" "match"
        Expect.stringContains compiled.Cypher "WHERE (p.age > $minAge)" "where"
        Expect.stringContains compiled.Cypher "RETURN p" "return"
    }

    test "relationship roundtrip" {
        let cypher = "MATCH (p:Person)-[:ACTED_IN]->(m:Movie) RETURN p.name, m.title"
        let parsed = parse cypher
        let compiled = Fyper.CypherCompiler.compile parsed
        Expect.stringContains compiled.Cypher "ACTED_IN" "rel type"
        Expect.stringContains compiled.Cypher "RETURN" "return"
    }

    test "CREATE roundtrip" {
        let cypher = "CREATE (p:Person {name: 'Tom', age: 50})"
        let parsed = parse cypher
        let compiled = Fyper.CypherCompiler.compile parsed
        Expect.stringContains compiled.Cypher "CREATE" "create"
        Expect.stringContains compiled.Cypher ":Person" "label"
    }

    test "DELETE roundtrip" {
        let cypher = "MATCH (p:Person) DETACH DELETE p"
        let parsed = parse cypher
        let compiled = Fyper.CypherCompiler.compile parsed
        Expect.stringContains compiled.Cypher "DETACH DELETE p" "detach delete"
    }
]
