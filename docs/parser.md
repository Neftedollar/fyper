# Fyper.Parser Documentation

The `Fyper.Parser` package provides a zero-external-dependency Cypher parser that converts Cypher query strings into Fyper's typed AST (`Fyper.Ast`). This enables round-tripping (parse Cypher string -> manipulate AST -> compile back to string), query analysis, validation, and transformation workflows.

## Package

```
Fyper.Parser  (depends on: Fyper)
```

## Quick Start

```fsharp
open Fyper.Parser
open Fyper.Ast
open Fyper.CypherCompiler

// Parse a Cypher string into an AST
let query = CypherParser.parse "MATCH (p:Person) WHERE p.age > 30 RETURN p.name"
// query.Clauses:
//   [ Match([NodePattern("p", Some "Person", Map.empty)], false)
//     Where(BinOp(Property("p", "age"), Gt, Literal 30L))
//     Return([{ Expr = Property("p", "name"); Alias = None }], false) ]

// Compile it back to a Cypher string
let compiled = compile query
// compiled.Cypher = "MATCH (p:Person)\nWHERE (p.age > 30)\nRETURN p.name"
```

## Lexer

The lexer (`Fyper.Parser.Lexer`) converts a Cypher string into a token list using a hand-written, character-by-character scanner. It is defined in `src/Fyper.Parser/Lexer.fs`.

### Token Types

```fsharp
type Token =
    // ── Keywords ──
    | MATCH | OPTIONAL | WHERE | RETURN | WITH | CREATE | MERGE
    | DELETE | DETACH | SET | REMOVE | ORDER | BY | ASC | DESC
    | SKIP | LIMIT | UNWIND | AS | DISTINCT | UNION | ALL
    | ON | CASE | WHEN | THEN | ELSE | END
    | AND | OR | XOR | NOT | IN | IS | NULL
    | TRUE | FALSE
    | CONTAINS | STARTS | ENDS
    | EXISTS | CALL | YIELD

    // ── Symbols ──
    | LPAREN | RPAREN         // ( )
    | LBRACKET | RBRACKET     // [ ]
    | LBRACE | RBRACE         // { }
    | COLON | COMMA | DOT     // : , .
    | PIPE | STAR | PLUS      // | * +
    | MINUS | SLASH | PERCENT  // - / %
    | CARET | EQ | NEQ        // ^ = <>
    | LT | GT | LTE | GTE    // < > <= >=
    | ARROW_RIGHT             // ->
    | ARROW_LEFT              // <-
    | DASH                    // -
    | DOLLAR                  // $
    | REGEX_MATCH             // =~
    | PLUS_EQ                 // +=

    // ── Literals ──
    | STRING of string        // 'hello' or "hello"
    | INTEGER of int64        // 42
    | FLOAT of float          // 3.14
    | IDENTIFIER of string    // variable/label names
    | PARAMETER of string     // $paramName

    // ── Control ──
    | EOF
    | NEWLINE
```

### Lexer API

```fsharp
module Lexer =
    /// Create a new lexer state from an input string
    val create : input: string -> LexerState

    /// Read the next token from the lexer state
    val nextToken : state: LexerState -> Token

    /// Tokenize an entire Cypher string into a token list (newlines stripped)
    val tokenize : input: string -> Token list
```

### Lexer Features

- **Case-insensitive keywords**: `match`, `MATCH`, and `Match` all produce `MATCH`.
- **String literals**: Single-quoted (`'hello'`) and double-quoted (`"hello"`) with escape sequences (`\n`, `\t`, `\\`, `\'`, `\"`).
- **Backtick identifiers**: `` `my variable` `` for identifiers with special characters.
- **Parameters**: `$paramName` produces `PARAMETER "paramName"`.
- **Line comments**: `// comment` are skipped.
- **Number handling**: Integers and floats. The lexer distinguishes `.` (property access) from `..` (range operator) from decimal point in numbers.
- **Multi-character operators**: `->`, `<-`, `<=`, `>=`, `<>`, `!=`, `=~`, `+=`.

## Parser

The parser (`Fyper.Parser.CypherParser`) is a recursive-descent parser that converts token streams into Fyper's `Clause`, `Expr`, and `Pattern` AST types. It is defined in `src/Fyper.Parser/Parser.fs`.

### Parser API

```fsharp
module CypherParser =
    /// Parse a full Cypher query string into a CypherQuery AST.
    /// Parameters referenced as $name are collected into the Parameters map (with null values).
    val parse : cypher: string -> CypherQuery<obj>

    /// Parse Cypher and return only the clause list (no parameter collection).
    val parseClauses' : cypher: string -> Clause list
```

### Supported Cypher Grammar

The parser handles the following clause types:

| Clause | Example |
|---|---|
| `MATCH` | `MATCH (n:Person)` |
| `OPTIONAL MATCH` | `OPTIONAL MATCH (n)-[r]->(m)` |
| `WHERE` | `WHERE n.age > 30 AND n.name CONTAINS 'A'` |
| `RETURN` | `RETURN n, m.name AS title` |
| `RETURN DISTINCT` | `RETURN DISTINCT n.label` |
| `WITH` | `WITH n, count(*) AS cnt` |
| `WITH DISTINCT` | `WITH DISTINCT n.type AS t` |
| `CREATE` | `CREATE (n:Person {name: 'Alice'})` |
| `MERGE` | `MERGE (n:Person {name: 'Alice'}) ON MATCH SET n.age = 31 ON CREATE SET n.age = 30` |
| `DELETE` | `DELETE n, m` |
| `DETACH DELETE` | `DETACH DELETE n` |
| `SET` | `SET n.age = 31, n:Active` |
| `REMOVE` | `REMOVE n.temp, n:Inactive` |
| `ORDER BY` | `ORDER BY n.age DESC, n.name ASC` |
| `SKIP` | `SKIP 10` |
| `LIMIT` | `LIMIT 25` |
| `UNWIND` | `UNWIND [1, 2, 3] AS x` |
| `UNION` / `UNION ALL` | `UNION ALL` |
| `CALL` | `CALL db.labels() YIELD label` |

