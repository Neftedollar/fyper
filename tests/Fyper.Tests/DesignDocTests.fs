module Fyper.Tests.DesignDocTests

/// Tests verifying EVERY example from docs/DESIGN.md works correctly.
/// These must all pass for 1.0.0.

open Expecto
open Fyper
open Fyper.Ast
open Fyper.CypherCompiler

type Person = { Name: string; Age: int }
type Movie = { Title: string; Released: int }
type Knows = { Since: int }
type PersonWithEmail = { Name: string; Age: int; Email: string option }
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
            matchRel (p -- edge<ActedIn> --> m)
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

    test "incoming relationship with edgeIn" {
        let query = cypher {
            for p in node<Person> do
            for m in node<Movie> do
            matchRel (p -- edgeIn<ActedIn> --> m)
            select (p.Name, m.Title)
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "ACTED_IN" "relationship type"
        Expect.stringContains c "<-[" (sprintf "incoming arrow. Got:\n%s" c)
    }

    test "undirected relationship with edgeUn" {
        let query = cypher {
            for p in node<Person> do
            for q in node<Person> do
            matchRel (p -- edgeUn<Knows> --> q)
            select (p, q)
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "KNOWS" "relationship type"
        // Undirected: no arrow, just -[]-
        let hasNoArrow = not (c.Contains("->")) || not (c.Contains("<-"))
        Expect.isTrue (c.Contains("-[") && c.Contains("]-")) "undirected brackets"
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

    test "create relationship via createRel" {
        let query = cypher {
            for p in node<Person> do
            for m in node<Movie> do
            where (p.Name = "Tom")
            createRel (p -- edge<ActedIn> --> m)
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "CREATE" "create"
        Expect.stringContains c "ACTED_IN" (sprintf "relationship type. Got:\n%s" c)
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
let designDocRemoveTests = testList "Design Doc: REMOVE & CALL" [

    test "removeProperty generates REMOVE p.prop" {
        let query = cypher {
            for p in node<PersonWithEmail> do
            where (p.Name = "Tom")
            removeProperty p.Email
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "REMOVE" "remove"
        Expect.stringContains c "email" (sprintf "property name. Got:\n%s" c)
    }

    test "removeLabel generates REMOVE p:Label" {
        let query = cypher {
            for p in node<Person> do
            removeLabel p "Admin"
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "REMOVE" "remove"
        Expect.stringContains c ":Admin" (sprintf "label. Got:\n%s" c)
    }

    test "createRelWith generates CREATE with relationship properties" {
        let query = cypher {
            for p in node<Person> do
            for m in node<Movie> do
            where (p.Name = "Tom")
            createRelWith (p -- edge<ActedIn> --> m) { Roles = ["Neo"] }
        }
        let c, pars = Cypher.toCypher query
        Expect.stringContains c "CREATE" "create"
        Expect.stringContains c "ACTED_IN" "rel type"
        Expect.stringContains c "roles" (sprintf "property name. Got:\n%s" c)
    }

    test "existsRel in where" {
        let query = cypher {
            for p in node<Person> do
            for m in node<Movie> do
            where (existsRel (p -- edge<ActedIn> --> m))
            select p
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "EXISTS" (sprintf "exists. Got:\n%s" c)
    }

    test "callProc generates CALL procedure" {
        let query = cypher {
            for _p in node<Person> do
            callProc "db.labels" ["label"]
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "CALL db.labels()" (sprintf "call. Got:\n%s" c)
        Expect.stringContains c "YIELD label" (sprintf "yield. Got:\n%s" c)
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

    test "anonymous record in select" {
        let query = cypher {
            for p in node<Person> do
            select {| Age = p.Age; Count = count() |}
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "RETURN" "return"
        Expect.stringContains c "age" "age field"
        Expect.stringContains c "count(*)" "count"
    }

    test "variable-length path via matchPath" {
        let query = cypher {
            for p in node<Person> do
            for q in node<Person> do
            matchPath (p -- edge<ActedIn> --> q) (Between(1, 5))
            select (p, q)
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "*1..5" "path length"
        Expect.stringContains c "ACTED_IN" "relationship type"
    }

    test "variable-length path AnyLength" {
        let query = cypher {
            for p in node<Person> do
            for q in node<Person> do
            matchPath (p -- edge<ActedIn> --> q) AnyLength
            select (p, q)
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "*" "any length star"
    }

    test "collect in select" {
        let query = cypher {
            for p in node<Person> do
            select (collect(p.Name))
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "collect" "collect"
    }

    test "sum and avg in select" {
        let querySum = cypher {
            for p in node<Person> do
            select (sum(p.Age))
        }
        let queryAvg = cypher {
            for p in node<Person> do
            select (avg(p.Age))
        }
        let cSum, _ = Cypher.toCypher querySum
        let cAvg, _ = Cypher.toCypher queryAvg
        Expect.stringContains cSum "sum" "sum"
        Expect.stringContains cAvg "avg" "avg"
    }

    test "caseWhen with property comparison" {
        let query = cypher {
            for p in node<Person> do
            select (caseWhen (p.Age >= 18) p.Name "minor")
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "CASE WHEN" "case when"
        Expect.stringContains c "THEN" "then"
        Expect.stringContains c "ELSE" "else"
        Expect.stringContains c "END" "end"
    }

    test "optionalNode with matchRel" {
        let query = cypher {
            for p in node<Person> do
            for m in optionalNode<Movie> do
            matchRel (p -- edge<ActedIn> --> m)
            select (p, m)
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "MATCH (p:Person)" "regular match"
        Expect.stringContains c "OPTIONAL MATCH (m:Movie)" "optional match"
        Expect.stringContains c "ACTED_IN" "relationship type"
    }

    test "multiple where conditions" {
        let query = cypher {
            for p in node<Person> do
            where (p.Age > 18 && p.Name.StartsWith("A"))
            select p
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "AND" "and"
        Expect.stringContains c "STARTS WITH" "starts with"
    }

    test "or condition in where" {
        let query = cypher {
            for p in node<Person> do
            where (p.Age < 18 || p.Age > 65)
            select p
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "OR" "or"
    }

    test "equality with string" {
        let query = cypher {
            for p in node<Person> do
            where (p.Name = "Alice")
            select p
        }
        let c, pars = Cypher.toCypher query
        Expect.stringContains c "= $" "equality parameterized"
        Expect.isTrue (pars |> Map.exists (fun _ v -> v = box "Alice")) "Alice param"
    }

    test "EndsWith in where" {
        let query = cypher {
            for p in node<Person> do
            where (p.Name.EndsWith("son"))
            select p
        }
        let c, _ = Cypher.toCypher query
        Expect.stringContains c "ENDS WITH" "ends with"
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
