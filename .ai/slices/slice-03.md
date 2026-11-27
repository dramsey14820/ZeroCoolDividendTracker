# Slice 03: Portfolios & Accounts Management

- **Status**: `Ready`
- **Risk**: `High`
- **Owner / Stage**: Stage 2 (Gemini Mini-Spec) -> Stages 3–5 (Local Ollama) -> Stage 6 (Two-Tier Review)

---

## 1. Scope & Target Files

### In Scope
- Define `PortfolioDto`, `AccountDto`, request DTOs, and `IPortfolioService` in `DividendTracker.Core/Features/Portfolios/`.
- Implement `PortfolioService` using `ISqliteConnectionFactory`, `TimeProvider`, and parameterized Dapper SQL against the existing `Portfolios` and `Accounts` tables.
- Register `AddPortfolioFeature()` in `DividendTracker.Web` and `DividendTracker.Maui`.
- Build the `/portfolios` Blazor page (`Portfolios.razor` + `Portfolios.razor.css`) in `DividendTracker.Shared.UI` using CSS Grid/Flexbox and `app.css` design tokens to list, create, edit, and delete portfolios and their accounts.
- Add xUnit + FluentAssertions integration tests for `PortfolioService` (real temp SQLite DB) and bUnit + NSubstitute component tests for `Portfolios.razor`.

### Out of Scope
- Securities, lookup taxonomies, holdings, market buys, or dividend payouts (Slices 04–06)
- Portfolio value, yield, or income analytics (Slice 07)
- Modal confirmation dialogs or JS interop (keep actions simple and pure Blazor)

### Target Files
- **Create**:
  - `src/DividendTracker.Core/Features/Portfolios/PortfolioDtos.cs`
  - `src/DividendTracker.Core/Features/Portfolios/IPortfolioService.cs`
  - `src/DividendTracker.Core/Features/Portfolios/PortfolioService.cs`
  - `src/DividendTracker.Core/Features/Portfolios/PortfolioServiceCollectionExtensions.cs`
  - `src/DividendTracker.Shared.UI/Pages/Portfolios.razor`
  - `src/DividendTracker.Shared.UI/Pages/Portfolios.razor.css`
  - `tests/DividendTracker.Tests/Features/Portfolios/PortfolioServiceTests.cs`
  - `tests/DividendTracker.Tests/Features/Portfolios/PortfoliosPageTests.cs`
- **Modify**:
  - `src/DividendTracker.Shared.UI/_Imports.razor` *(add `@using DividendTracker.Core.Features.Portfolios`)*
  - `src/DividendTracker.Web/Program.cs` *(register `builder.Services.AddPortfolioFeature();`; preserve explicit `Program.Main`)*
  - `src/DividendTracker.Maui/MauiProgram.cs` *(register `builder.Services.AddPortfolioFeature();`)*

---

## 2. Data Contracts / Interfaces / SQL Schema

### Required Namespaces & Packages
- Existing packages only (no new NuGet packages required).
- Key namespaces:
  - `Dapper`
  - `System.Globalization`
  - `Microsoft.Extensions.DependencyInjection`
  - `Microsoft.Extensions.DependencyInjection.Extensions` *(for `TryAddSingleton`)*
  - `DividendTracker.Core.Infrastructure.Persistence`
  - `Bunit`, `FluentAssertions`, `NSubstitute`, `Xunit`
- Analyzer notes (`-warnaserror`, `AnalysisLevel=latest-recommended`):
  - Use `CultureInfo.InvariantCulture` for ISO-8601 date formatting (`ToString("O", CultureInfo.InvariantCulture)`).
  - Use `ArgumentNullException.ThrowIfNull(...)` and `ArgumentException.ThrowIfNullOrWhiteSpace(...)`.
  - Pass `CommandDefinition` with `cancellationToken` to Dapper calls (`new CommandDefinition(sql, param, cancellationToken: cancellationToken)`).
  - In SQLite integration tests using temp files, call `SqliteConnection.ClearAllPools()` before deleting the temp file in `Dispose()`.

