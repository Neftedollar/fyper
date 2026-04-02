# Tasks: Fyper — F# Typed Cypher ORM

**Input**: Design documents from `/specs/001-fyper-full-orm/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/IGraphDriver.md

**Tests**: Tests are MANDATORY per constitution (Principles III, IV, V). Unit tests, property-based tests (FsCheck), and integration tests are required for every phase.

**Organization**: Tasks grouped by user story. US1 (core queries) and US6 (raw AST) are already implemented (Phases 1-3, 92 tests). Remaining work covers US2-US5.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story (US2, US3, US4, US5)
- Exact file paths included

## Phase 1: Setup

**Purpose**: Extend core types and create driver project scaffolding

- [x] T001 Add `DriverCapabilities` record to `src/Fyper/Driver.fs` with fields: `SupportsOptionalMatch`, `SupportsMerge`, `SupportsUnwind`, `SupportsCase`, `SupportsCallProcedure`, `SupportsExistsSubquery`, `SupportsNamedPaths`
- [x] T002 Add `IGraphTransaction` interface to `src/Fyper/Driver.fs` with `ExecuteReadAsync`, `ExecuteWriteAsync`, `CommitAsync`, `RollbackAsync`, `IAsyncDisposable`
- [x] T003 Extend `IGraphDriver` in `src/Fyper/Driver.fs` with `BeginTransactionAsync: unit -> Task<IGraphTransaction>` and `Capabilities: DriverCapabilities`
- [x] T004 Add exception types to `src/Fyper/Driver.fs`: `FyperException`, `FyperConnectionException`, `FyperQueryException`, `FyperMappingException`, `FyperUnsupportedFeatureException`
- [x] T005 Add `toCypher` function to `src/Fyper/Cypher.fs` returning `(string * Map<string, obj>)` from `CypherQuery<'T>`
- [x] T006 Add `inTransaction` function to `src/Fyper/Cypher.fs`: `IGraphDriver -> (IGraphTransaction -> Task<'T>) -> Task<'T>` with auto-commit/rollback
- [x] T007 Add capability validation to `src/Fyper/CypherCompiler.fs`: `validateCapabilities: DriverCapabilities -> Clause list -> unit` that throws `FyperUnsupportedFeatureException` for unsupported clauses
- [x] T008 [P] Create `src/Fyper.Neo4j/Fyper.Neo4j.fsproj` targeting net10.0 with dependency on `Fyper` and `Neo4j.Driver`
- [x] T009 [P] Create `src/Fyper.Age/Fyper.Age.fsproj` targeting net10.0 with dependency on `Fyper` and `Npgsql`
- [x] T010 [P] Create `tests/Fyper.Integration.Tests/Fyper.Integration.Tests.fsproj` targeting net10.0 with dependencies on `Fyper`, `Fyper.Neo4j`, `Fyper.Age`, `Expecto`, `Testcontainers.Neo4j`
- [x] T011 [P] Create `docker-compose.yml` at repo root with `apache/age` service (port 5432, user/pass: test/test, db: testdb)
- [x] T012 Write unit tests for `DriverCapabilities` and `toCypher` in `tests/Fyper.Tests/DriverTests.fs`
- [x] T013 Write unit tests for capability validation in `tests/Fyper.Tests/CapabilityTests.fs`

**Checkpoint**: Core types extended, driver projects scaffolded, all existing 92 tests + new tests pass.

---

## Phase 2: Foundational (Property-Based Tests for Existing Code)

**Purpose**: Add FsCheck property-based tests for Phases 1-3 code (constitution requirement IV)

**⚠️ CRITICAL**: These tests validate invariants across all existing code before adding new features.

- [x] T014 [P] Write FsCheck property tests for parameterization invariant in `tests/Fyper.Tests/PropertyTests.fs`: generated Cypher never contains literal values, all literals become `$pN` parameters
- [x] T015 [P] Write FsCheck property tests for Schema naming in `tests/Fyper.Tests/PropertyTests.fs`: PascalCase → camelCase roundtrip, `toCypherName` properties (lowercase first char, rest preserved)
- [x] T016 [P] Write FsCheck property tests for AST compilation in `tests/Fyper.Tests/PropertyTests.fs`: every `Expr` variant compiles without exception, every `Clause` variant compiles without exception (exhaustive DU coverage)
- [x] T017 [P] Write FsCheck property tests for ExprCompiler in `tests/Fyper.Tests/PropertyTests.fs`: F# quotation expressions with comparison/arithmetic/logical operators produce valid `Ast.Expr` trees

