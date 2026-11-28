# Slice 07: Yield & Income Analytics Dashboard

- **Status**: `Ready`
- **Risk**: `High`
- **Owner / Stage**: Stage 2 (Gemini Mini-Spec) -> Stages 3–5 (Local Ollama) -> Stage 6 (Two-Tier Review)

---

## 1. Scope & Target Files

### In Scope
- Define analytics DTOs (`DashboardSummaryDto`, `HoldingAnalyticsDto`, `AllocationItemDto`, `MonthlyIncomePointDto`, `DashboardAnalyticsDto`) and `IAnalyticsService` in `DividendTracker.Core/Features/Analytics/`.
- Implement `AnalyticsService` using `ISqliteConnectionFactory` and parameterized Dapper SQL against `Holdings`, `Portfolios`, `Accounts`, `Securities`, `InvestmentTypes`, `IndustryTypes`, `SectorTypes`, `MarketBuys`, and `DividendPayments`.
- Compute exact `decimal` metrics in C# (avoiding SQLite floating-point `SUM` on `TEXT` decimal columns):
  - **Portfolio & Per-Holding**: Total Market Value, Total Cost Basis, Unrealized Gain/Loss (amount & %), Trailing 12-Month (TTM) / Projected Annual Dividend Income, Projected Monthly Income (`ProjectedAnnualIncome / 12m`), Lifetime Dividend Income, **Current Yield %**, and **Yield on Cost (YoC) %**.
  - **Allocation Breakdowns**: By **Sector**, **Industry**, and **Investment Type** (Market Value, Cost Basis, Allocation %, TTM Income, Holding Count).
  - **Monthly Dividend Income History**: Chronological `(Year, Month)` buckets (`YYYY-MM`) showing Total, Cash, and Reinvested (DRIP) amounts plus Payout Count.
  - **Portfolio Filtering**: Optional `portfolioId` filter (all portfolios vs. a single actual or modelled portfolio) and optional `asOfDate` anchor for deterministic TTM calculations.
- Register `AddAnalyticsFeature()` in `DividendTracker.Web` and `DividendTracker.Maui`.
- Upgrade the `/` Dashboard page (`Home.razor` + `Home.razor.css`) in `DividendTracker.Shared.UI` using pure CSS Grid/Flexbox and `app.css` design tokens to display the portfolio filter, KPI summary cards, Sector/Industry/Investment Type allocation bars, monthly income history, and per-holding performance cards while preserving app metadata display.
- Add xUnit + FluentAssertions integration tests for `AnalyticsService` (real temp SQLite DB), bUnit + NSubstitute component tests for the Dashboard (`DashboardPageTests.cs`), and update `HomeComponentTests.cs` to register mocked `IAnalyticsService` and `IPortfolioService`.

### Out of Scope
- External live quote or dividend history fetching (Slice 08)
- Forward-looking declared dividend-rate overrides (v1 uses TTM actual payouts as the projected annual income run-rate)
- JSON export/import backup & restore (Slice 09)
- JavaScript charting libraries (use pure CSS Grid/Flexbox visual bars)

### Target Files
- **Create**:
  - `src/DividendTracker.Core/Features/Analytics/AnalyticsDtos.cs`
  - `src/DividendTracker.Core/Features/Analytics/IAnalyticsService.cs`
  - `src/DividendTracker.Core/Features/Analytics/AnalyticsService.cs`
  - `src/DividendTracker.Core/Features/Analytics/AnalyticsServiceCollectionExtensions.cs`
  - `tests/DividendTracker.Tests/Features/Analytics/AnalyticsServiceTests.cs`
  - `tests/DividendTracker.Tests/Features/Analytics/DashboardPageTests.cs`
