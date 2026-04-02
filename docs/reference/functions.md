---
layout: default
title: Functions
parent: Reference
nav_order: 2
---

# Cypher Module Functions

Functions in `Fyper.Cypher` for executing and inspecting queries.

## executeAsync

Execute a read query, return typed results.

```fsharp
val executeAsync : IGraphDriver -> CypherQuery<'T> -> Task<'T list>

let! people = query |> Cypher.executeAsync driver
```

## executeWriteAsync

Execute a write query, return affected entity count.

```fsharp
val executeWriteAsync : IGraphDriver -> CypherQuery<'T> -> Task<int>

let! count = createQuery |> Cypher.executeWriteAsync driver
```

## toCypher

Inspect generated Cypher without executing.

```fsharp
val toCypher : CypherQuery<'T> -> string * Map<string, obj>

let cypherString, parameters = query |> Cypher.toCypher
```

## inTransaction

Execute multiple queries atomically. Auto-commits on success, auto-rollbacks on exception.

```fsharp
val inTransaction : IGraphDriver -> (IGraphTransaction -> Task<'T>) -> Task<'T>

let! result = Cypher.inTransaction driver (fun tx -> task {
    let! _ = q1 |> Cypher.executeWriteAsync tx
    let! _ = q2 |> Cypher.executeWriteAsync tx
    return 2
})
```

## rawAsync

Execute a raw Cypher string (escape hatch).

```fsharp
val rawAsync : IGraphDriver -> string -> Map<string, obj> -> Task<GraphRecord list>

let! records = Cypher.rawAsync driver "MATCH (n) RETURN count(n) AS cnt" Map.empty
```

## toDebugString

Compile query to a human-readable debug string with parameters.

```fsharp
val toDebugString : CypherQuery<'T> -> string

printfn "%s" (Cypher.toDebugString query)
```
