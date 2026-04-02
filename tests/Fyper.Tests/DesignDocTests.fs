module Fyper.Tests.DesignDocTests

/// Tests verifying EVERY example from docs/DESIGN.md works correctly.
/// These must all pass for 1.0.0.

open Expecto
open Fyper
open Fyper.Ast
open Fyper.CypherCompiler

type Person = { Name: string; Age: int }
type Movie = { Title: string; Released: int }
type ActedIn = { Roles: string list }

[<Tests>]
let designDocQueryTests = testList "Design Doc: Queries" [

    test "simple match and select" {
        let query = cypher {
            for p in node<Person> do
            where (p.Age > 30)
            select p
        }
        let c, pars = Cypher.toCypher query
        Expect.stringContains c "MATCH (p:Person)" "match"
        Expect.stringContains c "WHERE" "where"
        Expect.stringContains c "RETURN p" "return"
        Expect.isTrue (pars |> Map.exists (fun _ v -> v = box 30)) "param 30"
    }

    test "multi-node query" {
        let query = cypher {
            for p in node<Person> do
            for m in node<Movie> do
            where (p.Age > 30)
            orderBy m.Released
            select (p.Name, m.Title)
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "MATCH (p:Person)" "person match"
        Expect.stringContains c "MATCH (m:Movie)" "movie match"
        Expect.stringContains c "WHERE" "where"
        Expect.stringContains c "ORDER BY" "order"
        Expect.stringContains c "RETURN" "return"
    }

    test "relationship matchRel extracts type and maintains variables" {
        let query = cypher {
            for p in node<Person> do
            for m in node<Movie> do
            matchRel (p -< edge<ActedIn> >- m)
            where (p.Age > 30)
            orderBy m.Released
            select (p.Name, m.Title)
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "MATCH (p:Person)" "person"
        Expect.stringContains c "MATCH (m:Movie)" "movie"
        // Relationship MATCH clause should contain the type
        Expect.stringContains c "ACTED_IN" (sprintf "relationship type should be in Cypher. Got:\n%s" c)
        Expect.stringContains c "WHERE" "where works after matchRel"
        Expect.stringContains c "ORDER BY" "orderBy works after matchRel"
        Expect.stringContains c "RETURN" "return works after matchRel"
    }

    test "closure variable capture" {
        let minAge = 25
        let query = cypher {
            for p in node<Person> do
            where (p.Age > minAge)
            select p
        }
        let _, pars = Cypher.toCypher query
        Expect.isTrue (pars |> Map.exists (fun _ v -> v = box 25)) "captured minAge"
    }

    test "order by descending with skip/limit" {
        let query = cypher {
            for p in node<Person> do
            orderByDesc p.Age
            skip 10
            limit 5
            select p
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "ORDER BY" "order"
        Expect.stringContains c "DESC" "desc"
        Expect.stringContains c "SKIP" "skip"
        Expect.stringContains c "LIMIT" "limit"
    }

    test "string Contains in where" {
        let query = cypher {
            for p in node<Person> do
            where (p.Name.Contains("Tom"))
            select p
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "CONTAINS" "contains"
    }

    test "AND and OR in where" {
        let query = cypher {
            for p in node<Person> do
            where (p.Age > 20 && p.Age < 50)
            select p
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "AND" "and"
    }

    test "select single property" {
        let query = cypher {
            for p in node<Person> do
            select p.Name
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "RETURN" "return"
        Expect.stringContains c "name" "property"
    }

    test "select tuple of properties" {
        let query = cypher {
            for p in node<Person> do
            for m in node<Movie> do
            select (p.Name, m.Title)
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "name" "p.name"
        Expect.stringContains c "title" "m.title"
    }

    test "optionalNode produces OPTIONAL MATCH" {
        let query = cypher {
            for p in node<Person> do
            for m in optionalNode<Movie> do
            select (p, m)
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "MATCH (p:Person)" "regular match"
        Expect.stringContains c "OPTIONAL MATCH (m:Movie)" "optional match"
    }

    test "aggregation count()" {
        let query = cypher {
            for p in node<Person> do
            select (count())
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "count(*)" "count"
    }

    test "return distinct" {
        let query = cypher {
            for p in node<Person> do
            selectDistinct p.Name
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "RETURN DISTINCT" "distinct"
    }
]

[<Tests>]
let designDocMutationTests = testList "Design Doc: Mutations" [

    test "create node with record" {
        let query = cypher {
            for _p in node<Person> do
            create { Name = "Tom"; Age = 50 }
        }
        let c, pars = Cypher.toCypher query
        Expect.stringContains c "CREATE" "create"
        Expect.stringContains c ":Person" "label"
        Expect.isTrue (pars |> Map.exists (fun _ v -> v = box "Tom")) "name"
        Expect.isTrue (pars |> Map.exists (fun _ v -> v = box 50)) "age"
    }

    test "delete with where" {
        let query = cypher {
            for p in node<Person> do
            where (p.Name = "Tom")
            detachDelete p
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "MATCH (p:Person)" "match"
        Expect.stringContains c "DETACH DELETE p" "detach delete"
    }

    test "set with record update" {
        let query = cypher {
            for p in node<Person> do
            where (p.Name = "Tom")
            set (fun p -> { p with Age = p.Age + 1 })
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "SET p.age" "set age"
    }

    test "set then select" {
        let query = cypher {
            for p in node<Person> do
            where (p.Name = "Tom")
            set (fun p -> { p with Age = p.Age + 1 })
            select p
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "SET" "set"
        Expect.stringContains c "RETURN" "return after set"
    }

    test "merge with onMatch and onCreate" {
        let query = cypher {
            for p in node<Person> do
            merge { Name = "Tom"; Age = 0 }
            onMatch (fun p -> { p with Age = 50 })
            onCreate (fun p -> { p with Age = 25 })
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "MERGE" "merge"
        Expect.stringContains c "ON MATCH SET" "on match"
        Expect.stringContains c "ON CREATE SET" "on create"
    }
]

[<Tests>]
let designDocAdvancedTests = testList "Design Doc: Advanced" [

    test "unwind" {
        let names = ["Tom"; "Alice"]
        let query = cypher {
            for _p in node<Person> do
            unwind names "name"
            select _p
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "UNWIND" "unwind"
        Expect.stringContains c "AS name" "alias"
    }

    test "with clause" {
        let query = cypher {
            for p in node<Person> do
            withClause p
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "WITH" "with"
    }

    test "caseWhen expression" {
        let query = cypher {
            for p in node<Person> do
            select (caseWhen (p.Age > 18) p.Name "minor")
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "CASE" "case"
        Expect.stringContains c "WHEN" "when"
        Expect.stringContains c "END" "end"
    }

    test "toCypher returns string and params" {
        let query = cypher {
            for p in node<Person> do
            where (p.Age > 30)
            select p
        }
        let cypher, pars = Cypher.toCypher query
        Expect.stringContains cypher "MATCH (p:Person)" "match"
        Expect.stringContains cypher "RETURN p" "return"
        Expect.isTrue (pars.Count > 0) "has params"
    }

    test "raw AST API escape hatch" {
        let query =
            Query.empty<Person>
            |> Query.matchNodes [NodePattern("p", Some "Person", Map.empty)]
            |> Query.where (BinOp(Property("p", "age"), Gt, Param "minAge"))
            |> Query.return' [{ Expr = Property("p", "name"); Alias = Some "name" }]
            |> Query.addParam "minAge" (box 30)
        let c, pars = Cypher.toCypher query
        Expect.stringContains c "MATCH (p:Person)" "match"
        Expect.stringContains c "WHERE (p.age > $minAge)" "where"
        Expect.stringContains c "RETURN p.name AS name" "return"
        Expect.equal (Map.find "minAge" pars) (box 30) "param"
    }
]
