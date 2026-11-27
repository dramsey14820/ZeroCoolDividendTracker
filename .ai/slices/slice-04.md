# Slice 04: Securities & Lookup Taxonomies

- **Status**: `In Progress`
- **Risk**: `High`
- **Owner / Stage**: Stage 2 (Gemini Mini-Spec) -> Stages 3–5 (Local Ollama) -> Stage 6 (Two-Tier Review)

---

## 1. Scope & Target Files

### In Scope
- Define `LookupCategory`, `LookupItemDto`, `LookupCatalogDto`, `SecurityDto`, request DTOs, and `ISecurityService` in `DividendTracker.Core/Features/Securities/`.
- Implement `SecurityService` using `ISqliteConnectionFactory`, `TimeProvider`, and parameterized Dapper SQL against the existing `InvestmentTypes`, `IndustryTypes`, `SectorTypes`, and `Securities` tables.
- Register `AddSecurityFeature()` in `DividendTracker.Web` and `DividendTracker.Maui`.
- Add a `Securities` navigation link in `NavMenu.razor` (and update `MainLayoutComponentTests.cs`).
- Build the `/securities` Blazor page (`Securities.razor` + `Securities.razor.css`) in `DividendTracker.Shared.UI` using CSS Grid/Flexbox and `app.css` design tokens to list, create, edit, and delete securities and manage lookup taxonomies (`InvestmentTypes`, `IndustryTypes`, `SectorTypes`).
- Add xUnit + FluentAssertions integration tests for `SecurityService` (real temp SQLite DB) and bUnit + NSubstitute component tests for `Securities.razor`.

### Out of Scope
- Holdings, market buys, or dividend payouts (Slices 05–06)
- Live quote fetching or external market data providers (Slice 08)
- Modal confirmation dialogs or JS interop (keep actions simple and pure Blazor)

### Target Files
- **Create**:
  - `src/DividendTracker.Core/Features/Securities/SecurityDtos.cs`
  - `src/DividendTracker.Core/Features/Securities/ISecurityService.cs`
  - `src/DividendTracker.Core/Features/Securities/SecurityService.cs`
  - `src/DividendTracker.Core/Features/Securities/SecurityServiceCollectionExtensions.cs`
  - `src/DividendTracker.Shared.UI/Pages/Securities.razor`
  - `src/DividendTracker.Shared.UI/Pages/Securities.razor.css`
  - `tests/DividendTracker.Tests/Features/Securities/SecurityServiceTests.cs`
  - `tests/DividendTracker.Tests/Features/Securities/SecuritiesPageTests.cs`
- **Modify**:
  - `src/DividendTracker.Shared.UI/_Imports.razor` *(add `@using DividendTracker.Core.Features.Securities`)*
  - `src/DividendTracker.Shared.UI/Layout/NavMenu.razor` *(add `<NavLink class="nav-link" href="securities">Securities</NavLink>` after `Portfolios`)*
  - `src/DividendTracker.Web/Program.cs` *(register `builder.Services.AddSecurityFeature();`; preserve explicit `Program.Main`)*
  - `src/DividendTracker.Maui/MauiProgram.cs` *(register `builder.Services.AddSecurityFeature();`)*
  - `tests/DividendTracker.Tests/Features/Shell/MainLayoutComponentTests.cs` *(update `ExpectedNavLinks` and count from `5` to `6` to include `"Securities"`)*

---

## 2. Data Contracts / Interfaces / SQL Schema

### Required Namespaces & Packages
- Existing packages only (no new NuGet packages required).
- Key namespaces:
  - `Dapper`
  - `Microsoft.Data.Sqlite`
  - `System.Globalization`
  - `Microsoft.Extensions.DependencyInjection`
  - `Microsoft.Extensions.DependencyInjection.Extensions` *(for `TryAddSingleton`)*
  - `DividendTracker.Core.Infrastructure.Persistence`
  - `Bunit`, `FluentAssertions`, `NSubstitute`, `Xunit`
