# C# / .NET Coding & Testing Standards

All C# code generated or modified in this repository must strictly follow these standards. Violations fail the deterministic build gate (`dotnet build -warnaserror`) or code review.

## 1. Runtime & Project Configuration

- **Target Framework**: Modern .NET (`.NET 10+`, LTS releases only).
- **Nullable Reference Types**: Always enabled (`<Nullable>enable</Nullable>`). Never suppress nullability warnings with `!` unless justified by a framework guarantee and documented.
- **Warnings as Errors**: Code must compile cleanly under `dotnet build -warnaserror`.
- **Namespaces**: Use file-scoped namespaces (`namespace MyApp.Features.Orders;`).

## 2. Explicit Application Entry Point (No Top-Level Statements)

- **Top-level statements are prohibited.**
- Every executable project (`Program.cs`) MUST define an explicit `Program` class with an explicit `Main` method:

```csharp
namespace MyApp;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        // Service registration & middleware pipeline
        var app = builder.Build();
        app.Run();
    }
}
```

- Using `public class Program` also ensures clean integration testing with `WebApplicationFactory<Program>`.

## 3. Vertical-Slice Architecture

- Organize features by **vertical slice** rather than horizontal technical layers:
  - Example: `Features/<FeatureName>/<ActionName>/` (e.g., `Features/Orders/CreateOrder/`).
  - Co-locate the slice's Request/Response DTOs (`record`), Handler/Service, Validator, Endpoint/Controller, and Data Access contract.
- Keep cross-cutting infrastructure minimal and explicit (`Infrastructure/`, `Common/`).
- Avoid speculative abstractions: only introduce an interface when required for testability (I/O boundary, database, external HTTP/clock) or multiple implementations.

## 4. C# Implementation Conventions

- **Immutability**: Prefer `sealed record` / `record` types for DTOs, commands, queries, and value objects.
- **Async / Await**:
  - Always propagate `CancellationToken cancellationToken = default` through async methods and I/O calls.
  - Never block on async code (`.Result`, `.Wait()`, `.GetAwaiter().GetResult()`).
- **Dependency Injection**: Use constructor injection (primary constructors or explicit readonly fields). Never use the service locator anti-pattern.
- **Data Access (Dapper / EF Core)**:
  - Always use parameterized queries (Dapper anonymous objects / `DynamicParameters` or EF Core LINQ). Never concatenate user input into SQL strings.
  - Keep read queries lightweight (`AsNoTracking()` in EF Core).
- **Error Handling & Logging**:
  - Never swallow exceptions with empty `catch` blocks.
  - Use structured logging (`ILogger<T>`) with message templates, never string interpolation in log calls.
  - Validate inputs at the slice boundary and return clear, typed errors or problem details.

## 5. Testing Conventions (xUnit + FluentAssertions + NSubstitute)

- **Framework**: `xUnit` (`[Fact]`, `[Theory]`, `[InlineData]`).
- **Assertions**: `FluentAssertions` (e.g., `result.Should().NotBeNull();`, `response.StatusCode.Should().Be(200);`).
- **Mocking**: `NSubstitute` (`Substitute.For<IRepository>()`, `.Returns(...)`, `.Received(1).SaveAsync(...)`).
- **Structure**: Follow strict **Arrange / Act / Assert** sections.
- **Naming & Traceability**:
  - Name test methods clearly after the slice's Gherkin scenarios: `MethodOrFeature_StateUnderTest_ExpectedBehavior`.
  - Every `Scenario` in `.ai/slices/slice-XX.md` must map to at least one automated test.
  - Keep unit tests deterministic and fast (no real network/disk/wall-clock dependencies; inject `TimeProvider` or clock interfaces where needed).

