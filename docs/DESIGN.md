# Fyper — Design Document

> **Note**: This is the original design document from project inception. Some syntax has evolved during implementation:
> - `match'` is now `matchRel`
> - `p -[edge<ActedIn>]-> m` is now `p -- edge<ActedIn> --> m`
> - `Query.match'` is now `Query.matchNodes`
>
> See [CE Operations](reference/ce-operations.md) for current syntax.

F# ORM / typed query builder for Cypher-based graph databases (Neo4j, Apache AGE, Memgraph).

## 1. Goals

- **Type-safe Cypher**: compile-time checked queries via F# type system
- **Idiomatic F#**: DUs, pattern matching, computation expressions, type inference
- **Multi-backend**: single query → Neo4j Bolt / Apache AGE (PostgreSQL) / any Cypher DB
- **Zero boilerplate schema**: plain F# records = graph nodes/relationships
- **Parameterized by default**: all values become `$params`, never string interpolation

## 2. User-Facing API

### 2.1 Schema Definition

Plain F# record types. Convention: type name = node label, field name → camelCase property.

```fsharp
// Nodes — just records
type Person = { Name: string; Age: int }
type Movie = { Title: string; Released: int }

// Relationships — just records
type ActedIn = { Roles: string list }
type Directed = { Since: int }
```

No attributes, no interfaces, no base classes required by default.

Optional customization:

```fsharp
[<Label "PERSON">]
type Person = { Name: string; Age: int }

[<CypherName "birth_year">]
type Person = { Name: string; BirthYear: int }
```

### 2.2 Queries — Computation Expression

Primary API. Uses `Quote` member for auto-quotation — user writes plain F# inside the CE.

```fsharp
// Simple node query
let findOldPeople = cypher {
    for p in node<Person> do
    where (p.Age > 30)
    select p
}
// → MATCH (p:Person) WHERE p.age > $p0 RETURN p

// With relationship
let findActors = cypher {
    for p in node<Person> do
    for m in node<Movie> do
    match' (p -[edge<ActedIn>]-> m)
    where (p.Age > 30 && m.Released >= 1999)
    orderBy m.Released
    select (p.Name, m.Title)
}
// → MATCH (p:Person), (m:Movie)
//   MATCH (p)-[:ACTED_IN]->(m)
//   WHERE p.age > $p0 AND m.released >= $p1
//   ORDER BY m.released
//   RETURN p.name, m.title

// Explicit quotation also works (Quote wraps it transparently)
let findByName name = cypher {
    for p in node<Person> do
    where <@ p.Name = name @>
    select p
}

// Optional match
let findWithOptionalMovies = cypher {
    for p in node<Person> do
    for m in optionalNode<Movie> do
    match' (p -[edge<ActedIn>]-> m)
    select (p, m)
}
// → MATCH (p:Person) OPTIONAL MATCH (p)-[:ACTED_IN]->(m:Movie) RETURN p, m

// Aggregation
let countByAge = cypher {
    for p in node<Person> do
    select {| Age = p.Age; Count = count() |}
}

// Limit/Skip
let paginated = cypher {
    for p in node<Person> do
    orderByDesc p.Age
    skip 10
    limit 5
    select p
}

// Create
let createPerson = cypher {
    create (node<Person> { Name = "Tom"; Age = 50 })
}

// Create with relationship
let createActedIn = cypher {
    for p in node<Person> do
    for m in node<Movie> do
    where (p.Name = "Tom" && m.Title = "The Matrix")
    create (p -[edge<ActedIn> { Roles = ["Neo"] }]-> m)
}

// Merge
let ensurePerson name = cypher {
    merge (node<Person> { Name = name; Age = 0 })
    onMatch (fun p -> { p with Age = p.Age }) // no-op, keeps existing
    onCreate (fun p -> { p with Age = 25 })   // default age
    select p
}

// Delete
let deletePerson = cypher {
    for p in node<Person> do
    where (p.Name = "Tom")
    detachDelete p
}

// Set / update
let birthday = cypher {
    for p in node<Person> do
    where (p.Name = "Tom")
    set (fun p -> { p with Age = p.Age + 1 })
    select p
}
```

### 2.3 Execution

```fsharp
open Fyper.Neo4j

let driver = Neo4jDriver("bolt://localhost:7687", "neo4j", "password")

// Read
let! people = findOldPeople |> Cypher.executeAsync driver
// people : Person list

// Write
let! count = createPerson |> Cypher.executeWriteAsync driver
// count : int (affected rows)

// Raw Cypher (escape hatch)
let! result = Cypher.rawAsync driver "MATCH (n) RETURN count(n) AS cnt" Map.empty
```

### 2.4 Raw AST API (escape hatch)

For when CE isn't flexible enough:

```fsharp
open Fyper.Ast
open Fyper.Compiler

let query =
    Query.empty
    |> Query.match' [NodePattern("p", Some "Person", Map.empty)]
    |> Query.where (BinOp(Property("p", "age"), Gt, Param "minAge"))
    |> Query.return' [{ Expr = Property("p", "name"); Alias = None }]
    |> Query.withParam "minAge" (box 30)

let cypherString, parameters = CypherCompiler.compile query
// cypherString = "MATCH (p:Person) WHERE p.age > $minAge RETURN p.name"
// parameters = Map ["minAge", box 30]
```

## 3. Architecture

```
┌──────────────────────────────────────────────────────┐
│              DSL Layer                                │
│  CypherBuilder (CE with Quote) + Operators           │
│  User writes:  cypher { for p in node<Person> do … } │
├──────────────────────────────────────────────────────┤
│              QueryTranslator                          │
│  Walks quoted CE → builds typed AST                  │
│  Expr<CypherQuery<'T>> → CypherQuery<'T>            │
├──────────────────────────────────────────────────────┤
│              Cypher AST (DU)                          │
│  Clause, Expr, Pattern — algebraic data types        │
├──────────────────────────────────────────────────────┤
│              CypherCompiler                           │
│  AST → Cypher string + Map<string, obj> params       │
│  Exhaustive pattern matching on all DU cases          │
├──────────────────────────────────────────────────────┤
│              IGraphDriver                             │
│  ExecuteReadAsync / ExecuteWriteAsync                │
├────────────┬─────────────┬───────────────────────────┤
│ Neo4j Bolt │ Apache AGE  │  (future backends)        │
├────────────┴─────────────┴───────────────────────────┤
│              ResultMapper                             │
│  GraphValue → F# records via reflection (cached)     │
└──────────────────────────────────────────────────────┘
```

Data flow: **CE → (Quote) → Expr tree → QueryTranslator → AST → CypherCompiler → string + params → Driver → GraphValue → ResultMapper → F# records**

## 4. Project Structure

```
Fyper.sln
├── src/
│   ├── Fyper/                         ← Main library (one NuGet package)
│   │   ├── Fyper.fsproj
│   │   ├── Schema.fs                  ← Naming conventions, metadata extraction
│   │   ├── Ast.fs                     ← Cypher AST as discriminated unions
│   │   ├── GraphValue.fs              ← Universal result type for graph data
│   │   ├── Driver.fs                  ← IGraphDriver interface
│   │   ├── ExprCompiler.fs            ← F# Quotation Expr → Cypher AST Expr
│   │   ├── CypherCompiler.fs          ← Full AST → Cypher string + params
│   │   ├── Operators.fs               ← Edge pattern operators for CE
│   │   ├── CypherBuilder.fs           ← Computation expression builder
│   │   ├── QueryTranslator.fs         ← Quoted CE → CypherQuery AST
│   │   ├── ResultMapper.fs            ← GraphValue → typed F# records
│   │   └── Cypher.fs                  ← Public API: executeAsync, etc.
│   ├── Fyper.Neo4j/
│   │   ├── Fyper.Neo4j.fsproj
│   │   └── Neo4jDriver.fs
│   └── Fyper.Age/
│       ├── Fyper.Age.fsproj
│       └── AgeDriver.fs
├── tests/
│   ├── Fyper.Tests/
│   │   ├── Fyper.Tests.fsproj
│   │   ├── SchemaTests.fs
│   │   ├── AstTests.fs
│   │   ├── CompilerTests.fs
│   │   ├── ExprCompilerTests.fs
│   │   ├── DslTests.fs
│   │   └── Program.fs
│   └── Fyper.Integration.Tests/
│       ├── Fyper.Integration.Tests.fsproj
│       ├── Neo4jTests.fs
│       └── AgeTests.fs
└── samples/
    └── Fyper.Sample/
        ├── Fyper.Sample.fsproj
        └── Program.fs
```

**F# compilation order** in `Fyper.fsproj` (dependencies go top to bottom):

```xml
<ItemGroup>
    <Compile Include="Schema.fs" />
    <Compile Include="Ast.fs" />
    <Compile Include="GraphValue.fs" />
    <Compile Include="Driver.fs" />
    <Compile Include="ExprCompiler.fs" />
    <Compile Include="CypherCompiler.fs" />
    <Compile Include="Operators.fs" />
    <Compile Include="CypherBuilder.fs" />
    <Compile Include="QueryTranslator.fs" />
    <Compile Include="ResultMapper.fs" />
    <Compile Include="Cypher.fs" />
</ItemGroup>
```

## 5. Type Definitions

### 5.1 Schema.fs — Metadata & Naming

