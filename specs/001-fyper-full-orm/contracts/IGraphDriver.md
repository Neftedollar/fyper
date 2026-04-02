# Contract: IGraphDriver Interface

**Date**: 2026-04-02
**Feature**: 001-fyper-full-orm

## Interface Definition

```fsharp
/// Abstract graph database driver.
/// Each backend (Neo4j, AGE, etc.) implements this interface.
type IGraphDriver =
    /// Execute a read query. Returns a list of result records.
    abstract ExecuteReadAsync:
        cypher: string *
        parameters: Map<string, obj>
        -> Task<GraphRecord list>

    /// Execute a write query. Returns the count of affected entities.
    abstract ExecuteWriteAsync:
        cypher: string *
        parameters: Map<string, obj>
        -> Task<int>

    /// Begin an explicit transaction for multi-statement atomicity.
    abstract BeginTransactionAsync: unit -> Task<IGraphTransaction>

    /// Declare which Cypher features this backend supports.
    abstract Capabilities: DriverCapabilities

    inherit IAsyncDisposable

/// Transaction scope — same read/write interface, scoped to a transaction.
type IGraphTransaction =
    abstract ExecuteReadAsync:
        cypher: string *
        parameters: Map<string, obj>
        -> Task<GraphRecord list>

    abstract ExecuteWriteAsync:
        cypher: string *
        parameters: Map<string, obj>
        -> Task<int>

    abstract CommitAsync: unit -> Task<unit>
    abstract RollbackAsync: unit -> Task<unit>

    inherit IAsyncDisposable
```

## DriverCapabilities

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

## Backend Capability Matrix

| Feature | Neo4j | Apache AGE | Memgraph (Tier 2) |
|---------|-------|------------|-------------------|
| MATCH | yes | yes | yes |
| WHERE | yes | yes | yes |
| RETURN | yes | yes | yes |
| CREATE | yes | yes | yes |
| DELETE / DETACH DELETE | yes | yes | yes |
| SET | yes | yes | yes |
| OPTIONAL MATCH | yes | no | yes |
| MERGE + ON MATCH/CREATE | yes | partial | yes |
| UNWIND | yes | no | yes |
| CASE | yes | no | yes |
| CALL procedure | yes | no | yes |
| EXISTS subquery | yes | no | yes |
| Named paths | yes | no | yes |
| Variable-length paths | yes | yes | yes |
| ORDER BY / SKIP / LIMIT | yes | yes | yes |

## Public API (Cypher module)

```fsharp
module Cypher =
    /// Execute a read query, return typed results.
    val executeAsync: IGraphDriver -> CypherQuery<'T> -> Task<'T list>

    /// Execute a write query, return affected count.
    val executeWriteAsync: IGraphDriver -> CypherQuery<'T> -> Task<int>

    /// Execute within an explicit transaction.
    val inTransaction: IGraphDriver -> (IGraphTransaction -> Task<'T>) -> Task<'T>

    /// Inspect generated Cypher without executing.
    val toCypher: CypherQuery<'T> -> string * Map<string, obj>

    /// Execute raw Cypher string.
    val rawAsync: IGraphDriver -> string -> Map<string, obj> -> Task<GraphRecord list>
```

## Error Contract

All public API functions may throw:

| Exception | When | Contains |
|-----------|------|----------|
| `FyperConnectionException` | Connection refused, auth failure, timeout | Inner exception from driver |
| `FyperQueryException` | Cypher syntax error, constraint violation | Query text, parameters |
| `FyperMappingException` | Result doesn't match target F# type | Target type, source GraphValue |
| `FyperUnsupportedFeatureException` | Query uses feature not in driver capabilities | Feature name, backend name |
