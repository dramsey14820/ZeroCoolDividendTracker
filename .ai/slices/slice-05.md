# Slice 05: Holdings & Market Buys Management

- **Status**: `In Progress`
- **Risk**: `High`
- **Owner / Stage**: Stage 2 (Gemini Mini-Spec) -> Stages 3–5 (Local Ollama) -> Stage 6 (Two-Tier Review)

---

## 1. Scope & Target Files

### In Scope
- Define `MarketBuyDto`, `HoldingDto`, request DTOs, and `IHoldingService` in `DividendTracker.Core/Features/Holdings/`.
- Implement `HoldingService` using `ISqliteConnectionFactory` and parameterized Dapper SQL against the existing `Holdings`, `MarketBuys`, `Portfolios`, `Accounts`, and `Securities` tables, computing exact `decimal` aggregates (`TotalShares`, `TotalCostBasis`, `AverageCostPerShare`, `MarketValue`).
- Register `AddHoldingFeature()` in `DividendTracker.Web` and `DividendTracker.Maui`.
- Build the `/holdings` Blazor page (`Holdings.razor` + `Holdings.razor.css`) in `DividendTracker.Shared.UI` using CSS Grid/Flexbox and `app.css` design tokens to filter holdings by portfolio, create/toggle DRIP/delete holdings, and record/delete market buy lots.
- Add xUnit + FluentAssertions integration tests for `HoldingService` (real temp SQLite DB) and bUnit + NSubstitute component tests for `Holdings.razor`.

### Out of Scope
- Dividend payout logging and automatic DRIP buy generation from payouts (Slice 06)
- Portfolio-wide yield, income, and allocation analytics dashboard (Slice 07)
- Live quote fetching or historical imports (Slice 08)
- Modal confirmation dialogs or JS interop (keep actions simple and pure Blazor)

### Target Files
- **Create**:
  - `src/DividendTracker.Core/Features/Holdings/HoldingDtos.cs`
  - `src/DividendTracker.Core/Features/Holdings/IHoldingService.cs`
  - `src/DividendTracker.Core/Features/Holdings/HoldingService.cs`
  - `src/DividendTracker.Core/Features/Holdings/HoldingServiceCollectionExtensions.cs`
  - `src/DividendTracker.Shared.UI/Pages/Holdings.razor`
  - `src/DividendTracker.Shared.UI/Pages/Holdings.razor.css`
  - `tests/DividendTracker.Tests/Features/Holdings/HoldingServiceTests.cs`
  - `tests/DividendTracker.Tests/Features/Holdings/HoldingsPageTests.cs`
- **Modify**:
  - `src/DividendTracker.Shared.UI/_Imports.razor` *(add `@using DividendTracker.Core.Features.Holdings`)*
  - `src/DividendTracker.Web/Program.cs` *(register `builder.Services.AddHoldingFeature();`; preserve explicit `Program.Main`)*
  - `src/DividendTracker.Maui/MauiProgram.cs` *(register `builder.Services.AddHoldingFeature();`)*

*(Note: `NavMenu.razor` already contains the `/holdings` link from Slice 01.)*

---

## 2. Data Contracts / Interfaces / SQL Schema

### Required Namespaces & Packages
- Existing packages only (no new NuGet packages required).
- Key namespaces:
  - `Dapper`
  - `Microsoft.Data.Sqlite`
  - `System.Globalization`
  - `Microsoft.Extensions.DependencyInjection`
  - `DividendTracker.Core.Infrastructure.Persistence`
  - `DividendTracker.Core.Features.Portfolios` *(for `IPortfolioService` in `Holdings.razor`)*
  - `DividendTracker.Core.Features.Securities` *(for `ISecurityService` in `Holdings.razor`)*
  - `Bunit`, `FluentAssertions`, `NSubstitute`, `Xunit`
