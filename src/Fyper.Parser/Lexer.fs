namespace Fyper.Parser

open System

/// Token types for Cypher lexical analysis
type Token =
    // Keywords
    | MATCH | OPTIONAL | WHERE | RETURN | WITH | CREATE | MERGE
    | DELETE | DETACH | SET | REMOVE | ORDER | BY | ASC | DESC
    | SKIP | LIMIT | UNWIND | AS | DISTINCT | UNION | ALL
    | ON | CASE | WHEN | THEN | ELSE | END
    | AND | OR | XOR | NOT | IN | IS | NULL
    | TRUE | FALSE
    | CONTAINS | STARTS | ENDS
    | EXISTS | CALL | YIELD
    // Symbols
    | LPAREN | RPAREN | LBRACKET | RBRACKET | LBRACE | RBRACE
    | COLON | COMMA | DOT | PIPE | STAR | PLUS | MINUS
    | SLASH | PERCENT | CARET | EQ | NEQ | LT | GT | LTE | GTE
    | ARROW_RIGHT   // ->
    | ARROW_LEFT    // <-
    | DASH          // -
    | DOLLAR        // $
    | REGEX_MATCH   // =~
    | PLUS_EQ       // +=
    // Literals
    | STRING of string
    | INTEGER of int64
    | FLOAT of float
    | IDENTIFIER of string
    | PARAMETER of string   // $paramName
    // Control
    | EOF
    | NEWLINE

/// Lexer position tracking
type LexerState = {
    Input: string
    mutable Pos: int
    mutable Line: int
    mutable Col: int
}

