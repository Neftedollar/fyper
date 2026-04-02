namespace Fyper.Parser

open Fyper.Ast
module A = Fyper.Ast

/// Parser state — walks a token list
type ParserState = {
    Tokens: Token array
    mutable Pos: int
}

/// Cypher parser — recursive descent, zero dependencies.
/// Parses Cypher query strings into Fyper's typed AST.
module CypherParser =

    // ─── State helpers ───

    let private create (tokens: Token list) : ParserState =
        { Tokens = Array.ofList tokens; Pos = 0 }

    let private peek (s: ParserState) : Token =
        if s.Pos < s.Tokens.Length then s.Tokens.[s.Pos] else EOF

    let private peekAt (s: ParserState) (offset: int) : Token =
        let i = s.Pos + offset
        if i < s.Tokens.Length then s.Tokens.[i] else EOF

    let private advance (s: ParserState) : Token =
        let t = peek s
        s.Pos <- s.Pos + 1
        t

    let private expect (s: ParserState) (expected: Token) : unit =
        let got = advance s
        if got <> expected then
            failwithf "Expected %A but got %A at position %d" expected got s.Pos

    let private expectIdentifier (s: ParserState) : string =
        match advance s with
        | IDENTIFIER name -> name
        | other -> failwithf "Expected identifier but got %A at position %d" other s.Pos

    let private tryConsume (s: ParserState) (expected: Token) : bool =
        if peek s = expected then
            advance s |> ignore
            true
        else false

    // ─── Expression parser (precedence climbing) ───

    let rec private parseExpr (s: ParserState) : Expr =
        parseOrExpr s

    and private parseOrExpr (s: ParserState) : Expr =
        let mutable left = parseXorExpr s
        while peek s = OR do
            advance s |> ignore
            let right = parseXorExpr s
            left <- BinOp(left, A.Or, right)
        left

    and private parseXorExpr (s: ParserState) : Expr =
        let mutable left = parseAndExpr s
        while peek s = XOR do
            advance s |> ignore
            let right = parseAndExpr s
            left <- BinOp(left, A.Xor, right)
        left

    and private parseAndExpr (s: ParserState) : Expr =
        let mutable left = parseNotExpr s
        while peek s = AND do
            advance s |> ignore
            let right = parseNotExpr s
            left <- BinOp(left, A.And, right)
        left

    and private parseNotExpr (s: ParserState) : Expr =
        if peek s = NOT then
            advance s |> ignore
            UnaryOp(A.Not, parseNotExpr s)
        else
            parseComparisonExpr s

    and private parseComparisonExpr (s: ParserState) : Expr =
        let left = parseStringExpr s
        match peek s with
        | EQ -> advance s |> ignore; BinOp(left, A.Eq, parseStringExpr s)
        | NEQ -> advance s |> ignore; BinOp(left, A.Neq, parseStringExpr s)
        | LT -> advance s |> ignore; BinOp(left, A.Lt, parseStringExpr s)
        | GT -> advance s |> ignore; BinOp(left, A.Gt, parseStringExpr s)
        | LTE -> advance s |> ignore; BinOp(left, A.Lte, parseStringExpr s)
        | GTE -> advance s |> ignore; BinOp(left, A.Gte, parseStringExpr s)
        | REGEX_MATCH -> advance s |> ignore; BinOp(left, A.RegexMatch, parseStringExpr s)
        | IN -> advance s |> ignore; BinOp(left, A.In, parseStringExpr s)
        | IS ->
            advance s |> ignore
            if tryConsume s NOT then
                expect s NULL
                UnaryOp(A.IsNotNull, left)
            else
                expect s NULL
                UnaryOp(A.IsNull, left)
        | _ -> left

    and private parseStringExpr (s: ParserState) : Expr =
        let left = parseAddExpr s
        match peek s with
        | CONTAINS -> advance s |> ignore; BinOp(left, A.Contains, parseAddExpr s)
        | STARTS ->
            advance s |> ignore
            // STARTS WITH
            if peek s = WITH then advance s |> ignore
            BinOp(left, A.StartsWith, parseAddExpr s)
        | ENDS ->
            advance s |> ignore
            // ENDS WITH
            if peek s = WITH then advance s |> ignore
            BinOp(left, A.EndsWith, parseAddExpr s)
        | _ -> left

    and private parseAddExpr (s: ParserState) : Expr =
        let mutable left = parseMulExpr s
        while peek s = PLUS || peek s = DASH do
            let op = if advance s = PLUS then A.Add else A.Sub
            let right = parseMulExpr s
            left <- BinOp(left, op, right)
        left

    and private parseMulExpr (s: ParserState) : Expr =
        let mutable left = parseUnaryExpr s
        while peek s = STAR || peek s = SLASH || peek s = PERCENT do
            let op =
                match advance s with
                | STAR -> A.Mul
                | SLASH -> A.Div
                | _ -> A.Mod
            let right = parseUnaryExpr s
            left <- BinOp(left, op, right)
        left

    and private parseUnaryExpr (s: ParserState) : Expr =
        match peek s with
        | DASH ->
            advance s |> ignore
            let inner = parsePrimaryExpr s
            // Negate: -expr → 0 - expr
            BinOp(Literal (box 0), A.Sub, inner)
        | _ -> parsePrimaryExpr s

    and private parsePrimaryExpr (s: ParserState) : Expr =
        match peek s with
        | LPAREN ->
            advance s |> ignore
            let expr = parseExpr s
            expect s RPAREN
            expr
        | LBRACKET ->
            advance s |> ignore
            let items = parseExprList s RBRACKET
            expect s RBRACKET
            ListExpr items
        | LBRACE ->
            advance s |> ignore
            let entries = parseMapEntries s
            expect s RBRACE
            MapExpr entries
        | STRING str -> advance s |> ignore; Literal (box str)
        | INTEGER i -> advance s |> ignore; Literal (box i)
        | FLOAT f -> advance s |> ignore; Literal (box f)
        | TRUE -> advance s |> ignore; Literal (box true)
        | FALSE -> advance s |> ignore; Literal (box false)
        | NULL -> advance s |> ignore; Null
        | PARAMETER name -> advance s |> ignore; Param name
        | DOLLAR ->
            advance s |> ignore
            let name = expectIdentifier s
            Param name
        | CASE -> parseCaseExpr s
        | EXISTS ->
            advance s |> ignore
            if peek s = LBRACE then
                advance s |> ignore
                let clauses = parseClauses s
                expect s RBRACE
                ExistsSubquery clauses
            else
                // exists(expr)
                expect s LPAREN
                let inner = parseExpr s
                expect s RPAREN
                UnaryOp(A.Exists, inner)
        | STAR -> advance s |> ignore; Variable "*"
        | IDENTIFIER name ->
            advance s |> ignore
            if peek s = LPAREN then
                // Function call: name(args)
                advance s |> ignore
                let args =
                    if peek s = RPAREN then []
                    else
                        if peek s = STAR then
                            advance s |> ignore
                            [Variable "*"]
                        else parseExprList s RPAREN
                expect s RPAREN
                FuncCall(name, args)
            elif peek s = DOT then
                // Property access: name.prop
                advance s |> ignore
                let prop =
                    match advance s with
                    | IDENTIFIER p -> p
                    | other -> failwithf "Expected property name after '.', got %A" other
                Property(name, prop)
            else
                Variable name
        | other ->
            failwithf "Unexpected token in expression: %A at position %d" other s.Pos

    and private parseExprList (s: ParserState) (terminator: Token) : Expr list =
        let items = ResizeArray<Expr>()
        if peek s <> terminator then
            items.Add(parseExpr s)
            while peek s = COMMA do
                advance s |> ignore
                items.Add(parseExpr s)
        items |> Seq.toList

    and private parseMapEntries (s: ParserState) : (string * Expr) list =
        let entries = ResizeArray<string * Expr>()
        if peek s <> RBRACE then
            let key = expectIdentifier s
            expect s COLON
            let value = parseExpr s
            entries.Add(key, value)
            while peek s = COMMA do
                advance s |> ignore
                let k = expectIdentifier s
                expect s COLON
                let v = parseExpr s
                entries.Add(k, v)
        entries |> Seq.toList

    and private parseCaseExpr (s: ParserState) : Expr =
        expect s CASE
        // Optional scrutinee
        let scrutinee =
            if peek s <> WHEN then Some (parseExpr s)
            else None
        let whens = ResizeArray<Expr * Expr>()
        while peek s = WHEN do
            advance s |> ignore
            let cond = parseExpr s
            expect s THEN
            let result = parseExpr s
            whens.Add(cond, result)
        let elseClause =
            if tryConsume s ELSE then Some (parseExpr s)
            else None
        expect s END
        CaseExpr(scrutinee, whens |> Seq.toList, elseClause)

    // ─── Pattern parser ───

    and private parsePattern (s: ParserState) : Pattern =
        let node = parseNodePattern s
        // Check for relationship chain
        parseRelChain s node

    and private parseNodePattern (s: ParserState) : Pattern =
        expect s LPAREN
        let alias =
            match peek s with
            | IDENTIFIER name -> advance s |> ignore; name
            | _ -> ""
        let label =
            if tryConsume s COLON then
                Some (expectIdentifier s)
            else None
        let props =
            if peek s = LBRACE then
                advance s |> ignore
                let entries = parseMapEntries s
                expect s RBRACE
                entries |> List.map (fun (k, v) -> k, v) |> Map.ofList
            else Map.empty
        expect s RPAREN
        NodePattern(alias, label, props)

    and private parseRelChain (s: ParserState) (from: Pattern) : Pattern =
        match peek s with
        | DASH ->
            advance s |> ignore
            if peek s = LBRACKET then
                // -[rel]-> or -[rel]-
                advance s |> ignore
                let relAlias, relType, relProps, pathLength = parseRelDetails s
                expect s RBRACKET
                let direction, to' =
                    if tryConsume s ARROW_RIGHT then
                        // ]->(node)
                        Outgoing, parseNodePattern s
                    elif tryConsume s DASH then
                        if tryConsume s GT then
                            // ]-> parsed as DASH GT
                            Outgoing, parseNodePattern s
                        else
                            // ]-(node)
                            Undirected, parseNodePattern s
                    else
                        Undirected, parseNodePattern s
                let relPattern = RelPattern(from, relAlias, relType, relProps, direction, pathLength, to')
                // Continue chain
                parseRelChain s relPattern
            elif peek s = LPAREN then
                // No relationship details: ()-()
                let to' = parseNodePattern s
                let relPattern = RelPattern(from, None, None, Map.empty, Undirected, None, to')
                parseRelChain s relPattern
            else
                from
        | ARROW_LEFT ->
            advance s |> ignore
            // <-[rel]-
            expect s LBRACKET
            let relAlias, relType, relProps, pathLength = parseRelDetails s
            expect s RBRACKET
            expect s DASH
            let to' = parseNodePattern s
            let relPattern = RelPattern(from, relAlias, relType, relProps, Incoming, pathLength, to')
            parseRelChain s relPattern
        | _ -> from

    and private parseRelDetails (s: ParserState) : string option * string option * Map<string, Expr> * PathLength option =
        let alias =
            match peek s with
            | IDENTIFIER name ->
                advance s |> ignore
                Some name
            | _ -> None
        let relType =
            if tryConsume s COLON then
                Some (expectIdentifier s)
            else None
        let pathLength =
            if tryConsume s STAR then
                Some (parsePathLength s)
            else None
        let props =
            if peek s = LBRACE then
                advance s |> ignore
                let entries = parseMapEntries s
                expect s RBRACE
                entries |> Map.ofList
            else Map.empty
        (alias, relType, props, pathLength)

    and private parsePathLength (s: ParserState) : PathLength =
        match peek s with
        | INTEGER n ->
            advance s |> ignore
            if peek s = DOT then
                advance s |> ignore
                expect s DOT
                match peek s with
                | INTEGER m -> advance s |> ignore; Between(int n, int m)
                | _ -> AtLeast (int n)
            else Exactly (int n)
        | DOT ->
            advance s |> ignore
            expect s DOT
            match peek s with
            | INTEGER m -> advance s |> ignore; AtMost (int m)
            | _ -> AnyLength
        | _ -> AnyLength

    // ─── Clause parsers ───

    and private parseClauses (s: ParserState) : Clause list =
        let clauses = ResizeArray<Clause>()
        let mutable cont = true
        while cont do
            match peek s with
            | MATCH -> clauses.Add(parseMatchClause s)
            | OPTIONAL ->
                advance s |> ignore
                expect s MATCH
                clauses.Add(parseMatchBody s true)
            | WHERE -> clauses.Add(parseWhereClause s)
            | RETURN -> clauses.Add(parseReturnClause s)
            | WITH -> clauses.Add(parseWithClause s)
            | CREATE -> clauses.Add(parseCreateClause s)
            | MERGE -> clauses.Add(parseMergeClause s)
            | DELETE -> clauses.Add(parseDeleteClause s false)
            | DETACH ->
                advance s |> ignore
                clauses.Add(parseDeleteClause s true)
            | SET -> clauses.Add(parseSetClause s)
            | REMOVE -> clauses.Add(parseRemoveClause s)
            | ORDER -> clauses.Add(parseOrderByClause s)
            | SKIP -> clauses.Add(parseSkipClause s)
            | LIMIT -> clauses.Add(parseLimitClause s)
            | UNWIND -> clauses.Add(parseUnwindClause s)
            | UNION -> clauses.Add(parseUnionClause s)
            | CALL -> clauses.Add(parseCallClause s)
            | _ -> cont <- false
        clauses |> Seq.toList

    and private parseMatchClause (s: ParserState) : Clause =
        expect s MATCH
        parseMatchBody s false

    and private parseMatchBody (s: ParserState) (optional: bool) : Clause =
        let patterns = ResizeArray<Pattern>()
        patterns.Add(parsePattern s)
        while peek s = COMMA do
            advance s |> ignore
            patterns.Add(parsePattern s)
        Match(patterns |> Seq.toList, optional)

    and private parseWhereClause (s: ParserState) : Clause =
        expect s WHERE
        Where(parseExpr s)

    and private parseReturnClause (s: ParserState) : Clause =
        expect s RETURN
        let distinct = tryConsume s DISTINCT
        let items = parseReturnItems s
        Return(items, distinct)

    and private parseWithClause (s: ParserState) : Clause =
        expect s WITH
        let distinct = tryConsume s DISTINCT
        let items = parseReturnItems s
        With(items, distinct)

    and private parseReturnItems (s: ParserState) : ReturnItem list =
        let items = ResizeArray<ReturnItem>()
        items.Add(parseReturnItem s)
        while peek s = COMMA do
            advance s |> ignore
            items.Add(parseReturnItem s)
        items |> Seq.toList

    and private parseReturnItem (s: ParserState) : ReturnItem =
        let expr = parseExpr s
        let alias =
            if tryConsume s AS then
                Some (expectIdentifier s)
            else None
        { Expr = expr; Alias = alias }

    and private parseCreateClause (s: ParserState) : Clause =
        expect s CREATE
        let patterns = ResizeArray<Pattern>()
        patterns.Add(parsePattern s)
        while peek s = COMMA do
            advance s |> ignore
            patterns.Add(parsePattern s)
        Create(patterns |> Seq.toList)

    and private parseMergeClause (s: ParserState) : Clause =
        expect s MERGE
        let pattern = parsePattern s
        let mutable onMatch = []
        let mutable onCreate = []
        while peek s = ON do
            advance s |> ignore
            match peek s with
            | MATCH ->
                advance s |> ignore
                expect s SET
                onMatch <- parseSetItems s
            | CREATE ->
                advance s |> ignore
                expect s SET
                onCreate <- parseSetItems s
            | other -> failwithf "Expected MATCH or CREATE after ON, got %A" other
        Merge(pattern, onMatch, onCreate)

    and private parseDeleteClause (s: ParserState) (detach: bool) : Clause =
        expect s DELETE
        let aliases = ResizeArray<string>()
        aliases.Add(expectIdentifier s)
        while peek s = COMMA do
            advance s |> ignore
            aliases.Add(expectIdentifier s)
        Delete(aliases |> Seq.toList, detach)

    and private parseSetClause (s: ParserState) : Clause =
        expect s SET
        Set(parseSetItems s)

    and private parseSetItems (s: ParserState) : SetItem list =
        let items = ResizeArray<SetItem>()
        items.Add(parseSetItem s)
        while peek s = COMMA do
            advance s |> ignore
            items.Add(parseSetItem s)
        items |> Seq.toList

    and private parseSetItem (s: ParserState) : SetItem =
        let owner = expectIdentifier s
        match peek s with
        | DOT ->
            // owner.prop = expr
            advance s |> ignore
            let prop = expectIdentifier s
            expect s EQ
            let value = parseExpr s
            SetProperty(owner, prop, value)
        | EQ ->
            // owner = expr (SetAllProperties)
            advance s |> ignore
            let value = parseExpr s
            SetAllProperties(owner, value)
        | PLUS_EQ ->
            // owner += expr (MergeProperties)
            advance s |> ignore
            let value = parseExpr s
            MergeProperties(owner, value)
        | COLON ->
            // owner:Label (AddLabel)
            advance s |> ignore
            let label = expectIdentifier s
            AddLabel(owner, label)
        | other -> failwithf "Expected '.', '=', '+=', or ':' in SET item, got %A" other

    and private parseRemoveClause (s: ParserState) : Clause =
        expect s REMOVE
        let items = ResizeArray<RemoveItem>()
        let parseOne () =
            let owner = expectIdentifier s
            match peek s with
            | DOT ->
                advance s |> ignore
                let prop = expectIdentifier s
                RemoveProperty(owner, prop)
            | COLON ->
                advance s |> ignore
                let label = expectIdentifier s
                RemoveLabel(owner, label)
            | other -> failwithf "Expected '.' or ':' in REMOVE item, got %A" other
        items.Add(parseOne ())
        while peek s = COMMA do
            advance s |> ignore
            items.Add(parseOne ())
        Remove(items |> Seq.toList)

    and private parseOrderByClause (s: ParserState) : Clause =
        expect s ORDER
        expect s BY
        let items = ResizeArray<Expr * SortDirection>()
        let parseOne () =
            let expr = parseExpr s
            let dir =
                if tryConsume s DESC then Descending
                elif tryConsume s ASC then Ascending
                else Ascending
            (expr, dir)
        items.Add(parseOne ())
        while peek s = COMMA do
            advance s |> ignore
            items.Add(parseOne ())
        OrderBy(items |> Seq.toList)

    and private parseSkipClause (s: ParserState) : Clause =
        expect s SKIP
        Skip(parseExpr s)

    and private parseLimitClause (s: ParserState) : Clause =
        expect s LIMIT
        Limit(parseExpr s)

    and private parseUnwindClause (s: ParserState) : Clause =
        expect s UNWIND
        let expr = parseExpr s
        expect s AS
        let alias = expectIdentifier s
        Unwind(expr, alias)

    and private parseUnionClause (s: ParserState) : Clause =
        expect s UNION
        let all = tryConsume s ALL
        Union all

    and private parseCallClause (s: ParserState) : Clause =
        expect s CALL
        let proc = expectIdentifier s
        // Allow dotted procedure names: db.labels()
        let mutable fullName = proc
        while peek s = DOT do
            advance s |> ignore
            fullName <- fullName + "." + expectIdentifier s
        expect s LPAREN
        let args = parseExprList s RPAREN
        expect s RPAREN
        let yields =
            if tryConsume s YIELD then
                let names = ResizeArray<string>()
                names.Add(expectIdentifier s)
                while peek s = COMMA do
                    advance s |> ignore
                    names.Add(expectIdentifier s)
                names |> Seq.toList
            else []
        Call(fullName, args, yields)

    // ─── Public API ───

    /// Parse a Cypher query string into a CypherQuery AST.
    let parse (cypher: string) : CypherQuery<obj> =
        let tokens = Lexer.tokenize cypher
        let state = create tokens
        let clauses = parseClauses state
        // Collect parameters from parsed expressions
        let mutable parameters = Map.empty
        let rec collectParams (expr: Expr) =
            match expr with
            | Param name -> parameters <- parameters |> Map.add name (box null)
            | BinOp(l, _, r) -> collectParams l; collectParams r
            | UnaryOp(_, e) -> collectParams e
            | FuncCall(_, args) -> args |> List.iter collectParams
            | ListExpr items -> items |> List.iter collectParams
            | MapExpr entries -> entries |> List.iter (snd >> collectParams)
            | CaseExpr(s, whens, e) ->
                s |> Option.iter collectParams
                whens |> List.iter (fun (c, r) -> collectParams c; collectParams r)
                e |> Option.iter collectParams
            | ExistsSubquery cls -> cls |> List.iter collectClauseParams
            | _ -> ()
        and collectClauseParams (clause: Clause) =
            match clause with
            | Where expr -> collectParams expr
            | Return(items, _) | With(items, _) -> items |> List.iter (fun i -> collectParams i.Expr)
            | Set items -> items |> List.iter (fun i -> match i with SetProperty(_, _, v) -> collectParams v | SetAllProperties(_, v) | MergeProperties(_, v) -> collectParams v | _ -> ())
            | OrderBy items -> items |> List.iter (fun (e, _) -> collectParams e)
            | Skip expr | Limit expr -> collectParams expr
            | Unwind(expr, _) -> collectParams expr
            | _ -> ()
        clauses |> List.iter collectClauseParams
        { Clauses = clauses; Parameters = parameters }

    /// Parse Cypher and return clauses only
    let parseClauses' (cypher: string) : Clause list =
        let tokens = Lexer.tokenize cypher
        let state = create tokens
        parseClauses state
