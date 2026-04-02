# Implementation Plan: Fyper — F# Typed Cypher ORM

**Branch**: `001-fyper-full-orm` | **Date**: 2026-04-02 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-fyper-full-orm/spec.md`

## Summary

Complete the Fyper F# Cypher ORM across remaining phases (4-7): Neo4j Bolt driver, Apache AGE PostgreSQL driver, mutation CE operations (CREATE/MERGE/SET/DELETE), advanced Cypher features (variable-length paths, UNWIND, CASE, aggregations), compile-time backend capability validation, explicit transaction support, and comprehensive property-based + integration test suites.

Phases 1-3 (Core AST, ExprCompiler, DSL/CE) are already implemented with 92 passing unit tests.

## Technical Context

**Language/Version**: F# 8, .NET 10.0 (net10.0), latest language version
**Primary Dependencies**:
- Core `Fyper`: FSharp.Core only (zero external deps)
- `Fyper.Neo4j`: Neo4j.Driver (latest)
- `Fyper.Age`: Npgsql (8.*)
- Tests: Expecto (10.*), Expecto.FsCheck (10.*), Testcontainers.Neo4j
**Storage**: Neo4j (Bolt protocol), PostgreSQL + Apache AGE extension
**Testing**: Expecto + FsCheck (unit/property), Testcontainers (Neo4j integration), docker-compose (AGE integration)
**Target Platform**: .NET 10.0, cross-platform (Windows/Linux/macOS)
**Project Type**: Library (3 NuGet packages: Fyper, Fyper.Neo4j, Fyper.Age)
**Performance Goals**: Deferred to benchmarking phase — no hard targets yet
**Constraints**: Zero external deps in core library; all values parameterized; exhaustive DU matching
**Scale/Scope**: Library targeting F# developers working with graph databases

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Evidence |
|-----------|--------|----------|
| I. Pure F# / Idiomatic Functional | PASS | All code F#, DUs, pipelines, modules, `task { }` |
| II. Zero External Dependencies | PASS | Core Fyper has zero NuGet refs; drivers have minimal deps |
| III. Test-First (NON-NEGOTIABLE) | PASS | 92 tests passing for Phases 1-3; new phases require tests first |
| IV. Property-Based Testing | PASS | FsCheck already in test project; property tests for parameterization, naming, AST compilation |
| V. Integration Testing | PENDING | Neo4j: Testcontainers; AGE: docker-compose — to be implemented in Phases 4-5 |
| VI. Multi-Backend Coverage | PENDING | Neo4j + AGE drivers not yet implemented; IGraphDriver interface ready |
| VII. Developer Experience First | PASS | Convention-based schema, Quote member auto-quotation, zero-ceremony CE |
| VIII. Parameterized Safety | PASS | All literals parameterized; property tests verify invariant |

**Gate result**: PASS — no violations. PENDING items are implementation targets, not principle violations.

## Project Structure

### Documentation (this feature)

```text
specs/001-fyper-full-orm/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── IGraphDriver.md
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── Fyper/                         ← Core library (existing, Phases 1-3 done)
│   ├── Schema.fs
│   ├── Ast.fs
│   ├── GraphValue.fs
│   ├── Driver.fs
│   ├── ExprCompiler.fs
│   ├── CypherCompiler.fs
│   ├── Operators.fs
│   ├── CypherBuilder.fs
│   ├── QueryTranslator.fs
│   ├── ResultMapper.fs
│   └── Cypher.fs
├── Fyper.Neo4j/                   ← Phase 4: Neo4j Bolt driver
│   ├── Fyper.Neo4j.fsproj
│   └── Neo4jDriver.fs
└── Fyper.Age/                     ← Phase 5: AGE PostgreSQL driver
    ├── Fyper.Age.fsproj
    └── AgeDriver.fs

tests/
├── Fyper.Tests/                   ← Unit + property tests (existing + new)
│   ├── SchemaTests.fs
│   ├── CompilerTests.fs
│   ├── ExprCompilerTests.fs
│   ├── DslTests.fs
│   ├── PropertyTests.fs           ← NEW: FsCheck property-based tests
│   ├── MutationTests.fs           ← NEW: Phase 6 mutation CE tests
│   ├── AdvancedTests.fs           ← NEW: Phase 7 advanced Cypher tests
│   └── Program.fs
└── Fyper.Integration.Tests/       ← Integration tests (new)
    ├── Fyper.Integration.Tests.fsproj
    ├── Neo4jTests.fs
    ├── AgeTests.fs
    └── Program.fs

docker-compose.yml                 ← AGE integration test environment
```

**Structure Decision**: Multi-project F# solution with strict compilation order in each fsproj. Core library is standalone; each driver is a separate NuGet package with minimal dependencies. Integration tests are a separate project to avoid pulling DB dependencies into unit tests.

## Complexity Tracking

> No constitution violations to justify.