- **Modify**:
  - `src/DividendTracker.Shared.UI/Pages/Home.razor`
  - `src/DividendTracker.Shared.UI/Pages/Home.razor.css`
  - `src/DividendTracker.Shared.UI/_Imports.razor` *(add `@using DividendTracker.Core.Features.Analytics`)*
  - `src/DividendTracker.Web/Program.cs` *(register `builder.Services.AddAnalyticsFeature();`; preserve explicit `Program.Main`)*
  - `src/DividendTracker.Maui/MauiProgram.cs` *(register `builder.Services.AddAnalyticsFeature();`)*
  - `tests/DividendTracker.Tests/Features/Shell/HomeComponentTests.cs` *(register `IAnalyticsService` and `IPortfolioService` substitutes)*

*(Note: `NavMenu.razor` already links `Dashboard` to `""` / `/`.)*

---

## 2. Data Contracts / Interfaces / SQL Schema

### Required Namespaces & Packages
- Existing packages only (no new NuGet packages required).
- Key namespaces:
  - `Dapper`
  - `Microsoft.Data.Sqlite`
  - `System.Globalization`
  - `Microsoft.Extensions.DependencyInjection`
  - `DividendTracker.Core.Common`
  - `DividendTracker.Core.Infrastructure.Persistence`
  - `DividendTracker.Core.Features.Portfolios`
  - `DividendTracker.Core.Features.Securities`
  - `DividendTracker.Core.Features.Holdings`
  - `DividendTracker.Core.Features.Dividends`
  - `Bunit`, `FluentAssertions`, `NSubstitute`, `Xunit`
- Analyzer & Dapper notes (`-warnaserror`, `AnalysisLevel=latest-recommended`):
  - Parse `PaymentDate` strings with `DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture)`.
  - Do **not** compute `SUM(...)` in SQLite over `TEXT` decimal columns; query raw rows (`NumberOfShares`, `PricePerShare`, `LastPrice`, `Amount`) mapped via `DecimalTextTypeHandler` and aggregate in C# with `decimal` math.
  - Pass `CommandDefinition` with `cancellationToken` to all Dapper calls.
  - Format currency/percent values in Razor with `CultureInfo.InvariantCulture`.
  - In SQLite integration tests using temp files, call `SqliteConnection.ClearAllPools()` before deleting the temp file in `Dispose()`.

### DTOs & Contracts
```csharp
namespace DividendTracker.Core.Features.Analytics;

public sealed record DashboardSummaryDto(
    decimal TotalMarketValue,
    decimal TotalCostBasis,
    decimal TotalUnrealizedGainLoss,
    decimal TotalUnrealizedGainLossPercent,
    decimal Trailing12MonthIncome,
    decimal ProjectedAnnualIncome,
    decimal ProjectedMonthlyIncome,
    decimal LifetimeDividendIncome,
    decimal PortfolioCurrentYieldPercent,
    decimal PortfolioYieldOnCostPercent,
    int HoldingCount);

public sealed record HoldingAnalyticsDto(
    long HoldingId,
    long PortfolioId,
    string PortfolioName,
    long AccountId,
    string AccountName,
    long SecurityId,
    string SecuritySymbol,
    string SecurityName,
    string SectorName,
    string IndustryName,
    string InvestmentTypeName,
    decimal LastPrice,
    decimal TotalShares,
    decimal TotalCostBasis,
    decimal MarketValue,
    decimal UnrealizedGainLoss,
    decimal UnrealizedGainLossPercent,
    decimal Trailing12MonthDividends,
    decimal ProjectedAnnualIncome,
    decimal ProjectedMonthlyIncome,
    decimal LifetimeDividends,
    decimal CurrentYieldPercent,
    decimal YieldOnCostPercent);

public sealed record AllocationItemDto(
    string Name,
    decimal MarketValue,
    decimal CostBasis,
    decimal AllocationPercent,
    decimal Trailing12MonthIncome,
    int HoldingCount);

public sealed record MonthlyIncomePointDto(
    int Year,
    int Month,
    string Label,
    decimal TotalAmount,
    decimal CashAmount,
    decimal ReinvestedAmount,
    int PayoutCount);

public sealed record DashboardAnalyticsDto(
    long? PortfolioId,
    DateOnly AsOfDate,
    DashboardSummaryDto Summary,
    IReadOnlyList<HoldingAnalyticsDto> Holdings,
    IReadOnlyList<AllocationItemDto> SectorAllocations,
    IReadOnlyList<AllocationItemDto> IndustryAllocations,
    IReadOnlyList<AllocationItemDto> InvestmentTypeAllocations,
    IReadOnlyList<MonthlyIncomePointDto> MonthlyIncomeHistory);
```

