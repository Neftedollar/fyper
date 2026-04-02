namespace Fyper

open Microsoft.FSharp.Quotations
open Fyper.Ast

/// The Cypher computation expression builder.
/// Uses the Quote member for auto-quotation — all expressions inside
/// `cypher { ... }` are captured as expression trees, not evaluated.
type CypherBuilder() =

    // Auto-quotation: entire CE body becomes an expression tree
    member _.Quote(e: Expr<'T>) : Expr<'T> = e

    // Run: receives the quoted CE, delegates to QueryTranslator
    member _.Run(e: Expr<CypherQuery<'T>>) : CypherQuery<'T> =
        QueryTranslator.translate<'T> e

    // ─── Core CE members (never called at runtime; exist for type checking) ───

    member _.Zero() : CypherQuery<unit> =
        { Clauses = []; Parameters = Map.empty }

    member _.Yield(_x: 'T) : CypherQuery<'T> =
        { Clauses = []; Parameters = Map.empty }

    member _.Return(x: 'T) : CypherQuery<'T> =
        { Clauses = []; Parameters = Map.empty }

    /// for p in node<Person> do → MATCH (p:Person)
    member _.For(_source: NodeSource<'T>, _body: 'T -> CypherQuery<'R>) : CypherQuery<'R> =
        failwith "quotation only"

    // ─── Custom operations ───

    /// WHERE predicate
    [<CustomOperation("where", MaintainsVariableSpace = true)>]
    member _.Where(_source: CypherQuery<'T>, [<ProjectionParameter>] _predicate: 'T -> bool) : CypherQuery<'T> =
        failwith "quotation only"

    /// SELECT projection (maps to Cypher RETURN)
    [<CustomOperation("select", AllowIntoPattern = true)>]
    member _.Select(_source: CypherQuery<'T>, [<ProjectionParameter>] _selector: 'T -> 'R) : CypherQuery<'R> =
        failwith "quotation only"

    /// ORDER BY expression (ascending)
    [<CustomOperation("orderBy", MaintainsVariableSpace = true)>]
    member _.OrderBy(_source: CypherQuery<'T>, [<ProjectionParameter>] _selector: 'T -> 'Key) : CypherQuery<'T> =
        failwith "quotation only"

    /// ORDER BY expression (descending)
    [<CustomOperation("orderByDesc", MaintainsVariableSpace = true)>]
    member _.OrderByDescending(_source: CypherQuery<'T>, [<ProjectionParameter>] _selector: 'T -> 'Key) : CypherQuery<'T> =
        failwith "quotation only"

    /// SKIP n
    [<CustomOperation("skip", MaintainsVariableSpace = true)>]
    member _.Skip(_source: CypherQuery<'T>, _count: int) : CypherQuery<'T> =
        failwith "quotation only"

    /// LIMIT n
    [<CustomOperation("limit", MaintainsVariableSpace = true)>]
    member _.Limit(_source: CypherQuery<'T>, _count: int) : CypherQuery<'T> =
        failwith "quotation only"

    /// MATCH relationship pattern via match'
    [<CustomOperation("matchRel", MaintainsVariableSpace = true)>]
    member _.MatchRel(_source: CypherQuery<'T>, _pattern: EdgePattern<'A, 'R, 'B>) : CypherQuery<'T> =
        failwith "quotation only"

[<AutoOpen>]
module CypherBuilderInstance =
    /// The global cypher computation expression builder
    let cypher = CypherBuilder()
