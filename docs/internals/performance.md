---
layout: default
title: Performance
nav_order: 9
---

# Performance

Benchmarked with BenchmarkDotNet on Apple M1 Pro, .NET 10.0.

## Compiler

| Operation | Mean | Allocated |
|-----------|------|-----------|
| Compile simple query (MATCH/WHERE/RETURN) | 890 ns | 2.3 KB |
| Compile complex query (8 clauses) | 3.2 us | 9.2 KB |
| toCypher simple | 808 ns | 2.3 KB |
| toCypher complex | 3.1 us | 9.2 KB |

## Parser

| Operation | Mean | Allocated |
|-----------|------|-----------|
| Lex simple query | 744 ns | 1.5 KB |
| Lex complex query | 2.4 us | 5.0 KB |
| Parse simple query | 1.2 us | 2.6 KB |
| Parse complex query | 3.5 us | 7.7 KB |
| Parse MERGE with ON MATCH/CREATE | 1.8 us | 4.2 KB |
| Full roundtrip: parse + compile | 2.0 us | 4.9 KB |
| Full roundtrip complex | 6.3 us | 15.0 KB |

## Schema

| Operation | Mean | Allocated |
|-----------|------|-----------|
| toCypherName (PascalCase -> camelCase) | 22 ns | 104 B |
| toRelType (PascalCase -> UPPER_SNAKE_CASE) | 51 ns | 144 B |
| getMeta (cached TypeMeta lookup) | 24 ns | 64 B |

## ResultMapper

| Operation | Mean | Allocated |
|-----------|------|-----------|
| Map GraphRecord -> Person record | 6.8 us | 4.3 KB |
| Map GraphRecord -> (string * string) tuple | 790 ns | 664 B |

## Run Benchmarks

```bash
dotnet run --project tests/Fyper.Benchmarks/ -c Release
```

For specific benchmarks:
```bash
dotnet run --project tests/Fyper.Benchmarks/ -c Release -- --filter "*Compiler*"
dotnet run --project tests/Fyper.Benchmarks/ -c Release -- --filter "*Parser*"
```