- Analyzer notes (`-warnaserror`, `AnalysisLevel=latest-recommended`):
  - Format and parse `DateOnly` with `CultureInfo.InvariantCulture` using `"yyyy-MM-dd"` (`ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)` and `DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture)`).
  - Do **not** rely on Dapper mapping SQLite `TEXT` directly to `DateOnly`; use an internal row record with `string PurchaseDate` and convert to `DateOnly` in C#.
  - Use `ArgumentNullException.ThrowIfNull(...)`, `ArgumentOutOfRangeException.ThrowIfNegative(...)`, and `ArgumentOutOfRangeException.ThrowIfNegativeOrZero(...)`.
  - Pass `CommandDefinition` with `cancellationToken` to all Dapper calls (`new CommandDefinition(sql, param, cancellationToken: cancellationToken)`).
  - In SQLite integration tests using temp files, call `SqliteConnection.ClearAllPools()` before deleting the temp file in `Dispose()`.

### DTOs & Contracts
```csharp
namespace DividendTracker.Core.Features.Holdings;

public sealed record MarketBuyDto(
    long Id,
    long HoldingId,
    decimal NumberOfShares,
    decimal PricePerShare,
    DateOnly PurchaseDate,
    bool IsDrip,
    long? DividendPaymentId)
{
    public decimal TotalCost => NumberOfShares * PricePerShare;
}

public sealed record HoldingDto(
    long Id,
    long PortfolioId,
    string PortfolioName,
    long AccountId,
    string AccountName,
    long SecurityId,
    string SecuritySymbol,
    string SecurityName,
    decimal LastPrice,
    bool IsDripEnabled,
    decimal TotalShares,
    decimal TotalCostBasis,
    decimal AverageCostPerShare,
    decimal MarketValue,
    IReadOnlyList<MarketBuyDto> MarketBuys);

public sealed record CreateHoldingRequest(
    long PortfolioId,
    long AccountId,
    long SecurityId,
    bool IsDripEnabled);

public sealed record UpdateHoldingRequest(
    long Id,
    bool IsDripEnabled);

public sealed record CreateMarketBuyRequest(
    long HoldingId,
    decimal NumberOfShares,
    decimal PricePerShare,
    DateOnly PurchaseDate,
    bool IsDrip = false);

public sealed record UpdateMarketBuyRequest(
    long Id,
    decimal NumberOfShares,
    decimal PricePerShare,
    DateOnly PurchaseDate,
    bool IsDrip);
```

### Interfaces & DI Extension
```csharp
namespace DividendTracker.Core.Features.Holdings;

public interface IHoldingService
{
    Task<IReadOnlyList<HoldingDto>> GetHoldingsAsync(long? portfolioId = null, CancellationToken cancellationToken = default);
    Task<HoldingDto> CreateHoldingAsync(CreateHoldingRequest request, CancellationToken cancellationToken = default);
    Task<HoldingDto> UpdateHoldingAsync(UpdateHoldingRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteHoldingAsync(long holdingId, CancellationToken cancellationToken = default);

    Task<MarketBuyDto> CreateMarketBuyAsync(CreateMarketBuyRequest request, CancellationToken cancellationToken = default);
    Task<MarketBuyDto> UpdateMarketBuyAsync(UpdateMarketBuyRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteMarketBuyAsync(long marketBuyId, CancellationToken cancellationToken = default);
}

public static class HoldingServiceCollectionExtensions
{
    public static IServiceCollection AddHoldingFeature(this IServiceCollection services);
}
```

