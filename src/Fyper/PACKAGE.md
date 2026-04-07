# Fyper

Type-safe Cypher queries in F#. Plain records as schema, computation expressions as queries, parameterized by default.

```fsharp
type Person = { Name: string; Age: int }
type Movie  = { Title: string; Released: int }
type ActedIn = { Roles: string list }

let findActors = cypher {
    for p in node<Person> do
    for m in node<Movie> do
    matchRel (p -- edge<ActedIn> --> m)
    where (p.Age > 30)
    orderBy m.Released
    select (p.Name, m.Title)
}
// MATCH (p:Person) MATCH (m:Movie) MATCH (p)-[:ACTED_IN]->(m)
// WHERE p.age > $p0 ORDER BY m.released RETURN p.name, m.title
```

## Features

- **Zero boilerplate** -- F# records = graph schema
- **Compile-time safety** -- quotation-based CE
- **Parameterized by default** -- all values become `$p0`, `$p1`
- **Multi-backend** -- Neo4j + Apache AGE from the same query
- **Zero dependencies** -- only FSharp.Core

## Quick Start

```bash
dotnet add package Fyper
dotnet add package Fyper.Neo4j    # or Fyper.Age
```

```fsharp
open Fyper
open Fyper.Neo4j

let driver = new Neo4jDriver(
    Neo4j.Driver.GraphDatabase.Driver("bolt://localhost:7687",
        Neo4j.Driver.AuthTokens.Basic("neo4j", "password")))

task {
    let! people = query |> Cypher.executeAsync driver
}
```

## CE Operations

`where` | `select` | `selectDistinct` | `orderBy` | `orderByDesc` | `skip` | `limit` | `matchRel` | `matchPath` | `create` | `createRel` | `set` | `delete` | `detachDelete` | `merge` | `onMatch` | `onCreate` | `unwind` | `withClause` | `caseWhen` | `count` | `sum` | `avg` | `collect` | `cypherMin` | `cypherMax`

## Links

- [Documentation](https://neftedollar.github.io/fyper/)
- [GitHub](https://github.com/Neftedollar/fyper)
- [Changelog](https://github.com/Neftedollar/fyper/blob/main/CHANGELOG.md)
