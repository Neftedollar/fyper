namespace Fyper

open System
open System.Threading.Tasks
open Fyper.GraphValue

// ─── Exceptions ───

/// Base exception for all Fyper errors.
type FyperException(message: string, inner: exn) =
    inherit Exception(message, inner)
    new(message) = FyperException(message, null)

/// Connection or authentication failure.
type FyperConnectionException(message: string, inner: exn) =
    inherit FyperException(message, inner)
    new(message) = FyperConnectionException(message, null)

/// Cypher query execution failure (syntax error, constraint violation).
type FyperQueryException(message: string, query: string, parameters: Map<string, obj>, inner: exn) =
    inherit FyperException(message, inner)
    member _.Query = query
    member _.Parameters = parameters

/// Result mapping failure (type mismatch between graph and F# type).
type FyperMappingException(message: string, targetType: Type, sourceValue: GraphValue, inner: exn) =
    inherit FyperException(message, inner)
    member _.TargetType = targetType
    member _.SourceValue = sourceValue
    new(message, targetType, sourceValue) = FyperMappingException(message, targetType, sourceValue, null)

/// Feature not supported by the current backend.
type FyperUnsupportedFeatureException(feature: string, backend: string) =
    inherit FyperException(sprintf "Feature '%s' is not supported by backend '%s'" feature backend)
    member _.Feature = feature
    member _.Backend = backend

// ─── Capability flags ───

/// Declares which Cypher features a backend supports.
/// Used to reject unsupported queries at construction time.
type DriverCapabilities = {
    SupportsOptionalMatch: bool
    SupportsMerge: bool
    SupportsUnwind: bool
    SupportsCase: bool
    SupportsCallProcedure: bool
    SupportsExistsSubquery: bool
    SupportsNamedPaths: bool
}

module DriverCapabilities =
    /// All features supported (e.g., Neo4j).
    let all = {
        SupportsOptionalMatch = true
        SupportsMerge = true
        SupportsUnwind = true
        SupportsCase = true
        SupportsCallProcedure = true
        SupportsExistsSubquery = true
        SupportsNamedPaths = true
    }

    /// Minimal feature set (e.g., Apache AGE).
    let minimal = {
        SupportsOptionalMatch = false
        SupportsMerge = false
        SupportsUnwind = false
        SupportsCase = false
        SupportsCallProcedure = false
        SupportsExistsSubquery = false
        SupportsNamedPaths = false
    }

// ─── Transaction interface ───

/// Transaction scope — same read/write interface, scoped to a transaction.
type IGraphTransaction =
    inherit IAsyncDisposable

    /// Execute a read query within this transaction.
    abstract ExecuteReadAsync: cypher: string * parameters: Map<string, obj> -> Task<GraphRecord list>

    /// Execute a write query within this transaction.
    abstract ExecuteWriteAsync: cypher: string * parameters: Map<string, obj> -> Task<int>

    /// Commit the transaction.
    abstract CommitAsync: unit -> Task<unit>

    /// Rollback the transaction.
    abstract RollbackAsync: unit -> Task<unit>

// ─── Driver interface ───

/// Abstract graph database driver.
/// Each backend (Neo4j, AGE, etc.) implements this interface.
type IGraphDriver =
    inherit IAsyncDisposable

    /// Execute a read query. Returns a list of result records.
    abstract ExecuteReadAsync: cypher: string * parameters: Map<string, obj> -> Task<GraphRecord list>

    /// Execute a write query. Returns the count of affected entities.
    abstract ExecuteWriteAsync: cypher: string * parameters: Map<string, obj> -> Task<int>

    /// Begin an explicit transaction for multi-statement atomicity.
    abstract BeginTransactionAsync: unit -> Task<IGraphTransaction>

    /// Declare which Cypher features this backend supports.
    abstract Capabilities: DriverCapabilities
