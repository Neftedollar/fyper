# Fyper API Reference

## Computation Expression: `cypher { }`

The `cypher` builder is the primary way to construct Cypher queries. All code inside the CE is auto-quoted -- it is captured as an expression tree and translated to Cypher, never executed as F#.

```fsharp
open Fyper

let query = cypher {
    for p in node<Person> do
    where (p.Age > 30)
    select p
}
```

### Node Binding: `for ... in node<'T> do`

Generates a `MATCH (alias:Label)` clause. The variable name becomes the Cypher alias. The type name becomes the label.

```fsharp
// MATCH (p:Person)
for p in node<Person> do

// OPTIONAL MATCH (m:Movie)
for m in optionalNode<Movie> do
```

**Signature**: `For : NodeSource<'T> * ('T -> CypherQuery<'R>) -> CypherQuery<'R>`

### `where`

Generates a `WHERE` clause from a boolean predicate.

```fsharp
// WHERE p.age > $p0
where (p.Age > 30)

// WHERE (p.name = $p0) AND (p.age >= $p1)
where (p.Name = "Alice" && p.Age >= 25)

// WHERE p.name CONTAINS $p0
where (p.Name.Contains("ali"))

// WHERE p.name STARTS WITH $p0
where (p.Name.StartsWith("A"))
```

**Signature**: `Where : CypherQuery<'T> * ('T -> bool) -> CypherQuery<'T>`

Supported operations in predicates:
- Comparison: `=`, `<>`, `>`, `>=`, `<`, `<=`
- Logical: `&&` (AND), `||` (OR), `not` (NOT)
- String: `.Contains()`, `.StartsWith()`, `.EndsWith()`
- Arithmetic: `+`, `-`, `*`, `/`, `%`

### `select`

Generates a `RETURN` clause. Accepts variables, property projections, tuples, and anonymous records.

```fsharp
// RETURN p
select p

// RETURN p.name AS name
select p.Name

// RETURN p, m
select (p, m)

// RETURN p.name AS name, count(*) AS count
select {| Name = p.Name; Count = count() |}
```

**Signature**: `Select : CypherQuery<'T> * ('T -> 'R) -> CypherQuery<'R>`

### `selectDistinct`

Generates a `RETURN DISTINCT` clause. Same projection syntax as `select`.

```fsharp
// RETURN DISTINCT p.name AS name
selectDistinct p.Name
```

**Signature**: `SelectDistinct : CypherQuery<'T> * ('T -> 'R) -> CypherQuery<'R>`

### `orderBy`

Generates an `ORDER BY ... ASC` clause.

```fsharp
// ORDER BY p.age
orderBy p.Age
```

**Signature**: `OrderBy : CypherQuery<'T> * ('T -> 'Key) -> CypherQuery<'T>`

### `orderByDesc`

Generates an `ORDER BY ... DESC` clause.

```fsharp
// ORDER BY p.age DESC
orderByDesc p.Age
```

**Signature**: `OrderByDescending : CypherQuery<'T> * ('T -> 'Key) -> CypherQuery<'T>`

### `skip`

Generates a `SKIP $skip_N` clause (parameterized).

```fsharp
// SKIP $skip_0
skip 10
```

**Signature**: `Skip : CypherQuery<'T> * int -> CypherQuery<'T>`

### `limit`

Generates a `LIMIT $limit_N` clause (parameterized).

```fsharp
// LIMIT $limit_0
limit 25
```

**Signature**: `Limit : CypherQuery<'T> * int -> CypherQuery<'T>`

### `matchRel`

Generates a `MATCH` clause with a relationship pattern using the `-<` and `>-` operators.

```fsharp
// MATCH (p:Person)-[:ACTED_IN]->(m:Movie)
matchRel (p -< edge<ActedIn> >- m)
```

**Signature**: `MatchRel : CypherQuery<'T> * ('T -> EdgePattern<'A, 'R, 'B>) -> CypherQuery<'T>`

### `matchPath`

Generates a `MATCH` clause with a variable-length path.

```fsharp
// MATCH (p:Person)-[:KNOWS*1..5]->(other:Person)
matchPath (p -< edge<Knows> >- other) (Between(1, 5))

// MATCH (p:Person)-[:KNOWS*]->(other:Person)
matchPath (p -< edge<Knows> >- other) AnyLength

// MATCH (p:Person)-[:KNOWS*3]->(other:Person)
matchPath (p -< edge<Knows> >- other) (Exactly 3)
```