```fsharp
module Fyper.Schema

open System
open System.Reflection
open System.Collections.Concurrent

// ─── Attributes (optional customization) ───

[<AttributeUsage(AttributeTargets.Class ||| AttributeTargets.Struct)>]
type LabelAttribute(name: string) =
    inherit Attribute()
    member _.Name = name

[<AttributeUsage(AttributeTargets.Property)>]
type CypherNameAttribute(name: string) =
    inherit Attribute()
    member _.Name = name

// ─── Naming convention ───

/// PascalCase → camelCase: "FirstName" → "firstName"
let toCypherName (name: string) : string =
    if String.IsNullOrEmpty name then name
    else string (Char.ToLowerInvariant name.[0]) + name.[1..]

// ─── Metadata types ───

type PropertyMeta = {
    FSharpName: string
    CypherName: string
    PropertyType: Type
    IsOption: bool
}

type TypeMeta = {
    ClrType: Type
    Label: string          // Node label or relationship type
    Properties: PropertyMeta list
}

// ─── Metadata cache ───

let private cache = ConcurrentDictionary<Type, TypeMeta>()

let resolveLabel (t: Type) : string =
    match t.GetCustomAttribute<LabelAttribute>() with
    | null -> t.Name
    | attr -> attr.Name

let resolvePropertyName (pi: PropertyInfo) : string =
    match pi.GetCustomAttribute<CypherNameAttribute>() with
    | null -> toCypherName pi.Name
    | attr -> attr.Name

let isOptionType (t: Type) : bool =
    t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>>

let getMeta (t: Type) : TypeMeta =
    cache.GetOrAdd(t, fun t ->
        let props =
            FSharpType.GetRecordFields(t)
            |> Array.map (fun pi -> {
                FSharpName = pi.Name
                CypherName = resolvePropertyName pi
                PropertyType = pi.PropertyType
                IsOption = isOptionType pi.PropertyType
            })
            |> Array.toList
        {
            ClrType = t
            Label = resolveLabel t
            Properties = props
        }
    )

let getMetaOf<'T> () : TypeMeta = getMeta typeof<'T>
```

### 5.2 Ast.fs — Cypher AST

```fsharp
module Fyper.Ast

/// Direction of a relationship in a pattern
type Direction =
    | Outgoing    // -[r:TYPE]->
    | Incoming    // <-[r:TYPE]-
    | Undirected  // -[r:TYPE]-

/// Variable-length path specification
type PathLength =
    | Exactly of int           // *3
    | Between of int * int     // *1..5
    | AtLeast of int           // *2..
    | AtMost of int            // *..5
    | AnyLength                // *

/// Pattern — represents a graph pattern in MATCH/CREATE/MERGE
type Pattern =
    | NodePattern of
        alias: string *
        label: string option *
        properties: Map<string, Expr>
    | RelPattern of
        from: Pattern *
        relAlias: string option *
        relType: string option *
        relProps: Map<string, Expr> *
        direction: Direction *
        pathLength: PathLength option *
        to': Pattern
    | NamedPath of
        pathName: string *
        pattern: Pattern

/// Cypher expression (used in WHERE, RETURN, SET, etc.)
and Expr =
    | Literal of obj                           // 42, "hello", true, null
    | Param of string                          // $paramName
    | Variable of string                       // p, m, r
    | Property of owner: string * name: string // p.name
    | BinOp of Expr * BinOp * Expr             // a > b
    | UnaryOp of UnaryOp * Expr                // NOT a
    | FuncCall of name: string * Expr list     // count(p), collect(p.name)
    | ListExpr of Expr list                    // [1, 2, 3]
    | MapExpr of (string * Expr) list          // {name: "Tom", age: 30}
    | CaseExpr of
        scrutinee: Expr option *
        whenClauses: (Expr * Expr) list *
        elseClause: Expr option
    | ExistsSubquery of Clause list            // EXISTS { MATCH ... }
    | Null

/// Binary operators
and BinOp =
    // Comparison
    | Eq | Neq | Gt | Gte | Lt | Lte
    // Logical
    | And | Or | Xor
    // String
    | Contains | StartsWith | EndsWith
    // Collection
    | In
    // Arithmetic
    | Add | Sub | Mul | Div | Mod
    // Regex
    | RegexMatch

/// Unary operators
and UnaryOp =
    | Not
    | IsNull
    | IsNotNull
    | Exists

/// Sort direction
and SortDirection = Ascending | Descending

/// Items in RETURN / WITH
and ReturnItem = {
    Expr: Expr
    Alias: string option
}

/// Items in SET clause
and SetItem =
    | SetProperty of owner: string * property: string * value: Expr
    | SetAllProperties of owner: string * value: Expr
    | MergeProperties of owner: string * value: Expr
    | AddLabel of owner: string * label: string

/// Items in REMOVE clause
and RemoveItem =
    | RemoveProperty of owner: string * property: string
    | RemoveLabel of owner: string * label: string

/// Cypher clauses — each becomes one line in the generated query
and Clause =
    | Match of patterns: Pattern list * optional: bool
    | Where of Expr
    | Return of items: ReturnItem list * distinct: bool
    | With of items: ReturnItem list * distinct: bool
    | Create of Pattern list
    | Merge of
        pattern: Pattern *
        onMatch: SetItem list *
        onCreate: SetItem list
    | Delete of aliases: string list * detach: bool
    | Set of SetItem list
    | Remove of RemoveItem list
    | OrderBy of (Expr * SortDirection) list
    | Skip of Expr
    | Limit of Expr
    | Unwind of expr: Expr * alias: string
    | Call of
        procedure: string *
        args: Expr list *
        yields: string list
    | Union of all: bool
    | RawCypher of string  // escape hatch

/// A complete Cypher query with phantom result type
type CypherQuery<'T> = {
    Clauses: Clause list
    Parameters: Map<string, obj>
}

/// Untyped query (for raw API)
type CypherQuery = {
    Clauses: Clause list
    Parameters: Map<string, obj>
}

// ─── Query builder helpers (raw AST API) ───

module Query =
    let empty<'T> : CypherQuery<'T> =
        { Clauses = []; Parameters = Map.empty }

    let untyped : CypherQuery =
        { Clauses = []; Parameters = Map.empty }

    let addClause (clause: Clause) (q: CypherQuery<'T>) : CypherQuery<'T> =
        { q with Clauses = q.Clauses @ [clause] }

    let addParam (name: string) (value: obj) (q: CypherQuery<'T>) : CypherQuery<'T> =
        { q with Parameters = q.Parameters |> Map.add name value }

    let match' (patterns: Pattern list) (q: CypherQuery<'T>) : CypherQuery<'T> =
        q |> addClause (Match(patterns, false))

    let optionalMatch (patterns: Pattern list) (q: CypherQuery<'T>) : CypherQuery<'T> =
        q |> addClause (Match(patterns, true))

    let where (expr: Expr) (q: CypherQuery<'T>) : CypherQuery<'T> =
        q |> addClause (Where expr)

    let return' (items: ReturnItem list) (q: CypherQuery<'T>) : CypherQuery<'T> =
        q |> addClause (Return(items, false))

    let returnDistinct (items: ReturnItem list) (q: CypherQuery<'T>) : CypherQuery<'T> =
        q |> addClause (Return(items, true))

    let orderBy (items: (Expr * SortDirection) list) (q: CypherQuery<'T>) : CypherQuery<'T> =
        q |> addClause (OrderBy items)

    let skip (n: int) (q: CypherQuery<'T>) : CypherQuery<'T> =
        q |> addClause (Skip(Param(sprintf "skip_%d" n)))
        |> addParam (sprintf "skip_%d" n) (box n)

    let limit (n: int) (q: CypherQuery<'T>) : CypherQuery<'T> =
        q |> addClause (Limit(Param(sprintf "limit_%d" n)))
        |> addParam (sprintf "limit_%d" n) (box n)

    let create (patterns: Pattern list) (q: CypherQuery<'T>) : CypherQuery<'T> =
        q |> addClause (Create patterns)

    let delete (aliases: string list) (q: CypherQuery<'T>) : CypherQuery<'T> =
        q |> addClause (Delete(aliases, false))

    let detachDelete (aliases: string list) (q: CypherQuery<'T>) : CypherQuery<'T> =
        q |> addClause (Delete(aliases, true))

    let set (items: SetItem list) (q: CypherQuery<'T>) : CypherQuery<'T> =
        q |> addClause (Set items)
```

### 5.3 GraphValue.fs — Universal Result Type

```fsharp
module Fyper.GraphValue

/// Universal representation of values returned from any Cypher database.
/// All drivers normalize their results into this type before result mapping.
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

and GraphNode = {
    Id: int64
    Labels: string list
    Properties: Map<string, GraphValue>
}

and GraphRel = {
    Id: int64
    RelType: string
    StartNodeId: int64
    EndNodeId: int64
    Properties: Map<string, GraphValue>
}

and GraphPath = {
    Nodes: GraphNode list
    Relationships: GraphRel list
}

/// A row of query results
type GraphRecord = {
    Keys: string list
    Values: Map<string, GraphValue>
}
```

### 5.4 Driver.fs — Interface

```fsharp
module Fyper.Driver

open System.Threading.Tasks
open Fyper.GraphValue

/// Abstract graph database driver.
/// Each backend (Neo4j, AGE, etc.) implements this interface.
type IGraphDriver =
    /// Execute a read query. Returns a list of result records.
    abstract ExecuteReadAsync:
        cypher: string *
        parameters: Map<string, obj>
        -> Task<GraphRecord list>

    /// Execute a write query. Returns the count of affected entities.
    abstract ExecuteWriteAsync:
        cypher: string *
        parameters: Map<string, obj>
        -> Task<int>

    /// Dispose/close the driver connection.
    inherit System.IAsyncDisposable
```