- Analyzer notes (`-warnaserror`, `AnalysisLevel=latest-recommended`):
  - Use `CultureInfo.InvariantCulture` for ISO-8601 date formatting (`ToString("O", CultureInfo.InvariantCulture)`) and currency/decimal formatting.
  - Use `ToUpperInvariant()` for normalizing ticker symbols (`CA1308`).
  - Use `ArgumentNullException.ThrowIfNull(...)`, `ArgumentException.ThrowIfNullOrWhiteSpace(...)`, and `ArgumentOutOfRangeException.ThrowIfNegative(...)`.
  - Pass `CommandDefinition` with `cancellationToken` to Dapper calls (`new CommandDefinition(sql, param, cancellationToken: cancellationToken)`).
  - In SQLite integration tests using temp files, call `SqliteConnection.ClearAllPools()` before deleting the temp file in `Dispose()`.

### DTOs & Contracts
```csharp
namespace DividendTracker.Core.Features.Securities;

public enum LookupCategory
{
    InvestmentType = 0,
    IndustryType = 1,
    SectorType = 2,
}

public sealed record LookupItemDto(
    long Id,
    string Name);

public sealed record LookupCatalogDto(
    IReadOnlyList<LookupItemDto> InvestmentTypes,
    IReadOnlyList<LookupItemDto> IndustryTypes,
    IReadOnlyList<LookupItemDto> SectorTypes);

public sealed record CreateLookupRequest(
    LookupCategory Category,
    string Name);

public sealed record UpdateLookupRequest(
    LookupCategory Category,
    long Id,
    string Name);

public sealed record SecurityDto(
    long Id,
    string Symbol,
    string Name,
    long InvestmentTypeId,
    string InvestmentTypeName,
    long IndustryTypeId,
    string IndustryTypeName,
    long SectorTypeId,
    string SectorTypeName,
    decimal LastPrice,
    string? LastPriceUpdatedUtc);

public sealed record CreateSecurityRequest(
    string Symbol,
    string Name,
    long InvestmentTypeId,
    long IndustryTypeId,
    long SectorTypeId,
    decimal LastPrice = 0m);

public sealed record UpdateSecurityRequest(
    long Id,
    string Symbol,
    string Name,
    long InvestmentTypeId,
    long IndustryTypeId,
    long SectorTypeId,
    decimal LastPrice);
```

### Interfaces & DI Extension
```csharp
namespace DividendTracker.Core.Features.Securities;

public interface ISecurityService
{
    Task<LookupCatalogDto> GetLookupsAsync(CancellationToken cancellationToken = default);
    Task<LookupItemDto> CreateLookupAsync(CreateLookupRequest request, CancellationToken cancellationToken = default);
    Task<LookupItemDto> UpdateLookupAsync(UpdateLookupRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteLookupAsync(LookupCategory category, long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SecurityDto>> GetSecuritiesAsync(CancellationToken cancellationToken = default);
    Task<SecurityDto> CreateSecurityAsync(CreateSecurityRequest request, CancellationToken cancellationToken = default);
    Task<SecurityDto> UpdateSecurityAsync(UpdateSecurityRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteSecurityAsync(long securityId, CancellationToken cancellationToken = default);
}

public static class SecurityServiceCollectionExtensions
{
    public static IServiceCollection AddSecurityFeature(this IServiceCollection services);
}
```

