---
layout: default
title: "Fyper — Type-safe Cypher ORM for F#"
description: "F# computation expression query builder for Neo4j and Apache AGE graph databases. Zero dependencies, compile-time safety, parameterized by default."
nav_order: 1
---

<script type="application/ld+json">
{
  "@context": "https://schema.org",
  "@type": "SoftwareSourceCode",
  "name": "Fyper",
  "description": "Type-safe Cypher query builder for F#. Computation expressions, parameterized by default, multi-backend (Neo4j, Apache AGE).",
  "programmingLanguage": "F#",
  "codeRepository": "https://github.com/Neftedollar/fyper",
  "license": "https://opensource.org/licenses/MIT",
  "author": {
    "@type": "Person",
    "name": "Roman Melnikov",
    "url": "https://github.com/Neftedollar"
  },
  "keywords": ["F#", "Cypher", "Neo4j", "Apache AGE", "graph database", "ORM", "query builder", "computation expression", "type-safe"],
  "operatingSystem": "Cross-platform",
  "applicationCategory": "DeveloperApplication"
}
</script>

# Fyper

Type-safe Cypher queries in F#.

```fsharp
type Person = { Name: string; Age: int }
type ActedIn = { Roles: string list }

let findActors = cypher {
    for p in node<Person> do
    for m in node<Movie> do
    matchRel (p -- edge<ActedIn> --> m)
    where (p.Age > 30)
    select (p.Name, m.Title)
}
// MATCH (p:Person) MATCH (m:Movie) MATCH (p)-[:ACTED_IN]->(m)
// WHERE p.age > $p0 RETURN p.name, m.title
```

## Install

```bash
dotnet add package Fyper              # Core
dotnet add package Fyper.Neo4j        # Neo4j driver
dotnet add package Fyper.Age          # Apache AGE driver
dotnet add package Fyper.Parser       # Cypher parser
```

## Guide

- [Getting Started](guide/getting-started.md) -- first query in 5 minutes
- [Relationships](guide/relationships.md) -- matchRel, OPTIONAL MATCH, paths
- [Mutations](guide/mutations.md) -- CREATE, SET, DELETE, MERGE
- [Transactions](guide/transactions.md) -- atomic multi-query operations
- [Parser](guide/parser.md) -- parse Cypher strings into AST

## Reference

- [CE Operations](reference/ce-operations.md) -- all operations at a glance
- [Functions](reference/functions.md) -- Cypher module API
- [Types](reference/types.md) -- AST, GraphValue, exceptions
- [Neo4j Driver](reference/neo4j.md) -- setup and usage
- [AGE Driver](reference/age.md) -- PostgreSQL + AGE setup

## Internals

- [Architecture](internals/architecture.md) -- data flow and modules
- [Performance](internals/performance.md) -- benchmarks

## Packages

| Package | Deps | Description |
|---------|------|-------------|
| `Fyper` | FSharp.Core | Query builder + compiler |
| `Fyper.Parser` | Fyper | Cypher string parser |
| `Fyper.Neo4j` | Neo4j.Driver | Neo4j Bolt driver |
| `Fyper.Age` | Npgsql | Apache AGE driver |
