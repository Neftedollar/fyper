# Feature Specification: Fyper — F# Typed Cypher ORM

**Feature Branch**: `001-fyper-full-orm`
**Created**: 2026-04-02
**Status**: Draft
**Input**: Complete F# typed Cypher ORM: CE DSL, multi-backend drivers (Neo4j, AGE, Memgraph), mutations, advanced Cypher, property-based tests

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Query Graph Data with Type Safety (Priority: P1)

Developer defines graph schema as plain F# records and queries graph databases using a typed computation expression. The CE auto-quotes F# expressions and compiles them into parameterized Cypher queries with full IntelliSense support.

**Why this priority**: This is the core value proposition — type-safe graph queries without boilerplate. Without this, the library has no purpose.

**Independent Test**: Define `Person` and `Movie` record types, write `cypher { for p in node<Person> do; where (p.Age > 30); select p }`, verify it produces `MATCH (p:Person) WHERE p.age > $p0 RETURN p` with parameter `p0 = 30`.

**Acceptance Scenarios**:

1. **Given** F# record types `Person = { Name: string; Age: int }` and `Movie = { Title: string; Released: int }`, **When** user writes `cypher { for p in node<Person> do; where (p.Age > 30); select p }`, **Then** Fyper produces `MATCH (p:Person) WHERE p.age > $p0 RETURN p` with parameters `{ p0: 30 }`
2. **Given** two node types and a relationship type, **When** user writes a multi-node query with `match'` for relationship pattern, **Then** Fyper produces correct Cypher with relationship traversal
3. **Given** a query with `orderBy`, `skip`, `limit`, **When** compiled, **Then** all values are parameterized and clauses appear in correct order
4. **Given** user writes `where (p.Name.Contains("Tom"))`, **When** compiled, **Then** produces `WHERE p.name CONTAINS $p0`
5. **Given** user writes `select (p.Name, m.Title)` (tuple projection), **When** compiled, **Then** produces `RETURN p.name, m.title`
6. **Given** a query with `optionalNode<Movie>`, **When** compiled, **Then** produces `OPTIONAL MATCH` for that node

---

### User Story 2 — Execute Queries Against Neo4j (Priority: P2)

Developer connects to a Neo4j database, executes typed Cypher queries, and receives results as strongly-typed F# records. The driver handles connection management, result mapping, and parameter conversion.

**Why this priority**: Neo4j is the most popular Cypher database. Real driver execution turns the query builder from a string generator into a usable ORM.

**Independent Test**: Start Neo4j via Testcontainers, create sample data, execute `cypher { for p in node<Person> do; select p }` via `Cypher.executeAsync`, verify returned `Person list` matches inserted data.

**Acceptance Scenarios**:

1. **Given** a running Neo4j instance and a typed query, **When** user calls `Cypher.executeAsync driver query`, **Then** results are returned as a typed `'T list`
2. **Given** a write query (CREATE), **When** user calls `Cypher.executeWriteAsync driver query`, **Then** affected entity count is returned
3. **Given** query results with node properties, **When** ResultMapper processes them, **Then** F# record fields are populated correctly (camelCase → PascalCase mapping)
4. **Given** a query returning `null` for optional properties, **When** mapped to F# record with `option` fields, **Then** `None` is produced
5. **Given** the driver is disposed, **When** user attempts to execute, **Then** a clear error is raised

---

### User Story 3 — Execute Queries Against Apache AGE (Priority: P3)

Developer connects to a PostgreSQL database with Apache AGE extension and executes the same typed Cypher queries. The AGE driver translates Cypher queries into AGE's SQL wrapper format and maps results back to the same GraphValue types.

**Why this priority**: AGE enables graph queries on PostgreSQL — the most common relational database. This proves the multi-backend architecture works and covers users who cannot adopt Neo4j.

**Independent Test**: Start PostgreSQL+AGE via docker-compose, create a graph, execute the same Person query as US2, verify identical typed results.

