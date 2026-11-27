# Slice 06: Dividend Payouts & DRIP Tracking

- **Status**: `Ready`
- **Risk**: `High`
- **Owner / Stage**: Stage 2 (Gemini Mini-Spec) -> Stages 3–5 (Local Ollama) -> Stage 6 (Two-Tier Review)

---

## 1. Scope & Target Files

### In Scope
- Define `DividendPaymentDto`, `RecordDividendRequest`, `UpdateDividendRequest`, and `IDividendService` in `DividendTracker.Core/Features/Dividends/`.
- Implement `DividendService` using `ISqliteConnectionFactory` and parameterized Dapper SQL against `DividendPayments`, `MarketBuys`, `Holdings`, `Portfolios`, `Accounts`, and `Securities`.
- Support atomic **DRIP (Dividend Reinvestment Plan)** tracking inside explicit SQLite transactions:
  - Recording a reinvested dividend (`IsReinvested = true`) automatically inserts a linked DRIP row in `MarketBuys` (`IsDrip = 1`, `DividendPaymentId = dividend.Id`), updating the holding's total shares and cost basis.
  - Updating a dividend synchronizes its linked DRIP `MarketBuy` (create, update, or delete when toggling `IsReinvested`).
  - Deleting a dividend removes its linked DRIP `MarketBuy` and the `DividendPayment` atomically so no orphaned DRIP buys remain.
- Register `AddDividendFeature()` in `DividendTracker.Web` and `DividendTracker.Maui`.
- Build the `/dividends` Blazor page (`Dividends.razor` + `Dividends.razor.css`) in `DividendTracker.Shared.UI` using pure CSS Grid/Flexbox and `app.css` design tokens to filter dividends by portfolio/holding, record cash or DRIP dividend payouts, and delete payouts.
- Add xUnit + FluentAssertions integration tests for `DividendService` (real temp SQLite DB) and bUnit + NSubstitute component tests for `Dividends.razor`.

### Out of Scope
- Automated historical dividend import from external market data providers (Slice 08)
- Portfolio-wide yield, Yield on Cost (YoC), annual/monthly income projections, and allocation analytics dashboard (Slice 07)
- JSON backup/restore export & import (Slice 09)
- Modal confirmation dialogs or JS interop (keep actions simple and pure Blazor)

### Target Files
- **Create**:
  - `src/DividendTracker.Core/Features/Dividends/DividendDtos.cs`
  - `src/DividendTracker.Core/Features/Dividends/IDividendService.cs`
  - `src/DividendTracker.Core/Features/Dividends/DividendService.cs`
  - `src/DividendTracker.Core/Features/Dividends/DividendServiceCollectionExtensions.cs`
  - `src/DividendTracker.Shared.UI/Pages/Dividends.razor`
  - `src/DividendTracker.Shared.UI/Pages/Dividends.razor.css`
  - `tests/DividendTracker.Tests/Features/Dividends/DividendServiceTests.cs`
  - `tests/DividendTracker.Tests/Features/Dividends/DividendsPageTests.cs`
- **Modify**:
  - `src/DividendTracker.Shared.UI/_Imports.razor` *(add `@using DividendTracker.Core.Features.Dividends`)*
  - `src/DividendTracker.Web/Program.cs` *(register `builder.Services.AddDividendFeature();`; preserve explicit `Program.Main`)*
  - `src/DividendTracker.Maui/MauiProgram.cs` *(register `builder.Services.AddDividendFeature();`)*

*(Note: `NavMenu.razor` already contains the `/dividends` link from Slice 01.)*

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
  - `DividendTracker.Core.Features.Holdings` *(for `IHoldingService` in `Dividends.razor` and integration tests)*
  - `DividendTracker.Core.Features.Portfolios` *(for `IPortfolioService` in `Dividends.razor` and integration tests)*
  - `DividendTracker.Core.Features.Securities` *(for `ISecurityService` in integration tests)*
  - `Bunit`, `FluentAssertions`, `NSubstitute`, `Xunit`
- Analyzer & Dapper notes (`-warnaserror`, `AnalysisLevel=latest-recommended`):
  - Format and parse `DateOnly` with `CultureInfo.InvariantCulture` using `"yyyy-MM-dd"` (`ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)` and `DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture)`).
  - Do **not** rely on Dapper mapping SQLite `TEXT` directly to `DateOnly`, `DateOnly?`, or `decimal?`; use an internal row record with `string PaymentDate`, `string? ExDividendDate`, `string? DripShares`, `string? DripPricePerShare` and parse them in C# with `CultureInfo.InvariantCulture`.
  - Use `ArgumentNullException.ThrowIfNull(...)` and `ArgumentOutOfRangeException.ThrowIfNegativeOrZero(...)`.
  - Pass `CommandDefinition` with `transaction` (when inside a transaction) and `cancellationToken` to all Dapper calls.
  - In SQLite integration tests using temp files, call `SqliteConnection.ClearAllPools()` before deleting the temp file in `Dispose()`.

