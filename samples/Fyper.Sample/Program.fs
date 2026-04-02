// ============================================================================
// Fyper 1.0.0 — Type-Safe Cypher for F#
// Comprehensive examples with output in comments
// ============================================================================

open Fyper
open Fyper.Ast

// ─── Schema: plain F# records, no attributes needed ───

type Person = { Name: string; Age: int }
type Movie = { Title: string; Released: int }
type ActedIn = { Roles: string list }
type Knows = { Since: int }
type Directed = { Year: int }

[<Label "FILM">]
type Film = { Title: string; [<CypherName "release_year">] Released: int }

let print title (q: CypherQuery<_>) =
    let c, p = Cypher.toCypher q
    printfn "── %s ──" title
    printfn "%s" c
    if not (Map.isEmpty p) then printfn "-- params: %A" p
    printfn ""

// ============================================================================
// 1. BASIC QUERIES
// ============================================================================

let basicQueries () =
    printfn "═══ BASIC QUERIES ═══\n"

    // Simple match + return
    cypher {
        for p in node<Person> do
        select p
    }
    |> print "Match all persons"
    // MATCH (p:Person)
    // RETURN p

    // Where with comparison
    cypher {
        for p in node<Person> do
        where (p.Age > 30)
        select p
    }
    |> print "Persons older than 30"
    // MATCH (p:Person)
    // WHERE (p.age > $p0)
    // RETURN p
    // -- params: map [("p0", 30)]

    // Where with equality
    cypher {
        for p in node<Person> do
        where (p.Name = "Alice")
        select p
    }
    |> print "Find Alice"
    // MATCH (p:Person)
    // WHERE (p.name = $p0)
    // RETURN p
    // -- params: map [("p0", "Alice")]

    // Closure variable capture
    let minAge = 25
    let city = "Berlin"
    cypher {
        for p in node<Person> do
        where (p.Age >= minAge)
        select p.Name
    }
    |> print "Capture closure variable"
    // MATCH (p:Person)
    // WHERE (p.age >= $p0)
    // RETURN p.name AS name
    // -- params: map [("p0", 25)]

    // Select single property
    cypher {
        for p in node<Person> do
        select p.Name
    }
    |> print "Select single property"
    // MATCH (p:Person)
    // RETURN p.name AS name

    // Select tuple of properties
    cypher {
        for p in node<Person> do
        select (p.Name, p.Age)
    }
    |> print "Select tuple"
    // MATCH (p:Person)
    // RETURN p.name AS name, p.age AS age

// ============================================================================
// 2. MULTIPLE NODES & ORDERING
// ============================================================================

let multiNodeQueries () =
    printfn "═══ MULTIPLE NODES & ORDERING ═══\n"

    // Two node types
    cypher {
        for p in node<Person> do
        for m in node<Movie> do
        select (p.Name, m.Title)
    }
    |> print "Two node match"
    // MATCH (p:Person)
    // MATCH (m:Movie)
    // RETURN p.name AS name, m.title AS title

    // Order by ascending
    cypher {
        for p in node<Person> do
        orderBy p.Name
        select p
    }
    |> print "Order by name (ASC)"
    // MATCH (p:Person)
    // ORDER BY p.name
    // RETURN p

    // Order by descending
    cypher {
        for p in node<Person> do
        orderByDesc p.Age
        select p
    }
    |> print "Order by age (DESC)"
    // MATCH (p:Person)
    // ORDER BY p.age DESC
    // RETURN p

    // Skip + Limit (pagination)
    cypher {
        for p in node<Person> do
        orderBy p.Name
        skip 10
        limit 5
        select p
    }
    |> print "Pagination: skip 10, limit 5"
    // MATCH (p:Person)
    // ORDER BY p.name
    // SKIP $skip_0
    // LIMIT $limit_1
    // RETURN p
    // -- params: map [("limit_1", 5); ("skip_0", 10)]

    // Complex WHERE with AND/OR
    cypher {
        for p in node<Person> do
        where (p.Age > 18 && p.Age < 65)
        select p
    }
    |> print "AND condition"
    // MATCH (p:Person)
    // WHERE ((p.age > $p0) AND (p.age < $p1))
    // RETURN p
    // -- params: map [("p0", 18); ("p1", 65)]

    cypher {
        for p in node<Person> do
        where (p.Age < 18 || p.Age > 65)
        select p
    }
    |> print "OR condition"
    // MATCH (p:Person)
    // WHERE ((p.age < $p0) OR (p.age > $p1))
    // RETURN p
    // -- params: map [("p0", 18); ("p1", 65)]

