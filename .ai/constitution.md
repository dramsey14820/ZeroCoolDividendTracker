# Project Constitution

> Keep under 300 tokens. Loaded into every local Ollama prompt.

## Stack Defaults
- **.NET**: .NET 8 / .NET 10, C# (`<Nullable>enable</Nullable>`, `-warnaserror`).
- **Data**: SQL Server with Dapper (parameterized SQL) or EF Core (`AsNoTracking()` for reads).
- **Tests**: xUnit + FluentAssertions + NSubstitute.

## Hard Constraints
- **Entry Point**: Explicit `public class Program` with `static void Main(string[] args)`. **No top-level statements.**
- **Structure**: Vertical slices (`Features/<Feature>/<Action>/`). Edit only the active slice's target files.
- **Safety**: Parameterized queries only; propagate `CancellationToken`; never swallow exceptions or block async calls.

## Token & Handoff Rules
- **Local Cap**: Ollama `num_ctx: 8192` (`qwen2.5-coder:7b` for test/code gen; `qwen3.5:9b` for fixes & Pass-1 review).
- **Context**: Pass only this file, active `slice-XX.md`, target files, and failing output. Never dump the full repo.
- **Escalation**: Max 2 local fix attempts, then escalate to Gemini with exact errors and minimal diff.
- **Gate**: `dotnet build -warnaserror` + `dotnet test` -> Pass-1 local diff -> Pass-2 Gemini `git diff --staged` audit.
