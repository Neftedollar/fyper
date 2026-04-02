module Fyper.Tests.AdvancedTests

open Expecto
open Fyper.Ast
open Fyper.CypherCompiler

[<Tests>]
let pathLengthTests = testList "Advanced: Variable-length paths" [
    test "Between(1,5) produces *1..5" {
        let pattern = RelPattern(
            NodePattern("p", Some "Person", Map.empty),
            Some "r", Some "KNOWS", Map.empty,
            Outgoing, Some (Between(1, 5)),
            NodePattern("q", Some "Person", Map.empty))
        let result = compilePattern pattern
        Expect.stringContains result "*1..5" "path length"
    }

    test "AnyLength produces *" {
        let pattern = RelPattern(
            NodePattern("p", None, Map.empty),
            None, Some "KNOWS", Map.empty,
            Outgoing, Some AnyLength,
            NodePattern("q", None, Map.empty))
        let result = compilePattern pattern
        Expect.stringContains result ":KNOWS*" "any length"
    }

    test "Exactly 3 produces *3" {
        let pattern = RelPattern(
            NodePattern("p", None, Map.empty),
            None, Some "KNOWS", Map.empty,
            Outgoing, Some (Exactly 3),
            NodePattern("q", None, Map.empty))
        let result = compilePattern pattern
        Expect.stringContains result "*3" "exactly 3"
    }

    test "AtLeast 2 produces *2.." {
        let pattern = RelPattern(
            NodePattern("p", None, Map.empty),
            None, Some "KNOWS", Map.empty,
            Outgoing, Some (AtLeast 2),
            NodePattern("q", None, Map.empty))
        let result = compilePattern pattern
        Expect.stringContains result "*2.." "at least 2"
    }

    test "AtMost 5 produces *..5" {
        let pattern = RelPattern(
            NodePattern("p", None, Map.empty),
            None, Some "KNOWS", Map.empty,
            Outgoing, Some (AtMost 5),
            NodePattern("q", None, Map.empty))
        let result = compilePattern pattern
        Expect.stringContains result "*..5" "at most 5"
    }
]

[<Tests>]
let unwindTests = testList "Advanced: UNWIND" [
    test "UNWIND produces correct Cypher" {
        let clause = Unwind(Param "list", "item")
        let result = compileClause clause
        Expect.equal result "UNWIND $list AS item" "unwind"
    }
]

[<Tests>]
let aggregationTests = testList "Advanced: Aggregation functions" [
    test "count() produces count(*)" {
        let result = compileExpr (FuncCall("count", [Variable "*"]))
        Expect.equal result "count(*)" "count"
    }

    test "count(p) produces count(p)" {
        let result = compileExpr (FuncCall("count", [Variable "p"]))
        Expect.equal result "count(p)" "count p"
    }

    test "collect(p.name) produces collect(p.name)" {
        let result = compileExpr (FuncCall("collect", [Property("p", "name")]))
        Expect.equal result "collect(p.name)" "collect"
    }

    test "sum(p.age) produces sum(p.age)" {
        let result = compileExpr (FuncCall("sum", [Property("p", "age")]))
        Expect.equal result "sum(p.age)" "sum"
    }

    test "avg(p.age) produces avg(p.age)" {
        let result = compileExpr (FuncCall("avg", [Property("p", "age")]))
        Expect.equal result "avg(p.age)" "avg"
    }

    test "min and max" {
        Expect.equal (compileExpr (FuncCall("min", [Property("p", "age")]))) "min(p.age)" "min"
        Expect.equal (compileExpr (FuncCall("max", [Property("p", "age")]))) "max(p.age)" "max"
    }
]

[<Tests>]
let distinctTests = testList "Advanced: DISTINCT" [
    test "RETURN DISTINCT" {
        let clause = Return([{ Expr = Variable "p"; Alias = None }], true)
        let result = compileClause clause
        Expect.equal result "RETURN DISTINCT p" "distinct"
    }

    test "RETURN without DISTINCT" {
        let clause = Return([{ Expr = Variable "p"; Alias = None }], false)
        let result = compileClause clause
        Expect.equal result "RETURN p" "no distinct"
    }
]

[<Tests>]
let withTests = testList "Advanced: WITH" [
    test "WITH clause" {
        let clause = With([{ Expr = Variable "p"; Alias = None }; { Expr = FuncCall("count", [Variable "*"]); Alias = Some "cnt" }], false)
        let result = compileClause clause
        Expect.equal result "WITH p, count(*) AS cnt" "with"
    }

    test "WITH DISTINCT" {
        let clause = With([{ Expr = Variable "p"; Alias = None }], true)
        let result = compileClause clause
        Expect.equal result "WITH DISTINCT p" "with distinct"
    }
]

[<Tests>]
let caseTests = testList "Advanced: CASE" [
    test "CASE WHEN/THEN/ELSE" {
        let caseExpr = CaseExpr(
            None,
            [(BinOp(Property("p", "age"), Gt, Param "p0"), Param "p1")
             (BinOp(Property("p", "age"), Lt, Param "p2"), Param "p3")],
            Some (Param "p4"))
        let result = compileExpr caseExpr
        Expect.stringContains result "CASE" "case"
        Expect.stringContains result "WHEN" "when"
        Expect.stringContains result "THEN" "then"
        Expect.stringContains result "ELSE" "else"
        Expect.stringContains result "END" "end"
    }

    test "CASE with scrutinee" {
        let caseExpr = CaseExpr(
            Some (Property("p", "status")),
            [(Param "p0", Param "p1")],
            None)
        let result = compileExpr caseExpr
        Expect.stringContains result "CASE p.status" "scrutinee"
    }
]
