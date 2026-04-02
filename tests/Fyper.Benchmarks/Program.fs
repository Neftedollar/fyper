module Fyper.Benchmarks.Program

open BenchmarkDotNet.Running

[<EntryPoint>]
let main argv =
    BenchmarkSwitcher
        .FromAssembly(typeof<CompilerBenchmarks>.Assembly)
        .Run(argv)
    |> ignore
    0
