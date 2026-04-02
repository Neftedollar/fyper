namespace Fyper

open System.Threading.Tasks
open Fyper.Ast
open Fyper.CypherCompiler
open Fyper.GraphValue

/// Public API for executing Cypher queries.
module Cypher =

    /// Execute a read query and return typed results
    let executeAsync<'T> (driver: IGraphDriver) (query: CypherQuery<'T>) : Task<'T list> =
        task {
            let compiled = compile query
            let! records = driver.ExecuteReadAsync(compiled.Cypher, compiled.Parameters)
            return records |> List.map ResultMapper.mapGraphRecord<'T>
        }

    /// Execute a write query and return the count of affected entities
    let executeWriteAsync<'T> (driver: IGraphDriver) (query: CypherQuery<'T>) : Task<int> =
        task {
            let compiled = compile query
            return! driver.ExecuteWriteAsync(compiled.Cypher, compiled.Parameters)
        }

    /// Execute raw Cypher (escape hatch)
    let rawAsync (driver: IGraphDriver) (cypherStr: string) (parameters: Map<string, obj>) : Task<GraphRecord list> =
        driver.ExecuteReadAsync(cypherStr, parameters)

    /// Compile a query to Cypher string for debugging/logging
    let toDebugString (query: CypherQuery<'T>) : string =
        let compiled = compile query
        sprintf "%s\n-- Parameters: %A" compiled.Cypher compiled.Parameters

    /// Inspect generated Cypher without executing.
    /// Returns (cypherString, parameters).
    let toCypher (query: CypherQuery<'T>) : string * Map<string, obj> =
        let compiled = compile query
        compiled.Cypher, compiled.Parameters

    /// Execute multiple queries within an explicit transaction.
    /// Auto-commits on success, auto-rollbacks on exception.
    let inTransaction (driver: IGraphDriver) (action: IGraphTransaction -> Task<'T>) : Task<'T> =
        task {
            let! tx = driver.BeginTransactionAsync()
            try
                let! result = action tx
                do! tx.CommitAsync()
                return result
            with ex ->
                try do! tx.RollbackAsync() with _ -> ()
                return raise (System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).SourceException)
        }
