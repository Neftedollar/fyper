namespace Fyper.Age

open System
open System.Text.Json
open System.Threading.Tasks
open Npgsql
open Fyper
open Fyper.GraphValue

module internal AgtypeParser =

    /// Parse AGE's agtype JSON-like text into GraphValue.
    /// Agtype returns vertices as: {id: N, label: "L", properties: {...}}::vertex
    /// Edges as: {id: N, label: "L", start_id: N, end_id: N, properties: {...}}::edge
    let rec parseAgtype (raw: string) : GraphValue =
        if String.IsNullOrWhiteSpace raw || raw = "null" then GNull
        else
            let trimmed = raw.Trim()
            // Strip ::vertex or ::edge suffix
            let value, suffix =
                if trimmed.EndsWith("::vertex") then
                    trimmed.[..trimmed.Length - 9], "vertex"
                elif trimmed.EndsWith("::edge") then
                    trimmed.[..trimmed.Length - 7], "edge"
                elif trimmed.EndsWith("::path") then
                    trimmed.[..trimmed.Length - 7], "path"
                else
                    trimmed, ""

            match suffix with
            | "vertex" -> parseVertex value
            | "edge" -> parseEdge value
            | _ -> parseScalar value

    and parseVertex (json: string) : GraphValue =
        try
            let doc = JsonDocument.Parse(json)
            let root = doc.RootElement
            let id =
                if root.TryGetProperty("id") |> fst then
                    root.GetProperty("id").GetInt64()
                else 0L
            let label =
                if root.TryGetProperty("label") |> fst then
                    root.GetProperty("label").GetString()
                else ""
            let props =
                if root.TryGetProperty("properties") |> fst then
                    root.GetProperty("properties").EnumerateObject()
                    |> Seq.map (fun p -> p.Name, jsonElementToGraphValue p.Value)
                    |> Map.ofSeq
                else Map.empty
            GNode { Id = id; Labels = [label]; Properties = props }
        with _ -> GString json

    and parseEdge (json: string) : GraphValue =
        try
            let doc = JsonDocument.Parse(json)
            let root = doc.RootElement
            let id = if root.TryGetProperty("id") |> fst then root.GetProperty("id").GetInt64() else 0L
            let label = if root.TryGetProperty("label") |> fst then root.GetProperty("label").GetString() else ""
            let startId = if root.TryGetProperty("start_id") |> fst then root.GetProperty("start_id").GetInt64() else 0L
            let endId = if root.TryGetProperty("end_id") |> fst then root.GetProperty("end_id").GetInt64() else 0L
            let props =
                if root.TryGetProperty("properties") |> fst then
                    root.GetProperty("properties").EnumerateObject()
                    |> Seq.map (fun p -> p.Name, jsonElementToGraphValue p.Value)
                    |> Map.ofSeq
                else Map.empty
            GRel { Id = id; RelType = label; StartNodeId = startId; EndNodeId = endId; Properties = props }
        with _ -> GString json

    and parseScalar (value: string) : GraphValue =
        if value = "true" then GBool true
        elif value = "false" then GBool false
        elif value = "null" then GNull
        elif value.StartsWith("\"") && value.EndsWith("\"") then
            GString (value.[1..value.Length - 2])
        else
            match Int64.TryParse(value) with
            | true, i -> GInt i
            | _ ->
                match Double.TryParse(value, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
                | true, f -> GFloat f
                | _ -> GString value

    and jsonElementToGraphValue (elem: JsonElement) : GraphValue =
        match elem.ValueKind with
        | JsonValueKind.Null | JsonValueKind.Undefined -> GNull
        | JsonValueKind.True -> GBool true
        | JsonValueKind.False -> GBool false
        | JsonValueKind.Number ->
            if elem.TryGetInt64() |> fst then GInt (elem.GetInt64())
            else GFloat (elem.GetDouble())
        | JsonValueKind.String -> GString (elem.GetString())
        | JsonValueKind.Array ->
            elem.EnumerateArray()
            |> Seq.map jsonElementToGraphValue
            |> Seq.toList
            |> GList
        | JsonValueKind.Object ->
            elem.EnumerateObject()
            |> Seq.map (fun p -> p.Name, jsonElementToGraphValue p.Value)
            |> Map.ofSeq
            |> GMap
        | _ -> GNull

module internal CypherWrapper =

    /// Wrap a Cypher query in AGE's SQL function call.
    /// Returns the SQL string and remapped parameters.
    let wrapCypher (graphName: string) (cypher: string) (returnAliases: string list) (parameters: Map<string, obj>) : string * obj[] =
        // Map parameters to positional $N references
        let paramList = parameters |> Map.toList
        let mutable remappedCypher = cypher
        let paramValues = ResizeArray<obj>()

        for (name, value) in paramList do
            let positional = sprintf "$%d" (paramValues.Count + 1)
            remappedCypher <- remappedCypher.Replace(sprintf "$%s" name, positional)
            paramValues.Add(value)

        let aliases =
            if List.isEmpty returnAliases then "result agtype"
            else returnAliases |> List.map (fun a -> sprintf "%s agtype" a) |> String.concat ", "

        let sql =
            if paramValues.Count = 0 then
                sprintf "SELECT * FROM cypher('%s', $$ %s $$) AS (%s)" graphName remappedCypher aliases
            else
                // AGE parameter passing via function arguments
                let paramPlaceholders =
                    paramValues |> Seq.mapi (fun i _ -> sprintf "$%d" (i + 1)) |> String.concat ", "
                sprintf "SELECT * FROM cypher('%s', $$ %s $$, '%s') AS (%s)" graphName remappedCypher paramPlaceholders aliases

        sql, paramValues.ToArray()

    /// Extract return aliases from a Cypher string.
    /// Simple heuristic: find RETURN clause and extract column names.
    let extractReturnAliases (cypher: string) : string list =
        let idx = cypher.IndexOf("RETURN", StringComparison.OrdinalIgnoreCase)
        if idx < 0 then ["result"]
        else
            let afterReturn = cypher.[idx + 6..].Trim()
            // Remove DISTINCT keyword if present
            let cleaned =
                if afterReturn.StartsWith("DISTINCT", StringComparison.OrdinalIgnoreCase) then
                    afterReturn.[8..].Trim()
                else afterReturn
            // Split by comma and extract alias or generate one
            cleaned.Split(',')
            |> Array.mapi (fun i part ->
                let trimmed = part.Trim()
                // Check for AS alias
                let asIdx = trimmed.IndexOf(" AS ", StringComparison.OrdinalIgnoreCase)
                if asIdx >= 0 then trimmed.[asIdx + 4..].Trim()
                // Use the expression as-is for simple vars
                elif trimmed.Contains(".") then sprintf "col%d" i
                else trimmed)
            |> Array.toList


type AgeTransaction internal (conn: NpgsqlConnection, tx: NpgsqlTransaction, graphName: string) =

    let executeQuery (cypher: string) (parameters: Map<string, obj>) =
        task {
            let aliases = CypherWrapper.extractReturnAliases cypher
            let sql, paramValues = CypherWrapper.wrapCypher graphName cypher aliases parameters

            use cmd = new NpgsqlCommand(sql, conn, tx)
            for i in 0 .. paramValues.Length - 1 do
                cmd.Parameters.AddWithValue(sprintf "p%d" i, paramValues.[i]) |> ignore

            use! reader = cmd.ExecuteReaderAsync()
            let records = ResizeArray<GraphRecord>()
            while reader.Read() do
                let values =
                    aliases
                    |> List.mapi (fun i alias ->
                        let raw = reader.GetValue(i)
                        alias, AgtypeParser.parseAgtype (string raw))
                    |> Map.ofList
                records.Add({ Keys = aliases; Values = values })
            return records |> Seq.toList
        }

    interface IGraphTransaction with
        member _.ExecuteReadAsync(cypher, parameters) = executeQuery cypher parameters
        member _.ExecuteWriteAsync(cypher, parameters) =
            task {
                let! records = executeQuery cypher parameters
                return records.Length
            }
        member _.CommitAsync() = task { do! tx.CommitAsync() }
        member _.RollbackAsync() = task { do! tx.RollbackAsync() }

    interface IAsyncDisposable with
        member _.DisposeAsync() =
            ValueTask(task {
                try do! tx.DisposeAsync() with _ -> ()
                try do! conn.DisposeAsync() with _ -> ()
            })


type AgeDriver(dataSource: NpgsqlDataSource, graphName: string) =
    let mutable disposed = false

    let initConnection (conn: NpgsqlConnection) =
        task {
            use initCmd = new NpgsqlCommand("LOAD 'age'", conn)
            do! initCmd.ExecuteNonQueryAsync() :> Task
            use pathCmd = new NpgsqlCommand("SET search_path = ag_catalog, \"$user\", public", conn)
            do! pathCmd.ExecuteNonQueryAsync() :> Task
        }

    let checkDisposed () =
        if disposed then raise (FyperConnectionException "AGE driver has been disposed")

    interface IGraphDriver with
        member _.ExecuteReadAsync(cypher, parameters) =
            task {
                checkDisposed ()
                let! conn = dataSource.OpenConnectionAsync()
                try
                    do! initConnection conn
                    let aliases = CypherWrapper.extractReturnAliases cypher
                    let sql, _paramValues = CypherWrapper.wrapCypher graphName cypher aliases parameters

                    use cmd = new NpgsqlCommand(sql, conn)
                    use! reader = cmd.ExecuteReaderAsync()
                    let records = ResizeArray<GraphRecord>()
                    while reader.Read() do
                        let values =
                            aliases
                            |> List.mapi (fun i alias ->
                                let raw = reader.GetValue(i)
                                alias, AgtypeParser.parseAgtype (string raw))
                            |> Map.ofList
                        records.Add({ Keys = aliases; Values = values })
                    return records |> Seq.toList
                finally
                    conn.Dispose()
            }

        member _.ExecuteWriteAsync(cypher, parameters) =
            task {
                checkDisposed ()
                let! conn = dataSource.OpenConnectionAsync()
                try
                    do! initConnection conn
                    let aliases = CypherWrapper.extractReturnAliases cypher
                    let sql, _paramValues = CypherWrapper.wrapCypher graphName cypher aliases parameters

                    use cmd = new NpgsqlCommand(sql, conn)
                    let! affected = cmd.ExecuteNonQueryAsync()
                    return affected
                finally
                    conn.Dispose()
            }

        member _.BeginTransactionAsync() =
            task {
                checkDisposed ()
                let! conn = dataSource.OpenConnectionAsync()
                do! initConnection conn
                let! tx = conn.BeginTransactionAsync()
                return AgeTransaction(conn, tx, graphName) :> IGraphTransaction
            }

        member _.Capabilities = {
            SupportsOptionalMatch = false
            SupportsMerge = false
            SupportsUnwind = false
            SupportsCase = false
            SupportsCallProcedure = false
            SupportsExistsSubquery = false
            SupportsNamedPaths = false
        }

    interface IAsyncDisposable with
        member _.DisposeAsync() =
            disposed <- true
            ValueTask.CompletedTask
