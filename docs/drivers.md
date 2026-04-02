# Fyper Driver Documentation

Fyper uses a driver abstraction (`IGraphDriver`) to execute Cypher queries against different graph database backends. Each driver normalizes results into Fyper's `GraphValue` types and declares which Cypher features it supports via `DriverCapabilities`.

## Available Drivers

| Package | Backend | NuGet Dependency | Capabilities |
|---|---|---|---|
| `Fyper.Neo4j` | Neo4j (Bolt protocol) | `Neo4j.Driver 5.x` | All features |
| `Fyper.Age` | Apache AGE (PostgreSQL) | `Npgsql 8.x` | Minimal (MATCH, WHERE, RETURN, CREATE, DELETE, SET, ORDER BY, SKIP, LIMIT) |

---

## Neo4j Driver

### Package

```xml
<PackageReference Include="Fyper.Neo4j" Version="1.0.0" />
```

### Setup

```fsharp
open Neo4j.Driver
open Fyper
open Fyper.Neo4j

// Create the Neo4j .NET driver
let neoDriver = GraphDatabase.Driver("bolt://localhost:7687", AuthTokens.Basic("neo4j", "password"))

// Wrap it in Fyper's Neo4jDriver
let driver = new Neo4jDriver(neoDriver) :> IGraphDriver
```

### Usage

```fsharp
open Fyper
open Fyper.Ast

type Person = { Name: string; Age: int }
type Movie = { Title: string; Year: int }
type ActedIn = { Role: string }

// Read query
let query = cypher {
    for p in node<Person> do
    where (p.Age > 30)
    select p
}

let! people = Cypher.executeAsync<Person> driver query

// Write query
let createQuery = cypher {
    for p in node<Person> do
    create { Name = "Alice"; Age = 30 }
}

let! affected = Cypher.executeWriteAsync driver createQuery
```

### How It Works

The `Neo4jDriver` wraps the official `Neo4j.Driver.IDriver` interface:

- **Read queries** use `session.ExecuteReadAsync` with an auto-managed session. The cursor is consumed into a list and the session is closed.
- **Write queries** use `session.ExecuteWriteAsync`. The return value is the sum of `NodesCreated + NodesDeleted + RelationshipsCreated + RelationshipsDeleted + PropertiesSet` from the result summary.
- **Transactions** are created via `session.BeginTransactionAsync()`. The `Neo4jTransaction` type wraps both the session and transaction, disposing both when done.

### Value Mapping

The internal `ValueMapper` module converts Neo4j types to `GraphValue`:

| Neo4j Type | GraphValue |
|---|---|
| `null` | `GNull` |
| `bool` | `GBool` |
| `int64`, `int` | `GInt` |
| `float`, `float32` | `GFloat` |
| `string` | `GString` |
| `INode` | `GNode { Id; Labels; Properties }` |
| `IRelationship` | `GRel { Id; RelType; StartNodeId; EndNodeId; Properties }` |
| `IPath` | `GPath { Nodes; Relationships }` |
| `IList` | `GList` (recursive) |
| `IDictionary` | `GMap` (recursive) |

Note: Node and relationship IDs are derived by hashing `ElementId` (string) to `int64`, since Neo4j 5.x uses string element IDs internally.

### Capabilities

```fsharp
// Neo4jDriver reports:
DriverCapabilities.all
// = { SupportsOptionalMatch = true
//     SupportsMerge = true
//     SupportsUnwind = true
//     SupportsCase = true
//     SupportsCallProcedure = true
//     SupportsExistsSubquery = true
//     SupportsNamedPaths = true }
```

### Disposal

```fsharp
// The Neo4jDriver implements IAsyncDisposable
do! (driver :> IAsyncDisposable).DisposeAsync()

// Or use `use` binding
use driver = new Neo4jDriver(neoDriver) :> IGraphDriver
```

After disposal, any method call raises `FyperConnectionException`.

---

## Apache AGE Driver

### Package

```xml
<PackageReference Include="Fyper.Age" Version="1.0.0" />
```