### Interfaces & DI Extension
```csharp
namespace DividendTracker.Core.Features.Analytics;

public interface IAnalyticsService
{
    Task<DashboardAnalyticsDto> GetDashboardAsync(
        long? portfolioId = null,
        DateOnly? asOfDate = null,
        CancellationToken cancellationToken = default);
}

public static class AnalyticsServiceCollectionExtensions
{
    public static IServiceCollection AddAnalyticsFeature(this IServiceCollection services);
}
```

### Implementation Notes
- **`AnalyticsService`**:
  - Constructor: `(ISqliteConnectionFactory connectionFactory)` with `ArgumentNullException.ThrowIfNull(connectionFactory)`.
  - **`GetDashboardAsync(long? portfolioId = null, DateOnly? asOfDate = null, CancellationToken cancellationToken = default)`**:
    - If `portfolioId.HasValue`, call `ArgumentOutOfRangeException.ThrowIfNegativeOrZero(portfolioId.Value)`.
    - `var effectiveAsOf = asOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow);`
    - `var ttmStartDate = effectiveAsOf.AddYears(-1).AddDays(1);` *(exact 12-month inclusive window `[ttmStartDate, effectiveAsOf]`)*.
    - Execute 3 simple parameterized queries on one open connection (no N+1):
      1. Holdings joined with `Portfolios`, `Accounts`, `Securities`, `InvestmentTypes`, `IndustryTypes`, `SectorTypes` filtered by `(@PortfolioId IS NULL OR h.PortfolioId = @PortfolioId)`.
      2. `MarketBuys` joined with `Holdings` filtered by `(@PortfolioId IS NULL OR h.PortfolioId = @PortfolioId)`.
      3. `DividendPayments` joined with `Holdings` filtered by `(@PortfolioId IS NULL OR h.PortfolioId = @PortfolioId)`.
    - Per-holding calculations (zero-division safe, `MidpointRounding.AwayFromZero` to 2 decimals for `%` and monthly income):
      - `TotalShares = buys.Sum(b => b.NumberOfShares)`
      - `TotalCostBasis = buys.Sum(b => b.NumberOfShares * b.PricePerShare)`
      - `MarketValue = TotalShares * LastPrice`
      - `UnrealizedGainLoss = MarketValue - TotalCostBasis`
      - `UnrealizedGainLossPercent = TotalCostBasis > 0m ? Math.Round((UnrealizedGainLoss / TotalCostBasis) * 100m, 2, MidpointRounding.AwayFromZero) : 0m`
      - Filter holding dividends to `PaymentDate <= effectiveAsOf`:
        - `LifetimeDividends = eligibleDividends.Sum(d => d.Amount)`
        - `Trailing12MonthDividends = eligibleDividends.Where(d => d.PaymentDate >= ttmStartDate).Sum(d => d.Amount)`
      - `ProjectedAnnualIncome = Trailing12MonthDividends`
      - `ProjectedMonthlyIncome = Math.Round(ProjectedAnnualIncome / 12m, 2, MidpointRounding.AwayFromZero)`
      - `CurrentYieldPercent = MarketValue > 0m ? Math.Round((ProjectedAnnualIncome / MarketValue) * 100m, 2, MidpointRounding.AwayFromZero) : 0m`
      - `YieldOnCostPercent = TotalCostBasis > 0m ? Math.Round((ProjectedAnnualIncome / TotalCostBasis) * 100m, 2, MidpointRounding.AwayFromZero) : 0m`
    - Order `Holdings` by `MarketValue DESC`, then `SecuritySymbol ASC`, then `HoldingId ASC`.
    - Portfolio `DashboardSummaryDto`:
      - Sum `TotalMarketValue`, `TotalCostBasis`, `Trailing12MonthIncome`, `LifetimeDividendIncome` across holdings.
      - `TotalUnrealizedGainLoss = TotalMarketValue - TotalCostBasis`
      - `TotalUnrealizedGainLossPercent`, `ProjectedAnnualIncome`, `ProjectedMonthlyIncome`, `PortfolioCurrentYieldPercent`, `PortfolioYieldOnCostPercent` computed with the same zero-division-safe formulas above.
    - `SectorAllocations`, `IndustryAllocations`, `InvestmentTypeAllocations`:
      - Group `Holdings` by `SectorName`, `IndustryName`, and `InvestmentTypeName` respectively.
      - `AllocationPercent = TotalMarketValue > 0m ? Math.Round((groupMarketValue / TotalMarketValue) * 100m, 2, MidpointRounding.AwayFromZero) : 0m`
      - Order each allocation list by `MarketValue DESC`, then `Name ASC`.
    - `MonthlyIncomeHistory`:
      - Group all dividends with `PaymentDate <= effectiveAsOf` by `(PaymentDate.Year, PaymentDate.Month)`.
      - `Label = $"{year:D4}-{month:D2}"` (using `CultureInfo.InvariantCulture`).
      - `ReinvestedAmount = group.Where(d => d.IsReinvested).Sum(d => d.Amount)`
      - `CashAmount = group.Where(d => !d.IsReinvested).Sum(d => d.Amount)`
      - `TotalAmount = CashAmount + ReinvestedAmount`
      - Order chronologically by `Year ASC`, `Month ASC`.
