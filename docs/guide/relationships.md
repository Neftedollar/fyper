---
layout: default
title: Relationships
description: "Graph relationships in Fyper: matchRel, OPTIONAL MATCH, variable-length paths, CREATE relationship."
nav_order: 4
---

# Relationships

Graph traversal patterns using typed edge operators.

## Basic Relationship Match

```fsharp
type Person = { Name: string; Age: int }
type Movie = { Title: string; Released: int }
type ActedIn = { Roles: string list }

let query = cypher {
    for p in node<Person> do
    for m in node<Movie> do
    matchRel (p -- edge<ActedIn> --> m)
    where (p.Age > 30)
    select (p.Name, m.Title)
}
// MATCH (p:Person) MATCH (m:Movie)
// MATCH (p)-[:ACTED_IN]->(m)
// WHERE p.age > $p0 RETURN p.name, m.title
```

The relationship type is extracted from the F# type name and converted to UPPER_SNAKE_CASE: `ActedIn` becomes `ACTED_IN`.

## OPTIONAL MATCH

Use `optionalNode<T>` to produce OPTIONAL MATCH. Results may be null when no match is found:

```fsharp
let query = cypher {
    for p in node<Person> do
    for m in optionalNode<Movie> do
    matchRel (p -- edge<ActedIn> --> m)
    select (p, m)
}
// MATCH (p:Person) OPTIONAL MATCH (m:Movie)
// MATCH (p)-[:ACTED_IN]->(m) RETURN p, m
```

## Variable-Length Paths

Use `matchPath` with a `PathLength` specifier for traversals:

```fsharp
// Friends within 1-5 hops
let query = cypher {
    for p in node<Person> do
    for q in node<Person> do
    matchPath (p -- edge<Knows> --> q) (Between(1, 5))
    select (p.Name, q.Name)
}
// MATCH (p)-[:KNOWS*1..5]->(q)
```

Available path lengths:
- `Between(min, max)` -- `*1..5`
- `Exactly n` -- `*3`
- `AtLeast n` -- `*2..`
- `AtMost n` -- `*..5`
- `AnyLength` -- `*`

## Create Relationship

Use `createRel` to create relationships between existing nodes:

```fsharp
let query = cypher {
    for p in node<Person> do
    for m in node<Movie> do
    where (p.Name = "Tom")
    createRel (p -- edge<ActedIn> --> m)
}
// MATCH (p:Person) MATCH (m:Movie)
// WHERE p.name = $p0
// CREATE (p)-[:ACTED_IN]->(m)
```

## Incoming & Undirected

```fsharp
// Incoming: (p)<-[:DIRECTED]-(m)
matchRel (p -- edgeIn<Directed> --> m)

// Undirected: (p)-[:KNOWS]-(q)
matchRel (p -- edgeUn<Knows> --> q)
```

`edge<R>` = outgoing (default), `edgeIn<R>` = incoming, `edgeUn<R>` = undirected.

## EXISTS Subquery

Check if a relationship exists in WHERE:

```fsharp
let q = cypher {
    for p in node<Person> do
    for m in node<Movie> do
    where (existsRel (p -- edge<ActedIn> --> m))
    select p
}
// WHERE EXISTS { MATCH (p)-[]->(m) }
```

## Naming Conventions

| F# Type | Cypher Type | Convention |
|---------|-------------|------------|
| `ActedIn` | `ACTED_IN` | UPPER_SNAKE_CASE |
| `Knows` | `KNOWS` | Single word stays uppercase |
| `PartOf` | `PART_OF` | Split on PascalCase boundaries |
| `[<Label "FRIEND_OF">] type FriendOf` | `FRIEND_OF` | Custom via attribute |

## See Also

- [Getting Started](getting-started.md) -- first query in 5 minutes
- [CE Operations Reference](../reference/ce-operations.md) -- full operation table
- [Mutations](mutations.md) -- CREATE, SET, DELETE
- [Architecture](../internals/architecture.md) -- how edge patterns are compiled
