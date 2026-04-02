namespace Fyper

open System.Threading.Tasks
open Fyper.Ast
open Fyper.CypherCompiler
open Fyper.GraphValue

/// <summary>Public API for executing and inspecting Cypher queries.</summary>
module Cypher =

    /// <summary>Execute a read query and return typed results.</summary>
    /// <param name="driver">Graph database driver (Neo4j, AGE, etc.).</param>
    /// <param name="query">Typed Cypher query built via the CE or raw AST API.</param>
    /// <returns>List of typed results mapped from graph records.</returns>
    let executeAsync<'T> (driver: IGraphDriver) (query: CypherQuery<'T>) : Task<'T list> =
        task {
            let compiled = compile query
            let! records = driver.ExecuteReadAsync(compiled.Cypher, compiled.Parameters)
            return records |> List.map ResultMapper.mapGraphRecord<'T>
        }

    /// <summary>Execute a write query and return the count of affected entities.</summary>
    /// <param name="driver">Graph database driver.</param>
    /// <param name="query">Typed Cypher mutation query (CREATE, SET, DELETE, MERGE).</param>
    /// <returns>Number of affected nodes, relationships, and properties.</returns>
    let executeWriteAsync<'T> (driver: IGraphDriver) (query: CypherQuery<'T>) : Task<int> =
        task {
            let compiled = compile query
            return! driver.ExecuteWriteAsync(compiled.Cypher, compiled.Parameters)
        }

    /// <summary>Execute a raw Cypher query string (escape hatch for unsupported features).</summary>
    /// <param name="driver">Graph database driver.</param>
    /// <param name="cypherStr">Raw Cypher query string.</param>
    /// <param name="parameters">Query parameters as key-value map.</param>
    /// <returns>List of untyped graph records.</returns>
    let rawAsync (driver: IGraphDriver) (cypherStr: string) (parameters: Map<string, obj>) : Task<GraphRecord list> =
        driver.ExecuteReadAsync(cypherStr, parameters)

    /// <summary>Compile a query to a human-readable debug string with parameters.</summary>
    /// <param name="query">Typed Cypher query.</param>
    /// <returns>Cypher string followed by parameter dump.</returns>
    let toDebugString (query: CypherQuery<'T>) : string =
        let compiled = compile query
        sprintf "%s\n-- Parameters: %A" compiled.Cypher compiled.Parameters

    /// <summary>Inspect generated Cypher without executing. Returns the compiled query string and parameters.</summary>
    /// <param name="query">Typed Cypher query.</param>
    /// <returns>Tuple of (Cypher string, parameter map).</returns>
    /// <example>let cypher, pars = query |> Cypher.toCypher</example>
    let toCypher (query: CypherQuery<'T>) : string * Map<string, obj> =
        let compiled = compile query
        compiled.Cypher, compiled.Parameters

    /// <summary>Execute multiple queries within an explicit transaction.
    /// Auto-commits on success, auto-rollbacks on exception.</summary>
    /// <param name="driver">Graph database driver.</param>
    /// <param name="action">Async function receiving a transaction context for executing queries.</param>
    /// <returns>The result of the action function.</returns>
    /// <example>
    /// let! result = Cypher.inTransaction driver (fun tx -> task {
    ///     let! _ = query1 |> Cypher.executeWriteAsync tx
    ///     let! _ = query2 |> Cypher.executeWriteAsync tx
    ///     return 2
    /// })
    /// </example>
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