- **`AnalyticsServiceCollectionExtensions`**:
  - Registers `services.AddSingleton<IAnalyticsService, AnalyticsService>();`.
- **`Home.razor` & `Home.razor.css`**:
  - `@page "/"`, `@inject IAppInfoService AppInfoService`, `@inject IAnalyticsService AnalyticsService`, `@inject IPortfolioService PortfolioService`.
  - Retain the compact app hero showing `ApplicationName`, `PlatformName`, and `v{Version}`.
  - Portfolio filter `<select>` (`All Portfolios` + each portfolio) triggering `AnalyticsService.GetDashboardAsync(selectedPortfolioId)`.
  - KPI summary grid (Market Value, Cost Basis & Unrealized Gain/Loss, Projected Annual & Monthly Income, Current Yield & Yield on Cost, Lifetime Dividends).
  - Allocation Breakdown section (3 responsive cards for Sector, Industry, and Investment Type with pure CSS width-scaled percentage bars).
  - Monthly Income History section (pure CSS Flexbox bars showing `Label`, `TotalAmount`, Cash vs. DRIP breakdown, and payout count).
  - Per-Holding Performance grid showing symbol, name, portfolio/account, sector, shares, market value, cost basis, gain/loss, TTM dividends, Current Yield %, and YoC %.
  - Empty state when `Holdings.Count == 0`.
  - Pure CSS Grid & Flexbox using `--color-*`, `--space-*`, `--radius-*` from `app.css`; interactive controls have `min-height: 44px` for Android touch targets.

### SQL Schema Reference *(already created in Slice 02)*
```sql
-- Portfolios(Id, Name, Description, IsModelled, CreatedUtc)
-- Accounts(Id, PortfolioId, Name, AccountType, CreatedUtc)
-- InvestmentTypes(Id, Name), IndustryTypes(Id, Name), SectorTypes(Id, Name)
-- Securities(Id, Symbol, Name, InvestmentTypeId, IndustryTypeId, SectorTypeId, LastPrice, LastPriceUpdatedUtc)
-- Holdings(Id, PortfolioId, AccountId, SecurityId, IsDripEnabled)
-- MarketBuys(Id, HoldingId, NumberOfShares, PricePerShare, PurchaseDate, IsDrip, DividendPaymentId)
-- DividendPayments(Id, HoldingId, PaymentDate, ExDividendDate, Amount, IsReinvested, Notes)
```

---

## 3. Behavioral Acceptance Criteria (Gherkin)