### 5.5 ExprCompiler.fs — F# Quotations → Cypher AST Expr

This module translates F# quotation expressions (from the CE's auto-quoting) into Cypher AST `Expr` nodes.

```fsharp
module Fyper.ExprCompiler

open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Quotations.Patterns
open Fyper.Ast
open Fyper.Schema

/// Compilation state: collects parameters as it encounters literals
type ExprCompileState = {
    mutable ParamIndex: int
    mutable Parameters: Map<string, obj>
}

let newState () = { ParamIndex = 0; Parameters = Map.empty }

let addParam (state: ExprCompileState) (value: obj) : string =
    let name = sprintf "p%d" state.ParamIndex
    state.ParamIndex <- state.ParamIndex + 1
    state.Parameters <- state.Parameters |> Map.add name value
    name

/// Compile an F# quotation Expr into a Cypher AST Expr.
/// Handles:
///   - Property access: p.Age → Property("p", "age")
///   - Comparison operators: >, <, =, <>, >=, <=
///   - Logical operators: &&, ||, not
///   - String methods: .Contains(), .StartsWith(), .EndsWith()
///   - List.contains → IN operator
///   - Literals → parameterized values
///   - Variable references
let rec compile (state: ExprCompileState) (expr: FSharp.Quotations.Expr) : Ast.Expr =
    match expr with
    // ─── Property access: p.Name → Property("p", "name") ───
    | PropertyGet(Some(Var v), propInfo, []) ->
        let cypherName = toCypherName propInfo.Name
        Property(v.Name, cypherName)

    // ─── Nested property: p.Address.City ───
    | PropertyGet(Some inner, propInfo, []) ->
        // Compile inner, but for Cypher this flattens
        let cypherName = toCypherName propInfo.Name
        match compile state inner with
        | Property(owner, prop) -> Property(owner, sprintf "%s.%s" prop cypherName)
        | Variable name -> Property(name, cypherName)
        | other -> other // fallback

    // ─── Comparison operators ───
    | Call(None, mi, [lhs; rhs]) when mi.Name = "op_Equality" ->
        BinOp(compile state lhs, Eq, compile state rhs)
    | Call(None, mi, [lhs; rhs]) when mi.Name = "op_Inequality" ->
        BinOp(compile state lhs, Neq, compile state rhs)
    | Call(None, mi, [lhs; rhs]) when mi.Name = "op_GreaterThan" ->
        BinOp(compile state lhs, Gt, compile state rhs)
    | Call(None, mi, [lhs; rhs]) when mi.Name = "op_GreaterThanOrEqual" ->
        BinOp(compile state lhs, Gte, compile state rhs)
    | Call(None, mi, [lhs; rhs]) when mi.Name = "op_LessThan" ->
        BinOp(compile state lhs, Lt, compile state rhs)
    | Call(None, mi, [lhs; rhs]) when mi.Name = "op_LessThanOrEqual" ->
        BinOp(compile state lhs, Lte, compile state rhs)

    // ─── Arithmetic ───
    | Call(None, mi, [lhs; rhs]) when mi.Name = "op_Addition" ->
        BinOp(compile state lhs, Add, compile state rhs)
    | Call(None, mi, [lhs; rhs]) when mi.Name = "op_Subtraction" ->
        BinOp(compile state lhs, Sub, compile state rhs)
    | Call(None, mi, [lhs; rhs]) when mi.Name = "op_Multiply" ->
        BinOp(compile state lhs, Mul, compile state rhs)
    | Call(None, mi, [lhs; rhs]) when mi.Name = "op_Division" ->
        BinOp(compile state lhs, Div, compile state rhs)
    | Call(None, mi, [lhs; rhs]) when mi.Name = "op_Modulus" ->
        BinOp(compile state lhs, Mod, compile state rhs)

    // ─── Logical: && (represented as IfThenElse in quotations) ───
    | IfThenElse(cond, ifTrue, ifFalse) when ifTrue.Type = typeof<bool> ->
        // && is: if cond then ifTrue else false
        // || is: if cond then true else ifFalse
        match ifTrue, ifFalse with
        | _, Value((:? bool as b), _) when b = false ->
            BinOp(compile state cond, And, compile state cond) // degenerate
        | Value((:? bool as b), _), _ when b = true ->
            BinOp(compile state cond, Or, compile state ifFalse)
        | _ ->
            BinOp(compile state cond, And, compile state ifTrue)

    // ─── not ───
    | Call(None, mi, [inner]) when mi.Name = "Not" || mi.Name = "op_LogicalNot" ->
        UnaryOp(Not, compile state inner)

    // ─── String methods ───
    | Call(Some instance, mi, [arg]) when mi.Name = "Contains" && mi.DeclaringType = typeof<string> ->
        BinOp(compile state instance, Contains, compile state arg)
    | Call(Some instance, mi, [arg]) when mi.Name = "StartsWith" && mi.DeclaringType = typeof<string> ->
        BinOp(compile state instance, StartsWith, compile state arg)
    | Call(Some instance, mi, [arg]) when mi.Name = "EndsWith" && mi.DeclaringType = typeof<string> ->
        BinOp(compile state instance, EndsWith, compile state arg)

    // ─── List.contains → IN ───
    | Call(None, mi, [elem; collection]) when mi.Name = "Contains" ->
        BinOp(compile state elem, In, compile state collection)

    // ─── Variables ───
    | Var v -> Variable v.Name

    // ─── Literals → parameters ───
    | Value(v, _) when v <> null ->
        let paramName = addParam state v
        Param paramName
    | Value(null, _) -> Null

    // ─── Unsupported ───
    | e -> failwithf "Unsupported expression in Cypher query: %A" e
```

### 5.6 CypherCompiler.fs — AST → Cypher String

