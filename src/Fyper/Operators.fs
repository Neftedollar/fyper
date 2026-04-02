namespace Fyper

/// Phantom types and operators for use inside the cypher { } computation expression.
/// These operators exist ONLY for type-checking and quotation capture.
/// They throw at runtime — they are never called, only captured as expression trees.
[<AutoOpen>]
module Operators =

    /// Phantom type representing a graph relationship type reference.
    type EdgeType<'R> = | EdgeType

    /// <summary>Create a typed edge/relationship reference.</summary>
    /// <example>matchRel (p -- edge&lt;ActedIn&gt; --> m)</example>
    let edge<'R> : EdgeType<'R> = EdgeType

    /// Source type for <c>for ... in ... do</c> node bindings in the CE.
    type NodeSource<'T> = | NodeSource

    /// <summary>Create a typed node source for MATCH.</summary>
    /// <example>for p in node&lt;Person&gt; do</example>
    let node<'T> : NodeSource<'T> = NodeSource

    /// <summary>Create a typed node source for OPTIONAL MATCH.</summary>
    /// <example>for m in optionalNode&lt;Movie&gt; do</example>
    let optionalNode<'T> : NodeSource<'T> = NodeSource

    /// Intermediate type for partial edge patterns: <c>from -- edge&lt;R&gt;</c>
    type PartialEdge<'A, 'R> = { __phantom: unit }

    /// Complete edge pattern: <c>from -- edge&lt;R&gt; --> to</c>
    type EdgePattern<'A, 'R, 'B> = { __phantom: unit }

    // ─── Relationship operators ───
    // Cypher: (p)-[:ACTED_IN]->(m)
    // Fyper:   p -- edge<ActedIn> --> m

    /// <summary>Left part of relationship pattern. Connects node to edge type.</summary>
    /// <remarks>Only valid inside cypher { } CE. Throws at runtime.</remarks>
    /// <example>p -- edge&lt;ActedIn&gt;</example>
    let ( -- ) (a: 'A) (r: EdgeType<'R>) : PartialEdge<'A, 'R> =
        failwith "This operator is only valid inside a cypher { } computation expression"

    /// <summary>Outgoing arrow. Completes relationship pattern with target node.</summary>
    /// <remarks>Only valid inside cypher { } CE. Throws at runtime.</remarks>
    /// <example>p -- edge&lt;ActedIn&gt; --> m</example>
    let ( --> ) (partial: PartialEdge<'A, 'R>) (b: 'B) : EdgePattern<'A, 'R, 'B> =
        failwith "This operator is only valid inside a cypher { } computation expression"

    /// <summary>Incoming arrow. Completes relationship pattern with source node.</summary>
    /// <remarks>Only valid inside cypher { } CE. Throws at runtime.</remarks>
    /// <example>p &lt;-- edge&lt;ActedIn&gt; -- m</example>
    let ( <-- ) (partial: PartialEdge<'A, 'R>) (b: 'B) : EdgePattern<'A, 'R, 'B> =
        failwith "This operator is only valid inside a cypher { } computation expression"

    // Keep old operators as aliases for backwards compatibility
    /// Alias for <c>--</c> (backwards compatibility).
    let ( -< ) (a: 'A) (r: EdgeType<'R>) : PartialEdge<'A, 'R> =
        failwith "This operator is only valid inside a cypher { } computation expression"

    /// Alias for <c>--></c> (backwards compatibility).
    let ( >- ) (partial: PartialEdge<'A, 'R>) (b: 'B) : EdgePattern<'A, 'R, 'B> =
        failwith "This operator is only valid inside a cypher { } computation expression"

    // ─── Cypher aggregate functions (quotation-only stubs) ───

    /// <summary>Cypher count(*) aggregate function.</summary>
    /// <returns>Count of matching rows.</returns>
    let count () : int64 =
        failwith "quotation only"

    /// <summary>Cypher count(DISTINCT x) aggregate function.</summary>
    /// <param name="x">Expression to count distinct values of.</param>
    let countDistinct (x: 'T) : int64 =
        failwith "quotation only"

    /// <summary>Cypher sum(x) aggregate function.</summary>
    /// <param name="x">Numeric expression to sum.</param>
    let sum (x: 'T) : 'T =
        failwith "quotation only"

    /// <summary>Cypher avg(x) aggregate function.</summary>
    /// <param name="x">Numeric expression to average.</param>
    let avg (x: 'T) : float =
        failwith "quotation only"

    /// <summary>Cypher collect(x) aggregate function. Collects values into a list.</summary>
    /// <param name="x">Expression to collect.</param>
    let collect (x: 'T) : 'T list =
        failwith "quotation only"

    /// <summary>Cypher size(x) function. Returns the size of a list or string.</summary>
    /// <param name="x">List or string expression.</param>
    let size (x: 'T) : int64 =
        failwith "quotation only"

    /// <summary>Cypher min(x) aggregate function.</summary>
    /// <param name="x">Expression to find minimum of.</param>
    let cypherMin (x: 'T) : 'T =
        failwith "quotation only"

    /// <summary>Cypher max(x) aggregate function.</summary>
    /// <param name="x">Expression to find maximum of.</param>
    let cypherMax (x: 'T) : 'T =
        failwith "quotation only"

    // ─── CASE expression builder (quotation-only) ───

    /// <summary>Cypher CASE WHEN expression.</summary>
    /// <param name="condition">Boolean condition for WHEN clause.</param>
    /// <param name="result">Value returned when condition is true (THEN).</param>
    /// <param name="elseResult">Value returned when condition is false (ELSE).</param>
    /// <returns>The matched result value.</returns>
    /// <example>select (caseWhen (p.Age &gt; 18) p.Name "minor")</example>
    let caseWhen (condition: bool) (result: 'T) (elseResult: 'T) : 'T =
        failwith "quotation only"
