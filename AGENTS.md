# Repository Guidelines

## Project Structure & Module Organization
- Solution file: `MBW.Utilities.Journal.sln`.
- Library code: `src/MBW.Utilities.Journal/` (journal strategies, helpers, factory).
- Tests: `src/MBW.Utilities.Journal.Tests/` (helpers, scenario tests, example usage).
- Repo-level settings: `Directory.Build.props`, `PublicAPI.*.txt`.

## Build, Test, and Development Commands
- Build: `dotnet build MBW.Utilities.Journal.sln` — restores and compiles all projects.
- Run tests: `dotnet test MBW.Utilities.Journal.sln` — executes xUnit suites, including WAL and sparse scenarios.
- Target a single test file: `dotnet test --filter WalTests` (or other class names).

## Coding Style & Naming Conventions
- Language: C# with `internal` defaults unless API surface is intentional.
- Indentation: 4 spaces; keep braces on new lines (project’s prevailing style).
- Naming: PascalCase for types/methods, camelCase for locals/fields; private fields often prefixed with `_`.
- Structs for on-disk layouts use `[StructLayout(LayoutKind.Sequential, Pack = 1)]` and expose `StructSize`.
- Favor span-based APIs; override the `Span<byte>`/`ReadOnlySpan<byte>` methods rather than array overloads.

## Testing Guidelines
- Framework: xUnit (`[Fact]`, `[Theory]`).
- Place new tests under `src/MBW.Utilities.Journal.Tests/`; mirror feature area (e.g., `WalTests`, `SparseTests`).
- Keep scenario-driven names (e.g., `RecoverCommittedTest`); assert both journaled view and origin where relevant.
- Run `dotnet test` before submitting; add regression tests for any bug fixes.

## Commit & Pull Request Guidelines
- Commits: concise subject in imperative mood; group related changes together.
- PRs should describe the scenario covered, risks (e.g., journal replay, corruption handling), and test results (`dotnet test` output).
- Include references to issues/links when applicable; attach failure cases or repro steps for journal corruption/rollback paths.
