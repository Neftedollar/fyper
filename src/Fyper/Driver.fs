namespace Fyper

open System
open System.Threading.Tasks
open Fyper.GraphValue

/// Abstract graph database driver.
/// Each backend (Neo4j, AGE, etc.) implements this interface.
type IGraphDriver =
    inherit IAsyncDisposable

    /// Execute a read query. Returns a list of result records.
    abstract ExecuteReadAsync: cypher: string * parameters: Map<string, obj> -> Task<GraphRecord list>

    /// Execute a write query. Returns the count of affected entities.
    abstract ExecuteWriteAsync: cypher: string * parameters: Map<string, obj> -> Task<int>
