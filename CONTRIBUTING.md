# Contributing to Fyper

## Quick Start

```bash
git clone https://github.com/Neftedollar/fyper.git
cd fyper
dotnet test tests/Fyper.Tests/
dotnet run --project samples/Fyper.Sample/
```

## Development

- **.NET 10.0** SDK required (see `global.json`)
- **F# only** — no C# projects
- Tests: **Expecto** + **FsCheck** (property-based)
- Integration tests need Docker: `docker compose up -d`

## Code Style

- Idiomatic F#: `|>` pipelines, `match` expressions, `module` over `class`
- Records over classes for data types
- DUs over enums/inheritance
- `task { }` for async (not `async { }`)
- Exhaustive pattern matching — no wildcard catch-alls on core DUs
- `.editorconfig` enforces formatting

## Making Changes

1. Fork the repo and create a branch from `main`
2. Write tests first (TDD)
3. Make your changes
4. Run `dotnet test tests/Fyper.Tests/` — all 249 tests must pass
5. Update docs if you changed the public API
6. Submit a PR

## Architecture

See [docs/internals/architecture.md](docs/internals/architecture.md) for the data flow and module map.

Key rule: **all values must be parameterized**. No literal values in generated Cypher strings. Property-based tests verify this.

## Adding a CE Operation

1. Add the operation to `src/Fyper/CypherBuilder.fs` as a `[<CustomOperation>]`
2. Handle it in `src/Fyper/QueryTranslator.fs` `walkExpr`
3. Add tests in `tests/Fyper.Tests/`
4. Update `docs/reference/ce-operations.md`

## Adding a Driver

1. Create `src/Fyper.YourDb/` with a new `.fsproj`
2. Implement `IGraphDriver` and `IGraphTransaction`
3. Set `Capabilities` to declare supported features
4. Add integration tests
5. Add docs in `docs/reference/`

## Commit Style

[Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`
