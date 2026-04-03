---
layout: default
title: Mutations
description: "CREATE, SET, DELETE, MERGE in Fyper CE. Mutate graph data with F# record syntax and automatic parameterization."
nav_order: 5
---

# Mutations

Create, update, and delete graph data using the CE.

## CREATE Node

```fsharp
let query = cypher {
    for _p in node<Person> do
    create { Name = "Alice"; Age = 30 }
}
// CREATE (p:Person {age: $p0, name: $p1})
```

## CREATE Relationship

```fsharp
let query = cypher {
    for p in node<Person> do
    for m in node<Movie> do
    where (p.Name = "Tom" && m.Title = "The Matrix")
    createRel (p -- edge<ActedIn> --> m)
}
// CREATE (p)-[:ACTED_IN]->(m)
```

## SET (Update)

Use F# record update syntax. Only changed fields generate SET clauses:

```fsharp
// Increment age
let query = cypher {
    for p in node<Person> do
    where (p.Name = "Tom")
    set (fun p -> { p with Age = p.Age + 1 })
}
// SET p.age = (p.age + $p0)

// Set literal value
let query = cypher {
    for p in node<Person> do
    where (p.Name = "Tom")
    set (fun p -> { p with Age = 51 })
}
// SET p.age = $p0

// SET then RETURN
let query = cypher {
    for p in node<Person> do
    where (p.Name = "Tom")
    set (fun p -> { p with Age = p.Age + 1 })
    select p
}
// SET p.age = (p.age + $p0) RETURN p
```

## DELETE

```fsharp
// Simple delete
let query = cypher {
    for p in node<Person> do
    where (p.Name = "Bob")
    delete p
}
// DELETE p

// Detach delete (removes relationships first)
let query = cypher {
    for p in node<Person> do
    where (p.Name = "Bob")
    detachDelete p
}
// DETACH DELETE p
```

## MERGE

Create if not exists, update if exists:

```fsharp
let query = cypher {
    for p in node<Person> do
    merge { Name = "Tom"; Age = 0 }
    onMatch (fun p -> { p with Age = 50 })
    onCreate (fun p -> { p with Age = 25 })
}
// MERGE (p:Person {age: $p0, name: $p1})
// ON MATCH SET p.age = $p2
// ON CREATE SET p.age = $p3
```

## Transactions

Wrap multiple mutations in an atomic transaction:

```fsharp
task {
    let! result = Cypher.inTransaction driver (fun tx -> task {
        let! _ = createAlice |> Cypher.executeWriteAsync tx
        let! _ = createBob |> Cypher.executeWriteAsync tx
        return 2
    })
    // Both committed or both rolled back
}
```
