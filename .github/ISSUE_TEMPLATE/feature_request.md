---
name: Feature Request
about: Suggest a new feature for Fyper
labels: enhancement
---

## Use Case

What are you trying to accomplish?

## Proposed Syntax

```fsharp
// How you'd like to write it in the CE
let query = cypher {
    for p in node<Person> do
    // your proposed syntax here
}
```

## Expected Cypher Output

```cypher
// What Cypher should be generated
MATCH (p:Person) ...
```

## Alternatives Considered

Any alternative approaches you've considered.
