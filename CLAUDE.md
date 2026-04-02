# Fyper — F# Cypher ORM

Typed query builder for Cypher-based graph databases. Leverages F# discriminated unions, computation expressions, and quotations for compile-time safe graph queries.

## Architecture

Full design spec: `docs/DESIGN.md`

Data flow: `cypher { } CE → (Quote) → Expr tree → QueryTranslator → AST (DUs) → CypherCompiler → Cypher string + params → IGraphDriver → GraphValue → ResultMapper → F# records`

## Project Layout

```
src/Fyper/           ← Main library (one NuGet: Core + DSL + Compiler)
src/Fyper.Neo4j/     ← Neo4j Bolt driver
src/Fyper.Age/       ← Apache AGE (PostgreSQL) driver
tests/Fyper.Tests/   ← Unit tests (Expecto)
tests/Fyper.Integration.Tests/  ← Integration tests (Testcontainers)
```

## F# Compilation Order (Fyper.fsproj)

F# requires strict top-down ordering. Dependencies flow downward:

1. Schema.fs — naming conventions, metadata extraction
2. Ast.fs — Cypher AST as discriminated unions
3. GraphValue.fs — universal graph result type
4. Driver.fs — IGraphDriver interface
5. ExprCompiler.fs — F# quotations → Cypher AST Expr
6. CypherCompiler.fs — AST → Cypher string + params
7. Operators.fs — edge pattern operators (quotation-only, throw at runtime)
8. CypherBuilder.fs — CE builder with Quote member
9. QueryTranslator.fs — walks quoted CE → builds CypherQuery AST
10. ResultMapper.fs — GraphValue → typed F# records
11. Cypher.fs — public execution API

## Implementation Rules

- **Phased delivery**: Phase 1 (Core+Compiler) → Phase 2 (ExprCompiler) → Phase 3 (DSL/CE) → Phase 4 (Neo4j) → Phase 5 (AGE) → Phase 6 (Mutations) → Phase 7 (Advanced)
- **Each phase must have passing tests before starting the next**
- **Zero external deps** in main Fyper library (only FSharp.Core)
- **All values parameterized**: literals become `$p0`, `$p1` etc., never inlined into Cypher strings
- **Exhaustive pattern matching**: compiler must handle all DU cases — no wildcard catch-alls on Clause/Expr/Pattern
- **Convention over configuration**: type name = label, PascalCase field → camelCase property

## Coding Conventions

- Idiomatic F#: prefer `|>` pipelines, `match` expressions, `module` over `class`
- Records over classes for data types
- DUs over enums/inheritance for sum types
- `task { }` for async (not `async { }`)
- No mutable state except in ExprCompiler.ExprCompileState and TranslateState.ParamCounter
- Module functions over instance methods where possible
- Tests use Expecto `test`, `testList`, `testTask` syntax

## Target

- .NET 10.0, F# 8, latest language version

## Active Technologies
- F# 8, .NET 10.0 (net10.0), latest language version (001-fyper-full-orm)
- Neo4j (Bolt protocol), PostgreSQL + Apache AGE extension (001-fyper-full-orm)

## Recent Changes
- 001-fyper-full-orm: Added F# 8, .NET 10.0 (net10.0), latest language version
