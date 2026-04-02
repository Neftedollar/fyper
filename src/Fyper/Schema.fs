namespace Fyper

open System
open System.Reflection
open System.Collections.Concurrent
open Microsoft.FSharp.Reflection

/// Attribute to override the default node label or relationship type name.
[<AllowNullLiteral>]
[<AttributeUsage(AttributeTargets.Class ||| AttributeTargets.Struct)>]
type LabelAttribute(name: string) =
    inherit Attribute()
    member _.Name = name

/// Attribute to override the default Cypher property name for a record field.
[<AllowNullLiteral>]
[<AttributeUsage(AttributeTargets.Property)>]
type CypherNameAttribute(name: string) =
    inherit Attribute()
    member _.Name = name

module Schema =

    /// PascalCase → camelCase: "FirstName" → "firstName", "Age" → "age"
    let toCypherName (name: string) : string =
        if String.IsNullOrEmpty name then name
        else string (Char.ToLowerInvariant name.[0]) + name.[1..]

    type PropertyMeta = {
        FSharpName: string
        CypherName: string
        PropertyType: Type
        IsOption: bool
    }

    type TypeMeta = {
        ClrType: Type
        Label: string
        Properties: PropertyMeta list
    }

    let private cache = ConcurrentDictionary<Type, TypeMeta>()

    let resolveLabel (t: Type) : string =
        match t.GetCustomAttribute<LabelAttribute>() with
        | null -> t.Name
        | attr -> attr.Name

    let resolvePropertyName (pi: PropertyInfo) : string =
        match pi.GetCustomAttribute<CypherNameAttribute>() with
        | null -> toCypherName pi.Name
        | attr -> attr.Name

    let isOptionType (t: Type) : bool =
        t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>>

    let getMeta (t: Type) : TypeMeta =
        cache.GetOrAdd(t, fun t ->
            let props =
                if FSharpType.IsRecord t then
                    FSharpType.GetRecordFields(t)
                    |> Array.map (fun pi -> {
                        FSharpName = pi.Name
                        CypherName = resolvePropertyName pi
                        PropertyType = pi.PropertyType
                        IsOption = isOptionType pi.PropertyType
                    })
                    |> Array.toList
                else
                    []
            {
                ClrType = t
                Label = resolveLabel t
                Properties = props
            }
        )

    /// PascalCase → UPPER_SNAKE_CASE: "ActedIn" → "ACTED_IN", "PartOf" → "PART_OF"
    let toRelType (name: string) : string =
        if String.IsNullOrEmpty name then name
        else
            let sb = System.Text.StringBuilder()
            for i in 0 .. name.Length - 1 do
                let c = name.[i]
                if i > 0 && Char.IsUpper c && (i + 1 < name.Length && Char.IsLower name.[i + 1] || Char.IsLower name.[i - 1]) then
                    sb.Append('_') |> ignore
                sb.Append(Char.ToUpperInvariant c) |> ignore
            sb.ToString()

    let getMetaOf<'T> () : TypeMeta = getMeta typeof<'T>