**Acceptance Scenarios**:

1. **Given** a PostgreSQL database with AGE extension, **When** user executes a typed Cypher query, **Then** AGE driver wraps it in `SELECT * FROM cypher('graph', $$ ... $$) AS (result agtype)` and returns typed results
2. **Given** the same `CypherQuery<Person>` used against Neo4j, **When** executed against AGE, **Then** the user gets identical `Person list` results
3. **Given** AGE's `agtype` result format, **When** ResultMapper processes it, **Then** values are correctly converted to GraphValue and then to F# records
4. **Given** AGE requires a named graph, **When** driver is configured with graph name, **Then** all queries target that graph

---

### User Story 4 — Mutate Graph Data via CE (Priority: P4)

Developer creates, updates, merges, and deletes graph nodes and relationships using the same computation expression DSL. Mutations use record syntax for property assignment.

**Why this priority**: Read-only queries cover only half the use cases. Mutations complete the ORM and make Fyper viable for production applications.

**Independent Test**: Write `cypher { create (node<Person> { Name = "Tom"; Age = 50 }) }`, execute against Neo4j, then query back and verify the node exists.

**Acceptance Scenarios**:

1. **Given** a Person record, **When** user writes `create (node<Person> { Name = "Tom"; Age = 50 })`, **Then** produces `CREATE (p:Person {name: $p0, age: $p1})`
2. **Given** two matched nodes, **When** user writes `create (p -[edge<ActedIn> { Roles = ["Neo"] }]-> m)`, **Then** produces a CREATE for the relationship with parameterized properties
3. **Given** a MERGE scenario, **When** user writes `merge` with `onMatch` and `onCreate` handlers, **Then** produces `MERGE ... ON MATCH SET ... ON CREATE SET ...`
4. **Given** a matched node, **When** user writes `set (fun p -> { p with Age = p.Age + 1 })`, **Then** produces `SET p.age = p.age + 1`
5. **Given** a matched node, **When** user writes `detachDelete p`, **Then** produces `DETACH DELETE p`

---

### User Story 5 — Advanced Cypher Features (Priority: P5)

Developer uses advanced Cypher capabilities: variable-length paths, CASE expressions, UNWIND, subqueries, aggregation functions, and DISTINCT. These cover complex real-world graph traversal patterns.

**Why this priority**: Advanced features enable production-grade usage patterns that go beyond basic CRUD.

**Independent Test**: Write a query with `pathLength (Between(1, 5))` for friend-of-friend traversal, verify generated Cypher includes `*1..5`.

**Acceptance Scenarios**:

1. **Given** a relationship pattern, **When** user specifies `pathLength (Between(1, 5))`, **Then** produces `-[r:KNOWS*1..5]->`
2. **Given** an UNWIND expression, **When** user writes `unwind names "name"`, **Then** produces `UNWIND $p0 AS name`
3. **Given** aggregation functions in select, **When** user writes `select {| Age = p.Age; Count = count() |}`, **Then** produces `RETURN p.age, count(*)`
4. **Given** a CASE expression, **When** compiled, **Then** produces `CASE WHEN ... THEN ... ELSE ... END`
5. **Given** a query needing `RETURN DISTINCT`, **When** user writes `selectDistinct p`, **Then** produces `RETURN DISTINCT p`

---

### User Story 6 — Raw AST Escape Hatch (Priority: P6)

Developer who needs Cypher features not yet covered by the CE can build queries using the raw AST API (`Query.empty |> Query.match' ... |> Query.where ...`) and execute them through the same drivers.

**Why this priority**: Ensures users are never blocked by DSL limitations. The escape hatch builds trust and adoption.

**Independent Test**: Build a query using `Query.empty |> Query.match' |> Query.where |> Query.return'`, compile with `CypherCompiler.compile`, verify output matches expected Cypher.

**Acceptance Scenarios**:

