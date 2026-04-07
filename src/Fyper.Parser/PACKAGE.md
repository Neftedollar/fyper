# Fyper.Parser

Zero-dependency Cypher parser for F#. Parses Cypher query strings into Fyper's typed AST.

## Usage

```fsharp
open Fyper.Parser

let parsed = CypherParser.parse
    "MATCH (p:Person)-[:ACTED_IN]->(m:Movie) WHERE p.age > 30 RETURN p.name, m.title"

// parsed.Clauses = [Match(RelPattern(...)); Where(BinOp(...)); Return(...)]
```

## Roundtrip

Parse Cypher, inspect the AST, compile back:

```fsharp
let parsed = CypherParser.parse "MATCH (p:Person) WHERE p.age > $min RETURN p"
let compiled = Fyper.CypherCompiler.compile parsed
printfn "%s" compiled.Cypher
// MATCH (p:Person) WHERE (p.age > $min) RETURN p
```

## Supported Grammar

All Cypher clauses: MATCH, OPTIONAL MATCH, WHERE, RETURN, WITH, CREATE, MERGE (ON MATCH/ON CREATE), DELETE, DETACH DELETE, SET, REMOVE, ORDER BY, SKIP, LIMIT, UNWIND, UNION, CALL.

Expressions: comparison, arithmetic, logical (AND/OR/NOT), string ops (CONTAINS, STARTS WITH, ENDS WITH), IS NULL, IN, CASE WHEN, EXISTS subqueries.

Patterns: nodes, relationships (outgoing/incoming/undirected), variable-length paths (`*1..5`, `*`, `*2..`), named relationships, inline properties.

## Dependencies

Only `Fyper` core (which depends only on FSharp.Core). No parser generators, no external libraries.

## Links

- [Documentation](https://neftedollar.github.io/fyper/guide/parser.html)
- [GitHub](https://github.com/Neftedollar/fyper)