**Checkpoint**: Property-based test suite established. All invariants verified across existing codebase.

---

## Phase 3: User Story 2 — Execute Queries Against Neo4j (Priority: P2)

**Goal**: Developer connects to Neo4j, executes typed queries, receives strongly-typed F# records.

**Independent Test**: Start Neo4j via Testcontainers, create Person data, execute `cypher { for p in node<Person> do; select p }`, verify returned `Person list`.

### Tests for US2

- [x] T018 [P] [US2] Write Neo4j integration tests in `tests/Fyper.Integration.Tests/Neo4jTests.fs`: container setup, basic read query, write query (CREATE), result mapping (camelCase → PascalCase), option field mapping (null → None), disposed driver error
- [x] T019 [P] [US2] Write Neo4j transaction integration tests in `tests/Fyper.Integration.Tests/Neo4jTests.fs`: multi-statement commit, auto-rollback on exception

### Implementation for US2

- [x] T020 [US2] Implement `Neo4jDriver` class in `src/Fyper.Neo4j/Neo4jDriver.fs`: constructor accepting `Neo4j.Driver.IDriver`, implement `IGraphDriver.ExecuteReadAsync` (session → run → map `IRecord` → `GraphRecord`), implement `IGraphDriver.ExecuteWriteAsync`, implement `Capabilities` (all features = true for Neo4j)
- [x] T021 [US2] Implement Neo4j `GraphValue` mapping in `src/Fyper.Neo4j/Neo4jDriver.fs`: `INode` → `GNode`, `IRelationship` → `GRel`, `IPath` → `GPath`, primitives → `GBool`/`GInt`/`GFloat`/`GString`, `null` → `GNull`, lists → `GList`, maps → `GMap`
- [x] T022 [US2] Implement `Neo4jTransaction` class in `src/Fyper.Neo4j/Neo4jDriver.fs`: wraps `IAsyncTransaction`, implements `IGraphTransaction` with `CommitAsync`/`RollbackAsync`
- [x] T023 [US2] Implement `BeginTransactionAsync` on `Neo4jDriver` in `src/Fyper.Neo4j/Neo4jDriver.fs`: creates session, begins transaction, returns `Neo4jTransaction`
- [x] T024 [US2] Implement `IAsyncDisposable.DisposeAsync` on `Neo4jDriver` in `src/Fyper.Neo4j/Neo4jDriver.fs`: disposes underlying `IDriver`
- [x] T025 [US2] Add `tests/Fyper.Integration.Tests/Program.fs` with Expecto test runner entry point

**Checkpoint**: Neo4j driver fully functional. Integration tests pass against real Neo4j via Testcontainers. `Cypher.executeAsync neo4jDriver query` returns typed results.

---

## Phase 4: User Story 3 — Execute Queries Against Apache AGE (Priority: P3)

**Goal**: Developer connects to PostgreSQL+AGE, executes same typed Cypher queries, gets identical results.

**Independent Test**: Start AGE via docker-compose, create graph + Person data, execute same query as US2, verify identical `Person list`.

### Tests for US3

- [x] T026 [P] [US3] Write AGE integration tests in `tests/Fyper.Integration.Tests/AgeTests.fs`: connection setup with `LOAD 'age'`, graph creation, basic read query, write query (CREATE), result mapping from agtype, graph name configuration
- [x] T027 [P] [US3] Write AGE transaction integration tests in `tests/Fyper.Integration.Tests/AgeTests.fs`: PostgreSQL transaction commit, auto-rollback on exception
- [x] T028 [P] [US3] Write cross-backend integration test in `tests/Fyper.Integration.Tests/CrossBackendTests.fs`: same `CypherQuery<Person>` executes against both Neo4j and AGE, returns identical typed results

### Implementation for US3