### DTOs & Contracts
```csharp
namespace DividendTracker.Core.Features.Dividends;

public sealed record DividendPaymentDto(
    long Id,
    long HoldingId,
    long PortfolioId,
    string PortfolioName,
    long AccountId,
    string AccountName,
    long SecurityId,
    string SecuritySymbol,
    string SecurityName,
    DateOnly PaymentDate,
    DateOnly? ExDividendDate,
    decimal Amount,
    bool IsReinvested,
    string Notes,
    long? DripMarketBuyId,
    decimal? DripShares,
    decimal? DripPricePerShare);

public sealed record RecordDividendRequest(
    long HoldingId,
    DateOnly PaymentDate,
    DateOnly? ExDividendDate,
    decimal Amount,
    bool IsReinvested,
    string Notes = "",
    decimal? DripShares = null,
    decimal? DripPricePerShare = null);

public sealed record UpdateDividendRequest(
    long Id,
    DateOnly PaymentDate,
    DateOnly? ExDividendDate,
    decimal Amount,
    bool IsReinvested,
    string Notes = "",
    decimal? DripShares = null,
    decimal? DripPricePerShare = null);
```

### Interfaces & DI Extension
```csharp
namespace DividendTracker.Core.Features.Dividends;

public interface IDividendService
{
    Task<IReadOnlyList<DividendPaymentDto>> GetDividendsAsync(
        long? portfolioId = null,
        long? holdingId = null,
        CancellationToken cancellationToken = default);

    Task<DividendPaymentDto> RecordDividendAsync(
        RecordDividendRequest request,
        CancellationToken cancellationToken = default);

    Task<DividendPaymentDto> UpdateDividendAsync(
        UpdateDividendRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteDividendAsync(
        long dividendPaymentId,
        CancellationToken cancellationToken = default);
}

public static class DividendServiceCollectionExtensions
{
    public static IServiceCollection AddDividendFeature(this IServiceCollection services);
}
```

