# Fyper Code Review -- 1.0.0 Release Candidate

**Reviewer:** Claude Opus 4.6  
**Date:** 2026-04-02  
**Scope:** All source files in `src/Fyper/`, `src/Fyper.Parser/`, `src/Fyper.Neo4j/`, `src/Fyper.Age/`, and `tests/Fyper.Tests/`  
**Codebase size:** ~2,700 LOC (library) + ~2,340 LOC (tests), 239 passing tests  

---

## 1. Code Quality -- Rating: 8/10

The codebase is well-structured, idiomatic F#, and follows the conventions laid out in `CLAUDE.md`.

### Strengths

- **Excellent DU design.** The `Ast.fs` AST is clean, comprehensive, and self-documenting. All Cypher constructs are represented as DUs with named tuple fields, which is ideal.
- **Pipeline-friendly API.** `Query.empty |> Query.matchNodes |> Query.where |> Query.return'` reads naturally.
- **Module-first design.** Nearly everything is a module function, not a class method. Records over classes. DUs over enums.
- **Proper `task { }` usage** throughout (not `async { }`).
- **ConcurrentDictionary caching** in `Schema.fs:42` is appropriate for the metadata cache.

### Issues

**I-1: `QueryTranslator.fs:330-348` -- Dead code in `updateLastMerge`.**  
The `go` function (lines 331-336) is defined but never called. The actual implementation uses `List.tryFindIndexBack` + `List.mapi` (lines 338-348). The dead `go` function should be removed.

```fsharp
// Dead code -- unreachable
let rec go (revClauses: Clause list) =
    match revClauses with
    | Merge(p, om, oc) :: rest -> (update (p, om, oc)) :: rest |> List.rev
    | c :: rest -> go rest |> List.rev |> fun r -> r @ [c] |> List.rev
    | [] -> clauses
```

**I-2: `ExprCompiler.fs` -- Excessive `Microsoft.FSharp.Quotations.Patterns.` prefixes.**  
Every pattern match branch spells out the full namespace. A module alias (`module QP = Microsoft.FSharp.Quotations.Patterns`) like `QueryTranslator.fs` uses would halve the line lengths and improve readability.

**I-3: `CypherCompiler.fs:80` -- `literalToString` string escaping is naive.**  
Only single-quotes are escaped (`s.Replace("'", "\\'")`). While this function is used for the `Literal` case (which should be rare since all user values go through `Param`), a Cypher string can also contain backslashes. The escape `\\'` is correct for Neo4j but may not work on all backends.

**I-4: `Ast.fs:129` -- `addClause` uses list append `@` which is O(n).**  
`q.Clauses @ [clause]` traverses the full clause list each time. For typical query sizes (5-15 clauses), this is negligible. But if someone builds queries in a loop, performance degrades. Consider prepending + reversing, or using a `ResizeArray` internally.

---

## 2. Correctness -- Rating: 7/10

### Issues

**I-5: `ExprCompiler.fs:156` -- `Let` binding discards the binding value.**  
```fsharp
| QP.Let(_, _, body) -> compile state body
```
This skips the `let` binding entirely and only compiles the body. If the let-bound variable is referenced in the body, the compilation will hit the `Var` case and produce a `Variable` reference -- but the actual value from the binding is lost. This works in the CE context because the QueryTranslator handles the let-binding resolution, but if `ExprCompiler.compile` is used standalone on a `let x = 42 in x > 10`, the `42` would be lost and `x` would compile to `Variable "x"` instead of `Param "p0"`.

**I-6: `QueryTranslator.fs:258-260` -- Silent fallback swallows unknown expressions.**  
```fsharp
// Fallback
| _ -> state
```
If the translator encounters a quotation pattern it doesn't recognize, it silently ignores it. This means typos or unsupported CE operations produce incorrect queries with missing clauses rather than a clear error. This is the most concerning correctness issue for a 1.0 release.

**I-7: `QueryTranslator.fs:519` -- Edge pattern variable extraction is fragile.**  
```fsharp
| [fromName; toName] | [_; fromName; toName] ->
```
This assumes the `from` and `to` variables are always the last two in the list (or the only two). The `_` skip accommodates the CE's internal variable-space variable. However, if the CE desugaring changes (e.g., F# compiler update) or if more complex patterns are used, this heuristic could break silently. The pattern should be more explicit about what it skips.

**I-8: `ResultMapper.fs:29-31` -- `GList` conversion uses reflection to invoke `List.ofSeq`.**  
This works but is fragile. The assembly lookup `typeof<list<int>>.Assembly.GetType("Microsoft.FSharp.Collections.ListModule")` depends on internal FSharp.Core assembly layout. If FSharp.Core reorganizes (it has done so historically), this breaks at runtime with a cryptic NullReferenceException. Consider using `FSharpValue.MakeUnion` or a generic helper instead.