- [x] T029 [US3] Implement agtype parser in `src/Fyper.Age/AgeDriver.fs`: parse AGE JSON-like agtype text into `GraphValue` tree (nodes, relationships, primitives, lists, maps)
- [x] T030 [US3] Implement Cypher-to-SQL wrapper in `src/Fyper.Age/AgeDriver.fs`: `wrapCypher: string -> string list -> string` producing `SELECT * FROM cypher('{graph}', $$ {cypher} $$) AS ({aliases} agtype)` with return alias extraction from compiled Cypher
- [x] T031 [US3] Implement AGE parameter binding in `src/Fyper.Age/AgeDriver.fs`: convert `Map<string, obj>` parameters to positional `$N` AGE function arguments
- [x] T032 [US3] Implement `AgeDriver` class in `src/Fyper.Age/AgeDriver.fs`: constructor accepting `NpgsqlDataSource` and `graphName`, implement `IGraphDriver.ExecuteReadAsync` (open connection → `LOAD 'age'` → execute wrapped query → parse agtype → `GraphRecord`), implement `ExecuteWriteAsync`, implement `Capabilities` (AGE subset: no OPTIONAL MATCH, no MERGE, no UNWIND, no CASE, no CALL, no EXISTS subquery, no named paths)
- [x] T033 [US3] Implement `AgeTransaction` class in `src/Fyper.Age/AgeDriver.fs`: wraps `NpgsqlConnection` + `NpgsqlTransaction`, implements `IGraphTransaction`
- [x] T034 [US3] Implement `BeginTransactionAsync` and `DisposeAsync` on `AgeDriver` in `src/Fyper.Age/AgeDriver.fs`

**Checkpoint**: AGE driver fully functional. Same typed queries work against both Neo4j and AGE. Integration tests pass via docker-compose.

---

## Phase 5: User Story 4 — Mutate Graph Data via CE (Priority: P4)

**Goal**: Developer creates, updates, merges, deletes nodes/relationships using the CE DSL.

**Independent Test**: Write `cypher { create (node<Person> { Name = "Tom"; Age = 50 }) }`, verify Cypher output. Execute against Neo4j, query back, verify node exists.

### Tests for US4

- [x] T035 [P] [US4] Write unit tests for CREATE in CE in `tests/Fyper.Tests/MutationTests.fs`: `create (node<T> { ... })` → `CREATE (n:Label {props})`, create relationship `p -[edge<T> { ... }]-> m`
- [x] T036 [P] [US4] Write unit tests for MERGE in CE in `tests/Fyper.Tests/MutationTests.fs`: `merge` with `onMatch`/`onCreate` handlers → `MERGE ... ON MATCH SET ... ON CREATE SET ...`
- [x] T037 [P] [US4] Write unit tests for SET in CE in `tests/Fyper.Tests/MutationTests.fs`: `set (fun p -> { p with Age = p.Age + 1 })` → `SET p.age = p.age + 1`
- [x] T038 [P] [US4] Write unit tests for DELETE in CE in `tests/Fyper.Tests/MutationTests.fs`: `delete p` → `DELETE p`, `detachDelete p` → `DETACH DELETE p`
- [x] T039 [P] [US4] Write FsCheck property tests for mutations in `tests/Fyper.Tests/PropertyTests.fs`: all mutation clause compilation produces valid Cypher, all values parameterized in CREATE/SET
- [x] T040 [P] [US4] Write mutation integration tests in `tests/Fyper.Integration.Tests/Neo4jTests.fs`: CREATE + query back, MERGE idempotency, SET update verification, DELETE + verify gone (depends on US2 Neo4j driver)
- [x] T040b [P] [US4] Write AGE mutation integration tests in `tests/Fyper.Integration.Tests/AgeTests.fs`: CREATE + query back, SET update verification, DELETE + verify gone (depends on US3 AGE driver; skip MERGE — unsupported by AGE)

### Implementation for US4

- [x] T041 [US4] Add `create` custom operation to `src/Fyper/CypherBuilder.fs`: accepts node pattern with properties, translates to `Create` clause
- [x] T042 [US4] Add `merge`, `onMatch`, `onCreate` custom operations to `src/Fyper/CypherBuilder.fs`
- [x] T043 [US4] Add `set` custom operation to `src/Fyper/CypherBuilder.fs`: accepts record update lambda `(fun p -> { p with ... })`
- [x] T044 [US4] Add `delete`, `detachDelete` custom operations to `src/Fyper/CypherBuilder.fs`
- [x] T045 [US4] Extend `src/Fyper/QueryTranslator.fs` to handle CREATE quotation patterns: `NewRecord` construction → `NodePattern` with parameterized property map
- [x] T046 [US4] Extend `src/Fyper/QueryTranslator.fs` to handle MERGE + ON MATCH/ON CREATE quotation patterns
- [x] T047 [US4] Extend `src/Fyper/QueryTranslator.fs` to handle SET quotation patterns: `{ p with Field = expr }` → `SetProperty` items
- [x] T048 [US4] Extend `src/Fyper/QueryTranslator.fs` to handle DELETE/DETACH DELETE quotation patterns