### Implementation Notes
- **`SecurityService`**:
  - Constructor: `(ISqliteConnectionFactory connectionFactory, TimeProvider timeProvider)`.
  - Lookup table routing: use an explicit static `switch` expression mapping `LookupCategory` to hardcoded SQL statements or constant table names (`InvestmentTypes`, `IndustryTypes`, `SectorTypes`), throwing `ArgumentOutOfRangeException` for undefined enum values. Never interpolate arbitrary strings into SQL.
  - Validation:
    - Lookup `Name`: required (non-whitespace), trimmed, max length `100` (throw `ArgumentException` if invalid).
    - Security `Symbol`: required (non-whitespace), trimmed, uppercased via `ToUpperInvariant()`, max length `20` (throw `ArgumentException` if invalid).
    - Security `Name`: required (non-whitespace), trimmed, max length `150` (throw `ArgumentException` if invalid).
    - Security `LastPrice`: must be `>= 0m` (`ArgumentOutOfRangeException.ThrowIfNegative(request.LastPrice)`).
  - Lookup operations:
    - `GetLookupsAsync`: loads all rows from `InvestmentTypes`, `IndustryTypes`, and `SectorTypes` ordered by `Name, Id`.
    - `CreateLookupAsync`: checks for case-insensitive duplicate or handles `SqliteException` unique constraint (`SQLITE_CONSTRAINT_UNIQUE` / `SqliteExtendedErrorCode == 2067` or `SqliteErrorCode == 19`) by throwing `InvalidOperationException`; returns created `LookupItemDto`.
    - `UpdateLookupAsync`: updates `Name` where `Id = @Id`; if affected rows == 0, throws `KeyNotFoundException`; duplicate name throws `InvalidOperationException`.
    - `DeleteLookupAsync`: deletes where `Id = @Id`; if referenced by any `Securities` row (`ON DELETE RESTRICT`, `SqliteException` FK error `787` / `19`), throws `InvalidOperationException("Cannot delete lookup item that is referenced by one or more securities.")`; returns `affected > 0`.
  - Security operations:
    - `GetSecuritiesAsync`: single parameterized `INNER JOIN` query across `Securities`, `InvestmentTypes`, `IndustryTypes`, and `SectorTypes` (`ORDER BY s.Symbol, s.Id`) returning `IReadOnlyList<SecurityDto>` (no N+1 queries).
    - `CreateSecurityAsync`: validates taxonomy IDs exist (throws `KeyNotFoundException` if any is missing), sets `LastPriceUpdatedUtc = request.LastPrice > 0m ? timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture) : null`, inserts row (duplicate `Symbol` throws `InvalidOperationException`), and returns the joined `SecurityDto`.
    - `UpdateSecurityAsync`: validates taxonomy IDs exist (throws `KeyNotFoundException` if missing), sets `LastPriceUpdatedUtc = request.LastPrice > 0m ? timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture) : null`, updates row where `Id = @Id` (if affected rows == 0, throws `KeyNotFoundException`; duplicate `Symbol` throws `InvalidOperationException`), and returns the joined `SecurityDto`.
    - `DeleteSecurityAsync`: deletes where `Id = @Id`; if referenced by any `Holdings` row (`ON DELETE RESTRICT`), throws `InvalidOperationException`; returns `affected > 0`.
- **`SecurityServiceCollectionExtensions`**:
  - Calls `services.TryAddSingleton(TimeProvider.System);` and `services.AddSingleton<ISecurityService, SecurityService>();`.
- **`Securities.razor` & `Securities.razor.css`**:
  - `@page "/securities"`, `@inject ISecurityService SecurityService`.
  - Loads `GetLookupsAsync()` and `GetSecuritiesAsync()` in `OnInitializedAsync()`.
  - Security Create/Edit form (`Symbol`, `Name`, `InvestmentType` select, `IndustryType` select, `SectorType` select, `LastPrice` decimal input, Save/Cancel buttons) and inline error alert (`role="alert"`) catching `ArgumentException`, `ArgumentOutOfRangeException`, `InvalidOperationException`, and `KeyNotFoundException`.
  - Responsive CSS Grid of security cards/rows displaying `Symbol`, `Name`, taxonomy labels, `LastPrice` formatted with `CultureInfo.InvariantCulture`, and Edit/Delete buttons.
  - Lookup Taxonomies management section with an inline "Add Classification" form (`LookupCategory` select + `Name` input + Add button) and grouped lists for Investment Types, Industry Types, and Sector Types with Delete buttons.
  - Pure CSS Grid & Flexbox using `--color-*`, `--space-*`, `--radius-*` from `app.css`; interactive controls have `min-height: 44px` for Android touch targets.

