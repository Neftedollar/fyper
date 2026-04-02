module Fyper.Tests.ExprCompilerTests

open Expecto
open Fyper.Ast
open Fyper.ExprCompiler

type Person = { Name: string; Age: int }

[<Tests>]
let exprCompilerTests = testList "ExprCompiler" [

    test "compiles property access" {
        let state = newState()
        let result = compile state <@ fun (p: Person) -> p.Age @>
        // Lambda is unwrapped, inner body is PropertyGet(Var p, Age)
        match result with
        | Property(_, "age") -> ()
        | other -> failtestf "Expected Property(_, 'age'), got %A" other
    }

    test "compiles greater than" {
        let state = newState()
        let result = compile state <@ fun (p: Person) -> p.Age > 30 @>
        match result with
        | BinOp(Property(_, "age"), Gt, Param _) -> ()
        | other -> failtestf "Expected BinOp with Gt, got %A" other
    }

    test "compiles equality" {
        let state = newState()
        let result = compile state <@ fun (p: Person) -> p.Name = "Tom" @>
        match result with
        | BinOp(Property(_, "name"), Eq, Param _) -> ()
        | other -> failtestf "Expected BinOp with Eq, got %A" other
    }

    test "compiles AND (&&)" {
        let state = newState()
        let result = compile state <@ fun (p: Person) -> p.Age > 30 && p.Name = "Tom" @>
        match result with
        | BinOp(BinOp(_, Gt, _), And, BinOp(_, Eq, _)) -> ()
        | other -> failtestf "Expected AND expression, got %A" other
    }

    test "compiles OR (||)" {
        let state = newState()
        let result = compile state <@ fun (p: Person) -> p.Age > 30 || p.Name = "Tom" @>
        match result with
        | BinOp(BinOp(_, Gt, _), Or, BinOp(_, Eq, _)) -> ()
        | other -> failtestf "Expected OR expression, got %A" other
    }

    test "compiles NOT" {
        let state = newState()
        let result = compile state <@ fun (p: Person) -> not (p.Age > 30) @>
        match result with
        | UnaryOp(Not, BinOp(_, Gt, _)) -> ()
        | other -> failtestf "Expected NOT expression, got %A" other
    }

    test "compiles String.Contains" {
        let state = newState()
        let result = compile state <@ fun (p: Person) -> p.Name.Contains("om") @>
        match result with
        | BinOp(Property(_, "name"), Contains, Param _) -> ()
        | other -> failtestf "Expected CONTAINS, got %A" other
    }

    test "compiles String.StartsWith" {
        let state = newState()
        let result = compile state <@ fun (p: Person) -> p.Name.StartsWith("T") @>
        match result with
        | BinOp(Property(_, "name"), StartsWith, Param _) -> ()
        | other -> failtestf "Expected STARTS WITH, got %A" other
    }

    test "compiles String.EndsWith" {
        let state = newState()
        let result = compile state <@ fun (p: Person) -> p.Name.EndsWith("om") @>
        match result with
        | BinOp(Property(_, "name"), EndsWith, Param _) -> ()
        | other -> failtestf "Expected ENDS WITH, got %A" other
    }

    test "compiles arithmetic" {
        let state = newState()
        let result = compile state <@ fun (p: Person) -> p.Age + 1 @>
        match result with
        | BinOp(Property(_, "age"), Add, Param _) -> ()
        | other -> failtestf "Expected Add expression, got %A" other
    }

    test "parameterizes literal values" {
        let state = newState()
        let _ = compile state <@ fun (p: Person) -> p.Age > 30 && p.Name = "Tom" @>
        Expect.equal state.Parameters.Count 2 "Should have 2 parameters"
        Expect.isTrue (state.Parameters |> Map.exists (fun _ v -> v = box 30)) "should have 30"
        Expect.isTrue (state.Parameters |> Map.exists (fun _ v -> v = box "Tom")) "should have Tom"
    }

    test "null compiles to Null" {
        let state = newState()
        let result = compile state <@ null @>
        Expect.equal result Null ""
    }
]
