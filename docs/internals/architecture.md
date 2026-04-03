---
layout: default
title: Architecture
parent: Internals
description: "Fyper architecture: CE to quotation to AST to Cypher string data flow."
nav_order: 1
---

# Architecture

Fyper is a typed Cypher query builder for F#. It uses computation expressions, F# quotations, and discriminated unions to produce compile-time safe graph database queries that work against Neo4j and Apache AGE backends.

## Data Flow

```
cypher { ... }          (1) F# computation expression
       |
       v
   Expr<CypherQuery<'T>>  (2) Auto-quoted expression tree (via Quote member)
       |
       v
   QueryTranslator.translate   (3) Walks the quotation, builds typed AST
       |
       v
   CypherQuery<'T>        (4) AST: Clause list + Parameters map
       |
       v
   CypherCompiler.compile  (5) AST -> Cypher string + parameterized values
       |
       v
   IGraphDriver            (6) Executes against Neo4j (Bolt) or AGE (SQL wrapper)
       |
       v
   GraphRecord list        (7) Raw results normalized to GraphValue DU
       |
       v
   ResultMapper.mapGraphRecord<'T>  (8) Maps GraphValue -> typed F# records/tuples
       |
       v
   'T list                 (9) Final typed result
```

### Step-by-step

1. The developer writes a `cypher { }` computation expression using `for`, `where`, `select`, `matchRel`, and other custom operations.

2. The `CypherBuilder.Quote` member intercepts the entire CE body, capturing it as an `Expr<CypherQuery<'T>>` expression tree instead of evaluating it. This is the key mechanism -- none of the CE members are ever called at runtime.

3. `CypherBuilder.Run` receives the quoted expression and delegates to `QueryTranslator.translate<'T>`, which recursively walks the quotation tree. It recognizes CE method calls (`For`, `Where`, `Select`, `MatchRel`, etc.) and converts them into `Clause` and `Expr` AST nodes.

4. The result is a `CypherQuery<'T>` containing a list of `Clause` discriminated union values and a `Map<string, obj>` of parameterized values. The phantom type `'T` carries the expected result type.

5. `CypherCompiler.compile` pattern-matches over every `Clause` and `Expr` case to produce a Cypher string. All literal values are replaced with parameter references (`$p0`, `$p1`, etc.) -- no values are ever inlined into the query string.

6. The compiled query is sent to an `IGraphDriver` implementation (Neo4j via Bolt protocol, or AGE via SQL `cypher()` wrapper on PostgreSQL).

7. The driver normalizes all results into `GraphValue` / `GraphRecord` types -- a universal representation that abstracts over backend-specific result formats.

8. `ResultMapper` converts `GraphValue` trees into typed F# records, tuples, or scalar values using reflection.

## Module Responsibility Map

### src/Fyper/ (Core Library)

