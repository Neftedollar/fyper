# Data Model: Fyper — F# Typed Cypher ORM

**Date**: 2026-04-02
**Feature**: 001-fyper-full-orm

## Existing Types (Phases 1-3, implemented)

### Schema.fs

| Type | Kind | Fields | Purpose |
|------|------|--------|---------|
| `LabelAttribute` | Attribute | `Name: string` | Override node/rel label |
| `CypherNameAttribute` | Attribute | `Name: string` | Override property name |
| `PropertyMeta` | Record | `FSharpName`, `CypherName`, `PropertyType`, `IsOption` | Single property metadata |
| `TypeMeta` | Record | `ClrType`, `Label`, `Properties: PropertyMeta list` | Full type metadata (cached) |

### Ast.fs

| Type | Kind | Cases/Fields | Purpose |
|------|------|--------------|---------|
| `Direction` | DU | `Outgoing`, `Incoming`, `Undirected` | Relationship direction |
| `PathLength` | DU | `Exactly`, `Between`, `AtLeast`, `AtMost`, `AnyLength` | Variable-length paths |
| `Pattern` | DU | `NodePattern`, `RelPattern`, `NamedPath` | Graph patterns |
| `Expr` | DU | `Literal`, `Param`, `Variable`, `Property`, `BinOp`, `UnaryOp`, `FuncCall`, `ListExpr`, `MapExpr`, `CaseExpr`, `ExistsSubquery`, `Null` | Cypher expressions |
| `BinOp` | DU | `Eq`, `Neq`, `Gt`, `Gte`, `Lt`, `Lte`, `And`, `Or`, `Xor`, `Contains`, `StartsWith`, `EndsWith`, `In`, `Add`, `Sub`, `Mul`, `Div`, `Mod`, `RegexMatch` | Binary operators |
| `UnaryOp` | DU | `Not`, `IsNull`, `IsNotNull`, `Exists` | Unary operators |
| `SortDirection` | DU | `Ascending`, `Descending` | ORDER BY direction |
| `ReturnItem` | Record | `Expr`, `Alias: string option` | RETURN/WITH items |
| `SetItem` | DU | `SetProperty`, `SetAllProperties`, `MergeProperties`, `AddLabel` | SET clause items |
| `RemoveItem` | DU | `RemoveProperty`, `RemoveLabel` | REMOVE clause items |
| `Clause` | DU | `Match`, `Where`, `Return`, `With`, `Create`, `Merge`, `Delete`, `Set`, `Remove`, `OrderBy`, `Skip`, `Limit`, `Unwind`, `Call`, `Union`, `RawCypher` | Cypher clauses |
| `CypherQuery<'T>` | Record | `Clauses: Clause list`, `Parameters: Map<string, obj>` | Typed query |
| `CypherQuery` | Record | `Clauses: Clause list`, `Parameters: Map<string, obj>` | Untyped query |

### GraphValue.fs

| Type | Kind | Cases/Fields | Purpose |
|------|------|--------------|---------|
| `GraphValue` | DU | `GNull`, `GBool`, `GInt`, `GFloat`, `GString`, `GList`, `GMap`, `GNode`, `GRel`, `GPath` | Universal result type |
| `GraphNode` | Record | `Id: int64`, `Labels: string list`, `Properties: Map<string, GraphValue>` | Graph node |
| `GraphRel` | Record | `Id: int64`, `RelType: string`, `StartNodeId`, `EndNodeId`, `Properties` | Relationship |
| `GraphPath` | Record | `Nodes: GraphNode list`, `Relationships: GraphRel list` | Path |
| `GraphRecord` | Record | `Keys: string list`, `Values: Map<string, GraphValue>` | Result row |

### Driver.fs

| Type | Kind | Members | Purpose |
|------|------|---------|---------|
| `IGraphDriver` | Interface | `ExecuteReadAsync`, `ExecuteWriteAsync`, `IAsyncDisposable` | Database abstraction |

## New Types (Phases 4-7, to implement)

### Driver.fs — Extensions

| Type | Kind | Fields/Members | Purpose |
|------|------|----------------|---------|
| `DriverCapabilities` | Record | `SupportsOptionalMatch: bool`, `SupportsMerge: bool`, `SupportsUnwind: bool`, `SupportsCase: bool`, `SupportsCallProcedure: bool`, `SupportsExistsSubquery: bool` | Compile-time capability declaration |
| `IGraphDriver` (extended) | Interface | + `BeginTransactionAsync() -> Task<IGraphTransaction>`, + `Capabilities: DriverCapabilities` | Transaction + capabilities |
| `IGraphTransaction` | Interface | `ExecuteReadAsync`, `ExecuteWriteAsync`, `CommitAsync`, `RollbackAsync`, `IAsyncDisposable` | Transaction scope |

### Cypher.fs — Extensions

| Function | Signature | Purpose |
|----------|-----------|---------|
| `inTransaction` | `IGraphDriver -> (IGraphTransaction -> Task<'T>) -> Task<'T>` | Explicit transaction wrapper |
| `toCypher` | `CypherQuery<'T> -> string * Map<string, obj>` | Query inspection (ToCypher) |

### Neo4jDriver.fs (new)

| Type | Kind | Fields | Purpose |
|------|------|--------|---------|
| `Neo4jDriver` | Class | `driver: IDriver` (Neo4j.Driver) | Neo4j IGraphDriver implementation |
| `Neo4jTransaction` | Class | `tx: IAsyncTransaction` | Neo4j IGraphTransaction implementation |

### AgeDriver.fs (new)

| Type | Kind | Fields | Purpose |
|------|------|--------|---------|
| `AgeDriver` | Class | `dataSource: NpgsqlDataSource`, `graphName: string` | AGE IGraphDriver implementation |
| `AgeTransaction` | Class | `conn: NpgsqlConnection`, `tx: NpgsqlTransaction`, `graphName: string` | AGE IGraphTransaction implementation |

### Exceptions (new, in Driver.fs or separate Exceptions.fs)

| Type | Base | Fields | Purpose |
|------|------|--------|---------|
| `FyperException` | `exn` | `Message`, `InnerException` | Base exception |
| `FyperConnectionException` | `FyperException` | — | Connection/auth failures |
| `FyperQueryException` | `FyperException` | `Query: string`, `Parameters: Map<string, obj>` | Query execution failures |
| `FyperMappingException` | `FyperException` | `TargetType: Type`, `SourceValue: GraphValue` | Result mapping failures |
| `FyperUnsupportedFeatureException` | `FyperException` | `Feature: string`, `Backend: string` | Capability check failures |

## Entity Relationships

```
CypherQuery<'T>
  ├── contains → Clause list
  │     ├── references → Pattern (Match, Create, Merge)
  │     ├── references → Expr (Where, Return, Set, OrderBy, Skip, Limit)
  │     └── references → SetItem / RemoveItem (Set, Remove)
  └── contains → Map<string, obj> (Parameters)

IGraphDriver
  ├── has → DriverCapabilities
  ├── creates → IGraphTransaction
  ├── accepts → (string * Map<string, obj>) from CypherCompiler
  └── returns → GraphRecord list → ResultMapper → 'T list

TypeMeta (cached per F# record type)
  └── contains → PropertyMeta list
```

## State Transitions

Fyper is primarily stateless (query builder + compiler). The only stateful components:

1. **ExprCompileState**: Mutable param counter during quotation compilation (scoped to single query build)
2. **IGraphDriver**: Connection lifecycle — created → active → disposed
3. **IGraphTransaction**: created → active → committed/rolled back → disposed