**Checkpoint**: Mutation CE operations work. CREATE, MERGE, SET, DELETE all produce correct parameterized Cypher. Integration tests verify end-to-end against Neo4j.

---

## Phase 6: User Story 5 — Advanced Cypher Features (Priority: P5)

**Goal**: Developer uses variable-length paths, CASE, UNWIND, aggregations, DISTINCT in the CE.

**Independent Test**: Query with `pathLength (Between(1, 5))` generates `*1..5` in Cypher.

### Tests for US5

- [x] T049 [P] [US5] Write unit tests for variable-length paths in CE in `tests/Fyper.Tests/AdvancedTests.fs`: `pathLength (Between(1,5))` → `*1..5`, `AnyLength` → `*`, `Exactly 3` → `*3`
- [x] T050 [P] [US5] Write unit tests for UNWIND in CE in `tests/Fyper.Tests/AdvancedTests.fs`: `unwind list "alias"` → `UNWIND $p0 AS alias`
- [x] T051 [P] [US5] Write unit tests for aggregation functions in CE in `tests/Fyper.Tests/AdvancedTests.fs`: `count()`, `collect(p.Name)`, `sum(p.Age)`, `avg()`, `min()`, `max()`
- [x] T052 [P] [US5] Write unit tests for DISTINCT in CE in `tests/Fyper.Tests/AdvancedTests.fs`: `selectDistinct p` → `RETURN DISTINCT p`
- [x] T053 [P] [US5] Write unit tests for WITH in CE in `tests/Fyper.Tests/AdvancedTests.fs`: `with'` clause for query chaining
- [x] T054 [P] [US5] Write unit tests for CASE in CE in `tests/Fyper.Tests/AdvancedTests.fs`: CASE WHEN/THEN/ELSE/END expression
- [x] T055 [P] [US5] Write FsCheck property tests for advanced features in `tests/Fyper.Tests/PropertyTests.fs`: path length compilation invariants, aggregation function names valid, all values parameterized

### Implementation for US5

- [x] T056 [US5] Add `unwind` custom operation to `src/Fyper/CypherBuilder.fs` and handle in `src/Fyper/QueryTranslator.fs`
- [x] T057 [US5] Add `with'` custom operation to `src/Fyper/CypherBuilder.fs` for WITH clause and handle in `src/Fyper/QueryTranslator.fs`
- [x] T058 [US5] Add `selectDistinct` custom operation to `src/Fyper/CypherBuilder.fs` producing `Return(items, distinct=true)` and handle in `src/Fyper/QueryTranslator.fs`
- [x] T059 [US5] Add aggregation functions (`count`, `collect`, `sum`, `avg`, `min`, `max`) as module functions in `src/Fyper/Operators.fs` that compile to `FuncCall` AST nodes via `src/Fyper/QueryTranslator.fs`
- [x] T060 [US5] Add variable-length path support to CE edge patterns in `src/Fyper/Operators.fs` and `src/Fyper/QueryTranslator.fs`: `pathLength` modifier on relationship patterns
- [x] T061 [US5] Add CASE expression support to `src/Fyper/ExprCompiler.fs` and `src/Fyper/QueryTranslator.fs`: `caseWhen` function producing `CaseExpr` AST
- [x] T062 [P] [US5] Write advanced feature integration tests in `tests/Fyper.Integration.Tests/Neo4jTests.fs`: variable-length path traversal, UNWIND + CREATE, aggregation queries, DISTINCT results

**Checkpoint**: All advanced Cypher features work in the CE. Integration tests pass for complex queries.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, documentation, and cleanup across all stories

