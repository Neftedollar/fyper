module Fyper.Tests.SchemaTests

open Expecto
open Fyper
open Fyper.Schema

// Test types
type Person = { Name: string; Age: int }
type Movie = { Title: string; Released: int }

[<Label "PERSON">]
type CustomPerson = { FullName: string }

type WithOption = { Name: string; Nickname: string option }

[<Tests>]
let schemaTests = testList "Schema" [

    testList "toCypherName" [
        test "converts PascalCase to camelCase" {
            Expect.equal (toCypherName "FirstName") "firstName" ""
        }
        test "converts single-char" {
            Expect.equal (toCypherName "X") "x" ""
        }
        test "lowercases first letter only" {
            Expect.equal (toCypherName "Age") "age" ""
        }
        test "handles empty string" {
            Expect.equal (toCypherName "") "" ""
        }
        test "handles already camelCase" {
            Expect.equal (toCypherName "firstName") "firstName" ""
        }
    ]

    testList "resolveLabel" [
        test "uses type name by default" {
            let label = resolveLabel typeof<Person>
            Expect.equal label "Person" ""
        }
        test "uses Label attribute when present" {
            let label = resolveLabel typeof<CustomPerson>
            Expect.equal label "PERSON" ""
        }
    ]

    testList "getMeta" [
        test "extracts record fields as properties" {
            let meta = getMetaOf<Person>()
            Expect.equal meta.Label "Person" ""
            Expect.equal meta.Properties.Length 2 ""

            let nameProp = meta.Properties |> List.find (fun p -> p.FSharpName = "Name")
            Expect.equal nameProp.CypherName "name" ""
            Expect.equal nameProp.PropertyType typeof<string> ""
            Expect.isFalse nameProp.IsOption ""

            let ageProp = meta.Properties |> List.find (fun p -> p.FSharpName = "Age")
            Expect.equal ageProp.CypherName "age" ""
            Expect.equal ageProp.PropertyType typeof<int> ""
        }

        test "detects option types" {
            let meta = getMetaOf<WithOption>()
            let nickProp = meta.Properties |> List.find (fun p -> p.FSharpName = "Nickname")
            Expect.isTrue nickProp.IsOption ""
        }

        test "caches metadata" {
            let meta1 = getMetaOf<Person>()
            let meta2 = getMetaOf<Person>()
            Expect.isTrue (obj.ReferenceEquals(meta1, meta2)) "should return same cached instance"
        }
    ]
]