**Signature**: `MatchPath : CypherQuery<'T> * ('T -> EdgePattern<'A, 'R, 'B>) * PathLength -> CypherQuery<'T>`

`PathLength` is a discriminated union:

```fsharp
type PathLength =
    | Exactly of int        // *3
    | Between of int * int  // *1..5
    | AtLeast of int        // *2..
    | AtMost of int         // *..5
    | AnyLength             // *
```

### `create`

Generates a `CREATE` clause from a record literal.

```fsharp
// CREATE (p:Person {name: $p0, age: $p1})
create { Name = "Alice"; Age = 30 }
```

**Signature**: `Create : CypherQuery<'T> * 'V -> CypherQuery<'T>`

### `createRel`

Generates a `CREATE` clause for a relationship between existing CE variables.

```fsharp
// CREATE (p)-[:ACTED_IN]->(m)
createRel (p -< edge<ActedIn> >- m)
```

**Signature**: `CreateRel : CypherQuery<'T> * ('T -> EdgePattern<'A, 'R, 'B>) -> CypherQuery<'T>`

### `set`

Generates a `SET` clause from a record update expression. Only changed fields are included in the SET.

```fsharp
// SET p.age = $p0  (unchanged fields like p.name are omitted)
set (fun p -> { p with Age = 51 })
```

**Signature**: `Set : CypherQuery<'T> * ('T -> 'T) -> CypherQuery<'T>`

### `delete`

Generates a `DELETE` clause.

```fsharp
// DELETE p
delete p

// DELETE p, m
delete (p, m)
```

**Signature**: `Delete : CypherQuery<'T> * ('T -> 'V) -> CypherQuery<'T>`

### `detachDelete`

Generates a `DETACH DELETE` clause (deletes node and all connected relationships).

```fsharp
// DETACH DELETE p
detachDelete p
```

**Signature**: `DetachDelete : CypherQuery<'T> * ('T -> 'V) -> CypherQuery<'T>`

### `merge`

Generates a `MERGE` clause from a record literal.

```fsharp
// MERGE (p:Person {name: $p0, age: $p1})
merge { Name = "Alice"; Age = 30 }
```

**Signature**: `Merge : CypherQuery<'T> * 'V -> CypherQuery<'T>`

### `onMatch`

Adds `ON MATCH SET` to the last `MERGE` clause.

```fsharp
// MERGE (p:Person {name: $p0}) ON MATCH SET p.age = $p1
merge { Name = "Alice"; Age = 0 }
onMatch (fun p -> { p with Age = 31 })
```

**Signature**: `OnMatch : CypherQuery<'T> * ('T -> 'V) -> CypherQuery<'T>`

### `onCreate`

Adds `ON CREATE SET` to the last `MERGE` clause.

```fsharp
// MERGE (p:Person {name: $p0}) ON CREATE SET p.age = $p1
merge { Name = "Alice"; Age = 0 }
onCreate (fun p -> { p with Age = 30 })
```

**Signature**: `OnCreate : CypherQuery<'T> * ('T -> 'V) -> CypherQuery<'T>`

### `unwind`

Generates an `UNWIND ... AS ...` clause.

```fsharp
// UNWIND $p0 AS x
unwind [1; 2; 3] "x"
```

**Signature**: `Unwind : CypherQuery<'T> * 'V list * string -> CypherQuery<'T>`

### `withClause`

Generates a `WITH` clause for query chaining.

```fsharp
// WITH p.name AS name
withClause p.Name
```

**Signature**: `WithClause : CypherQuery<'T> * ('T -> 'R) -> CypherQuery<'R>`

---

## Cypher Module

The `Fyper.Cypher` module provides functions for executing and inspecting queries.

### `executeAsync`

Execute a read query and return typed results.

```fsharp
val executeAsync<'T> : driver: IGraphDriver -> query: CypherQuery<'T> -> Task<'T list>
```

```fsharp
let! people = Cypher.executeAsync driver query
// people : Person list
```

### `executeWriteAsync`

Execute a write query and return the count of affected entities.

```fsharp
val executeWriteAsync<'T> : driver: IGraphDriver -> query: CypherQuery<'T> -> Task<int>
```

```fsharp
let! affected = Cypher.executeWriteAsync driver createQuery
// affected : int (nodes created + deleted + relationships created + deleted + properties set)
```

### `rawAsync`

Execute raw Cypher string (escape hatch). Returns untyped `GraphRecord` results.

```fsharp
val rawAsync : driver: IGraphDriver -> cypher: string -> parameters: Map<string, obj> -> Task<GraphRecord list>
```

