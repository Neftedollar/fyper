<!--
  Sync Impact Report
  ==================
  Version change: (new) → 1.0.0
  Added principles:
    - I. Pure F# / Idiomatic Functional
    - II. Zero External Dependencies
    - III. Test-First (NON-NEGOTIABLE)
    - IV. Property-Based Testing (NON-NEGOTIABLE)
    - V. Integration Testing
    - VI. Multi-Backend Coverage
    - VII. Developer Experience First
    - VIII. Parameterized Safety
  Added sections:
    - Technology Stack & Constraints
    - Development Workflow
    - Governance
  Removed sections: none (initial version)
  Templates requiring updates:
    - .specify/templates/plan-template.md — ⚠ pending (update Technical Context defaults for F#/.NET 8)
    - .specify/templates/spec-template.md — ✅ compatible (no changes needed)
    - .specify/templates/tasks-template.md — ⚠ pending (tests are mandatory per constitution, override "OPTIONAL" wording)
  Follow-up TODOs: none
-->

# Fyper Constitution

## Core Principles

### I. Pure F# / Idiomatic Functional

All code MUST be written in F#. No C# projects, no mixed-language solutions.

- Prefer `|>` pipelines, `match` expressions, `module` over `class`
- Records over classes for data types
- Discriminated unions over enums/inheritance for sum types
- `task { }` for async (not `async { }`)
- Module functions over instance methods where possible
- No mutable state except where explicitly justified (ExprCompileState, ParamCounter)
- Exhaustive pattern matching on all DU cases — no wildcard catch-alls on core types (Clause/Expr/Pattern)

### II. Zero External Dependencies

The main `Fyper` library MUST depend only on `FSharp.Core`. No third-party NuGet packages in the core.

- Driver packages (`Fyper.Neo4j`, `Fyper.Age`) MAY depend on their respective client libraries
- Test projects MAY use any necessary packages (Expecto, FsCheck, Testcontainers, etc.)
- This constraint ensures the core library remains lightweight, portable, and free of transitive dependency risks

### III. Test-First (NON-NEGOTIABLE)

Every feature and every phase MUST have passing tests before proceeding to the next phase.

- Tests use Expecto `test`, `testList`, `testTask` syntax
- Red-Green-Refactor cycle: write failing test → implement → pass → refactor
- No phase transition without green tests for the current phase
- Unit tests cover all compiler output, AST construction, and query translation
- Every public API function MUST have at least one test

### IV. Property-Based Testing (NON-NEGOTIABLE)

FsCheck property-based tests MUST accompany unit tests for all non-trivial logic.

- AST → Cypher compilation MUST have roundtrip / invariant properties
- Query parameterization MUST be verified: no literal values leak into Cypher strings
- Schema convention mapping (PascalCase → camelCase, type → label) MUST have property tests
- ExprCompiler quotation translation MUST have generator-based coverage
- Property tests live alongside unit tests in `tests/Fyper.Tests/`

### V. Integration Testing

Integration tests MUST verify actual database interaction for every supported backend.

- Use Testcontainers when a well-maintained image exists (Neo4j has official Testcontainers support)
- When no suitable Testcontainers package exists, provide a minimal `docker-compose.yml` at the repo root
- Integration tests live in `tests/Fyper.Integration.Tests/`
- Each driver (`Fyper.Neo4j`, `Fyper.Age`) MUST have integration tests against a real database instance
- CI MUST run integration tests — they are not optional or "nightly-only"
- Keep the test environment setup to one command: `docker compose up -d` or automatic via Testcontainers

### VI. Multi-Backend Coverage

Fyper MUST target maximum coverage of Cypher-based graph databases.

- **Tier 1 (full support, integration-tested)**: Neo4j, Apache AGE (PostgreSQL)
- **Tier 2 (best-effort, community)**: Memgraph, FalkorDB, Amazon Neptune (OpenCypher subset)
- The `IGraphDriver` interface MUST abstract all backend differences
- Cypher dialect differences MUST be handled in the compiler/driver layer, never leaked to the user API
- New backend support MUST NOT require changes to the core `Fyper` library or user-facing DSL

### VII. Developer Experience First

The library MUST be maximally developer-friendly. Usability is a first-class design goal.

- Convention over configuration: plain F# records = graph schema, no attributes required by default
- Compile-time safety: invalid queries MUST fail at compile time, not runtime, wherever possible
- Clear error messages: when runtime errors occur, messages MUST reference the user's F# code, not internal AST
- Minimal ceremony: `cypher { for p in node<Person> do ... }` — no builder setup, no context objects
- IntelliSense-friendly: CE keywords, operators, and functions MUST provide good IDE completion
- Documentation via XML docs on all public API surfaces

### VIII. Parameterized Safety

All literal values MUST be parameterized. No value MUST ever be inlined into a Cypher query string.

- Literals become `$p0`, `$p1`, etc. with values passed as a parameter dictionary
- This applies to strings, numbers, booleans, dates, and collections
- This is a security invariant (injection prevention) and MUST be enforced by the compiler, not by convention
- Property tests MUST verify this invariant (see Principle IV)

## Technology Stack & Constraints

- **Runtime**: .NET 8.0, F# 8, latest language version
- **Core library**: `Fyper` — zero external dependencies (FSharp.Core only)
- **Driver packages**: `Fyper.Neo4j` (Neo4j.Driver), `Fyper.Age` (Npgsql)
- **Test framework**: Expecto + FsCheck
- **Integration tests**: Testcontainers (Neo4j), docker-compose (AGE, others)
- **Build**: `dotnet` CLI, standard `fsproj` files
- **CI**: All unit tests, property tests, and integration tests MUST pass on every PR
- **Packaging**: Single NuGet per project (`Fyper`, `Fyper.Neo4j`, `Fyper.Age`)
- **F# compilation order**: Strictly enforced in `fsproj` — see CLAUDE.md for canonical order

## Development Workflow

- **Phased delivery**: Phase 1 (Core+Compiler) → Phase 2 (ExprCompiler) → Phase 3 (DSL/CE) → Phase 4 (Neo4j) → Phase 5 (AGE) → Phase 6 (Mutations) → Phase 7 (Advanced)
- **Gate rule**: Each phase MUST have all tests green before the next phase begins
- **Commits**: Single author only. No `Co-Authored-By` trailers. Commits represent the developer's work
- **Commit style**: Conventional commits (`feat:`, `fix:`, `test:`, `docs:`, `refactor:`)
- **Branch strategy**: Feature branches off `main`, squash-merge or rebase-merge
- **Code review**: All changes reviewed against constitution principles before merge
- **No dead code**: Unused code MUST be deleted, not commented out or `_`-prefixed

## Governance

This constitution is the authoritative source of project principles and constraints. All implementation decisions, code reviews, and architectural choices MUST comply with these principles.

- **Amendment process**: Any principle change requires explicit justification documenting why the change is necessary and what impact it has on existing code
- **Versioning**: Constitution follows semantic versioning (MAJOR.MINOR.PATCH). Principle removal/redefinition = MAJOR, new principle/expansion = MINOR, clarification/typo = PATCH
- **Compliance review**: Every PR MUST be checked against applicable principles. The plan template's "Constitution Check" section enforces this at design time
- **Conflict resolution**: When principles conflict, safety (VIII) > correctness (III, IV) > usability (VII) > coverage (VI)
- **Runtime guidance**: See `CLAUDE.md` for implementation-level coding conventions and compilation order

**Version**: 1.0.0 | **Ratified**: 2026-04-02 | **Last Amended**: 2026-04-02
