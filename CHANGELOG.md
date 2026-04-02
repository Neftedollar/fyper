# Changelog

All notable changes to Fyper will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-04-02

### Added

- **Core query builder**: `cypher { }` computation expression with `Quote` member for auto-quotation
- **Schema convention**: plain F# records as graph nodes/relationships (PascalCase -> camelCase)
- **Full Cypher AST**: discriminated unions for all clause types (MATCH, WHERE, RETURN, CREATE, MERGE, DELETE, SET, ORDER BY, SKIP, LIMIT, UNWIND, WITH, UNION, CALL)
- **ExprCompiler**: F# quotations -> Cypher AST expressions (comparison, arithmetic, logical, string operators)
- **CypherCompiler**: AST -> parameterized Cypher string (all values as `$p0`, `$p1`, never inlined)
- **ResultMapper**: GraphValue -> typed F# records with option/list/tuple support
- **CE operations**: `where`, `select`, `orderBy`, `orderByDesc`, `skip`, `limit`, `matchRel`
- **Mutation CE operations**: `create`, `delete`, `detachDelete`, `set`, `merge`, `onMatch`, `onCreate`
- **Advanced CE operations**: `selectDistinct`, `unwind`, `withClause`, aggregation functions (`count`, `sum`, `avg`, `collect`, `cypherMin`, `cypherMax`), `caseWhen`
- **Neo4j driver** (`Fyper.Neo4j`): Bolt protocol via Neo4j.Driver, full GraphValue mapping, transaction support
- **Apache AGE driver** (`Fyper.Age`): PostgreSQL + AGE extension, agtype parser, Cypher-to-SQL wrapping, transaction support
- **Driver capabilities**: `DriverCapabilities` record with compile-time validation of backend feature support
- **Transaction API**: `Cypher.inTransaction` with auto-commit/rollback
- **Query inspection**: `Cypher.toCypher` returns `(string * Map<string, obj>)` without execution
- **Typed exceptions**: `FyperConnectionException`, `FyperQueryException`, `FyperMappingException`, `FyperUnsupportedFeatureException`
- **Raw AST API**: `Query.empty |> Query.matchNodes |> Query.where |> Query.return'` escape hatch
- **Custom naming**: `[<Label>]` and `[<CypherName>]` attributes for overriding conventions
- **Schema**: `[<Label>]`, `[<CypherName>]` attributes, `TypeMeta`/`PropertyMeta` caching
- **158 unit + property-based tests** (Expecto + FsCheck)
- **Integration test suite** for Neo4j (Testcontainers) and AGE (docker-compose)

[0.1.0]: https://github.com/example/fyper/releases/tag/v0.1.0
