# Fyper

Type-safe Cypher queries in F#. Plain records as schema, computation expressions as queries, parameterized by default.

```fsharp
type Person = { Name: string; Age: int }

let adults = cypher {
    for p in node<Person> do
    where (p.Age >= 18)
    orderBy p.Name
    select p
}
// -> MATCH (p:Person) WHERE p.age >= $p0 ORDER BY p.name RETURN p
```

## Why Fyper

- **Zero boilerplate schema** — F# records are your graph schema. No attributes, no base classes, no code generation.
- **Compile-time safety** — invalid queries fail at compile time via F# quotations, not at runtime against the database.
- **Parameterized by default** — every value becomes `$p0`, `$p1`. No string interpolation, no injection.
- **Multi-backend** — same query runs on Neo4j and Apache AGE (PostgreSQL). Write once, deploy anywhere.
- **Zero dependencies** — core library depends only on `FSharp.Core`.

## Install

```bash
dotnet add package Fyper            # Core (query builder + compiler)
dotnet add package Fyper.Neo4j      # Neo4j driver
dotnet add package Fyper.Age        # Apache AGE (PostgreSQL) driver
```

## Quick Start

### Define your schema

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
    where (p.Age > 30 && m.Released >= 2000)
    orderBy m.Released
    select (p.Name, m.Title)
}
```

### Inspect without executing

```fsharp
let cypherString, parameters = findActors |> Cypher.toCypher
// "MATCH (p:Person) MATCH (m:Movie) WHERE (p.age > $p0) AND (m.released >= $p1)
//  ORDER BY m.released RETURN p.name, m.title"
// parameters: { p0: 30, p1: 2000 }
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

## Mutations

```fsharp
// CREATE
let q = cypher {
    for _p in node<Person> do
    create { Name = "Alice"; Age = 30 }
}

// SET (record update syntax)
let q = cypher {
    for p in node<Person> do
    where (p.Name = "Alice")
    set (fun p -> { p with Age = p.Age + 1 })
}

// DELETE
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
// Aggregation
let q = cypher { for p in node<Person> do; select (count()) }

// RETURN DISTINCT
let q = cypher { for p in node<Person> do; selectDistinct p.Name }

// UNWIND
let names = ["Tom"; "Alice"]
let q = cypher { for _p in node<Person> do; unwind names "name"; select _p }

// CASE expression
let q = cypher {
    for p in node<Person> do
    select (caseWhen (p.Age > 18) p.Name "minor")
}

// Raw Cypher (escape hatch)
let! records = Cypher.rawAsync driver "MATCH (n) RETURN count(n) AS cnt" Map.empty
```

## Backend Capabilities

Each driver declares which Cypher features it supports. Unsupported features are rejected at query construction time:

| Feature | Neo4j | Apache AGE |
|---------|-------|------------|
| MATCH / WHERE / RETURN | yes | yes |
| CREATE / DELETE / SET | yes | yes |
| OPTIONAL MATCH | yes | no |
| MERGE | yes | no |
| UNWIND | yes | no |
| CASE | yes | no |
| Variable-length paths | yes | yes |

## Custom Naming

Override conventions when needed:

```fsharp
[<Label "PERSON">]
type Person = { Name: string; [<CypherName "birth_year">] BirthYear: int }
```

## Cypher Parser

Parse Cypher strings back into the typed AST — useful for query analysis, transformation, and validation. Zero dependencies beyond `Fyper` core.

```fsharp
open Fyper.Parser

let parsed = CypherParser.parse "MATCH (p:Person)-[:ACTED_IN]->(m:Movie) WHERE p.age > 30 RETURN p.name, m.title"
// parsed.Clauses = [Match(...); Where(...); Return(...)]

// Roundtrip: parse → compile
let compiled = Fyper.CypherCompiler.compile parsed
printfn "%s" compiled.Cypher
```

## Project Structure

| Package | Description | Dependencies |
|---------|-------------|--------------|
| `Fyper` | Core query builder + compiler | FSharp.Core only |
| `Fyper.Parser` | Cypher string parser | Fyper only |
| `Fyper.Neo4j` | Neo4j Bolt driver | Neo4j.Driver |
| `Fyper.Age` | Apache AGE driver | Npgsql |

## Development

```bash
# Run unit + property-based tests (239 tests)
dotnet test tests/Fyper.Tests/

# Run integration tests (requires Docker)
docker compose up -d
dotnet test tests/Fyper.Integration.Tests/
docker compose down

# Run benchmarks
dotnet run --project tests/Fyper.Benchmarks/ -c Release

# Run sample app
dotnet run --project samples/Fyper.Sample/
```

## License

[MIT](LICENSE)
