module Fyper.Tests.CapabilityTests

open Expecto
open Fyper
open Fyper.Ast
open Fyper.CypherCompiler

[<Tests>]
let capabilityValidationTests = testList "Capability validation" [
    test "all capabilities passes any clause" {
        let clauses = [
            Match([NodePattern("p", Some "Person", Map.empty)], true)  // OPTIONAL MATCH
            Merge(NodePattern("p", Some "Person", Map.empty), [], [])
            Unwind(Param "list", "item")
            Call("db.labels", [], ["label"])
        ]
        // Should not throw
        validateCapabilities "Neo4j" DriverCapabilities.all clauses
    }

    test "minimal capabilities rejects OPTIONAL MATCH" {
        let clauses = [ Match([NodePattern("p", Some "Person", Map.empty)], true) ]
        Expect.throwsT<FyperUnsupportedFeatureException>
            (fun () -> validateCapabilities "AGE" DriverCapabilities.minimal clauses)
            "should reject OPTIONAL MATCH"
    }

    test "minimal capabilities rejects MERGE" {
        let clauses = [ Merge(NodePattern("p", Some "Person", Map.empty), [], []) ]
        Expect.throwsT<FyperUnsupportedFeatureException>
            (fun () -> validateCapabilities "AGE" DriverCapabilities.minimal clauses)
            "should reject MERGE"
    }

    test "minimal capabilities rejects UNWIND" {
        let clauses = [ Unwind(Param "list", "item") ]
        Expect.throwsT<FyperUnsupportedFeatureException>
            (fun () -> validateCapabilities "AGE" DriverCapabilities.minimal clauses)
            "should reject UNWIND"
    }

    test "minimal capabilities rejects CALL procedure" {
        let clauses = [ Call("db.labels", [], ["label"]) ]
        Expect.throwsT<FyperUnsupportedFeatureException>
            (fun () -> validateCapabilities "AGE" DriverCapabilities.minimal clauses)
            "should reject CALL"
    }

    test "minimal capabilities rejects CASE expression in WHERE" {
        let caseExpr = CaseExpr(None, [(BinOp(Variable "x", Eq, Param "p0"), Param "p1")], Some (Param "p2"))
        let clauses = [ Where caseExpr ]
        Expect.throwsT<FyperUnsupportedFeatureException>
            (fun () -> validateCapabilities "AGE" DriverCapabilities.minimal clauses)
            "should reject CASE"
    }

    test "minimal capabilities rejects EXISTS subquery" {
        let subquery = ExistsSubquery [Match([NodePattern("n", Some "Node", Map.empty)], false)]
        let clauses = [ Where subquery ]
        Expect.throwsT<FyperUnsupportedFeatureException>
            (fun () -> validateCapabilities "AGE" DriverCapabilities.minimal clauses)
            "should reject EXISTS subquery"
    }

    test "minimal capabilities rejects named paths" {
        let namedPath = NamedPath("path", NodePattern("p", Some "Person", Map.empty))
        let clauses = [ Match([namedPath], false) ]
        Expect.throwsT<FyperUnsupportedFeatureException>
            (fun () -> validateCapabilities "AGE" DriverCapabilities.minimal clauses)
            "should reject named paths"
    }

    test "minimal capabilities allows basic MATCH/WHERE/RETURN" {
        let clauses = [
            Match([NodePattern("p", Some "Person", Map.empty)], false)
            Where(BinOp(Property("p", "age"), Gt, Param "p0"))
            Return([{ Expr = Variable "p"; Alias = None }], false)
        ]
        // Should not throw
        validateCapabilities "AGE" DriverCapabilities.minimal clauses
    }

    test "minimal capabilities allows CREATE" {
        let clauses = [ Create [NodePattern("p", Some "Person", Map.ofList ["name", Param "p0"])] ]
        validateCapabilities "AGE" DriverCapabilities.minimal clauses
    }

    test "minimal capabilities allows DELETE" {
        let clauses = [ Delete(["p"], true) ]
        validateCapabilities "AGE" DriverCapabilities.minimal clauses
    }

    test "exception contains feature and backend name" {
        let clauses = [ Match([NodePattern("p", Some "Person", Map.empty)], true) ]
        try
            validateCapabilities "Apache AGE" DriverCapabilities.minimal clauses
            failtest "should have thrown"
        with
        | :? FyperUnsupportedFeatureException as ex ->
            Expect.equal ex.Feature "OPTIONAL MATCH" "feature name"
            Expect.equal ex.Backend "Apache AGE" "backend name"
    }
]