```fsharp
module Fyper.CypherCompiler

open Fyper.Ast

/// Compile result
type CompileResult = {
    Cypher: string
    Parameters: Map<string, obj>
}

// ─── Expression → string ───

let rec compileExpr (expr: Expr) : string =
    match expr with
    | Literal v -> literalToString v
    | Param name -> sprintf "$%s" name
    | Variable name -> name
    | Null -> "null"
    | Property(owner, name) -> sprintf "%s.%s" owner name
    | BinOp(lhs, op, rhs) ->
        sprintf "(%s %s %s)" (compileExpr lhs) (compileBinOp op) (compileExpr rhs)
    | UnaryOp(op, inner) ->
        match op with
        | Not -> sprintf "NOT (%s)" (compileExpr inner)
        | IsNull -> sprintf "%s IS NULL" (compileExpr inner)
        | IsNotNull -> sprintf "%s IS NOT NULL" (compileExpr inner)
        | Exists -> sprintf "exists(%s)" (compileExpr inner)
    | FuncCall(name, args) ->
        let argsStr = args |> List.map compileExpr |> String.concat ", "
        sprintf "%s(%s)" name argsStr
    | ListExpr items ->
        let itemsStr = items |> List.map compileExpr |> String.concat ", "
        sprintf "[%s]" itemsStr
    | MapExpr entries ->
        let entriesStr =
            entries
            |> List.map (fun (k, v) -> sprintf "%s: %s" k (compileExpr v))
            |> String.concat ", "
        sprintf "{%s}" entriesStr
    | CaseExpr(scrutinee, whens, elseClause) ->
        let sb = System.Text.StringBuilder()
        sb.Append("CASE") |> ignore
        scrutinee |> Option.iter (fun s -> sb.Append(sprintf " %s" (compileExpr s)) |> ignore)
        for (condition, result) in whens do
            sb.Append(sprintf " WHEN %s THEN %s" (compileExpr condition) (compileExpr result)) |> ignore
        elseClause |> Option.iter (fun e -> sb.Append(sprintf " ELSE %s" (compileExpr e)) |> ignore)
        sb.Append(" END") |> ignore
        sb.ToString()
    | ExistsSubquery clauses ->
        let inner = clauses |> List.map compileClause |> String.concat " "
        sprintf "EXISTS { %s }" inner

and compileBinOp (op: BinOp) : string =
    match op with
    | Eq -> "=" | Neq -> "<>" | Gt -> ">" | Gte -> ">=" | Lt -> "<" | Lte -> "<="
    | And -> "AND" | Or -> "OR" | Xor -> "XOR"
    | Contains -> "CONTAINS" | StartsWith -> "STARTS WITH" | EndsWith -> "ENDS WITH"
    | In -> "IN"
    | Add -> "+" | Sub -> "-" | Mul -> "*" | Div -> "/" | Mod -> "%"
    | RegexMatch -> "=~"

and literalToString (v: obj) : string =
    match v with
    | :? string as s -> sprintf "'%s'" (s.Replace("'", "\\'"))
    | :? bool as b -> if b then "true" else "false"
    | :? int as i -> string i
    | :? int64 as i -> string i
    | :? float as f -> string f
    | :? float32 as f -> string f
    | null -> "null"
    | v -> string v

// ─── Pattern → string ───

and compilePattern (pattern: Pattern) : string =
    match pattern with
    | NodePattern(alias, label, props) ->
        let labelStr = label |> Option.map (sprintf ":%s") |> Option.defaultValue ""
        let propsStr = compileInlineProps props
        sprintf "(%s%s%s)" alias labelStr propsStr

    | RelPattern(from, relAlias, relType, relProps, direction, pathLength, to') ->
        let relContent =
            let alias = relAlias |> Option.defaultValue ""
            let typ = relType |> Option.map (sprintf ":%s") |> Option.defaultValue ""
            let len = pathLength |> Option.map compilePathLength |> Option.defaultValue ""
            let props = compileInlineProps relProps
            sprintf "%s%s%s%s" alias typ len props

        let relStr = if relContent = "" then "" else relContent
        let fromStr = compilePattern from
        let toStr = compilePattern to'

        match direction with
        | Outgoing   -> sprintf "%s-[%s]->%s" fromStr relStr toStr
        | Incoming   -> sprintf "%s<-[%s]-%s" fromStr relStr toStr
        | Undirected -> sprintf "%s-[%s]-%s"  fromStr relStr toStr

    | NamedPath(name, inner) ->
        sprintf "%s = %s" name (compilePattern inner)

and compileInlineProps (props: Map<string, Expr>) : string =
    if Map.isEmpty props then ""
    else
        let entries =
            props
            |> Map.toList
            |> List.map (fun (k, v) -> sprintf "%s: %s" k (compileExpr v))
            |> String.concat ", "
        sprintf " {%s}" entries

and compilePathLength (pl: PathLength) : string =
    match pl with
    | Exactly n -> sprintf "*%d" n
    | Between(min, max) -> sprintf "*%d..%d" min max
    | AtLeast n -> sprintf "*%d.." n
    | AtMost n -> sprintf "*..%d" n
    | AnyLength -> "*"

// ─── Clause → string ───

and compileClause (clause: Clause) : string =
    match clause with
    | Match(patterns, optional) ->
        let keyword = if optional then "OPTIONAL MATCH" else "MATCH"
        let patternsStr = patterns |> List.map compilePattern |> String.concat ", "
        sprintf "%s %s" keyword patternsStr

    | Where expr ->
        sprintf "WHERE %s" (compileExpr expr)

    | Return(items, distinct) ->
        let keyword = if distinct then "RETURN DISTINCT" else "RETURN"
        let itemsStr = items |> List.map compileReturnItem |> String.concat ", "
        sprintf "%s %s" keyword itemsStr

    | With(items, distinct) ->
        let keyword = if distinct then "WITH DISTINCT" else "WITH"
        let itemsStr = items |> List.map compileReturnItem |> String.concat ", "
        sprintf "%s %s" keyword itemsStr

    | Create patterns ->
        let patternsStr = patterns |> List.map compilePattern |> String.concat ", "
        sprintf "CREATE %s" patternsStr

    | Merge(pattern, onMatch, onCreate) ->
        let sb = System.Text.StringBuilder()
        sb.Append(sprintf "MERGE %s" (compilePattern pattern)) |> ignore
        if not (List.isEmpty onMatch) then
            sb.Append(sprintf " ON MATCH SET %s" (compileSetItems onMatch)) |> ignore
        if not (List.isEmpty onCreate) then
            sb.Append(sprintf " ON CREATE SET %s" (compileSetItems onCreate)) |> ignore
        sb.ToString()

    | Delete(aliases, detach) ->
        let keyword = if detach then "DETACH DELETE" else "DELETE"
        sprintf "%s %s" keyword (String.concat ", " aliases)

    | Set items ->
        sprintf "SET %s" (compileSetItems items)

    | Remove items ->
        sprintf "REMOVE %s" (items |> List.map compileRemoveItem |> String.concat ", ")

    | OrderBy items ->
        let itemsStr =
            items
            |> List.map (fun (expr, dir) ->
                let dirStr = match dir with Ascending -> "" | Descending -> " DESC"
                sprintf "%s%s" (compileExpr expr) dirStr)
            |> String.concat ", "
        sprintf "ORDER BY %s" itemsStr

    | Skip expr -> sprintf "SKIP %s" (compileExpr expr)
    | Limit expr -> sprintf "LIMIT %s" (compileExpr expr)

    | Unwind(expr, alias) ->
        sprintf "UNWIND %s AS %s" (compileExpr expr) alias

    | Call(proc, args, yields) ->
        let argsStr = args |> List.map compileExpr |> String.concat ", "
        let yieldStr =
            if List.isEmpty yields then ""
            else sprintf " YIELD %s" (String.concat ", " yields)
        sprintf "CALL %s(%s)%s" proc argsStr yieldStr

    | Union all ->
        if all then "UNION ALL" else "UNION"

    | RawCypher s -> s

and compileReturnItem (item: ReturnItem) : string =
    match item.Alias with
    | Some alias -> sprintf "%s AS %s" (compileExpr item.Expr) alias
    | None -> compileExpr item.Expr

and compileSetItems (items: SetItem list) : string =
    items |> List.map compileSetItem |> String.concat ", "

and compileSetItem (item: SetItem) : string =
    match item with
    | SetProperty(owner, prop, value) ->
        sprintf "%s.%s = %s" owner prop (compileExpr value)
    | SetAllProperties(owner, value) ->
        sprintf "%s = %s" owner (compileExpr value)
    | MergeProperties(owner, value) ->
        sprintf "%s += %s" owner (compileExpr value)
    | AddLabel(owner, label) ->
        sprintf "%s:%s" owner label

and compileRemoveItem (item: RemoveItem) : string =
    match item with
    | RemoveProperty(owner, prop) -> sprintf "%s.%s" owner prop
    | RemoveLabel(owner, label) -> sprintf "%s:%s" owner label

// ─── Full query compilation ───

let compile (query: CypherQuery<'T>) : CompileResult =
    let cypher =
        query.Clauses
        |> List.map compileClause
        |> String.concat "\n"
    { Cypher = cypher; Parameters = query.Parameters }

let compileUntyped (query: CypherQuery) : CompileResult =
    let cypher =
        query.Clauses
        |> List.map compileClause
        |> String.concat "\n"
    { Cypher = cypher; Parameters = query.Parameters }
```

### 5.7 Operators.fs — Relationship Pattern Operators

These operators exist **only for type-checking and quotation capture**. They throw at runtime — they're never called, only captured as expression trees by the `Quote`-enabled CE.

```fsharp
module Fyper.Operators

/// Phantom type for edge/relationship type reference
type EdgeType<'R> = EdgeType

/// Create a typed edge reference
let edge<'R> : EdgeType<'R> = EdgeType

/// Phantom type for optional node matching
type OptionalNodeType<'T> = OptionalNodeType

/// Source types for `for ... in ... do` bindings
type NodeSource<'T> = NodeSource

/// Create a typed node source for matching
let node<'T> : NodeSource<'T> = NodeSource

/// Create an optional node source (OPTIONAL MATCH)
let optionalNode<'T> : NodeSource<'T> = NodeSource  // translator checks context

/// Partial pattern: node -[edge]-> ???
type PartialEdge<'A, 'R> = { __phantom: unit }

/// Complete edge pattern: node -[edge]-> node
type EdgePattern<'A, 'R, 'B> = { __phantom: unit }

// ─── Outgoing: a -[edge<R>]-> b ───

/// Start a right-directed pattern: a -[
let inline ( -[ ) (a: 'A) (r: EdgeType<'R>) : PartialEdge<'A, 'R> =
    failwith "This operator is only valid inside a cypher { } computation expression"

/// Complete a right-directed pattern: ]-> b
let inline ( ]-> ) (partial: PartialEdge<'A, 'R>) (b: 'B) : EdgePattern<'A, 'R, 'B> =
    failwith "This operator is only valid inside a cypher { } computation expression"

// ─── Incoming: a <-[edge<R>]- b ───

/// Start a left-directed pattern: a <-[
let inline ( <-[ ) (a: 'A) (r: EdgeType<'R>) : PartialEdge<'A, 'R> =
    failwith "This operator is only valid inside a cypher { } computation expression"

/// Complete a left-directed pattern: ]- b
let inline ( ]- ) (partial: PartialEdge<'A, 'R>) (b: 'B) : EdgePattern<'A, 'R, 'B> =
    failwith "This operator is only valid inside a cypher { } computation expression"

// ─── Undirected: a -[edge<R>]- b ───
// Reuses -[ and ]-

// ─── Cypher aggregate functions (quotation-only) ───

let count () : int64 = failwith "quotation only"
let countDistinct (x: 'T) : int64 = failwith "quotation only"
let sum (x: 'T) : 'T = failwith "quotation only"
let avg (x: 'T) : float = failwith "quotation only"
let min' (x: 'T) : 'T = failwith "quotation only"
let max' (x: 'T) : 'T = failwith "quotation only"
let collect (x: 'T) : 'T list = failwith "quotation only"
let head (x: 'T list) : 'T = failwith "quotation only"
let last (x: 'T list) : 'T = failwith "quotation only"
let size (x: 'T) : int64 = failwith "quotation only"
```

### 5.8 CypherBuilder.fs — Computation Expression