// ============================================================================
// 3. STRING OPERATIONS
// ============================================================================

let stringOps () =
    printfn "═══ STRING OPERATIONS ═══\n"

    cypher {
        for p in node<Person> do
        where (p.Name.Contains("Tom"))
        select p
    }
    |> print "CONTAINS"
    // MATCH (p:Person)
    // WHERE (p.name CONTAINS $p0)
    // RETURN p
    // -- params: map [("p0", "Tom")]

    cypher {
        for p in node<Person> do
        where (p.Name.StartsWith("A"))
        select p
    }
    |> print "STARTS WITH"
    // MATCH (p:Person)
    // WHERE (p.name STARTS WITH $p0)
    // RETURN p
    // -- params: map [("p0", "A")]

    cypher {
        for p in node<Person> do
        where (p.Name.EndsWith("son"))
        select p
    }
    |> print "ENDS WITH"
    // MATCH (p:Person)
    // WHERE (p.name ENDS WITH $p0)
    // RETURN p
    // -- params: map [("p0", "son")]

// ============================================================================
// 4. RELATIONSHIPS
// ============================================================================

let relationships () =
    printfn "═══ RELATIONSHIPS ═══\n"

    // Basic relationship match
    cypher {
        for p in node<Person> do
        for m in node<Movie> do
        matchRel (p -< edge<ActedIn> >- m)
        select (p.Name, m.Title)
    }
    |> print "Match relationship (ActedIn → ACTED_IN)"
    // MATCH (p:Person)
    // MATCH (m:Movie)
    // MATCH (p:Person)-[:ACTED_IN]->(m:Movie)
    // RETURN p.name AS name, m.title AS title

    // Relationship with WHERE
    cypher {
        for p in node<Person> do
        for m in node<Movie> do
        matchRel (p -< edge<ActedIn> >- m)
        where (p.Age > 30 && m.Released >= 2000)
        orderBy m.Released
        select (p.Name, m.Title)
    }
    |> print "Relationship with filter and sort"
    // MATCH (p:Person)
    // MATCH (m:Movie)
    // MATCH (p:Person)-[:ACTED_IN]->(m:Movie)
    // WHERE ((p.age > $p0) AND (m.released >= $p1))
    // ORDER BY m.released
    // RETURN p.name AS name, m.title AS title
    // -- params: map [("p0", 30); ("p1", 2000)]

    // OPTIONAL MATCH
    cypher {
        for p in node<Person> do
        for m in optionalNode<Movie> do
        select (p, m)
    }
    |> print "OPTIONAL MATCH"
    // MATCH (p:Person)
    // OPTIONAL MATCH (m:Movie)
    // RETURN p, m

    // Variable-length path
    cypher {
        for p in node<Person> do
        for q in node<Person> do
        matchPath (p -< edge<Knows> >- q) (Between(1, 5))
        select (p.Name, q.Name)
    }
    |> print "Variable-length path *1..5"
    // MATCH (p:Person)
    // MATCH (q:Person)
    // MATCH (p:Person)-[:KNOWS*1..5]->(q:Person)
    // RETURN p.name AS name, q.name AS name

    // Any-length path
    cypher {
        for p in node<Person> do
        for q in node<Person> do
        matchPath (p -< edge<Knows> >- q) AnyLength
        select q
    }
    |> print "Any-length path *"
    // MATCH (p:Person)
    // MATCH (q:Person)
    // MATCH (p:Person)-[:KNOWS*]->(q:Person)
    // RETURN q

    // Exactly N hops
    cypher {
        for p in node<Person> do
        for q in node<Person> do
        matchPath (p -< edge<Knows> >- q) (Exactly 3)
        select q
    }
    |> print "Exactly 3 hops"
    // MATCH (p:Person)
    // MATCH (q:Person)
    // MATCH (p:Person)-[:KNOWS*3]->(q:Person)
    // RETURN q

