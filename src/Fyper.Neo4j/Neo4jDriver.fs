namespace Fyper.Neo4j

open System
open System.Threading.Tasks
open Neo4j.Driver
open Fyper
open Fyper.GraphValue

module internal ValueMapper =

    let rec toGraphValue (value: obj) : GraphValue =
        match value with
        | null -> GNull
        | :? bool as b -> GBool b
        | :? int64 as i -> GInt i
        | :? int as i -> GInt (int64 i)
        | :? float as f -> GFloat f
        | :? float32 as f -> GFloat (float f)
        | :? string as s -> GString s
        | :? INode as node ->
            GNode {
                Id = node.ElementId |> hash |> int64
                Labels = node.Labels |> Seq.toList
                Properties = node.Properties |> Seq.map (fun kv -> kv.Key, toGraphValue kv.Value) |> Map.ofSeq
            }
        | :? IRelationship as rel ->
            GRel {
                Id = rel.ElementId |> hash |> int64
                RelType = rel.Type
                StartNodeId = rel.StartNodeElementId |> hash |> int64
                EndNodeId = rel.EndNodeElementId |> hash |> int64
                Properties = rel.Properties |> Seq.map (fun kv -> kv.Key, toGraphValue kv.Value) |> Map.ofSeq
            }
        | :? IPath as path ->
            GPath {
                Nodes =
                    path.Nodes
                    |> Seq.map (fun n ->
                        { Id = n.ElementId |> hash |> int64
                          Labels = n.Labels |> Seq.toList
                          Properties = n.Properties |> Seq.map (fun kv -> kv.Key, toGraphValue kv.Value) |> Map.ofSeq })
                    |> Seq.toList
                Relationships =
                    path.Relationships
                    |> Seq.map (fun r ->
                        { Id = r.ElementId |> hash |> int64
                          RelType = r.Type
                          StartNodeId = r.StartNodeElementId |> hash |> int64
                          EndNodeId = r.EndNodeElementId |> hash |> int64
                          Properties = r.Properties |> Seq.map (fun kv -> kv.Key, toGraphValue kv.Value) |> Map.ofSeq })
                    |> Seq.toList
            }
        | :? System.Collections.IList as list ->
            list
            |> Seq.cast<obj>
            |> Seq.map toGraphValue
            |> Seq.toList
            |> GList
        | :? System.Collections.IDictionary as dict ->
            dict
            |> Seq.cast<System.Collections.DictionaryEntry>
            |> Seq.map (fun e -> string e.Key, toGraphValue e.Value)
            |> Map.ofSeq
            |> GMap
        | v -> GString (string v)

    let recordToGraphRecord (record: IRecord) : GraphRecord =
        let keys = record.Keys |> Seq.toList
        let values =
            keys
            |> List.map (fun k -> k, toGraphValue record.[k])
            |> Map.ofList
        { Keys = keys; Values = values }

    let paramsToDict (parameters: Map<string, obj>) : System.Collections.Generic.IDictionary<string, obj> =
        parameters
        |> Map.toSeq
        |> dict


type Neo4jTransaction internal (session: IAsyncSession, tx: IAsyncTransaction) =
    interface IGraphTransaction with
        member _.ExecuteReadAsync(cypher, parameters) =
            task {
                let cursor = tx.RunAsync(cypher, ValueMapper.paramsToDict parameters)
                let! result = cursor
                let! records = result.ToListAsync()
                return records |> Seq.map ValueMapper.recordToGraphRecord |> Seq.toList
            }

        member _.ExecuteWriteAsync(cypher, parameters) =
            task {
                let! cursor = tx.RunAsync(cypher, ValueMapper.paramsToDict parameters)
                let! summary = cursor.ConsumeAsync()
                return summary.Counters.NodesCreated +
                       summary.Counters.NodesDeleted +
                       summary.Counters.RelationshipsCreated +
                       summary.Counters.RelationshipsDeleted +
                       summary.Counters.PropertiesSet
            }

        member _.CommitAsync() =
            task { do! tx.CommitAsync() }

        member _.RollbackAsync() =
            task { do! tx.RollbackAsync() }

    interface IAsyncDisposable with
        member _.DisposeAsync() =
            ValueTask(task {
                try do! session.CloseAsync() with _ -> ()
            })


type Neo4jDriver(driver: IDriver) =
    let mutable disposed = false

    let checkDisposed () =
        if disposed then raise (FyperConnectionException "Neo4j driver has been disposed")

    interface IGraphDriver with
        member _.ExecuteReadAsync(cypher, parameters) =
            task {
                checkDisposed ()
                let session = driver.AsyncSession()
                try
                    let! result =
                        session.ExecuteReadAsync(fun tx ->
                            task {
                                let! cursor = tx.RunAsync(cypher, ValueMapper.paramsToDict parameters)
                                let! records = cursor.ToListAsync()
                                return records |> Seq.map ValueMapper.recordToGraphRecord |> Seq.toList
                            })
                    return result
                finally
                    session.CloseAsync() |> ignore
            }

        member _.ExecuteWriteAsync(cypher, parameters) =
            task {
                checkDisposed ()
                let session = driver.AsyncSession()
                try
                    let! result =
                        session.ExecuteWriteAsync(fun tx ->
                            task {
                                let! cursor = tx.RunAsync(cypher, ValueMapper.paramsToDict parameters)
                                let! summary = cursor.ConsumeAsync()
                                return summary.Counters.NodesCreated +
                                       summary.Counters.NodesDeleted +
                                       summary.Counters.RelationshipsCreated +
                                       summary.Counters.RelationshipsDeleted +
                                       summary.Counters.PropertiesSet
                            })
                    return result
                finally
                    session.CloseAsync() |> ignore
            }

        member _.BeginTransactionAsync() =
            task {
                checkDisposed ()
                let session = driver.AsyncSession()
                let! tx = session.BeginTransactionAsync()
                return Neo4jTransaction(session, tx) :> IGraphTransaction
            }

        member _.Capabilities = DriverCapabilities.all

    interface IAsyncDisposable with
        member _.DisposeAsync() =
            disposed <- true
            ValueTask(task {
                do! driver.DisposeAsync()
            })