```fsharp
module Fyper.CypherBuilder

open Microsoft.FSharp.Quotations
open Fyper.Ast
open Fyper.Operators

type CypherBuilder() =
    // ─── Auto-quotation: all CE body becomes an expression tree ───
    member _.Quote(e: Expr<'T>) : Expr<'T> = e

    // ─── Run: receives the quoted CE, delegates to QueryTranslator ───
    member _.Run(e: Expr<CypherQuery<'T>>) : CypherQuery<'T> =
        QueryTranslator.translate e

    // ─── Core CE members ───
    // These are never called at runtime (everything is quoted).
    // They exist to make the F# type checker happy.

    member _.Zero() : CypherQuery<unit> =
        { Clauses = []; Parameters = Map.empty }

    member _.Yield(x: 'T) : CypherQuery<'T> =
        { Clauses = []; Parameters = Map.empty }

    member _.Return(x: 'T) : CypherQuery<'T> =
        { Clauses = []; Parameters = Map.empty }

    /// for p in node<Person> do → MATCH (p:Person)
    member _.For(source: NodeSource<'T>, body: 'T -> CypherQuery<'R>) : CypherQuery<'R> =
        failwith "quotation only"

    // ─── Custom operations ───

    /// WHERE predicate
    [<CustomOperation("where", MaintainsVariableSpace = true)>]
    member _.Where(source: CypherQuery<'T>, [<ProjectionParameter>] predicate: 'T -> bool) : CypherQuery<'T> =
        failwith "quotation only"

    /// SELECT projection (maps to RETURN)
    [<CustomOperation("select", AllowIntoPattern = true)>]
    member _.Select(source: CypherQuery<'T>, [<ProjectionParameter>] selector: 'T -> 'R) : CypherQuery<'R> =
        failwith "quotation only"

    /// ORDER BY expression (ascending)
    [<CustomOperation("orderBy", MaintainsVariableSpace = true)>]
    member _.OrderBy(source: CypherQuery<'T>, [<ProjectionParameter>] selector: 'T -> 'Key) : CypherQuery<'T> =
        failwith "quotation only"

    /// ORDER BY expression (descending)
    [<CustomOperation("orderByDesc", MaintainsVariableSpace = true)>]
    member _.OrderByDescending(source: CypherQuery<'T>, [<ProjectionParameter>] selector: 'T -> 'Key) : CypherQuery<'T> =
        failwith "quotation only"

    /// SKIP n
    [<CustomOperation("skip", MaintainsVariableSpace = true)>]
    member _.Skip(source: CypherQuery<'T>, count: int) : CypherQuery<'T> =
        failwith "quotation only"

    /// LIMIT n
    [<CustomOperation("limit", MaintainsVariableSpace = true)>]
    member _.Limit(source: CypherQuery<'T>, count: int) : CypherQuery<'T> =
        failwith "quotation only"

    /// MATCH relationship pattern
    [<CustomOperation("match'", MaintainsVariableSpace = true)>]
    member _.MatchRel(source: CypherQuery<'T>, pattern: EdgePattern<'A, 'R, 'B>) : CypherQuery<'T> =
        failwith "quotation only"

    /// CREATE pattern
    [<CustomOperation("create", MaintainsVariableSpace = true)>]
    member _.Create(source: CypherQuery<'T>, pattern: 'P) : CypherQuery<'T> =
        failwith "quotation only"

    /// SET (update properties)
    [<CustomOperation("set", MaintainsVariableSpace = true)>]
    member _.Set(source: CypherQuery<'T>, [<ProjectionParameter>] updater: 'T -> 'U) : CypherQuery<'T> =
        failwith "quotation only"

    /// DELETE
    [<CustomOperation("delete", MaintainsVariableSpace = true)>]
    member _.Delete(source: CypherQuery<'T>, [<ProjectionParameter>] selector: 'T -> 'U) : CypherQuery<'T> =
        failwith "quotation only"

    /// DETACH DELETE
    [<CustomOperation("detachDelete", MaintainsVariableSpace = true)>]
    member _.DetachDelete(source: CypherQuery<'T>, [<ProjectionParameter>] selector: 'T -> 'U) : CypherQuery<'T> =
        failwith "quotation only"

/// The global cypher computation expression builder
let cypher = CypherBuilder()
```

### 5.9 QueryTranslator.fs — Quotation → AST

The most complex module. Walks the quotation tree produced by the Quote-enabled CE and builds the Cypher AST.

```fsharp
module Fyper.QueryTranslator

open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Quotations.Patterns
open Microsoft.FSharp.Quotations.DerivedPatterns
open Fyper.Ast
open Fyper.Schema
open Fyper.ExprCompiler

/// Internal build state accumulated during translation
type TranslateState = {
    Clauses: Clause list
    Parameters: Map<string, obj>
    /// Maps quotation Var names → Cypher aliases
    VarAliases: Map<string, string>
    /// Maps quotation Var names → their CLR type (for label resolution)
    VarTypes: Map<string, System.Type>
    /// Counter for generating unique parameter names
    mutable ParamCounter: int
}

module TranslateState =
    let empty = {
        Clauses = []
        Parameters = Map.empty
        VarAliases = Map.empty
        VarTypes = Map.empty
        ParamCounter = 0
    }

/// Main entry point: translate a quoted CE into a CypherQuery
let translate<'T> (quotedCe: Expr<CypherQuery<'T>>) : CypherQuery<'T> =
    let state = TranslateState.empty
    let finalState = walkExpr state (quotedCe :> Expr)
    { Clauses = finalState.Clauses; Parameters = finalState.Parameters }

/// Walk the quotation tree recursively
and walkExpr (state: TranslateState) (expr: Expr) : TranslateState =
    match expr with
    // ─── For(node<T>, fun var -> body) → MATCH (var:Label) ───
    | SpecificCall <@@ Unchecked.defaultof<CypherBuilder>.For @@>
        (_, typeArgs, [_builder; nodeSource; Lambda(var, body)]) ->
        // Extract the node type from NodeSource<'T> generic argument
        let nodeType = var.Type
        let label = resolveLabel nodeType
        let alias = var.Name

        // Add MATCH clause
        let matchClause = Match([NodePattern(alias, Some label, Map.empty)], false)
        let state' = {
            state with
                Clauses = state.Clauses @ [matchClause]
                VarAliases = state.VarAliases |> Map.add var.Name alias
                VarTypes = state.VarTypes |> Map.add var.Name nodeType
        }

        // Continue walking the body
        walkExpr state' body

    // ─── Where(source, fun vars -> predicate) ───
    | SpecificCall <@@ Unchecked.defaultof<CypherBuilder>.Where @@>
        (_, _, [_builder; source; predicateLambda]) ->
        let state' = walkExpr state source
        let exprState = ExprCompiler.newState()
        exprState.ParamIndex <- state'.ParamCounter
        let cypherExpr = compilePredicate exprState predicateLambda state'.VarAliases
        let state'' = {
            state' with
                Clauses = state'.Clauses @ [Where cypherExpr]
                Parameters = Map.fold (fun acc k v -> Map.add k v acc) state'.Parameters exprState.Parameters
                ParamCounter = exprState.ParamIndex
        }
        state''

    // ─── Select(source, fun vars -> projection) → RETURN ───
    | SpecificCall <@@ Unchecked.defaultof<CypherBuilder>.Select @@>
        (_, _, [_builder; source; projectionLambda]) ->
        let state' = walkExpr state source
        let returnItems = compileProjection projectionLambda state'.VarAliases
        { state' with Clauses = state'.Clauses @ [Return(returnItems, false)] }

    // ─── OrderBy(source, fun vars -> key) ───
    | SpecificCall <@@ Unchecked.defaultof<CypherBuilder>.OrderBy @@>
        (_, _, [_builder; source; selectorLambda]) ->
        let state' = walkExpr state source
        let exprState = ExprCompiler.newState()
        let orderExpr = compileSelector exprState selectorLambda state'.VarAliases
        { state' with Clauses = state'.Clauses @ [OrderBy [(orderExpr, Ascending)]] }

    // ─── OrderByDescending ───
    | SpecificCall <@@ Unchecked.defaultof<CypherBuilder>.OrderByDescending @@>
        (_, _, [_builder; source; selectorLambda]) ->
        let state' = walkExpr state source
        let exprState = ExprCompiler.newState()
        let orderExpr = compileSelector exprState selectorLambda state'.VarAliases
        { state' with Clauses = state'.Clauses @ [OrderBy [(orderExpr, Descending)]] }

    // ─── Skip(source, n) ───
    | SpecificCall <@@ Unchecked.defaultof<CypherBuilder>.Skip @@>
        (_, _, [_builder; source; Value(n, _)]) ->
        let state' = walkExpr state source
        let paramName = sprintf "skip_%d" state'.ParamCounter
        { state' with
            Clauses = state'.Clauses @ [Skip(Param paramName)]
            Parameters = state'.Parameters |> Map.add paramName n
            ParamCounter = state'.ParamCounter + 1 }

    // ─── Limit(source, n) ───
    | SpecificCall <@@ Unchecked.defaultof<CypherBuilder>.Limit @@>
        (_, _, [_builder; source; Value(n, _)]) ->
        let state' = walkExpr state source
        let paramName = sprintf "limit_%d" state'.ParamCounter
        { state' with
            Clauses = state'.Clauses @ [Limit(Param paramName)]
            Parameters = state'.Parameters |> Map.add paramName n
            ParamCounter = state'.ParamCounter + 1 }

    // ─── MatchRel(source, edgePattern) → MATCH (a)-[r:TYPE]->(b) ───
    | SpecificCall <@@ Unchecked.defaultof<CypherBuilder>.MatchRel @@>
        (_, typeArgs, [_builder; source; patternExpr]) ->
        let state' = walkExpr state source
        let relPattern = compileEdgePattern patternExpr state'.VarAliases state'.VarTypes
        { state' with Clauses = state'.Clauses @ [Match([relPattern], false)] }

    // ─── Return(x) ───
    | SpecificCall <@@ Unchecked.defaultof<CypherBuilder>.Return @@>
        (_, _, [_builder; returnExpr]) ->
        let returnItems = compileReturnExpr returnExpr state.VarAliases
        { state with Clauses = state.Clauses @ [Return(returnItems, false)] }

    // ─── Yield / Zero (pass-through) ───
    | SpecificCall <@@ Unchecked.defaultof<CypherBuilder>.Yield @@> _ -> state
    | SpecificCall <@@ Unchecked.defaultof<CypherBuilder>.Zero @@> _ -> state

    // ─── Let binding (pass-through, walk body) ───
    | Let(_, _, body) -> walkExpr state body

    // ─── Fallback ───
    | _ -> state  // or: failwithf "Unsupported CE expression: %A" expr

/// Compile a WHERE predicate lambda
and compilePredicate (exprState: ExprCompileState) (lambda: Expr) (aliases: Map<string, string>) : Ast.Expr =
    match lambda with
    | Lambda(_, body) -> ExprCompiler.compile exprState body
    | _ -> ExprCompiler.compile exprState lambda

/// Compile a SELECT/RETURN projection lambda into ReturnItems
and compileProjection (lambda: Expr) (aliases: Map<string, string>) : ReturnItem list =
    match lambda with
    | Lambda(_, body) -> exprToReturnItems body aliases
    | _ -> exprToReturnItems lambda aliases

and exprToReturnItems (expr: Expr) (aliases: Map<string, string>) : ReturnItem list =
    match expr with
    // Single variable: select p → RETURN p
    | Var v ->
        [{ Expr = Variable v.Name; Alias = None }]

    // Tuple: select (p, m) → RETURN p, m
    | NewTuple items ->
        items |> List.collect (fun item -> exprToReturnItems item aliases)

    // Property: select p.Name → RETURN p.name
    | PropertyGet(Some(Var v), prop, []) ->
        [{ Expr = Property(v.Name, toCypherName prop.Name); Alias = Some (toCypherName prop.Name) }]

    // Anonymous record: select {| Name = p.Name; Age = p.Age |} → RETURN p.name AS name, p.age AS age
    | NewRecord(_, fields) ->
        // TODO: extract field names and expressions
        []

    | _ -> [{ Expr = Variable "?"; Alias = None }]

/// Compile edge pattern operators into a Pattern
and compileEdgePattern (expr: Expr) (aliases: Map<string, string>) (types: Map<string, System.Type>) : Pattern =
    // Recognize: a -[edge<R>]-> b
    // In quotation tree: Call(]->  , [Call(-[, [Var a, edge<R>]), Var b])
    match expr with
    | _ ->
        // Walk the expression tree to find the operator calls
        // Extract: from alias, relationship type, to alias, direction
        // Build: RelPattern(NodePattern(from), relType, direction, NodePattern(to))
        // Implementation depends on exact quotation structure
        failwith "TODO: implement edge pattern compilation"

/// Compile ORDER BY selector
and compileSelector (exprState: ExprCompileState) (lambda: Expr) (aliases: Map<string, string>) : Ast.Expr =
    match lambda with
    | Lambda(_, body) -> ExprCompiler.compile exprState body
    | _ -> ExprCompiler.compile exprState lambda

/// Compile a return expression (for Return CE member, not Select custom op)
and compileReturnExpr (expr: Expr) (aliases: Map<string, string>) : ReturnItem list =
    exprToReturnItems expr aliases
```