```fsharp
let! records = Cypher.rawAsync driver "MATCH (n) RETURN n LIMIT 10" Map.empty
```

### `toCypher`

Compile a query to its Cypher string and parameters without executing it. Useful for debugging, logging, and testing.

```fsharp
val toCypher : query: CypherQuery<'T> -> string * Map<string, obj>
```

```fsharp
let cypherStr, parameters = Cypher.toCypher query
// cypherStr = "MATCH (p:Person)\nWHERE (p.age > $p0)\nRETURN p"
// parameters = map ["p0", box 30]
```

### `toDebugString`

Like `toCypher` but returns a single formatted string including parameters.

```fsharp
val toDebugString : query: CypherQuery<'T> -> string
```

### `inTransaction`

Execute a function within an explicit transaction. Auto-commits on success, auto-rollbacks on exception.

```fsharp
val inTransaction : driver: IGraphDriver -> action: (IGraphTransaction -> Task<'T>) -> Task<'T>
```

```fsharp
let! result = Cypher.inTransaction driver (fun tx ->
    task {
        let! _ = tx.ExecuteWriteAsync("CREATE (n:Person {name: $name})", Map.ofList ["name", box "Alice"])
        let! _ = tx.ExecuteWriteAsync("CREATE (n:Person {name: $name})", Map.ofList ["name", box "Bob"])
        return ()
    })
```

---

## Operators Module

The `Fyper.Operators` module is `[<AutoOpen>]` and provides phantom types and quotation-only functions used inside `cypher { }`. All of these throw if called at runtime.

### Node Sources

```fsharp
val node<'T> : NodeSource<'T>
val optionalNode<'T> : NodeSource<'T>
```

`node<Person>` is used in `for p in node<Person> do` to generate `MATCH (p:Person)`.
`optionalNode<Movie>` generates `OPTIONAL MATCH (m:Movie)`.

### Edge Pattern Operators

```fsharp
type EdgeType<'R>
val edge<'R> : EdgeType<'R>

val ( -< ) : 'A -> EdgeType<'R> -> PartialEdge<'A, 'R>
val ( >- ) : PartialEdge<'A, 'R> -> 'B -> EdgePattern<'A, 'R, 'B>
```

Used together: `p -< edge<ActedIn> >- m` constructs a typed edge pattern. The relationship type name is derived from `'R` using `Schema.toRelType` (PascalCase to UPPER_SNAKE_CASE).

### Aggregate Functions

```fsharp
val count : unit -> int64            // count(*)
val countDistinct : 'T -> int64      // countDistinct(x)
val sum : 'T -> 'T                   // sum(x)
val avg : 'T -> float                // avg(x)
val collect : 'T -> 'T list          // collect(x)
val size : 'T -> int64               // size(x)
val cypherMin : 'T -> 'T             // min(x)
val cypherMax : 'T -> 'T             // max(x)
```

Note: `min` and `max` are named `cypherMin` and `cypherMax` to avoid shadowing F#'s built-in functions. They compile to `min()` and `max()` in Cypher.

### CASE Expression

```fsharp
val caseWhen : condition: bool -> result: 'T -> elseResult: 'T -> 'T
```

```fsharp
// CASE WHEN (p.age > $p0) THEN $p1 ELSE $p2 END
caseWhen (p.Age > 18) "adult" "minor"
```

---

## IGraphDriver Interface

```fsharp
type IGraphDriver =
    inherit IAsyncDisposable

    /// Execute a read query. Returns a list of result records.
    abstract ExecuteReadAsync: cypher: string * parameters: Map<string, obj> -> Task<GraphRecord list>

    /// Execute a write query. Returns the count of affected entities.
    abstract ExecuteWriteAsync: cypher: string * parameters: Map<string, obj> -> Task<int>

    /// Begin an explicit transaction for multi-statement atomicity.
    abstract BeginTransactionAsync: unit -> Task<IGraphTransaction>

    /// Declare which Cypher features this backend supports.
    abstract Capabilities: DriverCapabilities
```

## IGraphTransaction Interface

```fsharp
type IGraphTransaction =
    inherit IAsyncDisposable

    abstract ExecuteReadAsync: cypher: string * parameters: Map<string, obj> -> Task<GraphRecord list>
    abstract ExecuteWriteAsync: cypher: string * parameters: Map<string, obj> -> Task<int>
    abstract CommitAsync: unit -> Task<unit>
    abstract RollbackAsync: unit -> Task<unit>
```

