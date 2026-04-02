module Fyper.Integration.Tests.CrossBackendTests

open Expecto
open Fyper
open Fyper.Ast

type Person = { Name: string; Age: int }

[<Tests>]
let crossBackendTests = testList "Cross-Backend" [
    testTask "same CypherQuery compiles identically for both backends" {
        let query : CypherQuery<Person> = {
            Clauses = [
                Match([NodePattern("p", Some "Person", Map.empty)], false)
                Where(BinOp(Property("p", "age"), Gt, Param "p0"))
                Return([{ Expr = Variable "p"; Alias = None }], false)
            ]
            Parameters = Map.ofList ["p0", box 30]
        }
        let cypher, parameters = Cypher.toCypher query
        Expect.stringContains cypher "MATCH (p:Person)" "match"
        Expect.stringContains cypher "WHERE (p.age > $p0)" "where"
        Expect.stringContains cypher "RETURN p" "return"
        Expect.equal (Map.find "p0" parameters) (box 30) "param"
    }

    test "Neo4j capabilities support all features" {
        let caps = DriverCapabilities.all
        Expect.isTrue caps.SupportsOptionalMatch "opt match"
        Expect.isTrue caps.SupportsMerge "merge"
        Expect.isTrue caps.SupportsUnwind "unwind"
        Expect.isTrue caps.SupportsCase "case"
    }

    test "AGE capabilities restrict advanced features" {
        let caps = DriverCapabilities.minimal
        Expect.isFalse caps.SupportsOptionalMatch "opt match"
        Expect.isFalse caps.SupportsMerge "merge"
        Expect.isFalse caps.SupportsUnwind "unwind"
        Expect.isFalse caps.SupportsCase "case"
    }
]
