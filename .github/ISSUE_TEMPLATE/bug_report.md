---
name: Bug Report
about: Report a bug in Fyper
labels: bug
---

## Description

A clear description of what the bug is.

## To Reproduce

```fsharp
// Minimal reproduction code
let query = cypher {
    for p in node<Person> do
    // ...
}
```

## Expected Behavior

What you expected to happen.

## Actual Behavior

What actually happened. Include the generated Cypher if relevant:

```
// Output of Cypher.toCypher query
```

## Environment

- Fyper version:
- .NET version:
- Backend (Neo4j/AGE):
- OS:
