// Fyper Sample — Type-Safe Cypher Queries in F#
//
// This sample demonstrates Fyper's core features without requiring
// a running database. All queries compile to Cypher strings that
// you can inspect via Cypher.toCypher.
//
// To execute against a real database, add Fyper.Neo4j or Fyper.Age
// and see the README for connection examples.

open Fyper
open Fyper.Ast

// ─── Schema: plain F# records ───

type Person = { Name: string; Age: int }
type Movie = { Title: string; Released: int }
type ActedIn = { Roles: string list }

// ─── Queries ───

let example1 () =
    printfn "── Example 1: Simple query with WHERE ──"
    let query = cypher {
        for p in node<Person> do
        where (p.Age > 30)
        select p
    }
    let cypher, pars = Cypher.toCypher query
    printfn "Cypher: %s" cypher
    printfn "Params: %A" pars
    printfn ""

let example2 () =
    printfn "── Example 2: Multi-node with ORDER BY ──"
    let query = cypher {
        for p in node<Person> do
        for m in node<Movie> do
        where (p.Age > 25)
        orderBy m.Released
        skip 0
        limit 10
        select (p.Name, m.Title)
    }
    let cypher, pars = Cypher.toCypher query
    printfn "Cypher: %s" cypher
    printfn "Params: %A" pars
    printfn ""

let example3 () =
    printfn "── Example 3: Closure variable capture ──"
    let minAge = 21
    let nameFilter = "Tom"
    let query = cypher {
        for p in node<Person> do
        where (p.Age >= minAge && p.Name = nameFilter)
        select p.Name
    }
    let cypher, pars = Cypher.toCypher query
    printfn "Cypher: %s" cypher
    printfn "Params: %A" pars
    printfn ""

let example4 () =
    printfn "── Example 4: String operations ──"
    let query = cypher {
        for p in node<Person> do
        where (p.Name.Contains("Al"))
        select p
    }
    let cypher, pars = Cypher.toCypher query
    printfn "Cypher: %s" cypher
    printfn "Params: %A" pars
    printfn ""

let example5 () =
    printfn "── Example 5: CREATE node ──"
    let query = cypher {
        for _p in node<Person> do
        create { Name = "Alice"; Age = 30 }
    }
    let cypher, pars = Cypher.toCypher query
    printfn "Cypher: %s" cypher
    printfn "Params: %A" pars
    printfn ""

let example6 () =
    printfn "── Example 6: SET with record update ──"
    let query = cypher {
        for p in node<Person> do
        where (p.Name = "Tom")
        set (fun p -> { p with Age = p.Age + 1 })
    }
    let cypher, pars = Cypher.toCypher query
    printfn "Cypher: %s" cypher
    printfn "Params: %A" pars
    printfn ""

let example7 () =
    printfn "── Example 7: DELETE ──"
    let query = cypher {
        for p in node<Person> do
        where (p.Name = "Bob")
        detachDelete p
    }
    let cypher, pars = Cypher.toCypher query
    printfn "Cypher: %s" cypher
    printfn "Params: %A" pars
    printfn ""

let example8 () =
    printfn "── Example 8: Aggregation ──"
    let query = cypher {
        for p in node<Person> do
        select (count())
    }
    let cypher, pars = Cypher.toCypher query
    printfn "Cypher: %s" cypher
    printfn "Params: %A" pars
    printfn ""

let example9 () =
    printfn "── Example 9: RETURN DISTINCT ──"
    let query = cypher {
        for p in node<Person> do
        selectDistinct p.Name
    }
    let cypher, pars = Cypher.toCypher query
    printfn "Cypher: %s" cypher
    printfn "Params: %A" pars
    printfn ""

let example10 () =
    printfn "── Example 10: Raw AST API (escape hatch) ──"
    let query =
        Query.empty<Person>
        |> Query.matchNodes [NodePattern("p", Some "Person", Map.empty)]
        |> Query.where (BinOp(Property("p", "age"), Gt, Param "minAge"))
        |> Query.return' [{ Expr = Property("p", "name"); Alias = Some "name" }]
        |> Query.addParam "minAge" (box 30)
    let cypher, pars = Cypher.toCypher query
    printfn "Cypher: %s" cypher
    printfn "Params: %A" pars
    printfn ""

// ─── Run all examples ───

[<EntryPoint>]
let main _argv =
    printfn "Fyper 0.1.0 — Type-Safe Cypher for F#"
    printfn "========================================\n"
    example1 ()
    example2 ()
    example3 ()
    example4 ()
    example5 ()
    example6 ()
    example7 ()
    example8 ()
    example9 ()
    example10 ()
    printfn "All examples compiled successfully!"
    0
