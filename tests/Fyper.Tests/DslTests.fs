module Fyper.Tests.DslTests

open Expecto
open Fyper
open Fyper.Ast
open Fyper.CypherCompiler

type Person = { Name: string; Age: int }
type Movie = { Title: string; Released: int }

[<Tests>]
let dslTests = testList "DSL (Computation Expression)" [

    test "simple match and select" {
        let query = cypher {
            for p in node<Person> do
            select p
        }
        let result = compile query
        Expect.stringContains result.Cypher "MATCH" "should have MATCH"
        Expect.stringContains result.Cypher "Person" "should have Person label"
        Expect.stringContains result.Cypher "RETURN" "should have RETURN"
    }

    test "match with where" {
        let query = cypher {
            for p in node<Person> do
            where (p.Age > 30)
            select p
        }
        let result = compile query
        Expect.stringContains result.Cypher "MATCH" ""
        Expect.stringContains result.Cypher "Person" ""
        Expect.stringContains result.Cypher "WHERE" ""
        Expect.stringContains result.Cypher "RETURN" ""
    }

    test "multiple node matches" {
        let query = cypher {
            for p in node<Person> do
            for m in node<Movie> do
            select (p, m)
        }
        let result = compile query
        Expect.stringContains result.Cypher "Person" ""
        Expect.stringContains result.Cypher "Movie" ""
        Expect.stringContains result.Cypher "RETURN" ""
    }

    test "where parameterizes values" {
        let query = cypher {
            for p in node<Person> do
            where (p.Age > 30)
            select p
        }
        let result = compile query
        Expect.stringContains result.Cypher "$" "should use parameter"
        Expect.isTrue (result.Parameters |> Map.exists (fun _ v -> v = box 30)) "should have param 30"
    }

    test "captures closure variables as parameters" {
        let minAge = 25
        let query = cypher {
            for p in node<Person> do
            where (p.Age > minAge)
            select p
        }
        let result = compile query
        Expect.isTrue (result.Parameters |> Map.exists (fun _ v -> v = box 25)) "should capture minAge"
    }

    test "order by ascending" {
        let query = cypher {
            for p in node<Person> do
            orderBy p.Age
            select p
        }
        let result = compile query
        Expect.stringContains result.Cypher "ORDER BY" ""
    }

    test "order by descending" {
        let query = cypher {
            for p in node<Person> do
            orderByDesc p.Age
            select p
        }
        let result = compile query
        Expect.stringContains result.Cypher "ORDER BY" ""
        Expect.stringContains result.Cypher "DESC" ""
    }

    test "skip and limit" {
        let query = cypher {
            for p in node<Person> do
            skip 10
            limit 5
            select p
        }
        let result = compile query
        Expect.stringContains result.Cypher "SKIP" ""
        Expect.stringContains result.Cypher "LIMIT" ""
    }

    test "select single property" {
        let query = cypher {
            for p in node<Person> do
            select p.Name
        }
        let result = compile query
        Expect.stringContains result.Cypher "RETURN" ""
        Expect.stringContains result.Cypher "name" ""
    }

    test "where with AND" {
        let query = cypher {
            for p in node<Person> do
            where (p.Age > 20 && p.Age < 50)
            select p
        }
        let result = compile query
        Expect.stringContains result.Cypher "AND" ""
    }

    test "where with OR" {
        let query = cypher {
            for p in node<Person> do
            where (p.Age < 20 || p.Age > 50)
            select p
        }
        let result = compile query
        Expect.stringContains result.Cypher "OR" ""
    }

    test "where with string contains" {
        let query = cypher {
            for p in node<Person> do
            where (p.Name.Contains("Tom"))
            select p
        }
        let result = compile query
        Expect.stringContains result.Cypher "CONTAINS" ""
    }

    test "where with equality" {
        let query = cypher {
            for p in node<Person> do
            where (p.Name = "Alice")
            select p
        }
        let result = compile query
        Expect.stringContains result.Cypher "=" ""
    }

    test "debug string output" {
        let query = cypher {
            for p in node<Person> do
            where (p.Age > 30)
            select p
        }
        let debug = Cypher.toDebugString query
        Expect.stringContains debug "MATCH" ""
        Expect.stringContains debug "Parameters" ""
    }
]
