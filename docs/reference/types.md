---
layout: default
title: Types
parent: Reference
description: "Fyper types: CypherQuery, IGraphDriver, DriverCapabilities, GraphValue, exceptions."
nav_order: 3
---

# Types

Core types in the Fyper library.

## CypherQuery<'T>

A compiled query with phantom result type.

```fsharp
type CypherQuery<'T> = {
    Clauses: Clause list
    Parameters: Map<string, obj>
}
```

## IGraphDriver

Database abstraction implemented by each backend.

```fsharp
type IGraphDriver =
    inherit IAsyncDisposable
    abstract ExecuteReadAsync: cypher: string * parameters: Map<string, obj> -> Task<GraphRecord list>
    abstract ExecuteWriteAsync: cypher: string * parameters: Map<string, obj> -> Task<int>
    abstract BeginTransactionAsync: unit -> Task<IGraphTransaction>
    abstract Capabilities: DriverCapabilities
```

## DriverCapabilities

Declares which Cypher features a backend supports.

```fsharp
type DriverCapabilities = {
    SupportsOptionalMatch: bool
    SupportsMerge: bool
    SupportsUnwind: bool
    SupportsCase: bool
    SupportsCallProcedure: bool
    SupportsExistsSubquery: bool
    SupportsNamedPaths: bool
}
```

Predefined: `DriverCapabilities.all` (Neo4j), `DriverCapabilities.minimal` (AGE).

## Exceptions

| Exception | When |
|-----------|------|
| `FyperConnectionException` | Connection refused, auth failure |
| `FyperQueryException` | Cypher syntax error (includes query + params) |
| `FyperMappingException` | Result doesn't match F# type (includes target type + source value) |
| `FyperUnsupportedFeatureException` | Backend doesn't support a feature (includes feature + backend name) |

All inherit from `FyperException`.

## Schema Attributes

```fsharp
[<Label "CUSTOM_LABEL">]
type MyNode = { ... }

type MyNode = { [<CypherName "custom_name">] FieldName: string }
```