- [x] T063 [P] Write `tests/Fyper.Integration.Tests/TransactionTests.fs`: cross-backend transaction tests (Neo4j + AGE), rollback verification, nested query within transaction
- [x] T064 [P] Write `tests/Fyper.Integration.Tests/ErrorHandlingTests.fs`: `FyperConnectionException` on bad credentials, `FyperQueryException` on invalid Cypher, `FyperMappingException` on type mismatch, `FyperUnsupportedFeatureException` when AGE receives OPTIONAL MATCH
- [x] T065 [P] Add XML doc comments to all public API surfaces in `src/Fyper/Cypher.fs`, `src/Fyper/Driver.fs`, `src/Fyper/CypherBuilder.fs`
- [x] T066 [P] Write capability validation integration test in `tests/Fyper.Integration.Tests/AgeTests.fs`: verify AGE driver rejects queries with OPTIONAL MATCH, MERGE, UNWIND at query construction time
- [x] T067 Run full test suite: `dotnet test` across all test projects, verify all pass
- [x] T068 Run quickstart.md validation: execute each code example against real databases, verify output matches

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — extends existing code
- **Phase 2 (Property Tests)**: Depends on Phase 1 (needs DriverCapabilities for capability property tests)
- **Phase 3 (US2 Neo4j)**: Depends on Phase 1 (needs IGraphDriver extensions, Neo4j fsproj)
- **Phase 4 (US3 AGE)**: Depends on Phase 1 (needs IGraphDriver extensions, AGE fsproj); can run in parallel with Phase 3
- **Phase 5 (US4 Mutations)**: Depends on Phase 1 (core types); can run in parallel with Phases 3-4
- **Phase 6 (US5 Advanced)**: Depends on Phase 1 (core types); can run in parallel with Phases 3-5
- **Phase 7 (Polish)**: Depends on Phases 3-6 completion

### User Story Dependencies

- **US2 (Neo4j)**: Depends on Phase 1 setup only — fully independent
- **US3 (AGE)**: Depends on Phase 1 setup only — can parallel with US2
- **US4 (Mutations)**: Unit tests parallel with US2/US3. **Integration tests (T040, T040b) require US2 (Neo4j) and US3 (AGE) drivers to be implemented first**
- **US5 (Advanced)**: Unit tests parallel with US2/US3/US4. **Integration tests (T062) require US2 (Neo4j) driver to be implemented first**

### Within Each User Story

- Tests MUST be written and FAIL before implementation
- Unit tests before integration tests
- Core functionality before edge cases
- All tests green before moving to next phase

### Parallel Opportunities

- T008, T009, T010, T011 (project scaffolding) — all parallel
- T014, T015, T016, T017 (property tests) — all parallel
- T018, T019 (Neo4j tests) — parallel with each other
- T026, T027, T028 (AGE tests) — parallel with each other
- T035-T040 (mutation tests) — all parallel
- T049-T055 (advanced tests) — all parallel
- T063, T064, T065, T066 (polish) — all parallel
- After Phase 1: US2, US3, US4, US5 can all proceed in parallel

---

## Parallel Example: Phase 1 Setup

```bash
# All project scaffolding in parallel:
Task: T008 "Create Fyper.Neo4j.fsproj"
Task: T009 "Create Fyper.Age.fsproj"
Task: T010 "Create integration test fsproj"
Task: T011 "Create docker-compose.yml"
```

## Parallel Example: After Phase 1

```bash
# All user stories can start simultaneously:
Phase 3 (US2): T018-T025 (Neo4j driver)
Phase 4 (US3): T026-T034 (AGE driver)
Phase 5 (US4): T035-T048 (Mutations)
Phase 6 (US5): T049-T062 (Advanced)
```

---

## Implementation Strategy

### MVP First (US2 Only)

1. Complete Phase 1: Setup (T001-T013)
2. Complete Phase 2: Property Tests (T014-T017)
3. Complete Phase 3: US2 Neo4j Driver (T018-T025)
4. **STOP and VALIDATE**: Run all tests, verify Neo4j integration works end-to-end
5. Fyper is usable with Neo4j at this point

### Incremental Delivery

1. Phase 1 + 2 → Foundation ready
2. Add US2 (Neo4j) → Test independently → **MVP: usable library**
3. Add US3 (AGE) → Test independently → Multi-backend proven
4. Add US4 (Mutations) → Test independently → Full CRUD
5. Add US5 (Advanced) → Test independently → Production-ready
6. Phase 7 → Polish, docs, final validation

### Sequential Priority Strategy

If implementing alone:
1. Phase 1 → Phase 2 → Phase 3 (US2) → Phase 5 (US4) → Phase 4 (US3) → Phase 6 (US5) → Phase 7

Rationale: Neo4j driver first (most users), then mutations (most requested feature), then AGE (second backend), then advanced features.

---

## Notes

- [P] tasks = different files, no dependencies
- [US*] label maps task to specific user story
- Constitution requires tests FIRST — write tests, verify they fail, then implement
- Property-based tests (Phase 2) use FsCheck generators for AST types
- Integration tests for US4/US5 reuse the Neo4j driver from US2
- AGE integration tests require `docker compose up -d` before running
- Commit after each task or logical group
