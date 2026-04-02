module Fyper.Tests.DriverTests

open Expecto
open Fyper
open Fyper.Ast
open Fyper.CypherCompiler

[<Tests>]
let driverCapabilitiesTests = testList "DriverCapabilities" [
    test "all capabilities has all flags true" {
        let caps = DriverCapabilities.all
        Expect.isTrue caps.SupportsOptionalMatch "OptionalMatch"
        Expect.isTrue caps.SupportsMerge "Merge"
        Expect.isTrue caps.SupportsUnwind "Unwind"
        Expect.isTrue caps.SupportsCase "Case"
        Expect.isTrue caps.SupportsCallProcedure "CallProcedure"
        Expect.isTrue caps.SupportsExistsSubquery "ExistsSubquery"
        Expect.isTrue caps.SupportsNamedPaths "NamedPaths"
    }

    test "minimal capabilities has all flags false" {
        let caps = DriverCapabilities.minimal
        Expect.isFalse caps.SupportsOptionalMatch "OptionalMatch"
        Expect.isFalse caps.SupportsMerge "Merge"
        Expect.isFalse caps.SupportsUnwind "Unwind"
        Expect.isFalse caps.SupportsCase "Case"
        Expect.isFalse caps.SupportsCallProcedure "CallProcedure"
        Expect.isFalse caps.SupportsExistsSubquery "ExistsSubquery"
        Expect.isFalse caps.SupportsNamedPaths "NamedPaths"
    }
]

[<Tests>]
let toCypherTests = testList "Cypher.toCypher" [
    test "returns compiled cypher string and parameters" {
        let query : CypherQuery<obj> = {
            Clauses = [
                Match([NodePattern("p", Some "Person", Map.empty)], false)
                Where(BinOp(Property("p", "age"), Gt, Param "p0"))
                Return([{ Expr = Variable "p"; Alias = None }], false)
            ]
            Parameters = Map.ofList ["p0", box 30]
        }
        let cypher, parameters = Cypher.toCypher query
        Expect.stringContains cypher "MATCH (p:Person)" "match clause"
        Expect.stringContains cypher "WHERE (p.age > $p0)" "where clause"
        Expect.stringContains cypher "RETURN p" "return clause"
        Expect.equal (Map.find "p0" parameters) (box 30) "parameter value"
    }

    test "empty query returns empty string" {
        let query : CypherQuery<obj> = { Clauses = []; Parameters = Map.empty }
        let cypher, parameters = Cypher.toCypher query
        Expect.equal cypher "" "empty cypher"
        Expect.isEmpty parameters "empty params"
    }
]