// ============================================================================
// 5. MUTATIONS
// ============================================================================

let mutations () =
    printfn "═══ MUTATIONS ═══\n"

    // CREATE node
    cypher {
        for _p in node<Person> do
        create { Name = "Alice"; Age = 30 }
    }
    |> print "CREATE node"
    // MATCH (_p:Person)
    // CREATE (p:Person {age: $p0, name: $p1})
    // -- params: map [("p0", 30); ("p1", "Alice")]

    // CREATE relationship
    cypher {
        for p in node<Person> do
        for m in node<Movie> do
        where (p.Name = "Tom")
        createRel (p -< edge<ActedIn> >- m)
    }
    |> print "CREATE relationship"
    // MATCH (p:Person)
    // MATCH (m:Movie)
    // WHERE (p.name = $p0)
    // CREATE (p:Person)-[:ACTED_IN]->(m:Movie)
    // -- params: map [("p0", "Tom")]

    // SET with literal value
    cypher {
        for p in node<Person> do
        where (p.Name = "Tom")
        set (fun p -> { p with Age = 51 })
    }
    |> print "SET literal value"
    // MATCH (p:Person)
    // WHERE (p.name = $p0)
    // SET p.age = $p1
    // -- params: map [("p0", "Tom"); ("p1", 51)]

    // SET with arithmetic
    cypher {
        for p in node<Person> do
        where (p.Name = "Tom")
        set (fun p -> { p with Age = p.Age + 1 })
    }
    |> print "SET with arithmetic (birthday)"
    // MATCH (p:Person)
    // WHERE (p.name = $p0)
    // SET p.age = (p.age + $p1)
    // -- params: map [("p0", "Tom"); ("p1", 1)]

    // SET then RETURN
    cypher {
        for p in node<Person> do
        where (p.Name = "Tom")
        set (fun p -> { p with Age = p.Age + 1 })
        select p
    }
    |> print "SET then RETURN"
    // MATCH (p:Person)
    // WHERE (p.name = $p0)
    // SET p.age = (p.age + $p1)
    // RETURN p
    // -- params: map [("p0", "Tom"); ("p1", 1)]

    // DELETE
    cypher {
        for p in node<Person> do
        where (p.Name = "Bob")
        delete p
    }
    |> print "DELETE"
    // MATCH (p:Person)
    // WHERE (p.name = $p0)
    // DELETE p
    // -- params: map [("p0", "Bob")]

    // DETACH DELETE
    cypher {
        for p in node<Person> do
        where (p.Name = "Bob")
        detachDelete p
    }
    |> print "DETACH DELETE"
    // MATCH (p:Person)
    // WHERE (p.name = $p0)
    // DETACH DELETE p
    // -- params: map [("p0", "Bob")]

    // MERGE with ON MATCH / ON CREATE
    cypher {
        for p in node<Person> do
        merge { Name = "Tom"; Age = 0 }
        onMatch (fun p -> { p with Age = 50 })
        onCreate (fun p -> { p with Age = 25 })
    }
    |> print "MERGE with ON MATCH / ON CREATE"
    // MATCH (p:Person)
    // MERGE (p:Person {age: $p0, name: $p1}) ON MATCH SET p.age = $p2 ON CREATE SET p.age = $p3
    // -- params: map [("p0", 0); ("p1", "Tom"); ("p2", 50); ("p3", 25)]

// ============================================================================
// 6. AGGREGATION & ADVANCED
// ============================================================================