### Implementation Notes
- **`HoldingService`**:
  - Constructor: `(ISqliteConnectionFactory connectionFactory)`.
  - Validation:
    - `CreateHoldingRequest` / `UpdateHoldingRequest` / `CreateMarketBuyRequest` / `UpdateMarketBuyRequest`: `ArgumentNullException.ThrowIfNull(request)`.
    - `NumberOfShares`: must be `> 0m` (`ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.NumberOfShares)`).
    - `PricePerShare`: must be `>= 0m` (`ArgumentOutOfRangeException.ThrowIfNegative(request.PricePerShare)`).
  - Holding operations:
    - `GetHoldingsAsync(long? portfolioId = null)`: runs two parameterized queries (no N+1):
      1. `INNER JOIN` `Holdings` with `Portfolios`, `Accounts`, and `Securities` (optional `WHERE (@PortfolioId IS NULL OR h.PortfolioId = @PortfolioId)`), ordered by `p.Name, a.Name, s.Symbol, h.Id`.
      2. `MarketBuys` joined to `Holdings` with the same optional `@PortfolioId` filter, ordered by `mb.PurchaseDate DESC, mb.Id DESC`.
      - Groups `MarketBuys` by `HoldingId` and computes:
        - `TotalShares = buys.Sum(b => b.NumberOfShares)`
        - `TotalCostBasis = buys.Sum(b => b.TotalCost)`
        - `AverageCostPerShare = TotalShares > 0m ? TotalCostBasis / TotalShares : 0m`
        - `MarketValue = TotalShares * LastPrice`
    - `CreateHoldingAsync`: verifies `AccountId` exists and belongs to `PortfolioId` (`SELECT COUNT(1) FROM Accounts WHERE Id = @AccountId AND PortfolioId = @PortfolioId`; throws `KeyNotFoundException` if 0) and verifies `SecurityId` exists (`SELECT COUNT(1) FROM Securities WHERE Id = @SecurityId`; throws `KeyNotFoundException` if 0). Inserts into `Holdings` (catches SQLite unique constraint `SqliteErrorCode == 19` / `SqliteExtendedErrorCode == 2067` and throws `InvalidOperationException("A holding for this portfolio, account, and security already exists.")`), and returns the hydrated `HoldingDto`.
    - `UpdateHoldingAsync`: updates `IsDripEnabled` where `Id = @Id`; if affected rows == 0, throws `KeyNotFoundException`; returns the hydrated `HoldingDto` with its existing `MarketBuys` and computed metrics.
    - `DeleteHoldingAsync`: `DELETE FROM Holdings WHERE Id = @Id`; returns `affected > 0` (SQLite `ON DELETE CASCADE` removes child `MarketBuys`).
  - MarketBuy operations:
    - `CreateMarketBuyAsync`: validates `NumberOfShares > 0m` and `PricePerShare >= 0m`, verifies parent `HoldingId` exists (throws `KeyNotFoundException` if 0), inserts row with `PurchaseDate` formatted as `"yyyy-MM-dd"` and `IsDrip` as `0`/`1`, retrieves `last_insert_rowid()`, and returns `MarketBuyDto`.
    - `UpdateMarketBuyAsync`: validates `NumberOfShares > 0m` and `PricePerShare >= 0m`, updates `NumberOfShares`, `PricePerShare`, `PurchaseDate`, `IsDrip` where `Id = @Id`; if affected rows == 0, throws `KeyNotFoundException`; returns updated `MarketBuyDto`.
    - `DeleteMarketBuyAsync`: `DELETE FROM MarketBuys WHERE Id = @Id`; returns `affected > 0`.
- **`HoldingServiceCollectionExtensions`**:
  - Registers `services.AddSingleton<IHoldingService, HoldingService>();`.
- **`Holdings.razor` & `Holdings.razor.css`**:
  - `@page "/holdings"`, `@inject IHoldingService HoldingService`, `@inject IPortfolioService PortfolioService`, `@inject ISecurityService SecurityService`.
  - Loads portfolios, securities, and holdings in `OnInitializedAsync()`.
  - Portfolio filter dropdown (`All Portfolios` + each portfolio) that reloads holdings via `HoldingService.GetHoldingsAsync(filterPortfolioId)`.
  - "Add Holding" form (`Portfolio` select -> filtered `Account` select -> `Security` select -> `DRIP Enabled` checkbox -> Add button) with inline error alert (`role="alert"`) catching `ArgumentException`, `ArgumentOutOfRangeException`, `InvalidOperationException`, and `KeyNotFoundException`.
  - Responsive CSS Grid of holding cards displaying `SecuritySymbol`, `SecurityName`, `PortfolioName` / `AccountName`, DRIP toggle button, Delete holding button, computed metrics (`TotalShares`, `AverageCostPerShare`, `TotalCostBasis`, `MarketValue` formatted with `CultureInfo.InvariantCulture`), list of `MarketBuys` with Delete buttons, and an inline "Add Market Buy" form per holding (`PurchaseDate`, `NumberOfShares`, `PricePerShare`, `IsDrip` checkbox, Add Buy button).
  - Pure CSS Grid & Flexbox using `--color-*`, `--space-*`, `--radius-*` from `app.css`; interactive controls have `min-height: 44px` for Android touch targets.