1. **Given** the raw AST API, **When** user builds a query with `Query.match'`, `Query.where`, `Query.return'`, **Then** `CypherCompiler.compile` produces valid Cypher
2. **Given** a raw query, **When** user adds parameters with `Query.addParam`, **Then** parameters appear in the compiled output
3. **Given** a raw query, **When** executed through Neo4j or AGE driver, **Then** results are correctly returned and mapped

---

### Edge Cases

- What happens when a record field is `option<'T>` and the graph property is missing? → Map to `None`
- What happens when user references a variable not bound by `for ... in node<T> do`? → Compile-time error from quotation analysis
- What happens when Neo4j returns a `null` node in an OPTIONAL MATCH result? → Map to `GNull`, then to default/None in result
- What happens when AGE graph doesn't exist? → Clear error message at driver level
- What happens with empty result sets? → Return empty `'T list`
- What happens when a record type has no matching label in the database? → Query runs but returns no matches (no error)
- What happens with cyclic graph traversals in variable-length paths? → Cypher engine handles it; Fyper passes through
- What happens when parameterized values contain special characters (quotes, backslashes)? → Parameters are sent separately, no string escaping needed
- What happens when the database is unreachable or credentials are wrong? → Typed `FyperConnectionException` with clear message
- What happens on query timeout? → Typed exception propagated from underlying driver

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST compile F# computation expressions into parameterized Cypher query strings
- **FR-002**: System MUST support `for ... in node<T> do` syntax to declare node variables with type-inferred labels
- **FR-003**: System MUST support `where`, `select`, `orderBy`, `orderByDesc`, `skip`, `limit` CE operations
- **FR-004**: System MUST support `match'` with edge pattern operators (`-[edge<T>]->`, `<-[edge<T>]-`, `-[edge<T>]-`)
- **FR-005**: System MUST parameterize all literal values as `$p0`, `$p1` etc. — no values inlined into Cypher strings
- **FR-006**: System MUST map PascalCase F# field names to camelCase Cypher property names by convention
- **FR-007**: System MUST support `[<Label>]` and `[<CypherName>]` attributes for custom naming
- **FR-008**: System MUST support both auto-quotation (via `Quote` CE member) and explicit `<@ @>` quotations
- **FR-009**: System MUST provide `IGraphDriver` interface with `ExecuteReadAsync`, `ExecuteWriteAsync`, and `BeginTransactionAsync`
- **FR-010**: System MUST provide Neo4j Bolt driver implementing `IGraphDriver`
- **FR-011**: System MUST provide Apache AGE (PostgreSQL) driver implementing `IGraphDriver`
- **FR-012**: System MUST map graph results (`GraphValue`) to typed F# records via `ResultMapper`
- **FR-013**: System MUST support `option<'T>` fields mapped to nullable/missing graph properties
- **FR-014**: System MUST support CREATE, MERGE (with ON MATCH/ON CREATE), SET, DELETE, DETACH DELETE in the CE
- **FR-015**: System MUST support variable-length path patterns (`*1..5`, `*`, `*2..`)
- **FR-016**: System MUST support aggregation functions (`count`, `collect`, `sum`, `avg`, `min`, `max`)
- **FR-017**: System MUST support UNWIND, WITH, DISTINCT
- **FR-018**: System MUST provide a raw AST API (`Query.empty |> Query.match' ...`) as an escape hatch
- **FR-019**: System MUST have zero external dependencies in the core `Fyper` library (only FSharp.Core)
- **FR-020**: System MUST support OPTIONAL MATCH via `optionalNode<T>`
- **FR-021**: System MUST support tuple and anonymous record projections in `select`
- **FR-022**: System MUST support explicit transactions via `Cypher.inTransaction driver (fun tx -> ...)` for multi-statement atomicity; individual queries auto-commit by default
- **FR-023**: System MUST surface driver errors as typed F# exceptions (connection, authentication, query syntax, timeout) — not swallowed or wrapped in Result types
- **FR-024**: Each driver MUST declare its supported Cypher capabilities (clauses, functions, features) via capability flags
- **FR-025**: System MUST reject queries using unsupported features for a given backend at query construction time (before execution), not at database runtime
- **FR-026**: System MUST provide `ToCypher()` on `CypherQuery<'T>` returning `(string * Map<string, obj>)` for query inspection and debugging without execution
- **FR-027**: Driver implementations MUST accept pre-configured underlying clients (Neo4j `IDriver`, Npgsql `NpgsqlDataSource`) — no custom connection pooling