module Lexer =

    let private keywords =
        [
            "MATCH", MATCH; "OPTIONAL", OPTIONAL; "WHERE", WHERE
            "RETURN", RETURN; "WITH", WITH; "CREATE", CREATE
            "MERGE", MERGE; "DELETE", DELETE; "DETACH", DETACH
            "SET", SET; "REMOVE", REMOVE; "ORDER", ORDER
            "BY", BY; "ASC", ASC; "DESC", DESC
            "SKIP", SKIP; "LIMIT", LIMIT; "UNWIND", UNWIND
            "AS", AS; "DISTINCT", DISTINCT; "UNION", UNION; "ALL", ALL
            "ON", ON; "CASE", CASE; "WHEN", WHEN; "THEN", THEN
            "ELSE", ELSE; "END", END
            "AND", AND; "OR", OR; "XOR", XOR; "NOT", NOT
            "IN", IN; "IS", IS; "NULL", NULL
            "TRUE", TRUE; "FALSE", FALSE
            "CONTAINS", CONTAINS; "STARTS", STARTS; "ENDS", ENDS
            "EXISTS", EXISTS; "CALL", CALL; "YIELD", YIELD
        ] |> Map.ofList

    let create (input: string) : LexerState =
        { Input = input; Pos = 0; Line = 1; Col = 1 }

    let private peek (state: LexerState) : char option =
        if state.Pos < state.Input.Length then Some state.Input.[state.Pos]
        else None

    let private peekAt (state: LexerState) (offset: int) : char option =
        let i = state.Pos + offset
        if i < state.Input.Length then Some state.Input.[i]
        else None

    let private advance (state: LexerState) : unit =
        if state.Pos < state.Input.Length then
            if state.Input.[state.Pos] = '\n' then
                state.Line <- state.Line + 1
                state.Col <- 1
            else
                state.Col <- state.Col + 1
            state.Pos <- state.Pos + 1

    let private skipWhitespace (state: LexerState) : unit =
        while state.Pos < state.Input.Length &&
              (state.Input.[state.Pos] = ' ' || state.Input.[state.Pos] = '\t' ||
               state.Input.[state.Pos] = '\r') do
            advance state

    let private skipLineComment (state: LexerState) : unit =
        // // comment
        if state.Pos + 1 < state.Input.Length &&
           state.Input.[state.Pos] = '/' && state.Input.[state.Pos + 1] = '/' then
            while state.Pos < state.Input.Length && state.Input.[state.Pos] <> '\n' do
                advance state

    let private readString (state: LexerState) (quote: char) : string =
        advance state // skip opening quote
        let sb = System.Text.StringBuilder()
        while state.Pos < state.Input.Length && state.Input.[state.Pos] <> quote do
            if state.Input.[state.Pos] = '\\' then
                advance state
                if state.Pos < state.Input.Length then
                    match state.Input.[state.Pos] with
                    | 'n' -> sb.Append('\n') |> ignore
                    | 't' -> sb.Append('\t') |> ignore
                    | '\\' -> sb.Append('\\') |> ignore
                    | c when c = quote -> sb.Append(c) |> ignore
                    | c -> sb.Append('\\').Append(c) |> ignore
                    advance state
            else
                sb.Append(state.Input.[state.Pos]) |> ignore
                advance state
        if state.Pos < state.Input.Length then advance state // skip closing quote
        sb.ToString()

    let private readNumber (state: LexerState) : Token =
        let start = state.Pos
        let mutable isFloat = false
        while state.Pos < state.Input.Length && Char.IsDigit state.Input.[state.Pos] do
            advance state
        // Check for decimal point — but NOT for range operator (..)
        if state.Pos < state.Input.Length && state.Input.[state.Pos] = '.' then
            if state.Pos + 1 < state.Input.Length && state.Input.[state.Pos + 1] = '.' then
                () // Range operator like 1..5, don't consume
            elif state.Pos + 1 < state.Input.Length && Char.IsDigit state.Input.[state.Pos + 1] then
                isFloat <- true
                advance state // consume '.'
                while state.Pos < state.Input.Length && Char.IsDigit state.Input.[state.Pos] do
                    advance state
            else
                () // Trailing dot, don't consume
        let text = state.Input.[start .. state.Pos - 1]
        if isFloat then FLOAT (Double.Parse(text, Globalization.CultureInfo.InvariantCulture))
        else INTEGER (Int64.Parse text)

    let private readIdentifierOrKeyword (state: LexerState) : Token =
        let start = state.Pos
        while state.Pos < state.Input.Length &&
              (Char.IsLetterOrDigit state.Input.[state.Pos] || state.Input.[state.Pos] = '_') do
            advance state
        let text = state.Input.[start .. state.Pos - 1]
        let upper = text.ToUpperInvariant()
        match Map.tryFind upper keywords with
        | Some kw -> kw
        | None -> IDENTIFIER text

    let private readBacktickIdentifier (state: LexerState) : Token =
        advance state // skip opening backtick
        let start = state.Pos
        while state.Pos < state.Input.Length && state.Input.[state.Pos] <> '`' do
            advance state
        let text = state.Input.[start .. state.Pos - 1]
        if state.Pos < state.Input.Length then advance state // skip closing backtick
        IDENTIFIER text

    let nextToken (state: LexerState) : Token =
        skipWhitespace state
        if state.Pos + 1 < state.Input.Length &&
           state.Input.[state.Pos] = '/' && state.Input.[state.Pos + 1] = '/' then
            skipLineComment state
            skipWhitespace state

        match peek state with
        | None -> EOF
        | Some '\n' -> advance state; NEWLINE
        | Some '(' -> advance state; LPAREN
        | Some ')' -> advance state; RPAREN
        | Some '[' -> advance state; LBRACKET
        | Some ']' -> advance state; RBRACKET
        | Some '{' -> advance state; LBRACE
        | Some '}' -> advance state; RBRACE
        | Some ':' -> advance state; COLON
        | Some ',' -> advance state; COMMA
        | Some '.' -> advance state; DOT
        | Some '|' -> advance state; PIPE
        | Some '*' -> advance state; STAR
        | Some '/' -> advance state; SLASH
        | Some '%' -> advance state; PERCENT
        | Some '^' -> advance state; CARET
        | Some '\'' -> STRING (readString state '\'')
        | Some '"' -> STRING (readString state '"')
        | Some '`' -> readBacktickIdentifier state
        | Some '$' ->
            advance state
            let start = state.Pos
            while state.Pos < state.Input.Length &&
                  (Char.IsLetterOrDigit state.Input.[state.Pos] || state.Input.[state.Pos] = '_') do
                advance state
            PARAMETER (state.Input.[start .. state.Pos - 1])
        | Some '+' ->
            advance state
            match peek state with
            | Some '=' -> advance state; PLUS_EQ
            | _ -> PLUS
        | Some '-' ->
            advance state
            match peek state with
            | Some '>' -> advance state; ARROW_RIGHT
            | _ -> DASH
        | Some '<' ->
            advance state
            match peek state with
            | Some '=' -> advance state; LTE
            | Some '-' -> advance state; ARROW_LEFT
            | Some '>' -> advance state; NEQ
            | _ -> LT
        | Some '>' ->
            advance state
            match peek state with
            | Some '=' -> advance state; GTE
            | _ -> GT
        | Some '=' ->
            advance state
            match peek state with
            | Some '~' -> advance state; REGEX_MATCH
            | _ -> EQ
        | Some '!' ->
            advance state
            match peek state with
            | Some '=' -> advance state; NEQ
            | _ -> NOT
        | Some c when Char.IsDigit c -> readNumber state
        | Some c when Char.IsLetter c || c = '_' -> readIdentifierOrKeyword state
        | Some c ->
            advance state
            IDENTIFIER (string c)

    /// <summary>Tokenize an entire Cypher string into a list of tokens.</summary>
    /// <param name="input">Cypher query string.</param>
    /// <returns>List of tokens (keywords, identifiers, literals, operators). Newlines are stripped.</returns>
    let tokenize (input: string) : Token list =
        let state = create input
        let tokens = ResizeArray<Token>()
        let mutable cont = true
        while cont do
            let tok = nextToken state
            match tok with
            | NEWLINE -> () // skip newlines in token list
            | EOF -> cont <- false
            | _ -> tokens.Add(tok)
        tokens |> Seq.toList
