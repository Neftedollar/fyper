# Fyper.Age

Apache AGE (PostgreSQL) driver for [Fyper](https://www.nuget.org/packages/Fyper) -- the type-safe F# Cypher query builder. Run graph queries on PostgreSQL.

## Install

```bash
dotnet add package Fyper
dotnet add package Fyper.Age
```

## Prerequisites

- PostgreSQL with the [Apache AGE](https://age.apache.org/) extension
- A named graph created in AGE

## Usage

```fsharp
open Fyper
open Fyper.Age
open Npgsql

type Person = { Name: string; Age: int }

let ds = NpgsqlDataSource.Create("Host=localhost;Database=mydb;Username=user;Password=pass")
let driver = new AgeDriver(ds, graphName = "my_graph")

let query = cypher {
    for p in node<Person> do
    where (p.Age > 30)
    select p
}

task {
    let! people = query |> Cypher.executeAsync driver
    // Same typed results as Neo4j
}
```

## How It Works

Cypher queries are wrapped in AGE's SQL function:

```sql
SELECT * FROM cypher('graph', $$ MATCH (n:Person) RETURN n $$) AS (n agtype)
```

## Supported Features

MATCH, WHERE, RETURN, CREATE, DELETE, SET, ORDER BY, SKIP, LIMIT, variable-length paths.

**Not supported by AGE**: OPTIONAL MATCH, MERGE, UNWIND, CASE, CALL procedures. Fyper rejects these at query construction time.

## Dependencies

- [Npgsql](https://www.nuget.org/packages/Npgsql) (PostgreSQL driver)
- [Fyper](https://www.nuget.org/packages/Fyper) (core library)

## Links

- [Documentation](https://neftedollar.github.io/fyper/reference/age.html)
- [GitHub](https://github.com/Neftedollar/fyper)
