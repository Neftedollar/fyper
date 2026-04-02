# Research: Fyper — F# Typed Cypher ORM

**Date**: 2026-04-02
**Feature**: 001-fyper-full-orm

## Neo4j .NET Driver Integration

**Decision**: Use `Neo4j.Driver` NuGet package (official Bolt driver)

**Rationale**:
- Official, maintained by Neo4j team
- Async-first API matches F# `task { }` pattern
- Connection pooling built-in via `IDriver`
- Transaction support via `IAsyncSession.BeginTransactionAsync()`
- Results as `IRecord` with `Values` dictionary → map to `GraphValue`

**Alternatives considered**:
- Raw Bolt protocol implementation — rejected (massive effort, no benefit)
- Community Neo4jClient — rejected (LINQ-focused, heavier, less maintained)

**Key integration points**:
- `IDriver` → wraps as `IGraphDriver`
- `IRecord.Values` → convert to `Map<string, GraphValue>` → `GraphRecord`
- Node: `INode.Properties`, `INode.Labels` → `GNode`
- Relationship: `IRelationship.Properties`, `IRelationship.Type` → `GRel`
- Path: `IPath.Nodes`, `IPath.Relationships` → `GPath`
- Transaction: `IAsyncSession.BeginTransactionAsync()` → `IAsyncTransaction`

## Apache AGE PostgreSQL Integration

**Decision**: Use `Npgsql` (8.*) with raw SQL wrapping for AGE Cypher

**Rationale**:
- AGE has no official .NET client — must wrap via SQL
- Npgsql is the de facto PostgreSQL driver for .NET
- AGE Cypher runs via `SELECT * FROM cypher('graph_name', $$ CYPHER $$) AS (col agtype)`
- Agtype results returned as text (JSON-like format) requiring custom parsing

**Alternatives considered**:
- Custom AGE wire protocol — rejected (AGE uses PostgreSQL protocol, no separate wire format)
- Entity Framework + AGE — rejected (too heavy, wrong abstraction level)

**Key integration points**:
- Connection init: `LOAD 'age'; SET search_path = ag_catalog, "$user", public;`
- Query wrapping: `SELECT * FROM cypher('{graph}', $$ {cypher} $$) AS ({return_aliases} agtype)`
- Return aliases: must be derived from RETURN clause items in the AST
- Agtype parsing: JSON-like text → parse to `GraphValue` tree
- Parameters: AGE doesn't support `$param` directly in `$$` blocks — values must be inlined or passed via `cypher()` function args (research needed on AGE parameter binding)
- Transaction: standard PostgreSQL `BEGIN`/`COMMIT`/`ROLLBACK` via `NpgsqlTransaction`

**Critical gap**: AGE parameter binding. AGE's `cypher()` SQL function uses `$$` dollar-quoting, which means Cypher `$param` references don't map to PostgreSQL `@param`. Options:
1. Pass parameters as `cypher()` function arguments (AGE 1.4+ supports this)
2. Inline parameterized values safely (defeats the parameterization principle)
3. Use PostgreSQL `$N` parameter syntax mapped to AGE arguments

**Resolution**: Use AGE's argument passing: `SELECT * FROM cypher('g', $$ MATCH (n) WHERE n.age > $1 $$, $1) AS (n agtype)` — AGE supports positional `$N` parameters mapped to function arguments.

## Compile-Time Capability Flags

**Decision**: Use F# static type constraints or marker interfaces on driver types to encode supported features

**Rationale**:
- Per clarification: unsupported features must be rejected at compile time, not runtime
- F# supports `IRequiresOptionalMatch`, `ISupportsUnwind` marker interfaces on driver types
- Query execution functions can constrain: `executeAsync<'Driver when 'Driver :> ISupportsOptionalMatch>`
- Alternative: feature flag enum checked at query build time (simpler but runtime)

**Tradeoff**: Full compile-time enforcement via type constraints is complex (many combinations). Practical approach:
- Core Cypher features (MATCH, WHERE, RETURN, CREATE, DELETE) — universal, no flags needed
- Advanced features (OPTIONAL MATCH, MERGE, UNWIND, CASE) — capability DU on driver, checked at `Cypher.compile` time
- Compile-time means: checked when building the `CypherQuery`, before execution — not literally F# type system enforcement of every clause combination

**Resolution**: `DriverCapabilities` DU set on each `IGraphDriver`. `CypherCompiler.compile` validates query clauses against capabilities. Error at query construction time (before any I/O), which is effectively "compile time" for the user's workflow.

## Testcontainers for Neo4j

**Decision**: Use `Testcontainers.Neo4j` NuGet package

**Rationale**:
- Official Testcontainers support for .NET
- Auto-starts Neo4j Community Edition in Docker
- Provides connection string for `Neo4j.Driver`
- Cleanup automatic on dispose

**Integration**:
```fsharp
let neo4jContainer = Neo4jBuilder().Build()
// Start in test setup, get bolt URI, create Neo4jDriver, run tests, dispose
```

## Docker Compose for AGE

**Decision**: Minimal `docker-compose.yml` at repo root with `apache/age` image

**Rationale**:
- No Testcontainers package for AGE
- Constitution requires "one command" test setup
- `docker compose up -d` starts PostgreSQL+AGE, tests connect to localhost

**docker-compose.yml structure**:
```yaml
services:
  age:
    image: apache/age:latest
    ports: ["5432:5432"]
    environment:
      POSTGRES_USER: test
      POSTGRES_PASSWORD: test
      POSTGRES_DB: testdb
```

## Transaction API Design

**Decision**: `Cypher.inTransaction : IGraphDriver -> (IGraphTransaction -> Task<'T>) -> Task<'T>`

**Rationale**:
- Functional-style: pass a function that receives a transaction context
- Auto-commit on success, auto-rollback on exception
- Maps cleanly to both Neo4j sessions and PostgreSQL transactions
- No need for manual `begin`/`commit`/`rollback`

**IGraphTransaction**: extends `IGraphDriver` with same `ExecuteReadAsync`/`ExecuteWriteAsync` — queries within the function use the transaction automatically.

## Error Handling

**Decision**: Typed exception hierarchy rooted at `FyperException`

**Exceptions**:
- `FyperConnectionException` — connection/auth failures
- `FyperQueryException` — Cypher syntax errors, constraint violations
- `FyperMappingException` — result mapping failures (type mismatch)
- `FyperUnsupportedFeatureException` — feature not supported by backend (capability check)

**Rationale**: Thin wrappers around underlying driver exceptions with added context (query text, parameters). Inner exception preserved for debugging.
