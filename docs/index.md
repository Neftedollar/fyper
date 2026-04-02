---
layout: default
title: Home
nav_order: 1
---

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
// MATCH (p:Person) WHERE p.age >= $p0 ORDER BY p.name RETURN p
```

## Features

- **Zero boilerplate schema** -- F# records are your graph schema
- **Compile-time safety** -- quotation-based CE catches errors before runtime
- **Parameterized by default** -- all values become `$p0`, `$p1`
- **Multi-backend** -- Neo4j and Apache AGE from the same query
- **Zero dependencies** -- core library needs only FSharp.Core
- **Cypher parser** -- parse Cypher strings back into typed AST
- **Fast** -- sub-microsecond compilation

## Quick Install

```bash
dotnet add package Fyper
dotnet add package Fyper.Neo4j    # or Fyper.Age
```

## Documentation

- [Getting Started](getting-started.md) -- first query in 5 minutes
- [CE Reference](ce-reference.md) -- all computation expression operations
- [Relationships](relationships.md) -- matchRel, matchPath, createRel
- [Mutations](mutations.md) -- CREATE, SET, DELETE, MERGE
- [Parser](parser.md) -- parse Cypher strings into AST
- [Drivers](drivers.md) -- Neo4j and Apache AGE setup
- [Architecture](architecture.md) -- internal design and data flow
- [Performance](performance.md) -- benchmarks

## Packages

| Package | Description | Dependencies |
|---------|-------------|--------------|
| `Fyper` | Core query builder + compiler | FSharp.Core only |
| `Fyper.Parser` | Cypher string parser | Fyper only |
| `Fyper.Neo4j` | Neo4j Bolt driver | Neo4j.Driver |
| `Fyper.Age` | Apache AGE driver | Npgsql |
