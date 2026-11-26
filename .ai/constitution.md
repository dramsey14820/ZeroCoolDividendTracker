# Project Constitution

> Keep under 300 tokens. Loaded into every local Ollama prompt.

## Stack & Targets
- **.NET**: .NET 10+ (LTS only), C# (`<Nullable>enable</Nullable>`, `-warnaserror`).
- **UI**: Shared RCL (`*.Shared.UI`) hosted by **Blazor Server** (`*.Web`) and **.NET MAUI Blazor Hybrid** (`*.Maui` for Windows + Android). No XAML feature screens.
- **Styling**: Pure **CSS Grid** & **CSS Flexbox** with `.razor.css` isolation, mobile-first. No Bootstrap/Tailwind unless requested.
- **Desktop**: Unpackaged Windows build (`WindowsPackageType=None`) for **WiX** MSI; no MSIX-only APIs; write user data to AppData.
- **Data & Tests**: SQL Server (Dapper parameterized SQL or EF Core `AsNoTracking()`). xUnit + FluentAssertions + NSubstitute (+ bUnit).

## Hard Constraints
- **Entry Point**: Explicit `public class Program` + `static void Main(string[] args)`. **No top-level statements.**
- **Boundaries**: Vertical slices (`Features/<Feature>/<Action>/`). Razor injects `I*Service` only—never direct DB or platform APIs.
- **Safety**: Parameterized queries; propagate `CancellationToken`; never swallow exceptions or block async calls.

## Token & Handoff Rules
- **Local Cap**: Ollama `num_ctx: 8192` (`qwen2.5-coder:7b` gen; `qwen3.5:9b` fixes & Pass-1 review).
- **Context**: Pass only this file, active `slice-XX.md`, target files, and failing output. Never dump the repo.
- **Gate**: Max 2 local fix retries -> escalate to Gemini. `dotnet build -warnaserror` + `dotnet test` -> Pass-1 local -> Pass-2 Gemini `git diff --staged`.
