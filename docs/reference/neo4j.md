---
layout: default
title: Neo4j Driver
parent: Reference
description: "Neo4j Bolt driver for Fyper. Connection setup, value mapping, transactions."
nav_order: 4
---

# Neo4j Driver

## Install

```bash
dotnet add package Fyper.Neo4j
```

## Setup

```fsharp
open Fyper.Neo4j

let driver = new Neo4jDriver(
    Neo4j.Driver.GraphDatabase.Driver(
        "bolt://localhost:7687",
        Neo4j.Driver.AuthTokens.Basic("neo4j", "password")))
```

The driver wraps an existing `Neo4j.Driver.IDriver`. Connection pooling is managed by the underlying Neo4j driver.

## Usage

```fsharp
task {
    let! people = query |> Cypher.executeAsync driver
    let! count = mutation |> Cypher.executeWriteAsync driver
}
```

## Capabilities

All Cypher features supported: `DriverCapabilities.all`.

## Value Mapping

| Neo4j Type | GraphValue |
|-----------|------------|
| `INode` | `GNode` |
| `IRelationship` | `GRel` |
| `IPath` | `GPath` |
| `bool` | `GBool` |
| `int64` | `GInt` |
| `double` | `GFloat` |
| `string` | `GString` |
| `null` | `GNull` |
| list | `GList` |
| map | `GMap` |

## Dispose

```fsharp
do! (driver :> IAsyncDisposable).DisposeAsync()
```

Using a disposed driver throws `FyperConnectionException`.