### Prerequisites

- PostgreSQL with the [Apache AGE extension](https://age.apache.org/) installed
- A graph created in AGE: `SELECT create_graph('my_graph')`

### Setup

```fsharp
open Npgsql
open Fyper
open Fyper.Age

// Create an Npgsql data source
let dataSource = NpgsqlDataSource.Create("Host=localhost;Database=mydb;Username=postgres;Password=secret")

// Create the AGE driver, specifying the graph name
let driver = new AgeDriver(dataSource, "my_graph") :> IGraphDriver
```

### Usage

```fsharp
type Person = { Name: string; Age: int }

// Read query (uses only AGE-supported features)
let query = cypher {
    for p in node<Person> do
    where (p.Age > 30)
    select p
}

let! people = Cypher.executeAsync<Person> driver query
```

### How It Works

AGE executes Cypher by wrapping it in a SQL function call. The `AgeDriver` handles this transparently:

1. **Connection initialization**: Each connection runs `LOAD 'age'` and `SET search_path = ag_catalog, "$user", public` before executing queries.

2. **Cypher wrapping**: The internal `CypherWrapper` module transforms Cypher into AGE's SQL syntax:

   ```sql
   SELECT * FROM cypher('my_graph', $$ MATCH (p:Person) RETURN p $$) AS (p agtype)
   ```

3. **Parameter remapping**: Fyper's named parameters (`$p0`, `$p1`) are remapped to positional parameters (`$1`, `$2`) for PostgreSQL.

4. **Return alias extraction**: The wrapper extracts column aliases from the RETURN clause to build the SQL `AS (...)` type specification. Each column is typed as `agtype`.

### Agtype Parsing

AGE returns results in its `agtype` format -- a JSON-like representation with type suffixes. The internal `AgtypeParser` module handles conversion:

| AGE Agtype | GraphValue |
|---|---|
| `{id: N, label: "L", properties: {...}}::vertex` | `GNode` |
| `{id: N, label: "L", start_id: N, end_id: N, properties: {...}}::edge` | `GRel` |
| `"string"` | `GString` |
| `42` | `GInt` |
| `3.14` | `GFloat` |
| `true` / `false` | `GBool` |
| `null` | `GNull` |

The parser uses `System.Text.Json` to parse the JSON structure within agtype values.

### Capabilities

```fsharp
// AgeDriver reports minimal capabilities:
{ SupportsOptionalMatch = false
  SupportsMerge = false
  SupportsUnwind = false
  SupportsCase = false
  SupportsCallProcedure = false
  SupportsExistsSubquery = false
  SupportsNamedPaths = false }
```

If you use an unsupported feature with the AGE driver, Fyper raises `FyperUnsupportedFeatureException` at query construction time (not at database execution time):

```fsharp
// This will throw FyperUnsupportedFeatureException("OPTIONAL MATCH", "AGE")
let query = cypher {
    for p in optionalNode<Person> do
    select p
}
let! _ = Cypher.executeAsync driver query  // Exception before reaching DB
```

### Disposal

```fsharp
do! (driver :> IAsyncDisposable).DisposeAsync()
```

The AGE driver's `DisposeAsync` is a no-op (`ValueTask.CompletedTask`) since it does not own the `NpgsqlDataSource`. The data source should be disposed separately.

---

## DriverCapabilities Feature Matrix

| Feature | Neo4j | Apache AGE |
|---|---|---|
| `MATCH` | Yes | Yes |
| `OPTIONAL MATCH` | Yes | No |
| `WHERE` | Yes | Yes |
| `RETURN` / `RETURN DISTINCT` | Yes | Yes |
| `WITH` / `WITH DISTINCT` | Yes | Yes |
| `CREATE` | Yes | Yes |
| `MERGE` / `ON MATCH` / `ON CREATE` | Yes | No |
| `DELETE` / `DETACH DELETE` | Yes | Yes |
| `SET` | Yes | Yes |
| `REMOVE` | Yes | Yes |
| `ORDER BY` | Yes | Yes |
| `SKIP` / `LIMIT` | Yes | Yes |
| `UNWIND` | Yes | No |
| `CASE` expressions | Yes | No |
| `CALL` procedures | Yes | No |
| `EXISTS { }` subqueries | Yes | No |
| Named paths (`p = (a)-[r]->(b)`) | Yes | No |
| Variable-length paths (`*1..5`) | Yes | Yes |
| Aggregate functions (`count`, `sum`, etc.) | Yes | Yes |

---

## Transaction API

Both drivers implement the same transaction interface.

### Basic Usage

```fsharp
let! result = Cypher.inTransaction driver (fun tx ->
    task {
        // Multiple operations in one transaction
        let! _ = tx.ExecuteWriteAsync(
            "CREATE (n:Person {name: $name})",
            Map.ofList ["name", box "Alice"])
        let! _ = tx.ExecuteWriteAsync(
            "CREATE (n:Person {name: $name})",
            Map.ofList ["name", box "Bob"])
        return "done"
    })
// Transaction auto-commits on success
```

### Error Handling

`Cypher.inTransaction` auto-rolls back on exception:

```fsharp
try
    let! _ = Cypher.inTransaction driver (fun tx ->
        task {
            let! _ = tx.ExecuteWriteAsync("CREATE (n:Person {name: $name})", Map.ofList ["name", box "Alice"])
            failwith "something went wrong"
            return ()
        })
    ()
with ex ->
    // Transaction was rolled back. Alice was NOT created.
    printfn "Error: %s" ex.Message
```

### Manual Transaction Management

For more control, use `BeginTransactionAsync` directly:

```fsharp
let! tx = driver.BeginTransactionAsync()
try
    let! records = tx.ExecuteReadAsync("MATCH (n) RETURN count(n) AS cnt", Map.empty)
    let! _ = tx.ExecuteWriteAsync("CREATE (n:Log {timestamp: $ts})", Map.ofList ["ts", box System.DateTime.UtcNow])
    do! tx.CommitAsync()
finally
    do! (tx :> IAsyncDisposable).DisposeAsync()
```

### Neo4j Transaction Behavior

- Read queries inside a transaction use `tx.RunAsync`, keeping results within the transaction scope.
- Write queries return the sum of all counters (nodes/rels created/deleted + properties set).
- Disposing the `Neo4jTransaction` closes the underlying session.

### AGE Transaction Behavior

- Transactions use `NpgsqlConnection.BeginTransactionAsync()`.
- The AGE extension is loaded (`LOAD 'age'`) on the connection before starting the transaction.
- Disposing the `AgeTransaction` disposes both the `NpgsqlTransaction` and the `NpgsqlConnection`.

---

## Implementing a Custom Driver

To add support for a new graph database, implement `IGraphDriver`:

```fsharp
type MyCustomDriver() =
    interface IGraphDriver with
        member _.ExecuteReadAsync(cypher, parameters) =
            task {
                // Execute the cypher query against your backend
                // Convert results to GraphRecord list
                return []
            }

        member _.ExecuteWriteAsync(cypher, parameters) =
            task {
                // Execute write query
                // Return count of affected entities
                return 0
            }

        member _.BeginTransactionAsync() =
            task {
                // Return an IGraphTransaction implementation
                return Unchecked.defaultof<IGraphTransaction>
            }

        member _.Capabilities = {
            SupportsOptionalMatch = true
            SupportsMerge = false
            SupportsUnwind = true
            SupportsCase = true
            SupportsCallProcedure = false
            SupportsExistsSubquery = false
            SupportsNamedPaths = false
        }

    interface IAsyncDisposable with
        member _.DisposeAsync() = ValueTask.CompletedTask
```

Key requirements:
1. Normalize all results into `GraphValue` / `GraphRecord` types.
2. Set `Capabilities` accurately so that unsupported features are caught at construction time.
3. Implement `IAsyncDisposable` for resource cleanup.
4. Throw `FyperConnectionException` on connection errors and `FyperQueryException` on execution errors for consistent error handling.
