---
layout: default
title: Transactions
parent: Guide
nav_order: 5
---

# Transactions

## Auto-Commit (Default)

Each `executeAsync` / `executeWriteAsync` call auto-commits:

```fsharp
let! count = mutation |> Cypher.executeWriteAsync driver
// Committed immediately
```

## Explicit Transactions

Use `Cypher.inTransaction` for multi-statement atomicity:

```fsharp
task {
    let! result = Cypher.inTransaction driver (fun tx -> task {
        let! _ = cypher { for _p in node<Person> do; create { Name = "A"; Age = 1 } }
                 |> Cypher.executeWriteAsync tx
        let! _ = cypher { for _p in node<Person> do; create { Name = "B"; Age = 2 } }
                 |> Cypher.executeWriteAsync tx
        return 2
    })
    // Both committed atomically
}
```

If an exception occurs inside the function, the transaction is automatically rolled back:

```fsharp
try
    let! _ = Cypher.inTransaction driver (fun tx -> task {
        let! _ = q1 |> Cypher.executeWriteAsync tx
        failwith "something went wrong"
        return 1
    })
    ()
with _ ->
    // Transaction rolled back — q1 changes are discarded
    ()
```

## Manual Transaction Control

For advanced cases, use `IGraphTransaction` directly:

```fsharp
task {
    let! tx = driver.BeginTransactionAsync()
    try
        let! _ = tx.ExecuteWriteAsync(cypher, params)
        do! tx.CommitAsync()
    with _ ->
        do! tx.RollbackAsync()
    finally
        do! tx.DisposeAsync()
}
```
