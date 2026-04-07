---
title: "Type-Safe Cypher Queries in F# — Introducing Fyper"
published: false
description: "Write Neo4j and Apache AGE graph queries using F# computation expressions. Compile-time safety, parameterized by default, zero dependencies."
tags: fsharp, neo4j, graphdatabase, dotnet
cover_image:
canonical_url: https://neftedollar.github.io/fyper/
---

Every F# developer who's worked with Neo4j knows the pain: raw Cypher strings, no IntelliSense, no compile-time checks, and the constant fear of typos in property names.

```fsharp
// The old way — string-based, error-prone
let cypher = "MATCH (p:Preson) WHERE p.agee > 30 RETURN p"
// Two typos. You'll find out at runtime. Maybe in production.
```

I built [Fyper](https://github.com/Neftedollar/fyper) to fix this. It's a type-safe Cypher query builder that uses F# computation expressions:

```fsharp
type Person = { Name: string; Age: int }

let query = cypher {
    for p in node<Person> do
    where (p.Age > 30)
    select p
}
// Generates: MATCH (p:Person) WHERE p.age > $p0 RETURN p
// Parameters: { p0: 30 }
```

Typo in `p.Agee`? Compile error. Wrong type name? Compile error. Forgot to parameterize a value? Impossible — Fyper parameterizes everything by default.

## How It Works

Plain F# records are your schema. No attributes, no base classes, no code generation:

```fsharp
type Person = { Name: string; Age: int }
type Movie = { Title: string; Released: int }
type ActedIn = { Roles: string list }
```

Fyper conventions:
- Type name → node label (`Person` → `:Person`)
- PascalCase field → camelCase property (`FirstName` → `firstName`)
- Relationship type → UPPER_SNAKE_CASE (`ActedIn` → `ACTED_IN`)

## Relationships

```fsharp
let findActors = cypher {
    for p in node<Person> do
    for m in node<Movie> do
    matchRel (p -- edge<ActedIn> --> m)
    where (p.Age > 30 && m.Released >= 2000)
    orderBy m.Released
    select (p.Name, m.Title)
}
// MATCH (p:Person) MATCH (m:Movie)
// MATCH (p)-[:ACTED_IN]->(m)
// WHERE (p.age > $p0) AND (m.released >= $p1)
// ORDER BY m.released
// RETURN p.name, m.title
```

Variable-length paths work too:

```fsharp
matchPath (p -- edge<Knows> --> q) (Between(1, 5))
// MATCH (p)-[:KNOWS*1..5]->(q)
```

## Mutations

F# record update syntax for SET — only changed fields generate Cypher:

```fsharp
// Birthday: increment age
let birthday = cypher {
    for p in node<Person> do
    where (p.Name = "Tom")
    set (fun p -> { p with Age = p.Age + 1 })
    select p
}
// SET p.age = (p.age + $p0)

// MERGE with ON MATCH / ON CREATE
let ensurePerson = cypher {
    for p in node<Person> do
    merge { Name = "Tom"; Age = 0 }
    onMatch (fun p -> { p with Age = 50 })
    onCreate (fun p -> { p with Age = 25 })
}
```

## Multi-Backend: Same Query, Different Database

Write once, run on Neo4j or Apache AGE (PostgreSQL):

```fsharp
// Neo4j
let neo4j = new Neo4jDriver(
    GraphDatabase.Driver("bolt://localhost:7687",
        AuthTokens.Basic("neo4j", "password")))

// Apache AGE (PostgreSQL)
let age = new AgeDriver(
    NpgsqlDataSource.Create("Host=localhost;Database=mydb;..."),
    graphName = "movies")

// Same query, different backend
let! people = query |> Cypher.executeAsync neo4j
let! people = query |> Cypher.executeAsync age
```

Each driver declares which Cypher features it supports. Unsupported features (like OPTIONAL MATCH on AGE) are rejected at query construction time — not at the database.

## Inspect Without Executing

Debug queries without a database connection:

```fsharp
let cypher, params = query |> Cypher.toCypher
printfn "%s" cypher
printfn "%A" params
```

## Cypher Parser (Bonus)

Fyper includes a zero-dependency Cypher parser. Parse any Cypher string into a typed AST:

```fsharp
open Fyper.Parser

let parsed = CypherParser.parse
    "MATCH (p:Person)-[:ACTED_IN]->(m:Movie) RETURN p.name"
// parsed.Clauses = [Match(RelPattern(...)); Return(...)]

// Roundtrip: parse → compile
let compiled = CypherCompiler.compile parsed
```

## Performance

Benchmarked on Apple M1 Pro:

| Operation | Time |
|-----------|------|
| Compile simple query | 890 ns |
| Compile complex query (8 clauses) | 3.2 μs |
| Parse Cypher string | 1.2 μs |
| Full roundtrip (parse → compile) | 2.0 μs |

## Get Started

```bash
dotnet add package Fyper
dotnet add package Fyper.Neo4j    # or Fyper.Age
```

- **GitHub**: [github.com/Neftedollar/fyper](https://github.com/Neftedollar/fyper)
- **Docs**: [neftedollar.github.io/fyper](https://neftedollar.github.io/fyper/)
- **NuGet**: [nuget.org/packages/Fyper](https://www.nuget.org/packages/Fyper)

250 tests (unit + property-based + integration). MIT license. Zero dependencies in core.

---

If you've been writing raw Cypher strings from F# — try Fyper. I'd love feedback: [open an issue](https://github.com/Neftedollar/fyper/issues) or leave a comment here.