### DTOs & Contracts
```csharp
namespace DividendTracker.Core.Features.Portfolios;

public sealed record AccountDto(
    long Id,
    long PortfolioId,
    string Name,
    string AccountType,
    string CreatedUtc);

public sealed record PortfolioDto(
    long Id,
    string Name,
    string Description,
    bool IsModelled,
    string CreatedUtc,
    IReadOnlyList<AccountDto> Accounts);

public sealed record CreatePortfolioRequest(
    string Name,
    string? Description,
    bool IsModelled);

public sealed record UpdatePortfolioRequest(
    long Id,
    string Name,
    string? Description,
    bool IsModelled);

public sealed record CreateAccountRequest(
    long PortfolioId,
    string Name,
    string AccountType);

public sealed record UpdateAccountRequest(
    long Id,
    string Name,
    string AccountType);
```

### Interfaces & DI Extension
```csharp
namespace DividendTracker.Core.Features.Portfolios;

public interface IPortfolioService
{
    Task<IReadOnlyList<PortfolioDto>> GetPortfoliosAsync(CancellationToken cancellationToken = default);
    Task<PortfolioDto> CreatePortfolioAsync(CreatePortfolioRequest request, CancellationToken cancellationToken = default);
    Task<PortfolioDto> UpdatePortfolioAsync(UpdatePortfolioRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeletePortfolioAsync(long portfolioId, CancellationToken cancellationToken = default);
    Task<AccountDto> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default);
    Task<AccountDto> UpdateAccountAsync(UpdateAccountRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAccountAsync(long accountId, CancellationToken cancellationToken = default);
}

public static class PortfolioServiceCollectionExtensions
{
    public static IServiceCollection AddPortfolioFeature(this IServiceCollection services);
}
```

### Implementation Notes
- **`PortfolioService`**:
  - Constructor: `(ISqliteConnectionFactory connectionFactory, TimeProvider timeProvider)`.
  - Validation:
    - `Name` (Portfolio & Account): required (non-whitespace), trimmed, max length `100` (throw `ArgumentException` if invalid).
    - `Description`: trimmed; `null` stored as `""`.
    - `AccountType`: required (non-whitespace), trimmed, max length `50` (throw `ArgumentException` if invalid).
  - `GetPortfoliosAsync`: loads all rows from `Portfolios` (`ORDER BY Name, Id`) and all rows from `Accounts` (`ORDER BY Name, Id`) in two parameterized queries, then groups `Accounts` by `PortfolioId` into `PortfolioDto` instances (no N+1 queries).
  - `CreatePortfolioAsync`: inserts with `CreatedUtc = timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture)` and `IsModelled` as `0`/`1`, retrieves `last_insert_rowid()`, returns the created `PortfolioDto` with empty `Accounts`.
  - `UpdatePortfolioAsync`: updates `Name`, `Description`, `IsModelled` where `Id = @Id`; if affected rows == 0, throws `KeyNotFoundException`; returns updated `PortfolioDto` including its existing `Accounts`.
  - `DeletePortfolioAsync`: `DELETE FROM Portfolios WHERE Id = @Id`; returns `affected > 0` (SQLite `ON DELETE CASCADE` removes child accounts).
  - `CreateAccountAsync`: verifies parent portfolio exists (`SELECT COUNT(1) FROM Portfolios WHERE Id = @PortfolioId`); if 0, throws `KeyNotFoundException`; inserts account and returns `AccountDto`.
  - `UpdateAccountAsync`: updates `Name`, `AccountType` where `Id = @Id`; if affected rows == 0, throws `KeyNotFoundException`; returns updated `AccountDto`.
  - `DeleteAccountAsync`: `DELETE FROM Accounts WHERE Id = @Id`; returns `affected > 0`.
- **`PortfolioServiceCollectionExtensions`**:
  - Calls `services.TryAddSingleton(TimeProvider.System);` and `services.AddSingleton<IPortfolioService, PortfolioService>();`.