**I-9: `ResultMapper.fs:83-89` -- Tuple result mapping assumes keys are ordered.**  
```fsharp
record.Keys |> List.mapi (fun i key -> ...)
```
If the driver returns keys in a different order than the tuple expects, the mapping silently produces wrong values. `GraphRecord.Keys` order is driver-dependent (Map iteration order for `record.Values` is alphabetical by key, not insertion order). This is a latent bug: `(p.name, m.title)` would map `m.title` to the first tuple element if the keys are alphabetically sorted.

**I-10: `Neo4jDriver.fs:22-23` -- `ElementId` hashed to `int64` loses identity.**  
```fsharp
Id = node.ElementId |> hash |> int64
```
Neo4j 5.x uses string `ElementId` values (e.g., `"4:abc:123"`). Hashing these to `int64` creates collisions and makes IDs useless for lookups. The `GraphNode.Id` field should be `string` or the driver should parse the numeric suffix from the ElementId format. This is a data fidelity issue.

**I-11: `AgeDriver.fs:115-116` -- Parameter positional remapping uses string replacement.**  
```fsharp
remappedCypher <- remappedCypher.Replace(sprintf "$%s" name, positional)
```
If a parameter name is a prefix of another (e.g., `$p0` and `$p0_extra`), `String.Replace` will corrupt the longer parameter. Parameter names from the ExprCompiler are sequential (`p0`, `p1`, ...) so this is unlikely in practice, but user-provided parameter names via the raw API could trigger it.

---

## 3. Error Handling -- Rating: 8/10

### Strengths

- **Well-designed exception hierarchy.** `FyperException` -> `FyperConnectionException` / `FyperQueryException` / `FyperMappingException` / `FyperUnsupportedFeatureException`. Each carries relevant context (query text, parameters, target type, source value).
- **Capability validation** catches unsupported features before execution.
- **Transaction auto-rollback** in `Cypher.fs:43-53` is correctly implemented.

### Issues

**I-12: `Cypher.fs:51-52` -- Transaction re-raise loses stack trace.**  
```fsharp
with ex ->
    try do! tx.RollbackAsync() with _ -> ()
    return raise (System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).SourceException)
```
The `ExceptionDispatchInfo.Capture(ex).SourceException` returns the original exception but `raise` then wraps it with a new stack trace. The correct pattern is `ExceptionDispatchInfo.Capture(ex).Throw()` which preserves the original stack trace. The current code loses the original call site information.

**I-13: `ResultMapper.fs:65` -- `failwithf` for missing required property.**  
This throws a generic `System.Exception` instead of the `FyperMappingException` that was designed for this purpose. Should be:
```fsharp
raise (FyperMappingException(
    sprintf "Missing required property '%s' for type '%s'" cypherName recordType.Name,
    fi.PropertyType, GNull))
```

**I-14: `ExprCompiler.fs:158` and `QueryTranslator.fs:408,454` -- `failwithf` instead of typed exceptions.**  
Multiple error paths use `failwithf` (raw `System.Exception`) instead of the purpose-built `FyperException` types. Users can't reliably catch and handle these errors.

**I-15: `Neo4jDriver.fs:136-137` -- `session.CloseAsync()` in `finally` is fire-and-forget.**  
```fsharp
finally
    session.CloseAsync() |> ignore
```
The `Task` returned by `CloseAsync()` is discarded. If the close fails, the error is silently swallowed. If the close is slow, execution continues before the session is released, potentially exhausting the connection pool.

---

## 4. API Design -- Rating: 9/10

### Strengths

- **The CE API is excellent.** `cypher { for p in node<Person> do ... }` is intuitive and discoverable.
- **`Quote` member for auto-quotation** eliminates the need for explicit `<@ @>` brackets -- a significant ergonomic win.
- **Operator overloading for edge patterns** (`p -- edge<ActedIn> --> m`) is creative and readable.
- **`toCypher` for debugging** is essential and well-placed.
- **Dual API**: CE for typical use, raw `Query.empty |> ...` for advanced users.
- **`optionalNode<T>`** for OPTIONAL MATCH is elegant.
- **`set (fun p -> { p with Age = 51 })` using record-update syntax** is brilliant -- it's the most natural F# way to express property updates.

### Issues

**I-16: No `countDistinct` in CE tests.**  
`countDistinct` is defined in `Operators.fs:43` and handled in `ExprCompiler.fs:123`, but no test exercises it through the CE. It likely works, but it's an untested public API surface.