### 5.10 ResultMapper.fs

```fsharp
module Fyper.ResultMapper

open System
open System.Collections.Concurrent
open Microsoft.FSharp.Reflection
open Fyper.GraphValue

/// Cached compiled mappers: Type → (GraphRecord → obj)
let private mapperCache = ConcurrentDictionary<Type, GraphRecord -> obj>()

/// Convert a GraphValue to a CLR value of the expected type
let rec convertValue (targetType: Type) (value: GraphValue) : obj =
    match value with
    | GNull -> null
    | GBool b -> box b
    | GInt i ->
        if targetType = typeof<int> then box (int i)
        elif targetType = typeof<int64> then box i
        elif targetType = typeof<float> then box (float i)
        else box i
    | GFloat f ->
        if targetType = typeof<float32> then box (float32 f)
        else box f
    | GString s -> box s
    | GList items ->
        if targetType.IsGenericType then
            let elemType = targetType.GetGenericArguments().[0]
            let converted = items |> List.map (convertValue elemType)
            // Build F# list
            let listType = typedefof<_ list>.MakeGenericType(elemType)
            let empty = listType.GetProperty("Empty").GetValue(null)
            let cons = listType.GetMethod("Cons")
            List.foldBack (fun item acc -> cons.Invoke(null, [| item; acc |])) converted empty
        else box items
    | GMap m ->
        if FSharpType.IsRecord targetType then
            mapRecord targetType (GMap m)
        else box m
    | GNode node ->
        if FSharpType.IsRecord targetType then
            mapRecord targetType (GMap node.Properties)
        else box node
    | GRel rel ->
        if FSharpType.IsRecord targetType then
            mapRecord targetType (GMap rel.Properties)
        else box rel
    | GPath path -> box path

/// Map a GraphValue (GMap or GNode) to an F# record type
and mapRecord (recordType: Type) (value: GraphValue) : obj =
    let props =
        match value with
        | GMap m -> m
        | GNode n -> n.Properties
        | GRel r -> r.Properties
        | _ -> Map.empty

    let fields = FSharpType.GetRecordFields(recordType)
    let values =
        fields
        |> Array.map (fun fi ->
            let cypherName = Schema.toCypherName fi.Name
            match Map.tryFind cypherName props with
            | Some gv -> convertValue fi.PropertyType gv
            | None ->
                if Schema.isOptionType fi.PropertyType then box None
                else failwithf "Missing required property '%s' for type '%s'" cypherName recordType.Name
        )

    FSharpValue.MakeRecord(recordType, values)

/// Map a full GraphRecord to a typed result
let mapRecord<'T> (record: GraphRecord) : 'T =
    let targetType = typeof<'T>

    if FSharpType.IsRecord targetType then
        // Single record type: map first value or merge all
        let value =
            if record.Values.Count = 1 then
                record.Values |> Map.toList |> List.head |> snd
            else
                GMap(record.Values)
        convertValue targetType value :?> 'T

    elif FSharpType.IsTuple targetType then
        // Tuple: map each element by key order
        let elemTypes = FSharpType.GetTupleElements(targetType)
        let values =
            record.Keys
            |> List.mapi (fun i key ->
                let gv = record.Values.[key]
                convertValue elemTypes.[i] gv
            )
            |> Array.ofList
        FSharpValue.MakeTuple(values, targetType) :?> 'T

    else
        // Primitive type
        let value = record.Values |> Map.toList |> List.head |> snd
        convertValue targetType value :?> 'T
```

### 5.11 Cypher.fs — Public API

```fsharp
module Fyper.Cypher

open System.Threading.Tasks
open Fyper.Ast
open Fyper.CypherCompiler
open Fyper.Driver
open Fyper.ResultMapper

/// Execute a read query and return typed results
let executeAsync<'T> (driver: IGraphDriver) (query: CypherQuery<'T>) : Task<'T list> =
    task {
        let compiled = compile query
        let! records = driver.ExecuteReadAsync(compiled.Cypher, compiled.Parameters)
        return records |> List.map mapRecord<'T>
    }

/// Execute a write query and return the count of affected entities
let executeWriteAsync (driver: IGraphDriver) (query: CypherQuery<'T>) : Task<int> =
    task {
        let compiled = compile query
        return! driver.ExecuteWriteAsync(compiled.Cypher, compiled.Parameters)
    }

/// Execute raw Cypher (escape hatch)
let rawAsync (driver: IGraphDriver) (cypher: string) (parameters: Map<string, obj>) : Task<GraphValue.GraphRecord list> =
    driver.ExecuteReadAsync(cypher, parameters)

/// Compile a query to Cypher string (for debugging/logging)
let toDebugString (query: CypherQuery<'T>) : string =
    let compiled = compile query
    sprintf "%s\n-- Parameters: %A" compiled.Cypher compiled.Parameters
```

## 6. Driver Implementations

### 6.1 Neo4j Driver (Fyper.Neo4j)

Wraps `Neo4j.Driver` NuGet package.