### SQL Schema Reference *(already created in Slice 02)*
```sql
-- Holdings(Id, PortfolioId, AccountId, SecurityId, IsDripEnabled)
-- UNIQUE (PortfolioId, AccountId, SecurityId)
-- FOREIGN KEY (PortfolioId) REFERENCES Portfolios(Id) ON DELETE CASCADE
-- FOREIGN KEY (AccountId) REFERENCES Accounts(Id) ON DELETE CASCADE
-- FOREIGN KEY (SecurityId) REFERENCES Securities(Id) ON DELETE RESTRICT

-- MarketBuys(Id, HoldingId, NumberOfShares, PricePerShare, PurchaseDate, IsDrip, DividendPaymentId)
-- FOREIGN KEY (HoldingId) REFERENCES Holdings(Id) ON DELETE CASCADE
-- FOREIGN KEY (DividendPaymentId) REFERENCES DividendPayments(Id) ON DELETE SET NULL
```

---

## 3. Behavioral Acceptance Criteria (Gherkin)

```gherkin
Feature: Holdings & Market Buys Management

  Scenario: Create a holding, record fractional market buys, and compute position totals
    Given an initialized SQLite database with portfolio "Core Income", account "Fidelity Roth", and security "SCHD" priced at 80.00m
    When CreateHoldingAsync creates a holding for that portfolio, account, and security with IsDripEnabled true
    And CreateMarketBuyAsync records 10.5m shares at 75.00m on 2026-01-15 and 4.5m shares at 78.00m on 2026-02-15
    Then GetHoldingsAsync returns the holding with TotalShares 15.0m, TotalCostBasis 1138.50m, AverageCostPerShare 75.90m, and MarketValue 1200.00m
    And MarketBuys are ordered by PurchaseDate descending

  Scenario: Filter holdings by portfolio and update DRIP status and market buys
    Given two portfolios each with one holding and one market buy
    When GetHoldingsAsync is called with the first portfolio's Id
    Then only the first portfolio's holding is returned
    When UpdateHoldingAsync sets IsDripEnabled to false and UpdateMarketBuyAsync changes NumberOfShares to 20.0m
    Then GetHoldingsAsync reflects the updated DRIP flag and recalculated TotalShares and TotalCostBasis

  Scenario: Duplicate holding or mismatched portfolio-account is rejected
    Given portfolio A with account A1, portfolio B with account B1, and security "O"
    When CreateHoldingAsync is called with portfolio A and account B1
    Then a KeyNotFoundException is thrown
    When CreateHoldingAsync is called twice for portfolio A, account A1, and security "O"
    Then the second call throws an InvalidOperationException

  Scenario: Invalid market buy quantities or prices are rejected
    Given an existing holding
    When CreateMarketBuyAsync is called with NumberOfShares 0m
    Or CreateMarketBuyAsync is called with PricePerShare -5.00m
    Then an ArgumentOutOfRangeException is thrown and no buy is recorded

  Scenario: Deleting a holding cascades to its market buys
    Given an existing holding with two market buys
    When DeleteHoldingAsync is called for that holding
    Then DeleteHoldingAsync returns true
    And GetHoldingsAsync no longer contains the holding or its buys

  Scenario: Holdings page renders holdings and submits a new market buy via IHoldingService
    Given IHoldingService returns a holding for "SCHD" with 10m shares and MarketValue 800.00m
    And IPortfolioService and ISecurityService return available portfolios and securities
    When the Holdings Blazor component is rendered
    Then the markup displays "SCHD", "Core Income", "Fidelity Roth", and the formatted market value
    And submitting the inline market buy form calls CreateMarketBuyAsync and refreshes holdings
```

---

## 4. Implementation Subtasks

- [x] **Red**: Generate xUnit + FluentAssertions + NSubstitute + bUnit tests covering all Gherkin scenarios (`qwen2.5-coder:7b`)
- [ ] **Green**: Implement `HoldingDtos`, `IHoldingService`, `HoldingService`, DI registration, and `/holdings` Blazor UI (`qwen2.5-coder:7b`)
- [ ] **Verify**: Pass `dotnet build -warnaserror` and `dotnet test` (local fixes via `qwen3.5:9b`, max 2 retries)
- [ ] **Pass-1 Review**: Run local `qwen3.5:9b` review on `git diff --staged`
- [ ] **Pass-2 Audit**: Submit `git diff --staged` for Gemini architectural & security sign-off