**I-17: `withClause` naming is verbose.**  
The CE operation is named `withClause` to avoid collision with the `with` keyword, but `Cypher.with'` (used in the raw API module) is more idiomatic F#. Consider `with'` as the CE keyword too -- F# allows quoted identifiers in CE custom operations.

**I-18: `CypherBuilder.fs:89` -- `Create` CE member doesn't accept an alias parameter.**  
When creating a node, the alias is auto-generated from the first character of the label (`Schema.fs` resolveLabel -> lowercase first char). So `create { Name = "Tom"; Age = 50 }` generates `(p:Person {name: $p0, age: $p1})`. But if there are multiple Person nodes, they'd all get alias `p`, causing Cypher errors. The user has no way to specify the alias.

---

## 5. Performance -- Rating: 8/10

### Strengths

- **Schema metadata cached** in `ConcurrentDictionary` -- good for hot paths.
- **Parameterization avoids string formatting of values** -- no alloc-heavy `sprintf` for user data.
- **Parser uses `ResizeArray`** for token accumulation, avoiding repeated list allocation.

### Issues

**I-19: `Ast.fs:129` -- Repeated list append `@` for clause accumulation.**  
As noted in I-4, `q.Clauses @ [clause]` is O(n) per addition. With `n` clauses, building a query is O(n^2). For typical queries (5-15 clauses) this is negligible, but it's worth documenting as a known limitation.

**I-20: `QueryTranslator.fs` -- Multiple independent `ExprCompiler.newState()` allocations.**  
Each CE operation (Where, OrderBy, Select, etc.) creates a new `ExprCompileState`. The `ParamIndex` is synchronized manually via `exprState.ParamIndex <- state'.ParamCounter`. This works but creates unnecessary `Map.empty` allocations for the Parameters field of each temporary state. A single shared state would be cleaner.

**I-21: `AgeDriver.fs:208-214` -- `LOAD 'age'` executed on every query.**  
```fsharp
let initConnection (conn: NpgsqlConnection) =
    task {
        use initCmd = new NpgsqlCommand("LOAD 'age'", conn)
        ...
    }
```
Every read/write query opens a new connection and runs `LOAD 'age'` + `SET search_path`. This is a performance issue under load. The init should be done once per connection, or the AGE extension should be loaded via `postgresql.conf`.

---

## 6. Test Coverage -- Rating: 8/10

### Strengths

- **239 tests, all passing.**
- **Property-based tests** verify the parameterization invariant and naming conventions.
- **Design doc tests** ensure every documented example works -- excellent for a 1.0.
- **Roundtrip tests** (parse -> compile -> compare) validate parser/compiler consistency.
- **Capability validation tests** cover all feature flags.

### Gaps

**I-22: No `ResultMapper` unit tests.**  
The `ResultMapper` module is entirely untested. There are no tests for:
- `convertValue` with various type combinations (int -> int64, GNull -> option, etc.)
- `mapRecord` with missing optional fields
- `mapGraphRecord` with tuple types
- `mapGraphRecord` with single-value records
- Error cases (missing required field, type mismatch)

This is the **highest priority gap** -- result mapping is where runtime errors are most likely.

**I-23: No Neo4j/AGE driver unit tests.**  
The driver implementations (`Neo4jDriver.fs`, `AgeDriver.fs`) have zero unit tests. Key untested areas:
- `ValueMapper.toGraphValue` (Neo4j type mapping)
- `AgtypeParser.parseAgtype` (AGE JSON parsing)
- `CypherWrapper.wrapCypher` (SQL wrapping with parameter remapping)
- `CypherWrapper.extractReturnAliases` (regex-based alias extraction)
- Disposed driver behavior

**I-24: No negative test cases for `ExprCompiler`.**  
All ExprCompiler tests use valid quotations. There are no tests for:
- Unsupported expression types (what error do you get?)
- Deeply nested property access (e.g., `p.Address.City.ZipCode`)

**I-25: No test for `Cypher.inTransaction`.**  
The transaction API (`Cypher.fs:42-53`) is untested. The rollback-on-exception behavior is critical but only verifiable with a mock driver.

**I-26: No test for the `RawCypher` escape hatch.**  
While `RawCypher` is tested in the compiler tests, there is no test verifying it works through the public `Cypher.rawAsync` API.

**I-27: No test for `CypherBuilder.fs:30` -- `For` with non-record types.**  
What happens if someone writes `for x in node<int> do`? The Schema module will return no properties, and the label will be `Int32`. This should either work gracefully or produce a clear error.

---

## 7. Security (Parameterization Invariant) -- Rating: 9/10

### Strengths

