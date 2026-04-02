namespace Fyper

open System
open System.Collections.Concurrent
open Microsoft.FSharp.Reflection
open Fyper.GraphValue

/// Maps GraphRecord results to typed F# values.
module ResultMapper =

    /// Convert a GraphValue to a CLR value of the expected type
    let rec convertValue (targetType: Type) (value: GraphValue) : obj =
        match value with
        | GNull -> null
        | GBool b -> box b
        | GInt i ->
            if targetType = typeof<int> then box (int i)
            elif targetType = typeof<int64> then box i
            elif targetType = typeof<float> then box (float i)
            else box i
        | GFloat f ->
            if targetType = typeof<float32> then box (float32 f)
            else box f
        | GString s -> box s
        | GList items ->
            if targetType.IsGenericType then
                let elemType = targetType.GetGenericArguments().[0]
                let converted = items |> List.map (convertValue elemType)
                let listModule = typeof<list<int>>.Assembly.GetType("Microsoft.FSharp.Collections.ListModule")
                let ofSeqMi = listModule.GetMethod("OfSeq").MakeGenericMethod(elemType)
                ofSeqMi.Invoke(null, [| converted :> obj |])
            else box items
        | GMap m ->
            if FSharpType.IsRecord targetType then
                mapRecord targetType (GMap m)
            else box m
        | GNode node ->
            if FSharpType.IsRecord targetType then
                mapRecord targetType (GNode node)
            else box node
        | GRel rel ->
            if FSharpType.IsRecord targetType then
                mapRecord targetType (GRel rel)
            else box rel
        | GPath path -> box path

    /// Map a GraphValue (GMap or GNode) to an F# record type
    and mapRecord (recordType: Type) (value: GraphValue) : obj =
        let props =
            match value with
            | GMap m -> m
            | GNode n -> n.Properties
            | GRel r -> r.Properties
            | _ -> Map.empty

        let fields = FSharpType.GetRecordFields(recordType)
        let values =
            fields
            |> Array.map (fun fi ->
                let cypherName = Schema.toCypherName fi.Name
                match Map.tryFind cypherName props with
                | Some gv -> convertValue fi.PropertyType gv
                | None ->
                    if Schema.isOptionType fi.PropertyType then box None
                    else failwithf "Missing required property '%s' for type '%s'" cypherName recordType.Name
            )

        FSharpValue.MakeRecord(recordType, values)

    /// Map a full GraphRecord to a typed result
    let mapGraphRecord<'T> (record: GraphRecord) : 'T =
        let targetType = typeof<'T>

        if FSharpType.IsRecord targetType then
            let value =
                if record.Values.Count = 1 then
                    record.Values |> Map.toList |> List.head |> snd
                else
                    GMap(record.Values)
            convertValue targetType value :?> 'T

        elif FSharpType.IsTuple targetType then
            let elemTypes = FSharpType.GetTupleElements(targetType)
            let values =
                record.Keys
                |> List.mapi (fun i key ->
                    let gv = record.Values.[key]
                    convertValue elemTypes.[i] gv
                )
                |> Array.ofList
            FSharpValue.MakeTuple(values, targetType) :?> 'T

        else
            let value = record.Values |> Map.toList |> List.head |> snd
            convertValue targetType value :?> 'T
