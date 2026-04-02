module Fyper.Integration.Tests.Neo4jTests

open System
open System.Threading.Tasks
open Expecto
open Fyper
open Fyper.Ast
open Fyper.Neo4j
open Testcontainers.Neo4j

// Test record types
type Person = { Name: string; Age: int }
type Movie = { Title: string; Released: int }

let neo4jContainer =
    Neo4jBuilder()
        .WithImage("neo4j:5")
        .Build()

let createDriver () =
    let boltUri = neo4jContainer.GetConnectionString()
    let driver = Neo4j.Driver.GraphDatabase.Driver(boltUri, Neo4j.Driver.AuthTokens.None)
    new Neo4jDriver(driver)

[<Tests>]
let neo4jIntegrationTests = testList "Neo4j Integration" [
    testTask "container starts and driver connects" {
        do! neo4jContainer.StartAsync()
        try
            use driver = createDriver () :> IGraphDriver
            let! records = driver.ExecuteReadAsync("RETURN 1 AS n", Map.empty)
            Expect.hasLength records 1 "one result"
        finally
            neo4jContainer.StopAsync().Wait()
    }

    testTask "basic read query returns typed results" {
        do! neo4jContainer.StartAsync()
        try
            use driver = createDriver () :> IGraphDriver
            // Create test data
            let! _ = driver.ExecuteWriteAsync("CREATE (:Person {name: 'Tom', age: 50})", Map.empty)
            let! _ = driver.ExecuteWriteAsync("CREATE (:Person {name: 'Alice', age: 30})", Map.empty)

            // Query with typed result
            let query : CypherQuery<Person> = {
                Clauses = [
                    Match([NodePattern("p", Some "Person", Map.empty)], false)
                    Return([{ Expr = Variable "p"; Alias = None }], false)
                    OrderBy [(Property("p", "name"), Ascending)]
                ]
                Parameters = Map.empty
            }
            let! people = Cypher.executeAsync<Person> driver query
            Expect.hasLength people 2 "two people"
            let sorted = people |> List.sortBy (fun p -> p.Name)
            Expect.equal sorted.[0].Name "Alice" "first person"
            Expect.equal sorted.[1].Name "Tom" "second person"
            Expect.equal sorted.[1].Age 50 "Tom's age"

            // Cleanup
            let! _ = driver.ExecuteWriteAsync("MATCH (n) DETACH DELETE n", Map.empty)
            ()
        finally
            neo4jContainer.StopAsync().Wait()
    }

    testTask "write query returns affected count" {
        do! neo4jContainer.StartAsync()
        try
            use driver = createDriver () :> IGraphDriver
            let! count = driver.ExecuteWriteAsync(
                "CREATE (:Person {name: $name, age: $age})",
                Map.ofList ["name", box "Bob"; "age", box 25])
            Expect.isGreaterThan count 0 "nodes created"

            let! _ = driver.ExecuteWriteAsync("MATCH (n) DETACH DELETE n", Map.empty)
            ()
        finally
            neo4jContainer.StopAsync().Wait()
    }

    testTask "option field mapping — null becomes None" {
        do! neo4jContainer.StartAsync()
        try
            use driver = createDriver () :> IGraphDriver
            // Create node with missing optional field
            let! _ = driver.ExecuteWriteAsync("CREATE (:Item {name: 'test'})", Map.empty)

            let! records = driver.ExecuteReadAsync(
                "MATCH (n:Item) RETURN n.name AS name, n.missing AS missing",
                Map.empty)
            Expect.hasLength records 1 "one record"

            let! _ = driver.ExecuteWriteAsync("MATCH (n) DETACH DELETE n", Map.empty)
            ()
        finally
            neo4jContainer.StopAsync().Wait()
    }

    testTask "transaction commit" {
        do! neo4jContainer.StartAsync()
        try
            use driver = createDriver () :> IGraphDriver
            let! result = Cypher.inTransaction driver (fun tx ->
                task {
                    let! _ = tx.ExecuteWriteAsync(
                        "CREATE (:Person {name: $n})", Map.ofList ["n", box "TxPerson1"])
                    let! _ = tx.ExecuteWriteAsync(
                        "CREATE (:Person {name: $n})", Map.ofList ["n", box "TxPerson2"])
                    return 2
                })
            Expect.equal result 2 "both created"

            let! records = driver.ExecuteReadAsync(
                "MATCH (p:Person) WHERE p.name STARTS WITH 'TxPerson' RETURN p.name AS name",
                Map.empty)
            Expect.hasLength records 2 "two transaction nodes exist"

            let! _ = driver.ExecuteWriteAsync("MATCH (n) DETACH DELETE n", Map.empty)
            ()
        finally
            neo4jContainer.StopAsync().Wait()
    }

    testTask "transaction auto-rollback on exception" {
        do! neo4jContainer.StartAsync()
        try
            use driver = createDriver () :> IGraphDriver
            try
                let! _ = Cypher.inTransaction driver (fun tx ->
                    task {
                        let! _ = tx.ExecuteWriteAsync(
                            "CREATE (:Person {name: 'Rollback'})", Map.empty)
                        failwith "intentional error"
                        return 1
                    })
                ()
            with _ -> ()

            let! records = driver.ExecuteReadAsync(
                "MATCH (p:Person {name: 'Rollback'}) RETURN p", Map.empty)
            Expect.hasLength records 0 "rolled back — node should not exist"
        finally
            neo4jContainer.StopAsync().Wait()
    }

    testTask "disposed driver throws" {
        do! neo4jContainer.StartAsync()
        try
            let driver = createDriver ()
            do! (driver :> IAsyncDisposable).DisposeAsync()
            Expect.throwsT<FyperConnectionException>
                (fun () ->
                    (driver :> IGraphDriver).ExecuteReadAsync("RETURN 1", Map.empty)
                    |> Async.AwaitTask |> Async.RunSynchronously |> ignore)
                "should throw after dispose"
        finally
            neo4jContainer.StopAsync().Wait()
    }
]