### Expression Precedence

The parser implements correct operator precedence using precedence climbing:

| Precedence (low to high) | Operators |
|---|---|
| 1 | `OR` |
| 2 | `XOR` |
| 3 | `AND` |
| 4 | `NOT` (unary prefix) |
| 5 | `=`, `<>`, `<`, `>`, `<=`, `>=`, `=~`, `IN`, `IS NULL`, `IS NOT NULL` |
| 6 | `CONTAINS`, `STARTS WITH`, `ENDS WITH` |
| 7 | `+`, `-` |
| 8 | `*`, `/`, `%` |
| 9 | Unary `-` |
| 10 | Primary: literals, variables, property access, function calls, `(...)`, `[...]`, `{...}`, `CASE`, `EXISTS` |

### Expression Types Parsed

- **Literals**: strings, integers, floats, booleans, null
- **Parameters**: `$name`
- **Variables**: `n`, `*`
- **Property access**: `n.age`
- **Binary operations**: all comparison, logical, string, and arithmetic operators
- **Unary operations**: `NOT`, `IS NULL`, `IS NOT NULL`, `EXISTS`, negation
- **Function calls**: `count(*)`, `collect(n.name)`, `toUpper(n.name)`, etc.
- **List expressions**: `[1, 2, 3]`
- **Map expressions**: `{name: 'Alice', age: 30}`
- **CASE expressions**: `CASE WHEN x > 0 THEN 'pos' ELSE 'neg' END` and `CASE x WHEN 1 THEN 'one' END`
- **EXISTS subquery**: `EXISTS { MATCH (n)-[r]->(m) }`

### Pattern Parser

Patterns are parsed with full support for:

- **Node patterns**: `(alias:Label {prop: value})`
- **Relationship patterns**: `(a)-[r:TYPE]->(b)`, `(a)<-[r:TYPE]-(b)`, `(a)-[r:TYPE]-(b)`
- **Relationship chains**: `(a)-[:R1]->(b)-[:R2]->(c)` parsed as nested `RelPattern`
- **Variable-length paths**: `[r:TYPE*]`, `[r:TYPE*3]`, `[r:TYPE*1..5]`, `[r:TYPE*2..]`, `[r:TYPE*..3]`
- **Inline properties**: `(n:Person {name: 'Alice'})`
- **Optional components**: alias, label, properties, and relationship details are all optional

### SET Item Parsing

The parser recognizes four SET item forms:

| Syntax | AST | Meaning |
|---|---|---|
| `n.prop = expr` | `SetProperty("n", "prop", expr)` | Set single property |
| `n = expr` | `SetAllProperties("n", expr)` | Replace all properties |
| `n += expr` | `MergeProperties("n", expr)` | Merge properties |
| `n:Label` | `AddLabel("n", "Label")` | Add label |

### REMOVE Item Parsing

| Syntax | AST |
|---|---|
| `n.prop` | `RemoveProperty("n", "prop")` |
| `n:Label` | `RemoveLabel("n", "Label")` |

### CALL Clause

Supports dotted procedure names and optional YIELD:

```
CALL db.labels() YIELD label
CALL apoc.do.when(condition, query1, query2)
```

## Round-Trip Example

Parse a Cypher string, inspect the AST, and compile it back:

```fsharp
open Fyper.Parser
open Fyper.Ast
open Fyper.CypherCompiler

let input = """
MATCH (p:Person)-[r:ACTED_IN]->(m:Movie)
WHERE p.age > 30 AND m.year >= 2000
RETURN p.name AS name, m.title AS title
ORDER BY m.year DESC
LIMIT 10
"""

// Parse
let query = CypherParser.parse input

// Inspect clauses
for clause in query.Clauses do
    printfn "%A" clause
// Match([RelPattern(NodePattern("p", Some "Person", ...), ...)], false)
// Where(BinOp(BinOp(Property("p","age"), Gt, Literal 30L), And, BinOp(...)))
// Return([{Expr=Property("p","name"); Alias=Some "name"}; ...], false)
// OrderBy([(Property("m","year"), Descending)])
// Limit(Literal 10L)

// Compile back to Cypher
let result = compile query
printfn "%s" result.Cypher
// MATCH (p:Person)-[r:ACTED_IN]->(m:Movie)
// WHERE ((p.age > 30) AND (m.year >= 2000))
// RETURN p.name AS name, m.title AS title
// ORDER BY m.year DESC
// LIMIT 10
```

## AST Manipulation Example

Parse, transform, and re-compile:

```fsharp
open Fyper.Parser
open Fyper.Ast
open Fyper.CypherCompiler

let query = CypherParser.parse "MATCH (p:Person) RETURN p"

// Add a WHERE clause after MATCH
let withFilter = {
    query with
        Clauses =
            query.Clauses
            |> List.collect (fun c ->
                match c with
                | Match _ -> [c; Where(BinOp(Property("p", "age"), Gt, Param "minAge"))]
                | _ -> [c])
        Parameters = query.Parameters |> Map.add "minAge" (box 25)
}

let result = compile withFilter
// MATCH (p:Person)
// WHERE (p.age > $minAge)
// RETURN p
```
