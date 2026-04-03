---
layout: default
title: CE Operations
parent: Reference
description: "All Fyper CE operations: WHERE, SELECT, ORDER BY, matchRel, CREATE, SET, DELETE, MERGE, aggregations."
nav_order: 1
---

# CE Operations

All operations available inside `cypher { }`.

## Quick Reference

| Operation | Cypher | Example |
|-----------|--------|---------|
| `for p in node<T> do` | `MATCH (p:T)` | `for p in node<Person> do` |
| `for m in optionalNode<T> do` | `OPTIONAL MATCH (m:T)` | `for m in optionalNode<Movie> do` |
| `where (expr)` | `WHERE expr` | `where (p.Age > 30)` |
| `select expr` | `RETURN expr` | `select p` / `select (p.Name, m.Title)` |
| `selectDistinct expr` | `RETURN DISTINCT expr` | `selectDistinct p.Name` |
| `orderBy expr` | `ORDER BY expr` | `orderBy p.Age` |
| `orderByDesc expr` | `ORDER BY expr DESC` | `orderByDesc p.Age` |
| `skip n` | `SKIP $skip_N` | `skip 10` |
| `limit n` | `LIMIT $limit_N` | `limit 5` |
| `matchRel (a -- edge<R> --> b)` | `MATCH (a)-[:R]->(b)` | `matchRel (p -- edge<ActedIn> --> m)` |
| `matchPath (...) len` | `MATCH (a)-[:R*N..M]->(b)` | `matchPath (...) (Between(1,5))` |
| `create record` | `CREATE (:T {props})` | `create { Name = "Tom"; Age = 50 }` |
| `createRel (a -- edge<R> --> b)` | `CREATE (a)-[:R]->(b)` | `createRel (p -- edge<ActedIn> --> m)` |
| `set (fun x -> { x with ... })` | `SET x.prop = val` | `set (fun p -> { p with Age = 51 })` |
| `delete x` | `DELETE x` | `delete p` |
| `detachDelete x` | `DETACH DELETE x` | `detachDelete p` |
| `merge record` | `MERGE (:T {props})` | `merge { Name = "Tom"; Age = 0 }` |
| `onMatch (fun x -> ...)` | `ON MATCH SET ...` | `onMatch (fun p -> { p with Age = 50 })` |
| `onCreate (fun x -> ...)` | `ON CREATE SET ...` | `onCreate (fun p -> { p with Age = 25 })` |
| `unwind list alias` | `UNWIND $p AS alias` | `unwind names "name"` |
| `withClause expr` | `WITH expr` | `withClause p` |

## Aggregation Functions

| Function | Cypher | Example |
|----------|--------|---------|
| `count()` | `count(*)` | `select (count())` |
| `sum(expr)` | `sum(expr)` | `select (sum(p.Age))` |
| `avg(expr)` | `avg(expr)` | `select (avg(p.Age))` |
| `collect(expr)` | `collect(expr)` | `select (collect(p.Name))` |
| `cypherMin(expr)` | `min(expr)` | `select (cypherMin(p.Age))` |
| `cypherMax(expr)` | `max(expr)` | `select (cypherMax(p.Age))` |
| `countDistinct(expr)` | `countDistinct(expr)` | `select (countDistinct(p.Name))` |
| `size(expr)` | `size(expr)` | `select (size(p.Name))` |
| `caseWhen cond then else` | `CASE WHEN...END` | `caseWhen (p.Age > 18) p.Name "minor"` |

## WHERE Operators

| F# | Cypher |
|----|--------|
| `=` | `=` |
| `<>` | `<>` |
| `>` `>=` `<` `<=` | `>` `>=` `<` `<=` |
| `&&` | `AND` |
| `\|\|` | `OR` |
| `not` | `NOT` |
| `.Contains("x")` | `CONTAINS` |
| `.StartsWith("x")` | `STARTS WITH` |
| `.EndsWith("x")` | `ENDS WITH` |
| `+ - * / %` | `+ - * / %` |

## Path Lengths

| F# | Cypher |
|----|--------|
| `Between(1, 5)` | `*1..5` |
| `Exactly 3` | `*3` |
| `AtLeast 2` | `*2..` |
| `AtMost 5` | `*..5` |
| `AnyLength` | `*` |

## See Also

- [Getting Started](../guide/getting-started.md) -- first query tutorial
- [Relationships](../guide/relationships.md) -- matchRel and matchPath in depth
- [Mutations](../guide/mutations.md) -- CREATE, SET, DELETE examples
- [Functions Reference](functions.md) -- execution and inspection APIs
- [Types Reference](types.md) -- CypherQuery, AST types
