# Slice XX: <Concise Feature Slice Title>

- **Status**: `Draft` | `Ready` | `In Progress` | `In Review` | `Done`
- **Owner / Stage**: Stage 2 (Gemini Mini-Spec) -> Stages 3–5 (Local Ollama) -> Stage 6 (Two-Tier Review)

---

## 1. Scope & Target Files

### In Scope
- <Single cohesive vertical capability delivered by this slice>

### Out of Scope
- <Explicitly excluded items to prevent scope creep>

### Target Files
- **Create**:
  - `src/<Project>/Features/<Feature>/<Action>/<FileName>.cs`
  - `tests/<Project>.Tests/Features/<Feature>/<Action>/<FileName>Tests.cs`
- **Modify**:
  - `src/<Project>/Program.cs` *(only if DI/endpoint registration is needed; preserve explicit `Program.Main`)*

---

## 2. Data Contracts / Interfaces / SQL Schema

### DTOs & Contracts
```csharp
namespace <Project>.Features.<Feature>.<Action>;

public sealed record <Action>Request(/* properties */);
public sealed record <Action>Response(/* properties */);
```

### Interfaces
```csharp
public interface I<Feature>Repository
{
    Task<<ResultType>> <MethodName>Async(<Params>, CancellationToken cancellationToken = default);
}
```

### SQL Schema / Query Notes
```sql
-- Include only the DDL, table/column references, or parameterized query contract needed for this slice.
```

---

## 3. Behavioral Acceptance Criteria (Gherkin)

> Keep to **3–6 compact scenarios** covering happy path, validation/edge cases, and failure/not-found paths.

```gherkin
Feature: <Feature Slice Name>

  Scenario: Happy path - <expected success behavior>
    Given <precondition / initial state>
    When <action or request is executed>
    Then <expected result / state change>

  Scenario: Validation error - <invalid input>
    Given <invalid input condition>
    When <action or request is executed>
    Then <expected validation failure and no side effects>

  Scenario: Not found or conflict - <missing entity or duplicate state>
    Given <missing record or conflicting state>
    When <action or request is executed>
    Then <expected error response>
```

---

## 4. Implementation Subtasks

- [ ] **Red**: Generate xUnit + FluentAssertions + NSubstitute tests covering all Gherkin scenarios (`qwen2.5-coder:7b`)
- [ ] **Green**: Implement contracts, handler/service, data access, and DI registration (`qwen2.5-coder:7b`)
- [ ] **Verify**: Pass `dotnet build -warnaserror` and `dotnet test` (local fixes via `qwen3.5:9b`, max 2 retries)
- [ ] **Pass-1 Review**: Run local `qwen3.5:9b` review on `git diff --staged`
- [ ] **Pass-2 Audit**: Submit `git diff --staged` for Gemini architectural & security sign-off

