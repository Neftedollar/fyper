module Fyper.Tests.ResultMapperTests

open Expecto
open Fyper
open Fyper.GraphValue

type Person = { Name: string; Age: int }
type PersonOpt = { Name: string; Email: string option }

[<Tests>]
let resultMapperTests = testList "ResultMapper" [

    test "map GNode to record" {
        let record : GraphRecord = {
            Keys = ["p"]
            Values = Map.ofList [
                "p", GNode {
                    Id = 1L
                    Labels = ["Person"]
                    Properties = Map.ofList [
                        "name", GString "Tom"
                        "age", GInt 50L
                    ]
                }
            ]
        }
        let person = ResultMapper.mapGraphRecord<Person> record
        Expect.equal person.Name "Tom" "name"
        Expect.equal person.Age 50 "age"
    }

    test "map tuple from multiple keys" {
        let record : GraphRecord = {
            Keys = ["name"; "title"]
            Values = Map.ofList [
                "name", GString "Tom"
                "title", GString "The Matrix"
            ]
        }
        let (name, title) = ResultMapper.mapGraphRecord<string * string> record
        Expect.equal name "Tom" "name"
        Expect.equal title "The Matrix" "title"
    }

    test "map single scalar value" {
        let record : GraphRecord = {
            Keys = ["count"]
            Values = Map.ofList ["count", GInt 42L]
        }
        let result = ResultMapper.mapGraphRecord<int64> record
        Expect.equal result 42L "count"
    }

    test "map GNull to option None" {
        let record : GraphRecord = {
            Keys = ["p"]
            Values = Map.ofList [
                "p", GNode {
                    Id = 1L
                    Labels = ["PersonOpt"]
                    Properties = Map.ofList [
                        "name", GString "Tom"
                    ]
                }
            ]
        }
        let person = ResultMapper.mapGraphRecord<PersonOpt> record
        Expect.equal person.Name "Tom" "name"
        Expect.equal person.Email None "missing optional field"
    }

    test "map GInt to int (downcasts i64 to i32)" {
        let record : GraphRecord = {
            Keys = ["p"]
            Values = Map.ofList [
                "p", GNode {
                    Id = 1L
                    Labels = ["Person"]
                    Properties = Map.ofList [
                        "name", GString "X"
                        "age", GInt 25L
                    ]
                }
            ]
        }
        let person = ResultMapper.mapGraphRecord<Person> record
        Expect.equal person.Age 25 "int64 -> int conversion"
    }

    test "map GFloat to float" {
        let record : GraphRecord = {
            Keys = ["score"]
            Values = Map.ofList ["score", GFloat 3.14]
        }
        let result = ResultMapper.mapGraphRecord<float> record
        Expect.floatClose Accuracy.medium result 3.14 "float"
    }

    test "map GBool" {
        let record : GraphRecord = {
            Keys = ["active"]
            Values = Map.ofList ["active", GBool true]
        }
        let result = ResultMapper.mapGraphRecord<bool> record
        Expect.isTrue result "bool true"
    }

    test "map GString" {
        let record : GraphRecord = {
            Keys = ["name"]
            Values = Map.ofList ["name", GString "Alice"]
        }
        let result = ResultMapper.mapGraphRecord<string> record
        Expect.equal result "Alice" "string"
    }

    test "convertValue handles GList" {
        let glist = GList [GString "A"; GString "B"]
        // Direct convertValue returns a list object
        let result = ResultMapper.convertValue typeof<obj> glist
        Expect.isNotNull result "list converted"
    }

    test "missing required field throws" {
        let record : GraphRecord = {
            Keys = ["p"]
            Values = Map.ofList [
                "p", GNode {
                    Id = 1L
                    Labels = ["Person"]
                    Properties = Map.ofList ["name", GString "Tom"]
                    // Missing "age" field
                }
            ]
        }
        Expect.throws
            (fun () -> ResultMapper.mapGraphRecord<Person> record |> ignore)
            "should throw on missing required field"
    }
]