### SQL Schema Reference *(already created in Slice 02)*
```sql
-- InvestmentTypes(Id, Name UNIQUE)
-- IndustryTypes(Id, Name UNIQUE)
-- SectorTypes(Id, Name UNIQUE)
-- Securities(Id, Symbol UNIQUE, Name, InvestmentTypeId, IndustryTypeId, SectorTypeId, LastPrice, LastPriceUpdatedUtc)
-- FOREIGN KEY (InvestmentTypeId) REFERENCES InvestmentTypes(Id) ON DELETE RESTRICT
-- FOREIGN KEY (IndustryTypeId) REFERENCES IndustryTypes(Id) ON DELETE RESTRICT
-- FOREIGN KEY (SectorTypeId) REFERENCES SectorTypes(Id) ON DELETE RESTRICT
```

---

## 3. Behavioral Acceptance Criteria (Gherkin)

```gherkin
Feature: Securities & Lookup Taxonomies

  Scenario: Create, list, and update securities with normalized symbols and taxonomies
    Given an initialized SQLite database with default seeded lookups
    When CreateSecurityAsync is called with symbol " schd ", name "Schwab US Dividend Equity ETF", type "ETF", industry "General", sector "Financials", and LastPrice 82.45m
    Then GetSecuritiesAsync returns "SCHD" with joined taxonomy names and LastPrice 82.45m
    And LastPriceUpdatedUtc is populated
    When UpdateSecurityAsync updates the name to "Schwab Dividend ETF" and LastPrice to 84.10m
    Then GetSecuritiesAsync reflects the updated name and LastPrice 84.10m

  Scenario: Duplicate security symbol or negative price is rejected
    Given an existing security with symbol "O"
    When CreateSecurityAsync is called again with symbol "o"
    Then an InvalidOperationException is thrown
    When CreateSecurityAsync is called with symbol "MSFT" and LastPrice -1.00m
    Then an ArgumentOutOfRangeException is thrown

  Scenario: Lookup taxonomy CRUD enforces uniqueness and delete restriction
    Given an initialized SQLite database
    When CreateLookupAsync adds IndustryType "Retail REIT"
    Then GetLookupsAsync includes "Retail REIT" in IndustryTypes
    When CreateSecurityAsync references "Retail REIT"
    And DeleteLookupAsync is called for "Retail REIT"
    Then an InvalidOperationException is thrown and the lookup item remains

  Scenario: Missing taxonomy or security ID fails cleanly
    Given an initialized SQLite database
    When CreateSecurityAsync is called with non-existent SectorTypeId 9999
    Or UpdateSecurityAsync is called with non-existent Id 9999
    Then a KeyNotFoundException is thrown

  Scenario: Securities page renders securities and lookups and creates a new security via ISecurityService
    Given ISecurityService returns default lookups and an existing security "SCHD"
    When the Securities Blazor component is rendered
    Then the markup displays "SCHD", its taxonomy labels, and lookup taxonomy lists
    And submitting the new security form calls CreateSecurityAsync and refreshes the list
```

---

## 4. Implementation Subtasks

- [x] **Red**: Generate xUnit + FluentAssertions + NSubstitute + bUnit tests covering all Gherkin scenarios (`qwen2.5-coder:7b`)
- [ ] **Green**: Implement `SecurityDtos`, `ISecurityService`, `SecurityService`, DI registration, `NavMenu` link, and `/securities` Blazor UI (`qwen2.5-coder:7b`)
- [ ] **Verify**: Pass `dotnet build -warnaserror` and `dotnet test` (local fixes via `qwen3.5:9b`, max 2 retries)
- [ ] **Pass-1 Review**: Run local `qwen3.5:9b` review on `git diff --staged`
- [ ] **Pass-2 Audit**: Submit `git diff --staged` for Gemini architectural & security sign-off

