module Fyper.Integration.Tests.Neo4jTests

open System
open System.Threading.Tasks
open Expecto
open Fyper
open Fyper.Ast
open Fyper.Neo4j
open Testcontainers.Neo4j

type Person = { Name: string; Age: int }

let neo4jContainer =
    Neo4jBuilder("neo4j:5")
        .Build()

let mutable containerStarted = false

let ensureContainer () =
    task {
        if not containerStarted then
            do! neo4jContainer.StartAsync()
            containerStarted <- true
    }

let createDriver () =
    let boltUri = neo4jContainer.GetConnectionString()
    let driver = Neo4j.Driver.GraphDatabase.Driver(boltUri, Neo4j.Driver.AuthTokens.None)
    new Neo4jDriver(driver)

let cleanup (driver: IGraphDriver) =
    task { let! _ = driver.ExecuteWriteAsync("MATCH (n) DETACH DELETE n", Map.empty) in () }

[<Tests>]
let neo4jIntegrationTests = testSequenced <| testList "Neo4j Integration" [

    testTask "connect and basic query" {
        do! ensureContainer ()
        use driver = createDriver () :> IGraphDriver
        let! records = driver.ExecuteReadAsync("RETURN 1 AS n", Map.empty)
        Expect.hasLength records 1 "one result"
    }

    testTask "read query returns typed results" {
        do! ensureContainer ()
        use driver = createDriver () :> IGraphDriver
        let! _ = driver.ExecuteWriteAsync("CREATE (:Person {name: 'Tom', age: 50})", Map.empty)
        let! _ = driver.ExecuteWriteAsync("CREATE (:Person {name: 'Alice', age: 30})", Map.empty)

        let query : CypherQuery<Person> = {
            Clauses = [
                Match([NodePattern("p", Some "Person", Map.empty)], false)
                Return([{ Expr = Variable "p"; Alias = None }], false)
            ]
            Parameters = Map.empty
        }
        let! people = Cypher.executeAsync<Person> driver query
        Expect.hasLength people 2 "two people"
        let sorted = people |> List.sortBy (fun p -> p.Name)
        Expect.equal sorted.[0].Name "Alice" "first"
        Expect.equal sorted.[1].Name "Tom" "second"
        Expect.equal sorted.[1].Age 50 "age"
        do! cleanup driver
    }

    testTask "write query returns count" {
        do! ensureContainer ()
        use driver = createDriver () :> IGraphDriver
        let! count = driver.ExecuteWriteAsync(
            "CREATE (:Person {name: $name, age: $age})",
            Map.ofList ["name", box "Bob"; "age", box 25])
        Expect.isGreaterThan count 0 "created"
        do! cleanup driver
    }

    testTask "transaction commit" {
        do! ensureContainer ()
        use driver = createDriver () :> IGraphDriver
        let! result = Cypher.inTransaction driver (fun tx ->
            task {
                let! _ = tx.ExecuteWriteAsync("CREATE (:Person {name: $n})", Map.ofList ["n", box "Tx1"])
                let! _ = tx.ExecuteWriteAsync("CREATE (:Person {name: $n})", Map.ofList ["n", box "Tx2"])
                return 2
            })
        Expect.equal result 2 "both created"
        let! records = driver.ExecuteReadAsync(
            "MATCH (p:Person) WHERE p.name STARTS WITH 'Tx' RETURN p.name AS name", Map.empty)
        Expect.hasLength records 2 "committed"
        do! cleanup driver
    }

    testTask "transaction rollback on exception" {
        do! ensureContainer ()
        use driver = createDriver () :> IGraphDriver
        try
            let! _ = Cypher.inTransaction driver (fun tx ->
                task {
                    let! _ = tx.ExecuteWriteAsync("CREATE (:Person {name: 'Rollback'})", Map.empty)
                    failwith "intentional"
                    return 1
                })
            ()
        with _ -> ()
        let! records = driver.ExecuteReadAsync(
            "MATCH (p:Person {name: 'Rollback'}) RETURN p", Map.empty)
        Expect.hasLength records 0 "rolled back"
    }

    testTask "disposed driver throws" {
        do! ensureContainer ()
        let driver = createDriver ()
        do! (driver :> IAsyncDisposable).DisposeAsync()
        let threw = ref false
        try
            let! _ = (driver :> IGraphDriver).ExecuteReadAsync("RETURN 1", Map.empty)
            ()
        with
        | :? FyperConnectionException -> threw.Value <- true
        | :? AggregateException as ex when (ex.InnerException :? FyperConnectionException) -> threw.Value <- true
        Expect.isTrue threw.Value "should throw after dispose"
    }
]
