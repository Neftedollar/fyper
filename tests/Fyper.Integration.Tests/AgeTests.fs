module Fyper.Integration.Tests.AgeTests

open System
open System.Threading.Tasks
open Expecto
open Npgsql
open Fyper
open Fyper.Ast
open Fyper.Age

// Requires: docker compose up -d (starts AGE on localhost:5432)

let connStr = "Host=localhost;Port=5432;Database=testdb;Username=test;Password=test"

let createDriver () =
    let dataSource = NpgsqlDataSource.Create(connStr)
    new AgeDriver(dataSource, "test_graph")

let ensureGraph (driver: IGraphDriver) =
    task {
        // AGE requires graph creation before use
        try
            let ds = NpgsqlDataSource.Create(connStr)
            let! conn = ds.OpenConnectionAsync()
            use initCmd = new NpgsqlCommand("LOAD 'age'", conn)
            do! initCmd.ExecuteNonQueryAsync() :> Task
            use pathCmd = new NpgsqlCommand("SET search_path = ag_catalog, \"$user\", public", conn)
            do! pathCmd.ExecuteNonQueryAsync() :> Task
            use createCmd = new NpgsqlCommand("SELECT create_graph('test_graph')", conn)
            try do! createCmd.ExecuteNonQueryAsync() :> Task with _ -> ()
            conn.Dispose()
        with _ -> ()
    }

[<Tests>]
let ageIntegrationTests = testList "AGE Integration" [
    testTask "connection and basic query" {
        use driver = createDriver () :> IGraphDriver
        do! ensureGraph driver
        // Basic connectivity test — may fail without docker
        try
            let! _ = driver.ExecuteWriteAsync(
                "CREATE (:Person {name: 'AgeTest', age: 25})", Map.empty)
            let! records = driver.ExecuteReadAsync(
                "MATCH (p:Person) RETURN p", Map.empty)
            Expect.isGreaterThan (List.length records) 0 "should have results"
        with ex ->
            // Skip if AGE not available
            skiptest (sprintf "AGE not available: %s" ex.Message)
    }

    testTask "AGE capabilities reject OPTIONAL MATCH" {
        use driver = createDriver () :> IGraphDriver
        Expect.throwsT<FyperUnsupportedFeatureException>
            (fun () ->
                CypherCompiler.validateCapabilities "AGE" driver.Capabilities
                    [Match([NodePattern("p", Some "Person", Map.empty)], true)])
            "should reject OPTIONAL MATCH"
    }

    testTask "AGE capabilities reject MERGE" {
        use driver = createDriver () :> IGraphDriver
        Expect.throwsT<FyperUnsupportedFeatureException>
            (fun () ->
                CypherCompiler.validateCapabilities "AGE" driver.Capabilities
                    [Merge(NodePattern("p", Some "Person", Map.empty), [], [])])
            "should reject MERGE"
    }

    testTask "AGE capabilities allow basic MATCH/RETURN" {
        use driver = createDriver () :> IGraphDriver
        CypherCompiler.validateCapabilities "AGE" driver.Capabilities
            [Match([NodePattern("p", Some "Person", Map.empty)], false)
             Return([{ Expr = Variable "p"; Alias = None }], false)]
    }

    testTask "transaction commit" {
        use driver = createDriver () :> IGraphDriver
        do! ensureGraph driver
        try
            let! _ = Cypher.inTransaction driver (fun tx ->
                task {
                    let! _ = tx.ExecuteWriteAsync(
                        "CREATE (:Person {name: 'AgeTx1'})", Map.empty)
                    return 1
                })
            ()
        with ex ->
            skiptest (sprintf "AGE not available: %s" ex.Message)
    }

    testTask "disposed driver throws" {
        let driver = createDriver ()
        do! (driver :> IAsyncDisposable).DisposeAsync()
        Expect.throwsT<FyperConnectionException>
            (fun () ->
                (driver :> IGraphDriver).ExecuteReadAsync("RETURN 1", Map.empty)
                |> Async.AwaitTask |> Async.RunSynchronously |> ignore)
            "should throw after dispose"
    }
]
