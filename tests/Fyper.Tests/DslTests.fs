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

    // ─── Mutation CE tests ───

    test "delete variable" {
        let query = cypher {
            for p in node<Person> do
            where (p.Name = "Tom")
            delete p
        }
        let result = compile query
        Expect.stringContains result.Cypher "MATCH" "match"
        Expect.stringContains result.Cypher "WHERE" "where"
        Expect.stringContains result.Cypher "DELETE" "delete"
    }

    test "detach delete variable" {
        let query = cypher {
            for p in node<Person> do
            detachDelete p
        }
        let result = compile query
        Expect.stringContains result.Cypher "DETACH DELETE" "detach delete"
    }

    test "create node with record" {
        let query = cypher {
            for _p in node<Person> do
            create { Name = "Tom"; Age = 50 }
        }
        let result = compile query
        Expect.stringContains result.Cypher "CREATE" "create"
        Expect.stringContains result.Cypher "Person" "label"
        Expect.isTrue (result.Parameters |> Map.exists (fun _ v -> v = box "Tom")) "name param"
        Expect.isTrue (result.Parameters |> Map.exists (fun _ v -> v = box 50)) "age param"
    }

    test "set with record update — literal value" {
        let query = cypher {
            for p in node<Person> do
            where (p.Name = "Tom")
            set (fun p -> { p with Age = 51 })
        }
        let result = compile query
        Expect.stringContains result.Cypher "SET" "set"
        Expect.stringContains result.Cypher "age" "age property"
        // Age=51 should be parameterized (may be int or int64 depending on quotation)
        Expect.isTrue (result.Parameters.Count > 0) "has parameters"
        Expect.stringContains result.Cypher "$" "parameterized"
    }

    test "set with arithmetic expression" {
        let query = cypher {
            for p in node<Person> do
            where (p.Name = "Tom")
            set (fun p -> { p with Age = p.Age + 1 })
        }
        let result = compile query
        Expect.stringContains result.Cypher "SET" "set"
        Expect.stringContains result.Cypher "age" "property"
    }

    test "merge node pattern" {
        let query = cypher {
            for _p in node<Person> do
            merge { Name = "Tom"; Age = 0 }
        }
        let result = compile query
        Expect.stringContains result.Cypher "MERGE" "merge"
        Expect.stringContains result.Cypher "Person" "label"
    }

    // ─── Advanced CE tests ───

    test "select distinct" {
        let query = cypher {
            for p in node<Person> do
            selectDistinct p
        }
        let result = compile query
        Expect.stringContains result.Cypher "RETURN DISTINCT" "distinct"
    }

    test "select distinct property" {
        let query = cypher {
            for p in node<Person> do
            selectDistinct p.Name
        }
        let result = compile query
        Expect.stringContains result.Cypher "RETURN DISTINCT" "distinct"
        Expect.stringContains result.Cypher "name" "property"
    }

    test "unwind list with alias" {
        let names = ["Tom"; "Alice"]
        let query = cypher {
            for _p in node<Person> do
            unwind names "name"
            select _p
        }
        let result = compile query
        Expect.stringContains result.Cypher "UNWIND" "unwind"
        Expect.stringContains result.Cypher "AS name" "alias"
    }

    test "with clause" {
        let query = cypher {
            for p in node<Person> do
            withClause p
        }
        let result = compile query
        Expect.stringContains result.Cypher "WITH" "with clause"
    }

    // ─── Aggregation through CE ───

    test "select with count()" {
        let query = cypher {
            for p in node<Person> do
            select (count())
        }
        let result = compile query
        Expect.stringContains result.Cypher "count(*)" "count(*)"
    }

    test "select with collect(p.Name)" {
        let query = cypher {
            for p in node<Person> do
            select (collect(p.Name))
        }
        let result = compile query
        Expect.stringContains result.Cypher "collect" "collect"
        Expect.stringContains result.Cypher "name" "property"
    }

    test "select with sum(p.Age)" {
        let query = cypher {
            for p in node<Person> do
            select (sum(p.Age))
        }
        let result = compile query
        Expect.stringContains result.Cypher "sum" "sum"
    }

    test "select with cypherMin and cypherMax" {
        let queryMin = cypher {
            for p in node<Person> do
            select (cypherMin(p.Age))
        }
        let queryMax = cypher {
            for p in node<Person> do
            select (cypherMax(p.Age))
        }
        let resultMin = compile queryMin
        let resultMax = compile queryMax
        Expect.stringContains resultMin.Cypher "min" "min"
        Expect.stringContains resultMax.Cypher "max" "max"
    }

    // ─── MERGE with onMatch/onCreate ───

    test "merge with onMatch and onCreate" {
        let query = cypher {
            for p in node<Person> do
            merge { Name = "Tom"; Age = 0 }
            onMatch (fun p -> { p with Age = 50 })
            onCreate (fun p -> { p with Age = 25 })
        }
        let result = compile query
        Expect.stringContains result.Cypher "MERGE" "merge"
        Expect.stringContains result.Cypher "ON MATCH SET" "on match"
        Expect.stringContains result.Cypher "ON CREATE SET" "on create"
    }

    // ─── CASE expression ───

    test "caseWhen in select" {
        let query = cypher {
            for p in node<Person> do
            select (caseWhen (p.Age > 18) p.Name "minor")
        }
        let result = compile query
        Expect.stringContains result.Cypher "CASE" "case"
        Expect.stringContains result.Cypher "WHEN" "when"
        Expect.stringContains result.Cypher "THEN" "then"
        Expect.stringContains result.Cypher "ELSE" "else"
        Expect.stringContains result.Cypher "END" "end"
    }

    test "toCypher returns string and params" {
        let query = cypher {
            for p in node<Person> do
            where (p.Age > 30)
            select p
        }
        let cypherStr, parameters = Cypher.toCypher query
        Expect.stringContains cypherStr "MATCH" "has match"
        Expect.stringContains cypherStr "WHERE" "has where"
        Expect.stringContains cypherStr "RETURN" "has return"
        Expect.isTrue (parameters |> Map.exists (fun _ v -> v = box 30)) "has param"
    }
]
