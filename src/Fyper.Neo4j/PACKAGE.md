# Fyper.Neo4j

Neo4j Bolt driver for [Fyper](https://www.nuget.org/packages/Fyper) -- the type-safe F# Cypher query builder.

## Install

```bash
dotnet add package Fyper
dotnet add package Fyper.Neo4j
```

## Usage

```fsharp
open Fyper
open Fyper.Neo4j

type Person = { Name: string; Age: int }

let driver = new Neo4jDriver(
    Neo4j.Driver.GraphDatabase.Driver("bolt://localhost:7687",
        Neo4j.Driver.AuthTokens.Basic("neo4j", "password")))

let query = cypher {
    for p in node<Person> do
    where (p.Age > 30)
    select p
}

task {
    let! people = query |> Cypher.executeAsync driver
    for person in people do
        printfn "%s is %d" person.Name person.Age
}
```

## Transactions

```fsharp
task {
    let! result = Cypher.inTransaction driver (fun tx -> task {
        let! _ = q1 |> Cypher.executeWriteAsync tx
        let! _ = q2 |> Cypher.executeWriteAsync tx
        return 2
    })
}
```

## Capabilities

All Cypher features supported: MATCH, OPTIONAL MATCH, WHERE, RETURN, CREATE, MERGE, DELETE, SET, UNWIND, CASE, variable-length paths, CALL procedures.

## Dependencies

- [Neo4j.Driver](https://www.nuget.org/packages/Neo4j.Driver) (official Bolt driver)
- [Fyper](https://www.nuget.org/packages/Fyper) (core library)

## Links

- [Documentation](https://neftedollar.github.io/fyper/reference/neo4j.html)
- [GitHub](https://github.com/Neftedollar/fyper)