```fsharp
module Fyper.Neo4j

open System.Threading.Tasks
open Neo4j.Driver
open Fyper.Driver
open Fyper.GraphValue

type Neo4jDriver(uri: string, username: string, password: string) =
    let driver = GraphDatabase.Driver(uri, AuthTokens.Basic(username, password))

    let toGraphValue (value: obj) : GraphValue =
        match value with
        | null -> GNull
        | :? bool as b -> GBool b
        | :? int64 as i -> GInt i
        | :? int as i -> GInt (int64 i)
        | :? float as f -> GFloat f
        | :? string as s -> GString s
        | :? INode as n ->
            GNode {
                Id = n.Id
                Labels = n.Labels |> Seq.toList
                Properties = n.Properties |> Seq.map (fun kv -> kv.Key, toGraphValue kv.Value) |> Map.ofSeq
            }
        | :? IRelationship as r ->
            GRel {
                Id = r.Id
                RelType = r.Type
                StartNodeId = r.StartNodeId
                EndNodeId = r.EndNodeId
                Properties = r.Properties |> Seq.map (fun kv -> kv.Key, toGraphValue kv.Value) |> Map.ofSeq
            }
        | :? System.Collections.IList as lst ->
            GList [ for item in lst -> toGraphValue item ]
        | :? System.Collections.IDictionary as dict ->
            let map =
                [ for key in dict.Keys -> string key, toGraphValue dict.[key] ]
                |> Map.ofList
            GMap map
        | v -> GString (string v)

    let toRecord (record: IRecord) : GraphRecord =
        {
            Keys = record.Keys |> Seq.toList
            Values =
                record.Keys
                |> Seq.map (fun key -> key, toGraphValue record.[key])
                |> Map.ofSeq
        }

    interface IGraphDriver with
        member _.ExecuteReadAsync(cypher, parameters) =
            task {
                use session = driver.AsyncSession(fun cfg -> cfg.WithDefaultAccessMode(AccessMode.Read))
                let paramDict = parameters |> Map.toSeq |> dict
                let! result = session.RunAsync(cypher, paramDict)
                let! records = result.ToListAsync()
                return records |> Seq.map toRecord |> Seq.toList
            }

        member _.ExecuteWriteAsync(cypher, parameters) =
            task {
                use session = driver.AsyncSession(fun cfg -> cfg.WithDefaultAccessMode(AccessMode.Write))
                let paramDict = parameters |> Map.toSeq |> dict
                let! result = session.RunAsync(cypher, paramDict)
                let! summary = result.ConsumeAsync()
                return summary.Counters.NodesCreated + summary.Counters.RelationshipsCreated
                     + summary.Counters.NodesDeleted + summary.Counters.RelationshipsDeleted
                     + summary.Counters.PropertiesSet
            }

    interface System.IAsyncDisposable with
        member _.DisposeAsync() =
            driver.DisposeAsync()
```

### 6.2 Apache AGE Driver (Fyper.Age)

Wraps `Npgsql` NuGet package. Cypher runs as SQL extension:
```sql
SELECT * FROM cypher('graph_name', $$ MATCH (n:Person) RETURN n $$) AS (n agtype);
```

```fsharp
module Fyper.Age

open System.Threading.Tasks
open Npgsql
open Fyper.Driver
open Fyper.GraphValue

type AgeDriver(connectionString: string, graphName: string) =

    let wrapCypher (cypher: string) (returnAliases: string list) : string =
        let columns =
            if List.isEmpty returnAliases then "result agtype"
            else returnAliases |> List.map (fun a -> sprintf "%s agtype" a) |> String.concat ", "
        sprintf "SELECT * FROM cypher('%s', $$ %s $$) AS (%s)" graphName cypher columns

    let parseAgtype (value: obj) : GraphValue =
        // AGE returns results as agtype (jsonb-like).
        // Parse the agtype string representation into GraphValue.
        // Implementation depends on AGE's agtype format.
        // TODO: full agtype parser
        match value with
        | null -> GNull
        | :? string as s -> GString s
        | v -> GString (string v)

    interface IGraphDriver with
        member _.ExecuteReadAsync(cypher, parameters) =
            task {
                use conn = new NpgsqlConnection(connectionString)
                do! conn.OpenAsync()

                // Load AGE extension
                use! loadCmd = task { return conn.CreateCommand() }
                loadCmd.CommandText <- "LOAD 'age'; SET search_path = ag_catalog, \"$user\", public;"
                do! loadCmd.ExecuteNonQueryAsync() |> Task.ignore

                // TODO: extract return aliases from cypher string for column mapping
                let sql = wrapCypher cypher ["result"]
                use cmd = conn.CreateCommand()
                cmd.CommandText <- sql

                // Add parameters
                for kv in parameters do
                    cmd.Parameters.AddWithValue(kv.Key, kv.Value) |> ignore

                use! reader = cmd.ExecuteReaderAsync()
                let records = System.Collections.Generic.List<GraphRecord>()
                while! reader.ReadAsync() do
                    let values =
                        [0 .. reader.FieldCount - 1]
                        |> List.map (fun i -> reader.GetName(i), parseAgtype (reader.GetValue(i)))
                        |> Map.ofList
                    records.Add({
                        Keys = values |> Map.toList |> List.map fst
                        Values = values
                    })
                return records |> Seq.toList
            }

        member _.ExecuteWriteAsync(cypher, parameters) =
            task {
                use conn = new NpgsqlConnection(connectionString)
                do! conn.OpenAsync()

                use! loadCmd = task { return conn.CreateCommand() }
                loadCmd.CommandText <- "LOAD 'age'; SET search_path = ag_catalog, \"$user\", public;"
                do! loadCmd.ExecuteNonQueryAsync() |> Task.ignore

                let sql = wrapCypher cypher []
                use cmd = conn.CreateCommand()
                cmd.CommandText <- sql
                for kv in parameters do
                    cmd.Parameters.AddWithValue(kv.Key, kv.Value) |> ignore

                let! affected = cmd.ExecuteNonQueryAsync()
                return affected
            }

    interface System.IAsyncDisposable with
        member _.DisposeAsync() = System.Threading.Tasks.ValueTask()
```

## 7. Testing Strategy

### 7.1 Framework

- **Expecto** — F#-native test framework
- **FsCheck** — property-based testing (via Expecto.FsCheck)
- **Testcontainers** — for integration tests with real Neo4j/AGE

### 7.2 Unit Tests

**CompilerTests.fs** — AST → Cypher string:

```fsharp
[<Tests>]
let compilerTests = testList "CypherCompiler" [

    test "compiles simple MATCH" {
        let clause = Match([NodePattern("p", Some "Person", Map.empty)], false)
        let result = compileClause clause
        Expect.equal result "MATCH (p:Person)" ""
    }

    test "compiles WHERE with comparison" {
        let expr = BinOp(Property("p", "age"), Gt, Param "p0")
        let result = compileExpr expr
        Expect.equal result "(p.age > $p0)" ""
    }

    test "compiles relationship pattern" {
        let pattern =
            RelPattern(
                NodePattern("p", Some "Person", Map.empty),
                Some "r", Some "ACTED_IN", Map.empty,
                Outgoing, None,
                NodePattern("m", Some "Movie", Map.empty))
        let result = compilePattern pattern
        Expect.equal result "(p:Person)-[r:ACTED_IN]->(m:Movie)" ""
    }

    test "compiles full query" {
        let query : CypherQuery<obj> = {
            Clauses = [
                Match([NodePattern("p", Some "Person", Map.empty)], false)
                Where(BinOp(Property("p", "age"), Gt, Param "p0"))
                Return([{ Expr = Variable "p"; Alias = None }], false)
            ]
            Parameters = Map.ofList ["p0", box 30]
        }
        let result = compile query
        Expect.equal result.Cypher "MATCH (p:Person)\nWHERE (p.age > $p0)\nRETURN p" ""
    }

    test "compiles OPTIONAL MATCH" {
        let clause = Match([NodePattern("m", Some "Movie", Map.empty)], true)
        Expect.equal (compileClause clause) "OPTIONAL MATCH (m:Movie)" ""
    }

    test "compiles ORDER BY with direction" {
        let clause = OrderBy [
            Property("p", "age"), Descending
            Property("p", "name"), Ascending
        ]
        Expect.equal (compileClause clause) "ORDER BY p.age DESC, p.name" ""
    }

    test "compiles CREATE with properties" {
        let clause = Create [
            NodePattern("p", Some "Person", Map.ofList [
                "name", Param "name0"
                "age", Param "age0"
            ])
        ]
        Expect.equal (compileClause clause) "CREATE (p:Person {name: $name0, age: $age0})" ""
    }

    test "compiles MERGE with ON MATCH / ON CREATE" {
        let clause = Merge(
            NodePattern("p", Some "Person", Map.ofList ["name", Param "n"]),
            [SetProperty("p", "updated", Param "now")],
            [SetProperty("p", "created", Param "now")]
        )
        let result = compileClause clause
        Expect.stringContains result "MERGE (p:Person" ""
        Expect.stringContains result "ON MATCH SET" ""
        Expect.stringContains result "ON CREATE SET" ""
    }

    test "compiles DETACH DELETE" {
        let clause = Delete(["p"; "m"], true)
        Expect.equal (compileClause clause) "DETACH DELETE p, m" ""
    }

    test "compiles UNWIND" {
        let clause = Unwind(Param "names", "name")
        Expect.equal (compileClause clause) "UNWIND $names AS name" ""
    }

    test "compiles variable-length path" {
        let pattern =
            RelPattern(
                NodePattern("a", Some "Person", Map.empty),
                None, Some "KNOWS", Map.empty,
                Outgoing, Some (Between(1, 3)),
                NodePattern("b", Some "Person", Map.empty))
        Expect.equal (compilePattern pattern) "(a:Person)-[:KNOWS*1..3]->(b:Person)" ""
    }

    test "compiles function call" {
        let expr = FuncCall("count", [Variable "p"])
        Expect.equal (compileExpr expr) "count(p)" ""
    }
]
```

**ExprCompilerTests.fs** — F# quotations → Cypher AST Expr:

