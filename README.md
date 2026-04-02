# Fyper

[![CI](https://github.com/Neftedollar/fyper/actions/workflows/ci.yml/badge.svg)](https://github.com/Neftedollar/fyper/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Fyper.svg)](https://www.nuget.org/packages/Fyper)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

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

## Why Fyper

- **Zero boilerplate schema** -- F# records are your graph schema. No attributes, no base classes, no code generation.
- **Compile-time safety** -- quotation-based CE catches errors before runtime.
- **Parameterized by default** -- every value becomes `$p0`, `$p1`. No string interpolation, no injection.
- **Multi-backend** -- same query runs on Neo4j and Apache AGE (PostgreSQL).
- **Zero dependencies** -- core library depends only on `FSharp.Core`.
- **Fast** -- sub-microsecond compilation, ~1us parse, ~3us for complex queries ([benchmarks](#performance)).

## Install

```bash
dotnet add package Fyper              # Core query builder + compiler
dotnet add package Fyper.Parser       # Cypher string parser (zero deps)
dotnet add package Fyper.Neo4j        # Neo4j Bolt driver
dotnet add package Fyper.Age          # Apache AGE (PostgreSQL) driver
```

## Quick Start

### Define your schema

Plain F# records. No attributes required.

```fsharp
type Person = { Name: string; Age: int }
type Movie  = { Title: string; Released: int }
type ActedIn = { Roles: string list }
```

### Query

```fsharp
open Fyper

let findActors = cypher {
    for p in node<Person> do
    for m in node<Movie> do
    matchRel (p -- edge<ActedIn> --> m)
    where (p.Age > 30 && m.Released >= 2000)
    orderBy m.Released
    select (p.Name, m.Title)
}
```

### Inspect without executing

```fsharp
let cypherString, parameters = findActors |> Cypher.toCypher
// MATCH (p:Person) MATCH (m:Movie) MATCH (p)-[:ACTED_IN]->(m)
// WHERE (p.age > $p0) AND (m.released >= $p1)
// ORDER BY m.released RETURN p.name AS name, m.title AS title
```

### Execute against Neo4j

```fsharp
open Fyper.Neo4j

let driver = new Neo4jDriver(
    Neo4j.Driver.GraphDatabase.Driver(
        "bolt://localhost:7687",
        Neo4j.Driver.AuthTokens.Basic("neo4j", "password")))

task {
    let! results = findActors |> Cypher.executeAsync driver
    for (name, title) in results do
        printfn "%s acted in %s" name title
}
```

### Execute against Apache AGE

Same query, different backend:

```fsharp
open Fyper.Age
open Npgsql

let ds = NpgsqlDataSource.Create("Host=localhost;Database=mydb;Username=user;Password=pass")
let driver = new AgeDriver(ds, graphName = "movies")

task {
    let! results = findActors |> Cypher.executeAsync driver
    // identical typed results
}
```

## Relationships

```fsharp
// Match relationship (extracts type: ActedIn -> ACTED_IN)
let q = cypher {
    for p in node<Person> do
    for m in node<Movie> do
    matchRel (p -- edge<ActedIn> --> m)
    select (p.Name, m.Title)
}

// OPTIONAL MATCH
let q = cypher {
    for p in node<Person> do
    for m in optionalNode<Movie> do
    matchRel (p -- edge<ActedIn> --> m)
    select (p, m)
}

// Variable-length paths
let q = cypher {
    for p in node<Person> do
    for q in node<Person> do
    matchPath (p -- edge<ActedIn> --> q) (Between(1, 5))
    select (p, q)
}
// -> MATCH (p)-[:ACTED_IN*1..5]->(q)

// Create relationship between existing nodes
let q = cypher {
    for p in node<Person> do
    for m in node<Movie> do
    where (p.Name = "Tom")
    createRel (p -- edge<ActedIn> --> m)
}
```

## Mutations

```fsharp
// CREATE node
let q = cypher {
    for _p in node<Person> do
    create { Name = "Alice"; Age = 30 }
}

// SET with record update syntax
let q = cypher {
    for p in node<Person> do
    where (p.Name = "Alice")
    set (fun p -> { p with Age = p.Age + 1 })
}
// -> SET p.age = (p.age + $p0)

// DETACH DELETE
let q = cypher {
    for p in node<Person> do
    where (p.Name = "Bob")
    detachDelete p
}

// MERGE with ON MATCH / ON CREATE
let q = cypher {
    for p in node<Person> do
    merge { Name = "Tom"; Age = 0 }
    onMatch (fun p -> { p with Age = 50 })
    onCreate (fun p -> { p with Age = 25 })
}
```

## Transactions

```fsharp
task {
    let! result = Cypher.inTransaction driver (fun tx -> task {
        let! _ = cypher { for _p in node<Person> do; create { Name = "A"; Age = 1 } }
                 |> Cypher.executeWriteAsync tx
        let! _ = cypher { for _p in node<Person> do; create { Name = "B"; Age = 2 } }
                 |> Cypher.executeWriteAsync tx
        return 2
    })
    // Both committed atomically, or both rolled back on exception
}
```

## Advanced Features

```fsharp
// Aggregation functions
let q = cypher { for p in node<Person> do; select (count()) }
let q = cypher { for p in node<Person> do; select (sum(p.Age)) }
let q = cypher { for p in node<Person> do; select (collect(p.Name)) }

// Anonymous record projection
let q = cypher { for p in node<Person> do; select {| Age = p.Age; Count = count() |} }

// RETURN DISTINCT
let q = cypher { for p in node<Person> do; selectDistinct p.Name }

// UNWIND
let names = ["Tom"; "Alice"]
let q = cypher { for _p in node<Person> do; unwind names "name"; select _p }

// WITH clause
let q = cypher { for p in node<Person> do; withClause p }

// CASE expression
let q = cypher {
    for p in node<Person> do
    select (caseWhen (p.Age > 18) p.Name "minor")
}

// String operators
let q = cypher {
    for p in node<Person> do
    where (p.Name.Contains("Tom") || p.Name.StartsWith("A") || p.Name.EndsWith("son"))
    select p
}

// Raw Cypher (escape hatch)
let! records = Cypher.rawAsync driver "MATCH (n) RETURN count(n) AS cnt" Map.empty
```

## Cypher Parser

Parse Cypher strings into the typed AST -- useful for query analysis, transformation, and validation. Zero dependencies beyond `Fyper` core.

```fsharp
open Fyper.Parser

// Parse any Cypher string
let parsed = CypherParser.parse
    "MATCH (p:Person)-[:ACTED_IN]->(m:Movie) WHERE p.age > 30 RETURN p.name, m.title"

// parsed.Clauses = [Match(RelPattern(...)); Where(BinOp(...)); Return(...)]

// Roundtrip: parse -> compile
let compiled = Fyper.CypherCompiler.compile parsed
printfn "%s" compiled.Cypher

// Supports full Cypher:
// MATCH, OPTIONAL MATCH, WHERE, RETURN, WITH, CREATE, MERGE (ON MATCH/ON CREATE),
// DELETE, DETACH DELETE, SET, REMOVE, ORDER BY, SKIP, LIMIT, UNWIND, UNION, CALL,
// CASE WHEN, EXISTS subqueries, variable-length paths, IS NULL, CONTAINS, etc.
```

## Backend Capabilities

Each driver declares supported Cypher features. Unsupported features are rejected at query construction time, not at the database:

| Feature | Neo4j | Apache AGE |
|---------|-------|------------|
| MATCH / WHERE / RETURN | yes | yes |
| CREATE / DELETE / SET | yes | yes |
| OPTIONAL MATCH | yes | no |
| MERGE + ON MATCH/CREATE | yes | no |
| UNWIND | yes | no |
| CASE expressions | yes | no |
| Variable-length paths | yes | yes |
| ORDER BY / SKIP / LIMIT | yes | yes |
| Named paths | yes | no |
| CALL procedures | yes | no |

## Custom Naming

Override conventions when needed:

```fsharp
[<Label "PERSON">]
type Person = { Name: string; [<CypherName "birth_year">] BirthYear: int }
```

Default conventions:
- Type name = node label (`Person` -> `:Person`)
- PascalCase field = camelCase property (`FirstName` -> `firstName`)
- Relationship type = UPPER_SNAKE_CASE (`ActedIn` -> `ACTED_IN`)

## Performance

Benchmarked on Apple M1 Pro, .NET 10.0:

| Operation | Mean | Allocated |
|-----------|------|-----------|
| Compile simple query | 890 ns | 2.3 KB |
| Compile complex query (8 clauses) | 3.2 us | 9.2 KB |
| Lex simple Cypher string | 744 ns | 1.5 KB |
| Parse simple Cypher string | 1.2 us | 2.6 KB |
| Parse complex Cypher (rel + WHERE + ORDER BY) | 3.5 us | 7.7 KB |
| Full roundtrip: parse -> compile | 2.0 us | 4.9 KB |
| Schema: toCypherName | 22 ns | 104 B |
| Schema: getMeta (cached) | 24 ns | 64 B |
| ResultMapper: record | 6.8 us | 4.3 KB |
| ResultMapper: tuple | 790 ns | 664 B |

Run benchmarks: `dotnet run --project tests/Fyper.Benchmarks/ -c Release`

## Known Issues & Limitations

- **No incoming arrow operator** -- F# operator precedence makes `<--` ambiguous. For incoming relationships, swap the order: `matchRel (m -- edge<ActedIn> --> p)` produces `(m)-[:ACTED_IN]->(p)`.
- **No edge properties in CE** -- `edge<ActedIn>` carries only the type, not property values. For relationship properties, use the raw AST API.
- **REMOVE not in CE** -- use `Cypher.rawAsync` or raw AST for `REMOVE` operations.
- **CALL procedure not in CE** -- supported by the parser and AST, but no CE operation yet.
- **EXISTS subquery not in CE** -- supported in parser and AST only.
- **AGE dialect limitations** -- Apache AGE does not support OPTIONAL MATCH, MERGE, UNWIND, CASE. Fyper rejects these at query construction time.
- **Multi-field SET** -- `set (fun p -> { p with Name = "X"; Age = 30 })` changes both fields. Only the changed fields generate SET clauses, but both changes are in one SET (no separate SET per field).

## Project Structure

| Package | Description | Dependencies |
|---------|-------------|--------------|
| `Fyper` | Core query builder + compiler | FSharp.Core only |
| `Fyper.Parser` | Cypher string parser | Fyper only |
| `Fyper.Neo4j` | Neo4j Bolt driver | Neo4j.Driver |
| `Fyper.Age` | Apache AGE (PostgreSQL) driver | Npgsql |

## Development

```bash
# Run all 239 tests (unit + property-based + parser)
dotnet test tests/Fyper.Tests/

# Run integration tests (requires Docker)
docker compose up -d
dotnet test tests/Fyper.Integration.Tests/
docker compose down

# Run benchmarks
dotnet run --project tests/Fyper.Benchmarks/ -c Release

# Run sample app (10 examples, no database needed)
dotnet run --project samples/Fyper.Sample/
```

## License

[MIT](LICENSE)
