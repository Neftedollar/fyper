# Quickstart: Fyper — F# Typed Cypher ORM

## Install

```bash
# Core library (query builder + compiler)
dotnet add package Fyper

# Pick your database driver
dotnet add package Fyper.Neo4j    # Neo4j via Bolt
dotnet add package Fyper.Age      # Apache AGE via PostgreSQL
```

## Define Schema

Plain F# records. No attributes, no interfaces, no base classes.

```fsharp
type Person = { Name: string; Age: int }
type Movie = { Title: string; Released: int }
type ActedIn = { Roles: string list }
```

## Query

```fsharp
open Fyper

let findOldActors = cypher {
    for p in node<Person> do
    for m in node<Movie> do
    match' (p -[edge<ActedIn>]-> m)
    where (p.Age > 30)
    orderBy m.Released
    select (p.Name, m.Title)
}
```

## Inspect Generated Cypher

```fsharp
let cypherString, parameters = findOldActors |> Cypher.toCypher
// cypherString = "MATCH (p:Person), (m:Movie) MATCH (p)-[:ACTED_IN]->(m) WHERE p.age > $p0 ORDER BY m.released RETURN p.name, m.title"
// parameters = map ["p0", box 30]
```

## Execute Against Neo4j

```fsharp
open Fyper.Neo4j

let driver = new Neo4jDriver(Neo4j.Driver.GraphDatabase.Driver("bolt://localhost:7687", AuthTokens.Basic("neo4j", "password")))

task {
    let! results = findOldActors |> Cypher.executeAsync driver
    // results : (string * string) list

    for (name, title) in results do
        printfn "%s acted in %s" name title
}
```

## Execute Against Apache AGE

```fsharp
open Fyper.Age
open Npgsql

let dataSource = NpgsqlDataSource.Create("Host=localhost;Database=testdb;Username=test;Password=test")
let driver = new AgeDriver(dataSource, graphName = "movies")

task {
    let! results = findOldActors |> Cypher.executeAsync driver
    // Same typed results, different backend
}
```

## Mutations

```fsharp
let createPerson = cypher {
    create (node<Person> { Name = "Tom"; Age = 50 })
}

let! count = createPerson |> Cypher.executeWriteAsync driver
```

## Transactions

```fsharp
task {
    let! result = Cypher.inTransaction driver (fun tx -> task {
        let! _ = cypher { create (node<Person> { Name = "Alice"; Age = 30 }) }
                 |> Cypher.executeWriteAsync tx
        let! _ = cypher { create (node<Person> { Name = "Bob"; Age = 25 }) }
                 |> Cypher.executeWriteAsync tx
        return 2
    })
    // Both creates committed atomically, or both rolled back on exception
}
```

## Raw Cypher (Escape Hatch)

```fsharp
let! records = Cypher.rawAsync driver "MATCH (n) RETURN count(n) AS cnt" Map.empty
```

## Validation

Run tests:

```bash
# Unit + property-based tests
dotnet test tests/Fyper.Tests/

# Integration tests (requires Docker)
docker compose up -d          # Start AGE (Neo4j auto-starts via Testcontainers)
dotnet test tests/Fyper.Integration.Tests/
docker compose down
```