let advanced () =
    printfn "═══ AGGREGATION & ADVANCED ═══\n"

    // count(*)
    cypher {
        for p in node<Person> do
        select (count())
    }
    |> print "count(*)"
    // MATCH (p:Person)
    // RETURN count(*)

    // sum
    cypher {
        for p in node<Person> do
        select (sum(p.Age))
    }
    |> print "sum(p.age)"
    // MATCH (p:Person)
    // RETURN sum(p.age)

    // collect
    cypher {
        for p in node<Person> do
        select (collect(p.Name))
    }
    |> print "collect(p.name)"
    // MATCH (p:Person)
    // RETURN collect(p.name)

    // avg
    cypher {
        for p in node<Person> do
        select (avg(p.Age))
    }
    |> print "avg(p.age)"
    // MATCH (p:Person)
    // RETURN avg(p.age)

    // min / max
    cypher {
        for p in node<Person> do
        select (cypherMin(p.Age))
    }
    |> print "min(p.age)"
    // MATCH (p:Person)
    // RETURN min(p.age)

    cypher {
        for p in node<Person> do
        select (cypherMax(p.Age))
    }
    |> print "max(p.age)"
    // MATCH (p:Person)
    // RETURN max(p.age)

    // Anonymous record projection
    cypher {
        for p in node<Person> do
        select {| Name = p.Name; Count = count() |}
    }
    |> print "Anonymous record projection"
    // MATCH (p:Person)
    // RETURN count(*) AS count, p.name AS name

    // RETURN DISTINCT
    cypher {
        for p in node<Person> do
        selectDistinct p.Name
    }
    |> print "RETURN DISTINCT"
    // MATCH (p:Person)
    // RETURN DISTINCT p.name AS name

    // UNWIND
    let names = ["Tom"; "Alice"; "Bob"]
    cypher {
        for _p in node<Person> do
        unwind names "name"
        select _p
    }
    |> print "UNWIND"
    // MATCH (_p:Person)
    // UNWIND $p0 AS name
    // RETURN _p
    // -- params: map [("p0", ["Tom"; "Alice"; "Bob"])]

    // WITH clause
    cypher {
        for p in node<Person> do
        withClause p
    }
    |> print "WITH clause"
    // MATCH (p:Person)
    // WITH p

    // CASE WHEN
    cypher {
        for p in node<Person> do
        select (caseWhen (p.Age > 18) p.Name "minor")
    }
    |> print "CASE WHEN"
    // MATCH (p:Person)
    // RETURN CASE WHEN (p.age > $p0) THEN p.name ELSE $p1 END
    // -- params: map [("p0", 18); ("p1", "minor")]

// ============================================================================
// 7. CUSTOM NAMING
// ============================================================================

let customNaming () =
    printfn "═══ CUSTOM NAMING ═══\n"

    // Label attribute overrides type name
    cypher {
        for f in node<Film> do
        select f
    }
    |> print "[<Label \"FILM\">] type Film"
    // MATCH (f:FILM)
    // RETURN f

    // CypherName attribute overrides property name
    cypher {
        for f in node<Film> do
        where (f.Released > 2000)
        select f.Title
    }
    |> print "[<CypherName \"release_year\">] Released"
    // MATCH (f:FILM)
    // WHERE (f.release_year > $p0)
    // RETURN f.title AS title
    // -- params: map [("p0", 2000)]

// ============================================================================
// 8. RAW AST API (escape hatch)
// ============================================================================

let rawApi () =
    printfn "═══ RAW AST API ═══\n"

    // Build query programmatically
    let q =
        Query.empty<Person>
        |> Query.matchNodes [NodePattern("p", Some "Person", Map.empty)]
        |> Query.where (BinOp(Property("p", "age"), Gt, Param "minAge"))
        |> Query.return' [{ Expr = Property("p", "name"); Alias = Some "name" }]
        |> Query.addParam "minAge" (box 30)
    q |> print "Raw AST: Query.empty |> Query.matchNodes |> ..."
    // MATCH (p:Person)
    // WHERE (p.age > $minAge)
    // RETURN p.name AS name
    // -- params: map [("minAge", 30)]

    // Raw AST with relationship
    let q2 =
        Query.empty<obj>
        |> Query.matchNodes [
            RelPattern(
                NodePattern("p", Some "Person", Map.empty),
                Some "r", Some "ACTED_IN", Map.empty,
                Outgoing, None,
                NodePattern("m", Some "Movie", Map.empty))
        ]
        |> Query.return' [
            { Expr = Property("p", "name"); Alias = Some "actor" }
            { Expr = Property("m", "title"); Alias = Some "movie" }
        ]
    q2 |> print "Raw AST: relationship pattern"
    // MATCH (p:Person)-[r:ACTED_IN]->(m:Movie)
    // RETURN p.name AS actor, m.title AS movie

    // Raw AST with variable-length path
    let q3 =
        Query.empty<obj>
        |> Query.matchNodes [
            RelPattern(
                NodePattern("a", Some "Person", Map.empty),
                None, Some "KNOWS", Map.empty,
                Outgoing, Some (Between(1, 3)),
                NodePattern("b", Some "Person", Map.empty))
        ]
        |> Query.return' [{ Expr = Variable "b"; Alias = None }]
    q3 |> print "Raw AST: variable-length path *1..3"
    // MATCH (a:Person)-[:KNOWS*1..3]->(b:Person)
    // RETURN b

