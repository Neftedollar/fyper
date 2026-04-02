module Fyper.Tests.MutationTests

open Expecto
open Fyper.Ast
open Fyper.CypherCompiler

// Test mutation AST → Cypher compilation (doesn't need CE, tests compiler directly)

[<Tests>]
let createTests = testList "Mutation: CREATE" [
    test "CREATE node with properties" {
        let query : CypherQuery<unit> = {
            Clauses = [
                Create [NodePattern("p", Some "Person", Map.ofList ["name", Param "p0"; "age", Param "p1"])]
            ]
            Parameters = Map.ofList ["p0", box "Tom"; "p1", box 50]
        }
        let result = compile query
        Expect.stringContains result.Cypher "CREATE (p:Person {age: $p1, name: $p0})" "create clause"
        Expect.equal (Map.find "p0" result.Parameters) (box "Tom") "name param"
        Expect.equal (Map.find "p1" result.Parameters) (box 50) "age param"
    }

    test "CREATE relationship" {
        let query : CypherQuery<unit> = {
            Clauses = [
                Match([NodePattern("p", Some "Person", Map.empty); NodePattern("m", Some "Movie", Map.empty)], false)
                Create [
                    RelPattern(
                        NodePattern("p", None, Map.empty),
                        None, Some "ACTED_IN",
                        Map.ofList ["roles", Param "p0"],
                        Outgoing, None,
                        NodePattern("m", None, Map.empty))
                ]
            ]
            Parameters = Map.ofList ["p0", box ["Neo"]]
        }
        let result = compile query
        Expect.stringContains result.Cypher "CREATE (p)-[:ACTED_IN {roles: $p0}]->(m)" "create rel"
    }
]

[<Tests>]
let mergeTests = testList "Mutation: MERGE" [
    test "MERGE node" {
        let query : CypherQuery<unit> = {
            Clauses = [
                Merge(
                    NodePattern("p", Some "Person", Map.ofList ["name", Param "p0"]),
                    [SetProperty("p", "age", Param "p1")],
                    [SetProperty("p", "age", Param "p2")])
            ]
            Parameters = Map.ofList ["p0", box "Tom"; "p1", box 50; "p2", box 25]
        }
        let result = compile query
        Expect.stringContains result.Cypher "MERGE (p:Person {name: $p0})" "merge node"
        Expect.stringContains result.Cypher "ON MATCH SET p.age = $p1" "on match"
        Expect.stringContains result.Cypher "ON CREATE SET p.age = $p2" "on create"
    }
]

[<Tests>]
let setTests = testList "Mutation: SET" [
    test "SET property" {
        let query : CypherQuery<unit> = {
            Clauses = [
                Match([NodePattern("p", Some "Person", Map.empty)], false)
                Where(BinOp(Property("p", "name"), Eq, Param "p0"))
                Set [SetProperty("p", "age", BinOp(Property("p", "age"), Add, Param "p1"))]
            ]
            Parameters = Map.ofList ["p0", box "Tom"; "p1", box 1]
        }
        let result = compile query
        Expect.stringContains result.Cypher "SET p.age = (p.age + $p1)" "set property"
    }

    test "SET all properties" {
        let query : CypherQuery<unit> = {
            Clauses = [
                Match([NodePattern("p", Some "Person", Map.empty)], false)
                Set [SetAllProperties("p", MapExpr [("name", Param "p0"); ("age", Param "p1")])]
            ]
            Parameters = Map.ofList ["p0", box "Alice"; "p1", box 30]
        }
        let result = compile query
        Expect.stringContains result.Cypher "SET p = {name: $p0, age: $p1}" "set all"
    }
]

[<Tests>]
let deleteTests = testList "Mutation: DELETE" [
    test "DELETE node" {
        let query : CypherQuery<unit> = {
            Clauses = [
                Match([NodePattern("p", Some "Person", Map.empty)], false)
                Where(BinOp(Property("p", "name"), Eq, Param "p0"))
                Delete(["p"], false)
            ]
            Parameters = Map.ofList ["p0", box "Tom"]
        }
        let result = compile query
        Expect.stringContains result.Cypher "DELETE p" "delete"
        Expect.isFalse (result.Cypher.Contains("DETACH")) "not detach"
    }

    test "DETACH DELETE node" {
        let query : CypherQuery<unit> = {
            Clauses = [
                Match([NodePattern("p", Some "Person", Map.empty)], false)
                Delete(["p"], true)
            ]
            Parameters = Map.empty
        }
        let result = compile query
        Expect.stringContains result.Cypher "DETACH DELETE p" "detach delete"
    }
]