| File | Module | Responsibility |
|---|---|---|
| `Schema.fs` | `Fyper.Schema` | Naming conventions and type metadata extraction. Converts PascalCase to camelCase (`toCypherName`), PascalCase to UPPER_SNAKE_CASE (`toRelType`), extracts `TypeMeta` from F# record types. Supports `[<Label>]` and `[<CypherName>]` attribute overrides. Results are cached in a `ConcurrentDictionary`. |
| `Ast.fs` | `Fyper.Ast` | The Cypher AST as discriminated unions. Defines `Pattern` (node/relationship/named path), `Expr` (literals, params, properties, binary/unary ops, function calls, CASE, EXISTS subquery), `Clause` (MATCH, WHERE, RETURN, WITH, CREATE, MERGE, DELETE, SET, REMOVE, ORDER BY, SKIP, LIMIT, UNWIND, CALL, UNION, RawCypher), and `CypherQuery<'T>`. Also provides a `Query` helper module for raw AST construction. |
| `GraphValue.fs` | `Fyper.GraphValue` | Universal graph result type. Defines `GraphValue` (GNull, GBool, GInt, GFloat, GString, GList, GMap, GNode, GRel, GPath), plus `GraphNode`, `GraphRel`, `GraphPath`, and `GraphRecord` types. All drivers normalize their backend-specific results into this type. |
| `Driver.fs` | `Fyper` | Driver interfaces and exception types. Defines `IGraphDriver` (ExecuteReadAsync, ExecuteWriteAsync, BeginTransactionAsync, Capabilities), `IGraphTransaction` (ExecuteReadAsync, ExecuteWriteAsync, CommitAsync, RollbackAsync), `DriverCapabilities` flags, and the exception hierarchy: `FyperException`, `FyperConnectionException`, `FyperQueryException`, `FyperMappingException`, `FyperUnsupportedFeatureException`. |
| `ExprCompiler.fs` | `Fyper.ExprCompiler` | Compiles F# quotation expressions into Cypher AST `Expr` nodes. Handles property access, comparison operators, arithmetic, logical AND/OR/NOT, string operations (Contains, StartsWith, EndsWith), aggregate functions (count, sum, avg, collect, min, max), CASE WHEN, variable references, and literal parameterization. Maintains mutable `ExprCompileState` for parameter indexing. |
| `CypherCompiler.fs` | `Fyper.CypherCompiler` | Compiles the Cypher AST into a Cypher query string. Exhaustive pattern matching over all `Expr`, `Pattern`, and `Clause` DU cases. Also validates `DriverCapabilities` against the query's clauses to reject unsupported features at build time. |
| `Operators.fs` | `Fyper.Operators` | Phantom types and quotation-only operators. Defines `NodeSource<'T>`, `EdgeType<'R>`, `PartialEdge`, `EdgePattern`, the `--` / `-->` edge operators, `node<'T>` / `optionalNode<'T>` sources, and aggregate function stubs (`count`, `sum`, `avg`, `collect`, `cypherMin`, `cypherMax`, `caseWhen`). All throw at runtime -- they exist only for type checking and quotation capture. |
| `CypherBuilder.fs` | `Fyper.CypherBuilder` | The `cypher { }` computation expression builder. Key members: `Quote` (auto-quotation), `Run` (delegates to QueryTranslator), `For` (MATCH), `Where`, `Select` (RETURN), `OrderBy`, `OrderByDescending`, `Skip`, `Limit`, `MatchRel`, `MatchPath`, `Delete`, `DetachDelete`, `Create`, `CreateRel`, `Set`, `Merge`, `OnMatch`, `OnCreate`, `SelectDistinct`, `Unwind`, `WithClause`. The global `cypher` instance is defined in the `CypherBuilderInstance` module. |
| `QueryTranslator.fs` | `Fyper.QueryTranslator` | Walks the quoted computation expression tree and builds a `CypherQuery<'T>`. Recognizes all CypherBuilder method calls by name, resolves CE variable aliases from lambda/let desugaring, compiles predicates via `ExprCompiler`, compiles projections to `ReturnItem` lists, handles edge pattern extraction, SET record-update expressions, MERGE with ON MATCH/ON CREATE, and path length extraction. |
| `ResultMapper.fs` | `Fyper.ResultMapper` | Maps `GraphRecord` results to typed F# values. Handles records (mapping property names via `Schema.toCypherName`), tuples (positional mapping from `Keys`), and scalar types. Supports nested records, option types, and list types. |
| `Cypher.fs` | `Fyper.Cypher` | Public execution API. Functions: `executeAsync` (read + map to typed results), `executeWriteAsync` (write + return affected count), `rawAsync` (escape hatch for raw Cypher), `toCypher` (inspect generated Cypher without executing), `toDebugString` (Cypher + parameters), `inTransaction` (scoped transaction with auto-commit/rollback). |

### src/Fyper.Parser/ (Cypher Parser)

| File | Module | Responsibility |
|---|---|---|
| `Lexer.fs` | `Fyper.Parser.Lexer` | Tokenizes Cypher strings. Hand-written lexer with keyword recognition, string/number/identifier/parameter scanning, and support for all Cypher operator symbols. |
| `Parser.fs` | `Fyper.Parser.CypherParser` | Recursive-descent parser. Parses token streams into the same `Fyper.Ast` types used by the query builder. Handles all Cypher clauses, expressions (with correct operator precedence), patterns with relationship chains, and variable-length paths. |

### src/Fyper.Neo4j/

| File | Module | Responsibility |
|---|---|---|
| `Neo4jDriver.fs` | `Fyper.Neo4j` | Neo4j Bolt driver. Implements `IGraphDriver` and `IGraphTransaction` using `Neo4j.Driver`. Includes `ValueMapper` module for converting Neo4j `INode`/`IRelationship`/`IPath` objects to `GraphValue`. Reports `DriverCapabilities.all`. |

### src/Fyper.Age/

| File | Module | Responsibility |
|---|---|---|
| `AgeDriver.fs` | `Fyper.Age` | Apache AGE driver. Implements `IGraphDriver` and `IGraphTransaction` using `Npgsql`. Includes `AgtypeParser` for parsing AGE's agtype JSON format and `CypherWrapper` for wrapping Cypher queries in AGE's SQL `cypher()` function call. Reports `DriverCapabilities.minimal`. |

## F# Compilation Order

F# requires files to be compiled in strict dependency order -- a file can only reference types and functions defined in files listed above it in the `.fsproj`. The Fyper compilation order is:

