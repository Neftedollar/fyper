---
layout: default
title: Getting Started
description: "Write your first type-safe Cypher query in F# in 5 minutes. Install Fyper, define schema, query Neo4j or Apache AGE."
nav_order: 2
---

# Getting Started

Get your first type-safe Cypher query running in 5 minutes.

## Install

```bash
dotnet add package Fyper
```

## Define Schema

Plain F# records. No attributes, no interfaces, no base classes required.

```fsharp
type Person = { Name: string; Age: int }
type Movie  = { Title: string; Released: int }
type ActedIn = { Roles: string list }
```

Fyper conventions:
- Type name = node label (`Person` -> `:Person`)
- PascalCase field = camelCase property (`FirstName` -> `firstName`)
- Relationship type = UPPER_SNAKE_CASE (`ActedIn` -> `ACTED_IN`)

## Write a Query

```fsharp
open Fyper

let findOldPeople = cypher {
    for p in node<Person> do
    where (p.Age > 30)
    select p
}
```

This produces: `MATCH (p:Person) WHERE p.age > $p0 RETURN p` with parameter `p0 = 30`.

## Inspect Without Executing

```fsharp
let cypherString, parameters = findOldPeople |> Cypher.toCypher
printfn "Cypher: %s" cypherString
printfn "Params: %A" parameters
```

## Execute Against Neo4j

```bash
dotnet add package Fyper.Neo4j
```

```fsharp
open Fyper.Neo4j

let driver = new Neo4jDriver(
    Neo4j.Driver.GraphDatabase.Driver(
        "bolt://localhost:7687",
        Neo4j.Driver.AuthTokens.Basic("neo4j", "password")))

task {
    let! people = findOldPeople |> Cypher.executeAsync driver
    for person in people do
        printfn "%s is %d years old" person.Name person.Age
}
```

## Execute Against Apache AGE

```bash
dotnet add package Fyper.Age
```

```fsharp
open Fyper.Age
open Npgsql

let ds = NpgsqlDataSource.Create("Host=localhost;Database=mydb;Username=user;Password=pass")
let driver = new AgeDriver(ds, graphName = "movies")

task {
    let! people = findOldPeople |> Cypher.executeAsync driver
    // Same typed results, different backend
}
```

## Next Steps

- [CE Reference](ce-reference.md) -- all query operations
- [Mutations](mutations.md) -- CREATE, SET, DELETE
- [Relationships](relationships.md) -- graph traversal patterns

## See Also

- [CE Operations Reference](../reference/ce-operations.md) -- all operations at a glance
- [Types Reference](../reference/types.md) -- CypherQuery, IGraphDriver, exceptions
- [Neo4j Driver](../reference/neo4j.md) -- Neo4j connection setup
- [AGE Driver](../reference/age.md) -- Apache AGE setup
