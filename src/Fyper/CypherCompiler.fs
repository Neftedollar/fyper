namespace Fyper

open Fyper.Ast

/// <summary>Compiles Cypher AST (discriminated unions) to parameterized Cypher query strings.</summary>
module CypherCompiler =

    /// <summary>Result of compiling a CypherQuery — the Cypher string and collected parameters.</summary>
    type CompileResult = {
        /// The compiled Cypher query string with $param references.
        Cypher: string
        /// Parameter values keyed by name (p0, p1, etc.).
        Parameters: Map<string, obj>
    }

    // ─── Expression → string ───

    let rec compileExpr (expr: Expr) : string =
        match expr with
        | Literal v -> literalToString v
        | Param name -> sprintf "$%s" name
        | Variable name -> name
        | Null -> "null"
        | Property(owner, name) -> sprintf "%s.%s" owner name
        | BinOp(lhs, op, rhs) ->
            sprintf "(%s %s %s)" (compileExpr lhs) (compileBinOp op) (compileExpr rhs)
        | UnaryOp(op, inner) ->
            match op with
            | Not -> sprintf "NOT (%s)" (compileExpr inner)
            | IsNull -> sprintf "%s IS NULL" (compileExpr inner)
            | IsNotNull -> sprintf "%s IS NOT NULL" (compileExpr inner)
            | Exists -> sprintf "exists(%s)" (compileExpr inner)
        | FuncCall(name, args) ->
            let argsStr = args |> List.map compileExpr |> String.concat ", "
            sprintf "%s(%s)" name argsStr
        | ListExpr items ->
            let itemsStr = items |> List.map compileExpr |> String.concat ", "
            sprintf "[%s]" itemsStr
        | MapExpr entries ->
            let entriesStr =
                entries
                |> List.map (fun (k, v) -> sprintf "%s: %s" k (compileExpr v))
                |> String.concat ", "
            sprintf "{%s}" entriesStr
        | CaseExpr(scrutinee, whens, elseClause) ->
            let sb = System.Text.StringBuilder()
            sb.Append("CASE") |> ignore
            scrutinee |> Option.iter (fun s -> sb.Append(sprintf " %s" (compileExpr s)) |> ignore)
            for (condition, result) in whens do
                sb.Append(sprintf " WHEN %s THEN %s" (compileExpr condition) (compileExpr result)) |> ignore
            elseClause |> Option.iter (fun e -> sb.Append(sprintf " ELSE %s" (compileExpr e)) |> ignore)
            sb.Append(" END") |> ignore
            sb.ToString()
        | ExistsSubquery clauses ->
            let inner = clauses |> List.map compileClause |> String.concat " "
            sprintf "EXISTS { %s }" inner

    and compileBinOp (op: BinOp) : string =
        match op with
        | Eq -> "="
        | Neq -> "<>"
        | Gt -> ">"
        | Gte -> ">="
        | Lt -> "<"
        | Lte -> "<="
        | And -> "AND"
        | Or -> "OR"
        | Xor -> "XOR"
        | Contains -> "CONTAINS"
        | StartsWith -> "STARTS WITH"
        | EndsWith -> "ENDS WITH"
        | In -> "IN"
        | Add -> "+"
        | Sub -> "-"
        | Mul -> "*"
        | Div -> "/"
        | Mod -> "%"
        | RegexMatch -> "=~"

    and literalToString (v: obj) : string =
        match v with
        | :? string as s -> sprintf "'%s'" (s.Replace("'", "\\'"))
        | :? bool as b -> if b then "true" else "false"
        | :? int as i -> string i
        | :? int64 as i -> string i
        | :? float as f -> string f
        | :? float32 as f -> string f
        | null -> "null"
        | v -> string v

    // ─── Pattern → string ───

    and compilePattern (pattern: Pattern) : string =
        match pattern with
        | NodePattern(alias, label, props) ->
            let labelStr = label |> Option.map (sprintf ":%s") |> Option.defaultValue ""
            let propsStr = compileInlineProps props
            sprintf "(%s%s%s)" alias labelStr propsStr

        | RelPattern(from, relAlias, relType, relProps, direction, pathLength, to') ->
            let relContent =
                let alias = relAlias |> Option.defaultValue ""
                let typ = relType |> Option.map (sprintf ":%s") |> Option.defaultValue ""
                let len = pathLength |> Option.map compilePathLength |> Option.defaultValue ""
                let props = compileInlineProps relProps
                sprintf "%s%s%s%s" alias typ len props
            let fromStr = compilePattern from
            let toStr = compilePattern to'
            match direction with
            | Outgoing   -> sprintf "%s-[%s]->%s" fromStr relContent toStr
            | Incoming   -> sprintf "%s<-[%s]-%s" fromStr relContent toStr
            | Undirected -> sprintf "%s-[%s]-%s"  fromStr relContent toStr

        | NamedPath(name, inner) ->
            sprintf "%s = %s" name (compilePattern inner)

    and compileInlineProps (props: Map<string, Expr>) : string =
        if Map.isEmpty props then ""
        else
            let entries =
                props
                |> Map.toList
                |> List.map (fun (k, v) -> sprintf "%s: %s" k (compileExpr v))
                |> String.concat ", "
            sprintf " {%s}" entries

    and compilePathLength (pl: PathLength) : string =
        match pl with
        | Exactly n -> sprintf "*%d" n
        | Between(min, max) -> sprintf "*%d..%d" min max
        | AtLeast n -> sprintf "*%d.." n
        | AtMost n -> sprintf "*..%d" n
        | AnyLength -> "*"

    // ─── Clause → string ───

    and compileClause (clause: Clause) : string =
        match clause with
        | Match(patterns, optional) ->
            let keyword = if optional then "OPTIONAL MATCH" else "MATCH"
            let patternsStr = patterns |> List.map compilePattern |> String.concat ", "
            sprintf "%s %s" keyword patternsStr

        | Where expr ->
            sprintf "WHERE %s" (compileExpr expr)

        | Return(items, distinct) ->
            let keyword = if distinct then "RETURN DISTINCT" else "RETURN"
            let itemsStr = items |> List.map compileReturnItem |> String.concat ", "
            sprintf "%s %s" keyword itemsStr

        | With(items, distinct) ->
            let keyword = if distinct then "WITH DISTINCT" else "WITH"
            let itemsStr = items |> List.map compileReturnItem |> String.concat ", "
            sprintf "%s %s" keyword itemsStr

        | Create patterns ->
            let patternsStr = patterns |> List.map compilePattern |> String.concat ", "
            sprintf "CREATE %s" patternsStr

        | Merge(pattern, onMatch, onCreate) ->
            let sb = System.Text.StringBuilder()
            sb.Append(sprintf "MERGE %s" (compilePattern pattern)) |> ignore
            if not (List.isEmpty onMatch) then
                sb.Append(sprintf " ON MATCH SET %s" (compileSetItems onMatch)) |> ignore
            if not (List.isEmpty onCreate) then
                sb.Append(sprintf " ON CREATE SET %s" (compileSetItems onCreate)) |> ignore
            sb.ToString()

        | Delete(aliases, detach) ->
            let keyword = if detach then "DETACH DELETE" else "DELETE"
            sprintf "%s %s" keyword (String.concat ", " aliases)

        | Set items ->
            sprintf "SET %s" (compileSetItems items)

        | Remove items ->
            sprintf "REMOVE %s" (items |> List.map compileRemoveItem |> String.concat ", ")

        | OrderBy items ->
            let itemsStr =
                items
                |> List.map (fun (expr, dir) ->
                    let dirStr = match dir with Ascending -> "" | Descending -> " DESC"
                    sprintf "%s%s" (compileExpr expr) dirStr)
                |> String.concat ", "
            sprintf "ORDER BY %s" itemsStr

        | Skip expr -> sprintf "SKIP %s" (compileExpr expr)
        | Limit expr -> sprintf "LIMIT %s" (compileExpr expr)

        | Unwind(expr, alias) ->
            sprintf "UNWIND %s AS %s" (compileExpr expr) alias

        | Call(proc, args, yields) ->
            let argsStr = args |> List.map compileExpr |> String.concat ", "
            let yieldStr =
                if List.isEmpty yields then ""
                else sprintf " YIELD %s" (String.concat ", " yields)
            sprintf "CALL %s(%s)%s" proc argsStr yieldStr

        | Union all ->
            if all then "UNION ALL" else "UNION"

        | RawCypher s -> s

    and compileReturnItem (item: ReturnItem) : string =
        match item.Alias with
        | Some alias -> sprintf "%s AS %s" (compileExpr item.Expr) alias
        | None -> compileExpr item.Expr

    and compileSetItems (items: SetItem list) : string =
        items |> List.map compileSetItem |> String.concat ", "

    and compileSetItem (item: SetItem) : string =
        match item with
        | SetProperty(owner, prop, value) ->
            sprintf "%s.%s = %s" owner prop (compileExpr value)
        | SetAllProperties(owner, value) ->
            sprintf "%s = %s" owner (compileExpr value)
        | MergeProperties(owner, value) ->
            sprintf "%s += %s" owner (compileExpr value)
        | AddLabel(owner, label) ->
            sprintf "%s:%s" owner label

    and compileRemoveItem (item: RemoveItem) : string =
        match item with
        | RemoveProperty(owner, prop) -> sprintf "%s.%s" owner prop
        | RemoveLabel(owner, label) -> sprintf "%s:%s" owner label

    // ─── Full query compilation ───

    // ─── Capability validation ───

    let validateCapabilities (backend: string) (caps: DriverCapabilities) (clauses: Clause list) : unit =
        for clause in clauses do
            match clause with
            | Match(_, true) when not caps.SupportsOptionalMatch ->
                raise (FyperUnsupportedFeatureException("OPTIONAL MATCH", backend))
            | Merge _ when not caps.SupportsMerge ->
                raise (FyperUnsupportedFeatureException("MERGE", backend))
            | Unwind _ when not caps.SupportsUnwind ->
                raise (FyperUnsupportedFeatureException("UNWIND", backend))
            | Call _ when not caps.SupportsCallProcedure ->
                raise (FyperUnsupportedFeatureException("CALL procedure", backend))
            | _ -> ()
        // Check expressions for CASE and EXISTS subquery
        let rec checkExpr (expr: Expr) =
            match expr with
            | CaseExpr _ when not caps.SupportsCase ->
                raise (FyperUnsupportedFeatureException("CASE expression", backend))
            | ExistsSubquery _ when not caps.SupportsExistsSubquery ->
                raise (FyperUnsupportedFeatureException("EXISTS subquery", backend))
            | BinOp(l, _, r) -> checkExpr l; checkExpr r
            | UnaryOp(_, inner) -> checkExpr inner
            | FuncCall(_, args) -> args |> List.iter checkExpr
            | ListExpr items -> items |> List.iter checkExpr
            | MapExpr entries -> entries |> List.iter (snd >> checkExpr)
            | CaseExpr(s, whens, e) ->
                s |> Option.iter checkExpr
                whens |> List.iter (fun (c, r) -> checkExpr c; checkExpr r)
                e |> Option.iter checkExpr
            | ExistsSubquery cls -> cls |> List.iter (fun c -> checkClauseExprs c)
            | _ -> ()
        and checkClauseExprs (clause: Clause) =
            match clause with
            | Where expr -> checkExpr expr
            | Return(items, _) | With(items, _) -> items |> List.iter (fun i -> checkExpr i.Expr)
            | OrderBy items -> items |> List.iter (fun (e, _) -> checkExpr e)
            | Set items -> items |> List.iter (fun i -> match i with SetProperty(_, _, v) -> checkExpr v | SetAllProperties(_, v) | MergeProperties(_, v) -> checkExpr v | _ -> ())
            | _ -> ()
        // Check patterns for named paths
        let rec checkPattern (p: Pattern) =
            match p with
            | NamedPath _ when not caps.SupportsNamedPaths ->
                raise (FyperUnsupportedFeatureException("Named paths", backend))
            | NamedPath(_, inner) -> checkPattern inner
            | RelPattern(from, _, _, _, _, _, to') -> checkPattern from; checkPattern to'
            | _ -> ()
        for clause in clauses do
            checkClauseExprs clause
            match clause with
            | Match(patterns, _) | Create patterns -> patterns |> List.iter checkPattern
            | Merge(pattern, _, _) -> checkPattern pattern
            | _ -> ()

    // ─── Full query compilation ───

    let compile (query: CypherQuery<'T>) : CompileResult =
        let cypher =
            query.Clauses
            |> List.map compileClause
            |> String.concat "\n"
        { Cypher = cypher; Parameters = query.Parameters }
