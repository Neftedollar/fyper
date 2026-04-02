namespace Fyper.Benchmarks

open BenchmarkDotNet.Attributes
open Fyper
open Fyper.Ast
open Fyper.CypherCompiler
open Fyper.Parser

type Person = { Name: string; Age: int }
type Movie = { Title: string; Released: int }
type ActedIn = { Roles: string list }

[<MemoryDiagnoser>]
[<SimpleJob(iterationCount = 50)>]
type CompilerBenchmarks() =

    let simpleQuery : CypherQuery<obj> = {
        Clauses = [
            Match([NodePattern("p", Some "Person", Map.empty)], false)
            Where(BinOp(Property("p", "age"), Gt, Param "p0"))
            Return([{ Expr = Variable "p"; Alias = None }], false)
        ]
        Parameters = Map.ofList ["p0", box 30]
    }

    let complexQuery : CypherQuery<obj> = {
        Clauses = [
            Match([NodePattern("p", Some "Person", Map.empty)], false)
            Match([NodePattern("m", Some "Movie", Map.empty)], false)
            Match([
                RelPattern(
                    NodePattern("p", Some "Person", Map.empty),
                    None, Some "ACTED_IN", Map.empty,
                    Outgoing, None,
                    NodePattern("m", Some "Movie", Map.empty))
            ], false)
            Where(BinOp(
                BinOp(Property("p", "age"), Gt, Param "p0"),
                And,
                BinOp(Property("m", "released"), Gte, Param "p1")))
            OrderBy [(Property("m", "released"), Ascending)]
            Skip (Param "skip")
            Limit (Param "limit")
            Return([
                { Expr = Property("p", "name"); Alias = Some "name" }
                { Expr = Property("m", "title"); Alias = Some "title" }
            ], false)
        ]
        Parameters = Map.ofList ["p0", box 30; "p1", box 2000; "skip", box 0; "limit", box 10]
    }

    [<Benchmark(Description = "Compile simple query (MATCH/WHERE/RETURN)")>]
    member _.CompileSimple() =
        compile simpleQuery

    [<Benchmark(Description = "Compile complex query (2 MATCH/rel/WHERE/ORDER/SKIP/LIMIT/RETURN)")>]
    member _.CompileComplex() =
        compile complexQuery

    [<Benchmark(Description = "toCypher simple query")>]
    member _.ToCypherSimple() =
        Cypher.toCypher simpleQuery

    [<Benchmark(Description = "toCypher complex query")>]
    member _.ToCypherComplex() =
        Cypher.toCypher complexQuery


[<MemoryDiagnoser>]
[<SimpleJob(iterationCount = 50)>]
type ParserBenchmarks() =

    let simpleCypher = "MATCH (p:Person) WHERE p.age > $minAge RETURN p"

    let complexCypher =
        "MATCH (p:Person)-[:ACTED_IN]->(m:Movie) " +
        "WHERE p.age > $minAge AND m.released >= $minYear " +
        "ORDER BY m.released DESC " +
        "SKIP 0 LIMIT 10 " +
        "RETURN p.name AS name, m.title AS title"

    let mergeCypher =
        "MERGE (p:Person {name: 'Tom'}) " +
        "ON MATCH SET p.age = 50 " +
        "ON CREATE SET p.age = 25"

    [<Benchmark(Description = "Lex simple query")>]
    member _.LexSimple() =
        Lexer.tokenize simpleCypher

    [<Benchmark(Description = "Lex complex query")>]
    member _.LexComplex() =
        Lexer.tokenize complexCypher

    [<Benchmark(Description = "Parse simple query")>]
    member _.ParseSimple() =
        CypherParser.parse simpleCypher

    [<Benchmark(Description = "Parse complex query")>]
    member _.ParseComplex() =
        CypherParser.parse complexCypher

    [<Benchmark(Description = "Parse MERGE with ON MATCH/CREATE")>]
    member _.ParseMerge() =
        CypherParser.parse mergeCypher

    [<Benchmark(Description = "Full roundtrip: parse → compile")>]
    member _.RoundtripSimple() =
        let parsed = CypherParser.parse simpleCypher
        compile parsed

    [<Benchmark(Description = "Full roundtrip complex: parse → compile")>]
    member _.RoundtripComplex() =
        let parsed = CypherParser.parse complexCypher
        compile parsed


[<MemoryDiagnoser>]
[<SimpleJob(iterationCount = 50)>]
type SchemaBenchmarks() =

    [<Benchmark(Description = "toCypherName: PascalCase → camelCase")>]
    member _.ToCypherName() =
        Schema.toCypherName "FirstName"

    [<Benchmark(Description = "toRelType: PascalCase → UPPER_SNAKE_CASE")>]
    member _.ToRelType() =
        Schema.toRelType "ActedIn"

    [<Benchmark(Description = "getMeta: extract TypeMeta for Person")>]
    member _.GetMeta() =
        Schema.getMeta typeof<Person>


[<MemoryDiagnoser>]
[<SimpleJob(iterationCount = 50)>]
type ResultMapperBenchmarks() =

    let personRecord : GraphValue.GraphRecord = {
        Keys = ["p"]
        Values = Map.ofList [
            "p", GraphValue.GNode {
                Id = 1L
                Labels = ["Person"]
                Properties = Map.ofList [
                    "name", GraphValue.GString "Tom"
                    "age", GraphValue.GInt 50L
                ]
            }
        ]
    }

    let tupleRecord : GraphValue.GraphRecord = {
        Keys = ["name"; "title"]
        Values = Map.ofList [
            "name", GraphValue.GString "Tom"
            "title", GraphValue.GString "The Matrix"
        ]
    }

    [<Benchmark(Description = "Map GraphRecord → Person record")>]
    member _.MapPerson() =
        ResultMapper.mapGraphRecord<Person> personRecord

    [<Benchmark(Description = "Map GraphRecord → (string * string) tuple")>]
    member _.MapTuple() =
        ResultMapper.mapGraphRecord<string * string> tupleRecord
