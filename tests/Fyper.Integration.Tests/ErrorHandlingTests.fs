module Fyper.Integration.Tests.ErrorHandlingTests

open Expecto
open Fyper
open Fyper.Ast
open Fyper.CypherCompiler

[<Tests>]
let errorHandlingTests = testList "Error Handling" [
    test "FyperUnsupportedFeatureException contains feature and backend" {
        let ex = FyperUnsupportedFeatureException("OPTIONAL MATCH", "Apache AGE")
        Expect.equal ex.Feature "OPTIONAL MATCH" "feature"
        Expect.equal ex.Backend "Apache AGE" "backend"
        Expect.stringContains ex.Message "OPTIONAL MATCH" "message has feature"
        Expect.stringContains ex.Message "Apache AGE" "message has backend"
    }

    test "FyperQueryException contains query and parameters" {
        let ex = FyperQueryException("Syntax error", "MATCH (n RETURN n", Map.ofList ["p0", box 1], null)
        Expect.equal ex.Query "MATCH (n RETURN n" "query"
        Expect.equal (Map.find "p0" ex.Parameters) (box 1) "params"
    }

    test "FyperMappingException contains target type and source" {
        let ex = FyperMappingException("Type mismatch", typeof<int>, GraphValue.GString "not a number")
        Expect.equal ex.TargetType typeof<int> "target type"
        match ex.SourceValue with
        | GraphValue.GString s -> Expect.equal s "not a number" "source"
        | _ -> failtest "wrong source type"
    }

    test "FyperConnectionException is a FyperException" {
        let ex = FyperConnectionException("conn refused")
        Expect.isTrue ((ex :> exn) :? FyperException) "is FyperException"
        Expect.stringContains ex.Message "conn refused" "message"
    }

    test "capability validation rejects OPTIONAL MATCH on minimal" {
        Expect.throwsT<FyperUnsupportedFeatureException>
            (fun () ->
                validateCapabilities "AGE" DriverCapabilities.minimal
                    [Match([NodePattern("p", Some "Person", Map.empty)], true)])
            "rejects optional match"
    }

    test "capability validation rejects MERGE on minimal" {
        Expect.throwsT<FyperUnsupportedFeatureException>
            (fun () ->
                validateCapabilities "AGE" DriverCapabilities.minimal
                    [Merge(NodePattern("p", Some "Person", Map.empty), [], [])])
            "rejects merge"
    }

    test "capability validation rejects UNWIND on minimal" {
        Expect.throwsT<FyperUnsupportedFeatureException>
            (fun () ->
                validateCapabilities "AGE" DriverCapabilities.minimal
                    [Unwind(Param "list", "item")])
            "rejects unwind"
    }

    test "capability validation passes all on full" {
        validateCapabilities "Neo4j" DriverCapabilities.all
            [Match([NodePattern("p", Some "Person", Map.empty)], true)
             Merge(NodePattern("p", Some "Person", Map.empty), [], [])
             Unwind(Param "list", "item")
             Call("db.labels", [], ["label"])]
    }
]
