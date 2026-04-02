namespace Fyper

/// Phantom types and operators for use inside the cypher { } computation expression.
/// These operators exist ONLY for type-checking and quotation capture.
/// They throw at runtime — they are never called, only captured as expression trees.
[<AutoOpen>]
module Operators =

    /// Phantom type for edge/relationship type reference
    type EdgeType<'R> = | EdgeType

    /// Create a typed edge reference for use in match' patterns
    let edge<'R> : EdgeType<'R> = EdgeType

    /// Source type for `for ... in ... do` node bindings in the CE
    type NodeSource<'T> = | NodeSource

    /// Create a typed node source for MATCH
    let node<'T> : NodeSource<'T> = NodeSource

    /// Create a typed node source for OPTIONAL MATCH
    let optionalNode<'T> : NodeSource<'T> = NodeSource

    /// Partial edge pattern: from -[edge<R>]-> ???
    type PartialEdge<'A, 'R> = { __phantom: unit }

    /// Complete edge pattern: from -[edge<R>]-> to
    type EdgePattern<'A, 'R, 'B> = { __phantom: unit }

    // ─── Outgoing operators: a -[edge<R>]-> b ───

    let ( -< ) (a: 'A) (r: EdgeType<'R>) : PartialEdge<'A, 'R> =
        failwith "This operator is only valid inside a cypher { } computation expression"

    let ( >- ) (partial: PartialEdge<'A, 'R>) (b: 'B) : EdgePattern<'A, 'R, 'B> =
        failwith "This operator is only valid inside a cypher { } computation expression"

    // ─── Cypher aggregate functions (quotation-only stubs) ───

    let count () : int64 =
        failwith "quotation only"

    let countDistinct (x: 'T) : int64 =
        failwith "quotation only"

    let sum (x: 'T) : 'T =
        failwith "quotation only"

    let avg (x: 'T) : float =
        failwith "quotation only"

    let collect (x: 'T) : 'T list =
        failwith "quotation only"

    let size (x: 'T) : int64 =
        failwith "quotation only"