### Implementation Notes
- **`DividendService`**:
  - Constructor: `(ISqliteConnectionFactory connectionFactory)`.
  - Validation helper for `RecordDividendRequest` and `UpdateDividendRequest`:
    - `ArgumentNullException.ThrowIfNull(request)`.
    - `ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Amount)`.
    - `Notes = (request.Notes ?? string.Empty).Trim()`.
    - When `request.IsReinvested` is `true`:
      - If `request.DripPricePerShare is null or <= 0m`, throw `ArgumentOutOfRangeException(nameof(request), "DRIP price per share must be greater than zero when IsReinvested is true.")`.
      - If `request.DripShares is <= 0m`, throw `ArgumentOutOfRangeException(nameof(request), "DRIP shares must be greater than zero when provided.")`.
      - Effective DRIP shares: `request.DripShares ?? Math.Round(request.Amount / request.DripPricePerShare.Value, 6, MidpointRounding.AwayFromZero)`.
    - When `request.IsReinvested` is `false`: ignore `DripShares` / `DripPricePerShare` (no DRIP `MarketBuy` is created).
  - **`GetDividendsAsync(long? portfolioId = null, long? holdingId = null)`**:
    - Single parameterized query (no N+1):
      - `FROM DividendPayments dp`
      - `INNER JOIN Holdings h ON h.Id = dp.HoldingId`
      - `INNER JOIN Portfolios p ON p.Id = h.PortfolioId`
      - `INNER JOIN Accounts a ON a.Id = h.AccountId`
      - `INNER JOIN Securities s ON s.Id = h.SecurityId`
      - `LEFT JOIN MarketBuys mb ON mb.DividendPaymentId = dp.Id`
      - `WHERE (@PortfolioId IS NULL OR h.PortfolioId = @PortfolioId) AND (@HoldingId IS NULL OR dp.HoldingId = @HoldingId)`
      - `ORDER BY dp.PaymentDate DESC, dp.Id DESC`
  - **`RecordDividendAsync`**:
    - Opens connection and begins an explicit transaction (`await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);`).
    - Verifies `HoldingId` exists (`SELECT COUNT(1) FROM Holdings WHERE Id = @HoldingId`; throws `KeyNotFoundException` if 0).
    - Inserts into `DividendPayments` (`PaymentDate` as `"yyyy-MM-dd"`, `ExDividendDate` as `"yyyy-MM-dd"` or `NULL`, `Amount`, `IsReinvested` as `0`/`1`, `Notes`) and retrieves `last_insert_rowid()`.
    - If `IsReinvested` is `true`, inserts a linked row into `MarketBuys` (`HoldingId`, `NumberOfShares = effectiveDripShares`, `PricePerShare = request.DripPricePerShare.Value`, `PurchaseDate = PaymentDate`, `IsDrip = 1`, `DividendPaymentId = dividendId`).
    - Queries the hydrated `DividendPaymentDto` by `dp.Id = @Id` within the transaction, commits, and returns it.
  - **`UpdateDividendAsync`**:
    - Opens connection and begins an explicit transaction.
    - Looks up `HoldingId` from `SELECT HoldingId FROM DividendPayments WHERE Id = @Id`; if null, throws `KeyNotFoundException`.
    - Updates `DividendPayments` (`PaymentDate`, `ExDividendDate`, `Amount`, `IsReinvested`, `Notes`) where `Id = @Id`.
    - Synchronizes linked DRIP `MarketBuys` (`WHERE DividendPaymentId = @Id`):
      - If `IsReinvested` is `true`: checks for an existing linked `MarketBuys` row (`SELECT Id FROM MarketBuys WHERE DividendPaymentId = @Id`). If found, updates `NumberOfShares`, `PricePerShare`, `PurchaseDate`, `IsDrip = 1`; otherwise inserts a new linked `MarketBuys` row with `HoldingId`, `IsDrip = 1`, and `DividendPaymentId = request.Id`.
      - If `IsReinvested` is `false`: deletes any linked DRIP buy (`DELETE FROM MarketBuys WHERE DividendPaymentId = @Id`).
    - Queries the hydrated `DividendPaymentDto`, commits, and returns it.
  - **`DeleteDividendAsync(long dividendPaymentId)`**:
    - Opens connection and begins an explicit transaction.
    - Deletes any linked DRIP buy (`DELETE FROM MarketBuys WHERE DividendPaymentId = @Id`) so no orphaned DRIP lot remains despite `ON DELETE SET NULL`.
    - Deletes the dividend (`DELETE FROM DividendPayments WHERE Id = @Id`).
    - Commits and returns `affected > 0`.
- **`DividendServiceCollectionExtensions`**:
  - Registers `services.AddSingleton<IDividendService, DividendService>();`.