- **`Portfolios.razor` & `Portfolios.razor.css`**:
  - `@page "/portfolios"`, `@inject IPortfolioService PortfolioService`.
  - Loads portfolios in `OnInitializedAsync()`.
  - Form to create/edit a portfolio (`Name`, `Description`, `IsModelled` checkbox, Save/Cancel buttons) and displays an inline error alert (`role="alert"`) if `ArgumentException` or `KeyNotFoundException` is thrown.
  - Responsive CSS Grid of portfolio cards showing portfolio name, `Modelled` / `Actual` badge, description, Edit/Delete portfolio buttons, list of accounts with Delete button, and an inline "Add Account" form per portfolio (`Name` input, `AccountType` select with `Taxable`, `IRA`, `Roth IRA`, `401(k)`, `Model`, Add button).
  - Pure CSS Grid & Flexbox using `--color-*`, `--space-*`, `--radius-*` from `app.css`; interactive controls have `min-height: 44px` for Android touch targets.

### SQL Schema Reference *(already created in Slice 02)*
```sql
-- Portfolios(Id, Name, Description, IsModelled, CreatedUtc)
-- Accounts(Id, PortfolioId, Name, AccountType, CreatedUtc)
-- FOREIGN KEY (PortfolioId) REFERENCES Portfolios(Id) ON DELETE CASCADE
```

---

## 3. Behavioral Acceptance Criteria (Gherkin)

```gherkin
Feature: Portfolios & Accounts Management

  Scenario: Create and list actual and modelled portfolios with accounts
    Given an initialized SQLite database
    When a user creates an actual portfolio "Core Income" and a modelled portfolio "Watchlist"
    And adds a "Roth IRA" account named "Fidelity Roth" to "Core Income"
    Then GetPortfoliosAsync returns both portfolios ordered by name
    And "Core Income" includes the "Fidelity Roth" account with AccountType "Roth IRA"
    And "Watchlist" has IsModelled equal to true and zero accounts

  Scenario: Update portfolio and account details
    Given an existing portfolio "Core Income" with an account "Taxable Brokerage"
    When UpdatePortfolioAsync renames it to "Dividend Growth" with IsModelled set to true
    And UpdateAccountAsync updates the account name to "Schwab Taxable" and type to "Taxable"
    Then GetPortfoliosAsync reflects the updated portfolio and account values

  Scenario: Deleting a portfolio cascades to its accounts
    Given an existing portfolio with two accounts
    When DeletePortfolioAsync is called for that portfolio
    Then DeletePortfolioAsync returns true
    And GetPortfoliosAsync no longer contains the portfolio or its accounts

  Scenario: Blank portfolio or account name is rejected
    Given an initialized SQLite database
    When CreatePortfolioAsync is called with a whitespace Name
    Or CreateAccountAsync is called with a whitespace Name or AccountType
    Then an ArgumentException is thrown and no record is inserted

  Scenario: Creating an account for a missing portfolio fails cleanly
    Given an initialized SQLite database with no portfolio having Id 999
    When CreateAccountAsync is called with PortfolioId 999
    Then a KeyNotFoundException is thrown

  Scenario: Portfolios page renders portfolios and creates a new portfolio via IPortfolioService
    Given IPortfolioService returns an existing portfolio "Core Income" with account "Fidelity Roth"
    When the Portfolios Blazor component is rendered
    Then the markup displays "Core Income", "Actual", and "Fidelity Roth"
    And submitting the new portfolio form calls CreatePortfolioAsync and refreshes the list
```

---

## 4. Implementation Subtasks

- [ ] **Red**: Generate xUnit + FluentAssertions + NSubstitute + bUnit tests covering all Gherkin scenarios (`qwen2.5-coder:7b`)
- [ ] **Green**: Implement `PortfolioDtos`, `IPortfolioService`, `PortfolioService`, DI registration, and `/portfolios` Blazor UI (`qwen2.5-coder:7b`)
- [ ] **Verify**: Pass `dotnet build -warnaserror` and `dotnet test` (local fixes via `qwen3.5:9b`, max 2 retries)
- [ ] **Pass-1 Review**: Run local `qwen3.5:9b` review on `git diff --staged`
- [ ] **Pass-2 Audit**: Submit `git diff --staged` for Gemini architectural & security sign-off