// ============================================================================
// 9. PARSER (Cypher string → AST → Cypher string)
// ============================================================================

let parserExamples () =
    printfn "═══ PARSER (roundtrip) ═══\n"

    let roundtrip label input =
        let parsed = Fyper.Parser.CypherParser.parse input
        let compiled = CypherCompiler.compile parsed
        printfn "── %s ──" label
        printfn "Input:  %s" input
        printfn "Output: %s" (compiled.Cypher.Replace("\n", " "))
        printfn ""

    roundtrip "Simple query"
        "MATCH (p:Person) WHERE p.age > $minAge RETURN p"
    // Input:  MATCH (p:Person) WHERE p.age > $minAge RETURN p
    // Output: MATCH (p:Person) WHERE (p.age > $minAge) RETURN p

    roundtrip "Relationship"
        "MATCH (p:Person)-[:ACTED_IN]->(m:Movie) RETURN p.name, m.title"
    // Input:  MATCH (p:Person)-[:ACTED_IN]->(m:Movie) RETURN p.name, m.title
    // Output: MATCH (p:Person)-[:ACTED_IN]->(m:Movie) RETURN p.name, m.title

    roundtrip "CREATE with properties"
        "CREATE (p:Person {name: 'Tom', age: 50})"
    // Input:  CREATE (p:Person {name: 'Tom', age: 50})
    // Output: CREATE (p:Person {name: 'Tom', age: 50})

    roundtrip "MERGE with ON MATCH/CREATE"
        "MERGE (p:Person {name: 'Tom'}) ON MATCH SET p.age = 50 ON CREATE SET p.age = 25"
    // Input:  MERGE (p:Person {name: 'Tom'}) ON MATCH SET p.age = 50 ON CREATE SET p.age = 25
    // Output: MERGE (p:Person {name: 'Tom'}) ON MATCH SET p.age = 50 ON CREATE SET p.age = 25

    roundtrip "ORDER BY + LIMIT"
        "MATCH (p:Person) RETURN p ORDER BY p.age DESC LIMIT 10"
    // Input:  MATCH (p:Person) RETURN p ORDER BY p.age DESC LIMIT 10
    // Output: MATCH (p:Person) RETURN p ORDER BY p.age DESC LIMIT 10

    roundtrip "CASE expression"
        "MATCH (p:Person) RETURN CASE WHEN p.age > 18 THEN 'adult' ELSE 'minor' END AS status"
    // Input:  MATCH (p:Person) RETURN CASE WHEN p.age > 18 THEN 'adult' ELSE 'minor' END AS status
    // Output: MATCH (p:Person) RETURN CASE WHEN (p.age > 18) THEN 'adult' ELSE 'minor' END AS status

    roundtrip "Variable-length path"
        "MATCH (a:Person)-[:KNOWS*1..5]->(b:Person) RETURN b"
    // Input:  MATCH (a:Person)-[:KNOWS*1..5]->(b:Person) RETURN b
    // Output: MATCH (a:Person)-[:KNOWS*1..5]->(b:Person) RETURN b

    roundtrip "UNWIND + WITH"
        "UNWIND $names AS name WITH name MATCH (p:Person {name: name}) RETURN p"
    // Input:  UNWIND $names AS name WITH name MATCH (p:Person {name: name}) RETURN p
    // Output: UNWIND $names AS name WITH name MATCH (p:Person {name: name}) RETURN p

// ============================================================================

[<EntryPoint>]
let main _argv =
    printfn "Fyper 1.0.0 — Type-Safe Cypher for F#"
    printfn "═══════════════════════════════════════\n"

    basicQueries ()
    multiNodeQueries ()
    stringOps ()
    relationships ()
    mutations ()
    advanced ()
    customNaming ()
    rawApi ()
    parserExamples ()

    printfn "═══ All %d examples compiled successfully! ═══" 40
    0