- **All values parameterized by design.** The `ExprCompiler` converts every `Value` node to a `Param` reference. The property test at `PropertyTests.fs:72-85` verifies this invariant.
- **`Literal` AST case exists but is unreachable** from the CE path -- only the Parser creates `Literal` nodes (for parsed queries, not user-generated ones).
- **No string interpolation of user values** anywhere in the compilation pipeline.

### Potential Bypass Vectors

**I-28: `RawCypher` clause allows arbitrary Cypher injection.**  
```fsharp
| RawCypher of string
```
The `RawCypher` clause (and `Cypher.rawAsync`) is an intentional escape hatch, but it should be documented with a security warning. It's not accessible from the CE, only from the raw AST API, which limits exposure.

**I-29: `Literal` case in `compileExpr` inlines values into the Cypher string.**  
```fsharp
| Literal v -> literalToString v
```
If someone constructs an AST manually with `Literal (box "'; DROP TABLE users; --")`, it would be inlined (albeit with single-quote escaping). The `literalToString` function's escaping (`s.Replace("'", "\\'")`) would produce `\'`, which is NOT valid Cypher escape syntax (Cypher uses `''` to escape single quotes inside strings, not `\'`). This is a **Cypher injection vulnerability** for hand-crafted ASTs.

**I-30: Parser creates `Literal` nodes for parsed string/number values.**  
When the Parser parses `WHERE n.name = 'Tom'`, it creates `Literal (box "Tom")` instead of a `Param`. This means parsed-then-recompiled queries inline values instead of parameterizing them. The `collectParams` function in `Parser.fs:619-642` only collects `Param` references, not `Literal` values. Parsed queries that are recompiled will have inlined literals.

---

## Summary

| Area | Rating | Key Issues |
|------|--------|------------|
| Code Quality | 8/10 | Dead code in updateLastMerge, verbose namespaces in ExprCompiler |
| Correctness | 7/10 | Silent fallback in translator, fragile edge pattern extraction, Neo4j ID hashing, ResultMapper key ordering |
| Error Handling | 8/10 | Stack trace loss in transaction, raw exceptions instead of typed FyperException |
| API Design | 9/10 | Excellent CE ergonomics, creative operator design |
| Performance | 8/10 | AGE init-per-query overhead, O(n) clause append (negligible at typical sizes) |
| Test Coverage | 8/10 | No ResultMapper tests, no driver tests, no transaction tests |
| Security | 9/10 | Literal escaping uses wrong Cypher syntax, Parser creates Literals not Params |

**Overall: 8.1/10** -- Strong foundation for a 1.0 release with a few issues that should be addressed.

---

## Priority Fixes for 1.0.0

### P0 -- Must Fix Before Release

1. **I-29: Fix `literalToString` escaping.** Change `s.Replace("'", "\\'")` to `s.Replace("'", "''")` in `CypherCompiler.fs:80`. Cypher uses doubled single quotes, not backslash escaping. Current code is a **Cypher injection vector** for manually constructed ASTs.

2. **I-9: Fix tuple result mapping key ordering.** `ResultMapper.fs:83-89` must map by key name, not by position. Use the record field names or return-item aliases to match tuple elements to graph record values. Without this fix, tuple results will silently return wrong values if column names are not alphabetically ordered.

3. **I-6: Replace silent fallback with error.** Change `QueryTranslator.fs:260` from `| _ -> state` to `| _ -> failwithf "Unsupported expression in cypher CE: %A" expr`. Silent data loss in a query builder is unacceptable for a 1.0.

4. **I-22: Add ResultMapper tests.** This is the most error-prone module with zero test coverage. At minimum: GNull -> option, GInt -> int/int64, GNode -> record, tuple mapping, missing-field error.

### P1 -- Should Fix Before Release

5. **I-10: Neo4j ElementId hashing.** Store string IDs or parse numeric suffixes. Current approach loses node identity.
6. **I-12: Fix transaction stack trace preservation.** Use `ExceptionDispatchInfo.Capture(ex).Throw()` instead of `raise ... .SourceException`.
7. **I-13: Use `FyperMappingException`** in ResultMapper instead of raw `failwithf`.
8. **I-15: Await `session.CloseAsync()`** in Neo4jDriver's finally blocks.
9. **I-1: Remove dead code** in `updateLastMerge`.

### P2 -- Nice to Have

10. **I-2: Add `module QP` alias** in ExprCompiler for readability.
11. **I-11: Use `Regex.Replace` or ordered replacement** for AGE parameter remapping.
12. **I-23: Add driver unit tests** (ValueMapper, AgtypeParser, CypherWrapper).
13. **I-21: Cache AGE connection initialization** or document the performance implication.
14. **I-14: Replace `failwithf` with typed exceptions** throughout.
15. **I-30: Consider parameterizing Literal values** when recompiling parsed queries.