### Key Entities

- **CypherQuery<'T>**: A typed query containing Cypher clauses and parameters, phantom-typed by result type
- **Clause**: DU representing each Cypher clause (MATCH, WHERE, RETURN, CREATE, etc.)
- **Expr**: DU representing Cypher expressions (Property, BinOp, FuncCall, Param, etc.)
- **Pattern**: DU representing graph patterns (NodePattern, RelPattern, NamedPath)
- **GraphValue**: DU representing any value from any graph database (GNode, GRel, GString, etc.)
- **IGraphDriver**: Interface abstraction for database backends
- **TypeMeta / PropertyMeta**: Cached schema metadata extracted from F# record types

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Developer can write a typed graph query in under 5 lines of F# that compiles to valid Cypher
- **SC-002**: The same query definition executes against both Neo4j and Apache AGE without modification
- **SC-003**: All literal values are parameterized — zero string interpolation in any generated Cypher output
- **SC-004**: All Cypher AST DU cases are handled exhaustively — no wildcard catch-alls in compiler pattern matches
- **SC-005**: Property-based tests confirm parameterization invariant holds for all generated queries
- **SC-006**: Integration tests pass against real Neo4j and real Apache AGE instances
- **SC-007**: Core library has zero NuGet dependencies beyond FSharp.Core
- **SC-008**: Schema definition requires zero boilerplate — plain F# records work as nodes/relationships without attributes
- **SC-009**: Result mapping correctly handles all F# record field types including `option`, `list`, `int`, `string`, `float`, `bool`
- **SC-010**: All 7 implementation phases have passing unit tests and property-based tests

## Clarifications

### Session 2026-04-02

- Q: Should Fyper support explicit transactions? → A: Yes — explicit transaction API (`Cypher.inTransaction driver (fun tx -> ...)`) alongside auto-commit default. Each query auto-commits by default; multi-statement atomicity available via explicit transaction wrapper.
- Q: How should driver errors surface to users? → A: Standard F# exceptions. Driver/connection errors propagate as typed exceptions (e.g., `FyperConnectionException`). Underlying library exceptions are not swallowed.
- Q: How should Fyper handle AGE Cypher dialect limitations? → A: Compile-time capability flags. Driver declares supported clauses/features; compiler rejects unsupported features at build time rather than failing at runtime.
- Q: Should Fyper provide built-in diagnostics/logging? → A: `ToCypher()` method on `CypherQuery<'T>` returning `(string * Map<string, obj>)` for inspection. Zero-dependency, no ILogger required. Users layer their own logging.
- Q: Should Fyper manage connection pooling? → A: No. Delegate to underlying libraries. Drivers are thin wrappers that accept pre-configured clients (e.g., Neo4j `IDriver`, Npgsql `NpgsqlDataSource`). No custom pooling.

## Assumptions

- Developer has .NET 8+ SDK installed
- Neo4j integration tests use Testcontainers (official .NET support available)
- Apache AGE integration tests use docker-compose with `apache/age` image
- F# quotation mechanism (`Quote` CE member) provides sufficient expression coverage for common Cypher patterns
- Cypher dialect differences between Neo4j and AGE are handled via compile-time capability flags on each driver — unsupported features rejected before execution
- Memgraph and other Tier 2 backends will be community-contributed following the `IGraphDriver` pattern