```gherkin
Feature: Yield & Income Analytics Dashboard

  Scenario: Compute portfolio and per-holding market value, cost basis, unrealized gain, current yield, and yield on cost
    Given a portfolio with two holdings:
      | Symbol | Sector     | Industry | Type  | Shares | BuyPrice | LastPrice | TTM Dividends |
      | SCHD   | Financials | ETF      | ETF   | 10     | 75.00    | 80.00     | 30.00         |
      | O      | Real Estate| REIT     | REIT  | 20     | 50.00    | 60.00     | 60.00         |
    When GetDashboardAsync is called for that portfolio with asOfDate 2026-03-31
    Then TotalMarketValue is 2000.00m, TotalCostBasis is 1750.00m, and TotalUnrealizedGainLoss is 250.00m (14.29%)
    And Trailing12MonthIncome and ProjectedAnnualIncome are 90.00m and ProjectedMonthlyIncome is 7.50m
    And PortfolioCurrentYieldPercent is 4.50m and PortfolioYieldOnCostPercent is 5.14m
    And each HoldingAnalyticsDto reports accurate MarketValue, CurrentYieldPercent, and YieldOnCostPercent

  Scenario: Exclude dividends older than 12 months from TTM while preserving them in lifetime income and monthly history
    Given a holding with a 20.00m cash dividend on 2025-01-15, a 25.00m cash dividend on 2025-06-15, and a 35.00m DRIP dividend on 2026-03-15
    When GetDashboardAsync is called with asOfDate 2026-03-31
    Then Trailing12MonthIncome is 60.00m and LifetimeDividendIncome is 80.00m
    And MonthlyIncomeHistory contains 3 chronological points ("2025-01", "2025-06", "2026-03") with accurate CashAmount and ReinvestedAmount

  Scenario: Compute allocation breakdowns by sector, industry, and investment type
    Given holdings across two sectors with MarketValues 1200.00m (Real Estate) and 800.00m (Financials)
    When GetDashboardAsync is called
    Then SectorAllocations returns Real Estate at 60.00% and Financials at 40.00% ordered by MarketValue descending
    And IndustryAllocations and InvestmentTypeAllocations sum to 100.00% of TotalMarketValue

  Scenario: Filter dashboard metrics by portfolio and handle empty or zero-value holdings without divide-by-zero
    Given two portfolios where the first has a holding with zero buys and LastPrice 0m and the second has an active holding
    When GetDashboardAsync is called for the first portfolio
    Then TotalMarketValue, TotalCostBasis, CurrentYieldPercent, YieldOnCostPercent, and AllocationPercent are all 0m without throwing
    When GetDashboardAsync is called with portfolioId 0 or negative
    Then an ArgumentOutOfRangeException is thrown

  Scenario: Dashboard page renders analytics KPIs, allocations, monthly income history, and filters by portfolio
    Given IAnalyticsService returns a populated DashboardAnalyticsDto and IPortfolioService returns two portfolios
    When the Home Dashboard component is rendered
    Then the markup displays application metadata, formatted TotalMarketValue, ProjectedAnnualIncome, PortfolioCurrentYieldPercent, PortfolioYieldOnCostPercent, sector allocation, monthly history, and holding symbols
    And changing the portfolio filter calls IAnalyticsService.GetDashboardAsync with the selected portfolioId
```

---

## 4. Implementation Subtasks

- [ ] **Red**: Generate xUnit + FluentAssertions + NSubstitute + bUnit tests covering all Gherkin scenarios (`qwen2.5-coder:7b`)
- [ ] **Green**: Implement `AnalyticsDtos`, `IAnalyticsService`, `AnalyticsService`, DI registration, and `/` Dashboard UI (`qwen2.5-coder:7b`)
- [ ] **Verify**: Pass `dotnet build -warnaserror` and `dotnet test` (local fixes via `qwen3.5:9b`, max 2 retries)
- [ ] **Pass-1 Review**: Run local `qwen3.5:9b` review on `git diff --staged`
- [ ] **Pass-2 Audit**: Submit `git diff --staged` for Gemini architectural & security sign-off