## DriverCapabilities

```fsharp
type DriverCapabilities = {
    SupportsOptionalMatch: bool
    SupportsMerge: bool
    SupportsUnwind: bool
    SupportsCase: bool
    SupportsCallProcedure: bool
    SupportsExistsSubquery: bool
    SupportsNamedPaths: bool
}
```

Predefined configurations:

| Config | Used By | All Features? |
|---|---|---|
| `DriverCapabilities.all` | Neo4j | Yes |
| `DriverCapabilities.minimal` | Apache AGE | No -- all set to `false` |

---

## Schema Attributes

### `[<Label>]`

Override the default node label or relationship type name derived from the type name.

```fsharp
[<Label("Film")>]
type Movie = { Title: string; Year: int }
// MATCH (m:Film) instead of MATCH (m:Movie)
```

### `[<CypherName>]`

Override the default camelCase property name derived from the F# record field name.

```fsharp
type Person = {
    [<CypherName("full_name")>]
    Name: string
    Age: int
}
// WHERE p.full_name = $p0 instead of WHERE p.name = $p0
```

---

## Exception Types

All exceptions inherit from `FyperException`.

| Exception | When | Notable Members |
|---|---|---|
| `FyperException(message, ?inner)` | Base type for all Fyper errors | -- |
| `FyperConnectionException(message, ?inner)` | Connection or authentication failure (e.g., disposed driver) | -- |
| `FyperQueryException(message, query, parameters, inner)` | Cypher execution failure (syntax error, constraint violation) | `Query: string`, `Parameters: Map<string, obj>` |
| `FyperMappingException(message, targetType, sourceValue, ?inner)` | Result mapping failure (type mismatch) | `TargetType: Type`, `SourceValue: GraphValue` |
| `FyperUnsupportedFeatureException(feature, backend)` | Query uses a feature not supported by the backend | `Feature: string`, `Backend: string` |

---

## GraphValue Types

```fsharp
type GraphValue =
    | GNull
    | GBool of bool
    | GInt of int64
    | GFloat of float
    | GString of string
    | GList of GraphValue list
    | GMap of Map<string, GraphValue>
    | GNode of GraphNode
    | GRel of GraphRel
    | GPath of GraphPath

type GraphNode = { Id: int64; Labels: string list; Properties: Map<string, GraphValue> }
type GraphRel = { Id: int64; RelType: string; StartNodeId: int64; EndNodeId: int64; Properties: Map<string, GraphValue> }
type GraphPath = { Nodes: GraphNode list; Relationships: GraphRel list }
type GraphRecord = { Keys: string list; Values: Map<string, GraphValue> }
```

---

## Query Module (Raw AST API)

The `Ast.Query` module provides functional helpers for building queries without the CE.

```fsharp
val empty<'T> : CypherQuery<'T>
val addClause : Clause -> CypherQuery<'T> -> CypherQuery<'T>
val addParam : string -> obj -> CypherQuery<'T> -> CypherQuery<'T>
val matchNodes : Pattern list -> CypherQuery<'T> -> CypherQuery<'T>
val optionalMatch : Pattern list -> CypherQuery<'T> -> CypherQuery<'T>
val where : Expr -> CypherQuery<'T> -> CypherQuery<'T>
val return' : ReturnItem list -> CypherQuery<'T> -> CypherQuery<'T>
val returnDistinct : ReturnItem list -> CypherQuery<'T> -> CypherQuery<'T>
val with' : ReturnItem list -> CypherQuery<'T> -> CypherQuery<'T>
val orderBy : (Expr * SortDirection) list -> CypherQuery<'T> -> CypherQuery<'T>
val skip : int -> CypherQuery<'T> -> CypherQuery<'T>
val limit : int -> CypherQuery<'T> -> CypherQuery<'T>
val create : Pattern list -> CypherQuery<'T> -> CypherQuery<'T>
val delete : string list -> CypherQuery<'T> -> CypherQuery<'T>
val detachDelete : string list -> CypherQuery<'T> -> CypherQuery<'T>
val set : SetItem list -> CypherQuery<'T> -> CypherQuery<'T>
```

Example using pipeline style:

```fsharp
open Fyper.Ast

let query =
    Query.empty<Person>
    |> Query.matchNodes [NodePattern("p", Some "Person", Map.empty)]
    |> Query.where (BinOp(Property("p", "age"), Gt, Param "minAge"))
    |> Query.addParam "minAge" (box 30)
    |> Query.return' [{ Expr = Variable "p"; Alias = None }]
```