```
 1. Schema.fs        -- no dependencies (only FSharp.Core + System)
 2. Ast.fs           -- no dependencies
 3. GraphValue.fs    -- no dependencies
 4. Driver.fs        -- depends on GraphValue (for GraphRecord type)
 5. ExprCompiler.fs  -- depends on Ast, Schema
 6. CypherCompiler.fs -- depends on Ast, Driver (for capability validation)
 7. Operators.fs     -- depends on Ast (for PathLength type)
 8. QueryTranslator.fs -- depends on Ast, Schema, ExprCompiler
 9. CypherBuilder.fs -- depends on Ast, Operators, QueryTranslator
10. ResultMapper.fs  -- depends on GraphValue, Schema
11. Cypher.fs        -- depends on Ast, CypherCompiler, GraphValue, ResultMapper, Driver
```

This ordering means you cannot, for example, reference `CypherBuilder` from `Ast.fs`. It also means that adding a new file requires careful placement in the `.fsproj` item group.

## Key Design Decisions

### DU-Based AST

The entire Cypher language is represented as F# discriminated unions (`Clause`, `Expr`, `Pattern`, `BinOp`, `UnaryOp`, `SortDirection`, `Direction`, `PathLength`). This provides:

- **Exhaustive pattern matching**: The compiler ensures every AST node is handled. No wildcard catch-alls are used in `CypherCompiler`, so adding a new DU case forces updates everywhere it is consumed.
- **Composability**: Queries can be built programmatically via the `Query` module functions without using the CE.
- **Round-tripping**: The same AST is produced by both the CE query builder and the `Fyper.Parser`, enabling parse-transform-compile workflows.

### Quote Member for Auto-Quotation

The `CypherBuilder` uses the F# `Quote` CE member to automatically capture the entire CE body as a quotation (`Expr<CypherQuery<'T>>`). This means:

- Users write normal F# code inside `cypher { }` -- no explicit `<@ @>` quoting required.
- All CE members (`For`, `Where`, `Select`, etc.) are never actually called at runtime. They exist only for type-checking. Each one throws with `"quotation only"`.
- The `Run` member receives the quoted expression and passes it to `QueryTranslator.translate`, which walks the expression tree to build the AST.

This approach provides compile-time type safety (the F# compiler checks types, catches errors) while allowing runtime Cypher generation from the expression tree.

### Convention Over Configuration

Fyper maps F# types to Cypher labels and properties by convention:

- **Node labels**: F# type name as-is. `Person` becomes `:Person`.
- **Relationship types**: PascalCase to UPPER_SNAKE_CASE. `ActedIn` becomes `:ACTED_IN`.
- **Property names**: PascalCase to camelCase. `FirstName` becomes `firstName`.
- **Override via attributes**: `[<Label("CustomLabel")>]` on a type overrides the label. `[<CypherName("custom_name")>]` on a record field overrides the property name.
- **Variable aliases**: The CE variable name from `for p in node<Person>` becomes the Cypher alias `p`.

### Full Parameterization

All literal values in queries are extracted into parameters (`$p0`, `$p1`, etc.) -- never inlined into the Cypher string. This prevents injection, improves plan caching on the database, and is enforced by the `ExprCompiler` which assigns parameter names from a monotonically increasing counter.

### Capability Validation

Before executing a query, `CypherCompiler.validateCapabilities` checks the query's clauses against the driver's `DriverCapabilities`. If a query uses OPTIONAL MATCH but the driver does not support it (e.g., AGE), a `FyperUnsupportedFeatureException` is raised at construction time rather than at database execution time.

### Backend Abstraction via GraphValue

All drivers normalize their results into `GraphValue` -- a backend-agnostic DU representing nulls, booleans, integers, floats, strings, lists, maps, nodes, relationships, and paths. This means `ResultMapper` works identically regardless of whether the data came from Neo4j's Bolt protocol or AGE's agtype JSON format.

## Package Structure

Fyper ships as multiple NuGet packages:

| Package | Dependencies | Purpose |
|---|---|---|
| `Fyper` | FSharp.Core only | Core library: AST, CE builder, compiler, result mapper |
| `Fyper.Parser` | Fyper | Cypher string parser (zero external deps beyond Fyper) |
| `Fyper.Neo4j` | Fyper, Neo4j.Driver 5.x | Neo4j Bolt driver |
| `Fyper.Age` | Fyper, Npgsql 8.x | Apache AGE (PostgreSQL) driver |

All packages target `.NET 10.0`.

## See Also

- [CE Operations Reference](../reference/ce-operations.md) -- the user-facing DSL
- [Types Reference](../reference/types.md) -- AST types
- [Performance](performance.md) -- benchmark results
- [Parser](../guide/parser.md) -- reverse flow: Cypher string to AST
