namespace Fyper

module Ast =

    /// Direction of a relationship in a pattern
    type Direction =
        | Outgoing    // -[r:TYPE]->
        | Incoming    // <-[r:TYPE]-
        | Undirected  // -[r:TYPE]-

    /// Variable-length path specification
    type PathLength =
        | Exactly of int
        | Between of min: int * max: int
        | AtLeast of int
        | AtMost of int
        | AnyLength

    /// Pattern — represents a graph pattern in MATCH/CREATE/MERGE
    type Pattern =
        | NodePattern of
            alias: string *
            label: string option *
            properties: Map<string, Expr>
        | RelPattern of
            from: Pattern *
            relAlias: string option *
            relType: string option *
            relProps: Map<string, Expr> *
            direction: Direction *
            pathLength: PathLength option *
            to': Pattern
        | NamedPath of
            pathName: string *
            pattern: Pattern

    /// Cypher expression (used in WHERE, RETURN, SET, etc.)
    and Expr =
        | Literal of obj
        | Param of string
        | Variable of string
        | Property of owner: string * name: string
        | BinOp of Expr * BinOp * Expr
        | UnaryOp of UnaryOp * Expr
        | FuncCall of name: string * Expr list
        | ListExpr of Expr list
        | MapExpr of (string * Expr) list
        | CaseExpr of
            scrutinee: Expr option *
            whenClauses: (Expr * Expr) list *
            elseClause: Expr option
        | ExistsSubquery of Clause list
        | Null

    /// Binary operators
    and BinOp =
        | Eq | Neq | Gt | Gte | Lt | Lte
        | And | Or | Xor
        | Contains | StartsWith | EndsWith
        | In
        | Add | Sub | Mul | Div | Mod
        | RegexMatch

    /// Unary operators
    and UnaryOp =
        | Not
        | IsNull
        | IsNotNull
        | Exists

    /// Sort direction
    and SortDirection = Ascending | Descending

    /// Items in RETURN / WITH
    and ReturnItem = {
        Expr: Expr
        Alias: string option
    }

    /// Items in SET clause
    and SetItem =
        | SetProperty of owner: string * property: string * value: Expr
        | SetAllProperties of owner: string * value: Expr
        | MergeProperties of owner: string * value: Expr
        | AddLabel of owner: string * label: string

    /// Items in REMOVE clause
    and RemoveItem =
        | RemoveProperty of owner: string * property: string
        | RemoveLabel of owner: string * label: string

    /// Cypher clauses — each becomes one line in the generated query
    and Clause =
        | Match of patterns: Pattern list * optional: bool
        | Where of Expr
        | Return of items: ReturnItem list * distinct: bool
        | With of items: ReturnItem list * distinct: bool
        | Create of Pattern list
        | Merge of
            pattern: Pattern *
            onMatch: SetItem list *
            onCreate: SetItem list
        | Delete of aliases: string list * detach: bool
        | Set of SetItem list
        | Remove of RemoveItem list
        | OrderBy of (Expr * SortDirection) list
        | Skip of Expr
        | Limit of Expr
        | Unwind of expr: Expr * alias: string
        | Call of
            procedure: string *
            args: Expr list *
            yields: string list
        | Union of all: bool
        | RawCypher of string

    /// A complete Cypher query with phantom result type
    type CypherQuery<'T> = {
        Clauses: Clause list
        Parameters: Map<string, obj>
    }

    /// Query builder helpers (raw AST API)
    module Query =
        let empty<'T> : CypherQuery<'T> =
            { Clauses = []; Parameters = Map.empty }

        let addClause (clause: Clause) (q: CypherQuery<'T>) : CypherQuery<'T> =
            { q with Clauses = q.Clauses @ [clause] }

        let addParam (name: string) (value: obj) (q: CypherQuery<'T>) : CypherQuery<'T> =
            { q with Parameters = q.Parameters |> Map.add name value }

        let matchNodes (patterns: Pattern list) (q: CypherQuery<'T>) : CypherQuery<'T> =
            q |> addClause (Match(patterns, false))

        let optionalMatch (patterns: Pattern list) (q: CypherQuery<'T>) : CypherQuery<'T> =
            q |> addClause (Match(patterns, true))

        let where (expr: Expr) (q: CypherQuery<'T>) : CypherQuery<'T> =
            q |> addClause (Where expr)

        let return' (items: ReturnItem list) (q: CypherQuery<'T>) : CypherQuery<'T> =
            q |> addClause (Return(items, false))

        let returnDistinct (items: ReturnItem list) (q: CypherQuery<'T>) : CypherQuery<'T> =
            q |> addClause (Return(items, true))

        let with' (items: ReturnItem list) (q: CypherQuery<'T>) : CypherQuery<'T> =
            q |> addClause (With(items, false))

        let orderBy (items: (Expr * SortDirection) list) (q: CypherQuery<'T>) : CypherQuery<'T> =
            q |> addClause (OrderBy items)

        let skip (n: int) (q: CypherQuery<'T>) : CypherQuery<'T> =
            let paramName = sprintf "skip_%d" n
            q |> addClause (Skip(Param paramName))
            |> addParam paramName (box n)

        let limit (n: int) (q: CypherQuery<'T>) : CypherQuery<'T> =
            let paramName = sprintf "limit_%d" n
            q |> addClause (Limit(Param paramName))
            |> addParam paramName (box n)

        let create (patterns: Pattern list) (q: CypherQuery<'T>) : CypherQuery<'T> =
            q |> addClause (Create patterns)

        let delete (aliases: string list) (q: CypherQuery<'T>) : CypherQuery<'T> =
            q |> addClause (Delete(aliases, false))

        let detachDelete (aliases: string list) (q: CypherQuery<'T>) : CypherQuery<'T> =
            q |> addClause (Delete(aliases, true))

        let set (items: SetItem list) (q: CypherQuery<'T>) : CypherQuery<'T> =
            q |> addClause (Set items)