- **`Dividends.razor` & `Dividends.razor.css`**:
  - `@page "/dividends"`, `@inject IDividendService DividendService`, `@inject IHoldingService HoldingService`, `@inject IPortfolioService PortfolioService`.
  - Loads portfolios, holdings, and dividends in `OnInitializedAsync()`.
  - Filter bar with Portfolio dropdown (`All Portfolios` + portfolios) and Holding dropdown (`All Holdings` + holdings in selected portfolio) that reloads dividends via `DividendService.GetDividendsAsync(filterPortfolioId, filterHoldingId)`.
  - Summary metrics strip showing total dividends received (`Sum(d => d.Amount)`), payout count, and DRIP reinvested count (formatted with `CultureInfo.InvariantCulture`).
  - "Record Dividend Payout" form (`Holding` select -> defaults `IsReinvested` to selected holding's `IsDripEnabled` and `DripPricePerShare` to `LastPrice` if `> 0m`; `PaymentDate`, optional `ExDividendDate`, `Amount`, `Notes`, `Reinvested (DRIP)` checkbox, and conditional `DRIP Price / Share` + optional `DRIP Shares` inputs) with inline error alert (`role="alert"`) catching `ArgumentException`, `ArgumentOutOfRangeException`, `InvalidOperationException`, and `KeyNotFoundException`.
  - Responsive CSS Grid of dividend payout cards showing `SecuritySymbol`, `SecurityName`, `PortfolioName` / `AccountName`, `PaymentDate`, optional `ExDividendDate`, `Amount`, DRIP badge with `DripShares` @ `DripPricePerShare` when reinvested, `Notes`, and a `Delete` button.
  - Pure CSS Grid & Flexbox using `--color-*`, `--space-*`, `--radius-*` from `app.css`; interactive controls have `min-height: 44px` for Android touch targets.

### SQL Schema Reference *(already created in Slice 02)*
```sql
-- DividendPayments(Id, HoldingId, PaymentDate, ExDividendDate, Amount, IsReinvested, Notes)
-- FOREIGN KEY (HoldingId) REFERENCES Holdings(Id) ON DELETE CASCADE

-- MarketBuys(Id, HoldingId, NumberOfShares, PricePerShare, PurchaseDate, IsDrip, DividendPaymentId)
-- FOREIGN KEY (HoldingId) REFERENCES Holdings(Id) ON DELETE CASCADE
-- FOREIGN KEY (DividendPaymentId) REFERENCES DividendPayments(Id) ON DELETE SET NULL
```

---

## 3. Behavioral Acceptance Criteria (Gherkin)

```gherkin
Feature: Dividend Payouts & DRIP Tracking

  Scenario: Record a cash dividend payout and filter dividends by portfolio and holding
    Given an initialized SQLite database with two portfolios each having a holding
    When RecordDividendAsync logs a 25.50m cash dividend (IsReinvested false) on 2026-03-15 with ExDividendDate 2026-03-01 and Notes "Q1 payout" for the first holding
    And RecordDividendAsync logs a 12.00m cash dividend on 2026-03-20 for the second holding
    Then GetDividendsAsync with no filter returns both payouts ordered by PaymentDate descending
    And GetDividendsAsync filtered by the first portfolio or first holding returns only the 25.50m payout with DripMarketBuyId null

  Scenario: Record a DRIP dividend payout and automatically create a linked DRIP market buy
    Given an existing holding for "SCHD" with 10m shares purchased at 75.00m
    When RecordDividendAsync logs a 40.00m dividend on 2026-03-25 with IsReinvested true, DripPricePerShare 80.00m, and DripShares null
    Then the returned DividendPaymentDto has IsReinvested true, DripMarketBuyId populated, DripShares 0.5m, and DripPricePerShare 80.00m
    And HoldingService.GetHoldingsAsync reflects TotalShares 10.5m and a MarketBuy with IsDrip true and DividendPaymentId matching the dividend

  Scenario: Update a dividend payout to toggle DRIP on and off
    Given an existing cash dividend payout of 30.00m with IsReinvested false
    When UpdateDividendAsync sets IsReinvested true with DripShares 0.4m and DripPricePerShare 75.00m
    Then the updated dividend has DripMarketBuyId populated and HoldingService shows the linked DRIP MarketBuy
    When UpdateDividendAsync subsequently sets IsReinvested false
    Then DripMarketBuyId becomes null and the linked DRIP MarketBuy is removed from the holding

  Scenario: Invalid dividend amounts, invalid DRIP inputs, or missing holdings are rejected
    Given an existing holding
    When RecordDividendAsync is called with Amount 0m
    Or RecordDividendAsync is called with IsReinvested true and DripPricePerShare null or 0m
    Or RecordDividendAsync is called with IsReinvested true and DripShares -1m
    Then an ArgumentOutOfRangeException is thrown and no dividend or market buy is recorded
    When RecordDividendAsync is called with a non-existent HoldingId
    Then a KeyNotFoundException is thrown

  Scenario: Deleting a DRIP dividend payout removes both the dividend and its linked DRIP market buy
    Given an existing DRIP dividend payout linked to a DRIP MarketBuy
    When DeleteDividendAsync is called for that dividend
    Then DeleteDividendAsync returns true
    And GetDividendsAsync no longer contains the dividend
    And HoldingService.GetHoldingsAsync no longer contains the linked DRIP MarketBuy

  Scenario: Dividends page renders payouts and submits a new DRIP payout via IDividendService
    Given IDividendService returns a DRIP payout for "SCHD" of 40.00m
    And IPortfolioService and IHoldingService return available portfolios and holdings
    When the Dividends Blazor component is rendered
    Then the markup displays "SCHD", the formatted payout amount, and DRIP details
    And submitting the Record Dividend form calls RecordDividendAsync and refreshes dividends
```

---

## 4. Implementation Subtasks

- [ ] **Red**: Generate xUnit + FluentAssertions + NSubstitute + bUnit tests covering all Gherkin scenarios (`qwen2.5-coder:7b`)
- [ ] **Green**: Implement `DividendDtos`, `IDividendService`, `DividendService`, DI registration, and `/dividends` Blazor UI (`qwen2.5-coder:7b`)
- [ ] **Verify**: Pass `dotnet build -warnaserror` and `dotnet test` (local fixes via `qwen3.5:9b`, max 2 retries)
- [ ] **Pass-1 Review**: Run local `qwen3.5:9b` review on `git diff --staged`
- [ ] **Pass-2 Audit**: Submit `git diff --staged` for Gemini architectural & security sign-off