```fsharp
[<Tests>]
let exprCompilerTests = testList "ExprCompiler" [

    test "compiles property access" {
        let state = ExprCompiler.newState()
        let result = ExprCompiler.compile state <@ (Unchecked.defaultof<Person>).Age @>
        // Should produce Property("...", "age")
        // Exact var name depends on quotation structure
    }

    test "compiles greater than" {
        let state = ExprCompiler.newState()
        let result = ExprCompiler.compile state <@ 1 > 0 @>
        match result with
        | BinOp(_, Gt, _) -> ()
        | _ -> failtest "Expected Gt"
    }

    test "compiles AND" {
        let state = ExprCompiler.newState()
        let result = ExprCompiler.compile state <@ true && false @>
        match result with
        | BinOp(_, And, _) -> ()
        | _ -> failtest "Expected And"
    }

    test "compiles string Contains" {
        let state = ExprCompiler.newState()
        let result = ExprCompiler.compile state <@ "hello".Contains("ell") @>
        match result with
        | BinOp(_, Contains, _) -> ()
        | _ -> failtest "Expected Contains"
    }
]
```

**DslTests.fs** — CE → compiled Cypher (end-to-end):

```fsharp
// These tests verify the full pipeline:
// cypher { ... } → (Quote) → QueryTranslator → AST → CypherCompiler → string

type Person = { Name: string; Age: int }
type Movie = { Title: string; Released: int }
type ActedIn = { Roles: string list }

[<Tests>]
let dslTests = testList "DSL" [

    test "simple match and select" {
        let query = cypher {
            for p in node<Person> do
            select p
        }
        let result = CypherCompiler.compile query
        Expect.equal result.Cypher "MATCH (p:Person)\nRETURN p" ""
    }

    test "match with where" {
        let query = cypher {
            for p in node<Person> do
            where (p.Age > 30)
            select p
        }
        let result = CypherCompiler.compile query
        Expect.stringContains result.Cypher "MATCH (p:Person)" ""
        Expect.stringContains result.Cypher "WHERE" ""
        Expect.stringContains result.Cypher "p.age" ""
    }

    test "multiple matches" {
        let query = cypher {
            for p in node<Person> do
            for m in node<Movie> do
            select (p, m)
        }
        let result = CypherCompiler.compile query
        Expect.stringContains result.Cypher "MATCH (p:Person)" ""
        Expect.stringContains result.Cypher "MATCH (m:Movie)" ""
        Expect.stringContains result.Cypher "RETURN p, m" ""
    }

    test "with order by and limit" {
        let query = cypher {
            for p in node<Person> do
            orderByDesc p.Age
            limit 10
            select p
        }
        let result = CypherCompiler.compile query
        Expect.stringContains result.Cypher "ORDER BY p.age DESC" ""
        Expect.stringContains result.Cypher "LIMIT" ""
    }

    test "explicit quotation also works" {
        let query = cypher {
            for p in node<Person> do
            where <@ p.Age > 30 @>
            select p
        }
        let result = CypherCompiler.compile query
        Expect.stringContains result.Cypher "WHERE" ""
    }

    test "parameterizes literal values" {
        let query = cypher {
            for p in node<Person> do
            where (p.Age > 30)
            select p
        }
        let result = CypherCompiler.compile query
        // Should use $p0 instead of literal 30
        Expect.stringContains result.Cypher "$" ""
        Expect.isTrue (result.Parameters |> Map.exists (fun _ v -> v = box 30)) "should have param 30"
    }

    test "captures closure variables as parameters" {
        let minAge = 25
        let query = cypher {
            for p in node<Person> do
            where (p.Age > minAge)
            select p
        }
        let result = CypherCompiler.compile query
        Expect.isTrue (result.Parameters |> Map.exists (fun _ v -> v = box 25)) "should capture minAge"
    }
]
```

**SchemaTests.fs**:

```fsharp
[<Tests>]
let schemaTests = testList "Schema" [

    test "toCypherName converts PascalCase to camelCase" {
        Expect.equal (Schema.toCypherName "FirstName") "firstName" ""
        Expect.equal (Schema.toCypherName "Age") "age" ""
        Expect.equal (Schema.toCypherName "X") "x" ""
        Expect.equal (Schema.toCypherName "") "" ""
    }

    test "resolves label from type name" {
        let meta = Schema.getMetaOf<Person>()
        Expect.equal meta.Label "Person" ""
    }

    test "resolves label from attribute" {
        // [<Label "PERSON">] type CustomPerson = ...
        // Expect.equal meta.Label "PERSON" ""
    }

    test "extracts record fields as properties" {
        let meta = Schema.getMetaOf<Person>()
        Expect.equal meta.Properties.Length 2 ""
        let nameProp = meta.Properties |> List.find (fun p -> p.FSharpName = "Name")
        Expect.equal nameProp.CypherName "name" ""
    }
]
```

### 7.3 Integration Tests

Use Testcontainers to spin up real databases:

```fsharp
// Neo4jTests.fs
open Testcontainers.Neo4j

[<Tests>]
let neo4jIntegration = testList "Neo4j Integration" [

    testTask "round-trip create and query" {
        use! container = Neo4jBuilder().Build().StartAsync()
        use driver = new Neo4jDriver(container.GetConnectionString(), "neo4j", "neo4j")

        // Create
        let createQ = cypher {
            create (node<Person> { Name = "Alice"; Age = 30 })
        }
        do! Cypher.executeWriteAsync driver createQ |> Task.ignore

        // Query
        let findQ = cypher {
            for p in node<Person> do
            where (p.Name = "Alice")
            select p
        }
        let! results = Cypher.executeAsync<Person> driver findQ

        Expect.equal results.Length 1 ""
        Expect.equal results.[0].Name "Alice" ""
        Expect.equal results.[0].Age 30 ""
    }
]
```

### 7.4 Property-Based Tests

```fsharp
// Use FsCheck to generate random ASTs and verify:
// 1. Compiler never throws on valid ASTs
// 2. Compiled Cypher is non-empty
// 3. All parameters referenced in Cypher exist in the parameter map
// 4. Compile → parse → compile roundtrip (once we have a parser)
```

## 8. Implementation Phases

### Phase 1: Foundation (Core + Compiler)
**Files**: Schema.fs, Ast.fs, GraphValue.fs, CypherCompiler.fs
**Tests**: SchemaTests.fs, AstTests.fs, CompilerTests.fs
**Goal**: Can build ASTs manually and compile to valid Cypher strings.
**No CE, no drivers, no result mapping yet.**

### Phase 2: Expression Compiler
**Files**: ExprCompiler.fs
**Tests**: ExprCompilerTests.fs
**Goal**: Can translate F# quotations to Cypher AST expressions.

### Phase 3: DSL (Computation Expression)
**Files**: Operators.fs, CypherBuilder.fs, QueryTranslator.fs
**Tests**: DslTests.fs
**Goal**: `cypher { for p in node<Person> do ... }` works end-to-end.
**This is the hardest phase** — the QueryTranslator quotation walker is complex.

### Phase 4: Neo4j Driver
**Files**: Fyper.Neo4j/Neo4jDriver.fs, ResultMapper.fs, Cypher.fs
**Tests**: Neo4jTests.fs (integration)
**Goal**: Execute queries against real Neo4j, get typed results back.

### Phase 5: Apache AGE Driver
**Files**: Fyper.Age/AgeDriver.fs
**Tests**: AgeTests.fs (integration)
**Goal**: Same queries work against Apache AGE.

### Phase 6: Mutations
**Extend**: CypherBuilder with create/merge/set/delete operations
**Extend**: QueryTranslator to handle mutation clauses
**Goal**: Full CRUD via CE.

### Phase 7: Advanced Features
- Variable-length paths
- UNWIND
- WITH (intermediate projections)
- Subqueries
- Aggregation functions
- CASE expressions
- Raw Cypher escape hatch in CE

## 9. Dependencies

### Fyper (main library)
```xml
<ItemGroup>
    <PackageReference Include="FSharp.Core" Version="8.*" />
</ItemGroup>
```
Zero external dependencies. Only FSharp.Core.

### Fyper.Neo4j
```xml
<ItemGroup>
    <PackageReference Include="Neo4j.Driver" Version="5.*" />
    <ProjectReference Include="../Fyper/Fyper.fsproj" />
</ItemGroup>
```

### Fyper.Age
```xml
<ItemGroup>
    <PackageReference Include="Npgsql" Version="8.*" />
    <ProjectReference Include="../Fyper/Fyper.fsproj" />
</ItemGroup>
```

### Fyper.Tests
```xml
<ItemGroup>
    <PackageReference Include="Expecto" Version="10.*" />
    <PackageReference Include="Expecto.FsCheck" Version="10.*" />
    <PackageReference Include="YoloDev.Expecto.TestSdk" Version="0.14.*" />
    <ProjectReference Include="../../src/Fyper/Fyper.fsproj" />
</ItemGroup>
```

### Fyper.Integration.Tests
```xml
<ItemGroup>
    <PackageReference Include="Expecto" Version="10.*" />
    <PackageReference Include="Testcontainers.Neo4j" Version="3.*" />
    <PackageReference Include="Testcontainers.PostgreSql" Version="3.*" />
    <ProjectReference Include="../../src/Fyper/Fyper.fsproj" />
    <ProjectReference Include="../../src/Fyper.Neo4j/Fyper.Neo4j.fsproj" />
    <ProjectReference Include="../../src/Fyper.Age/Fyper.Age.fsproj" />
</ItemGroup>
```

## 10. Target

- **.NET 8.0** (LTS)
- **F# 8**
- **Language version**: latest
