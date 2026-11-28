# Slice 08: External Market Data Fetching & Caching

- **Status**: `In Progress`
- **Risk**: `High`
- **Owner / Stage**: Stage 2 (Gemini Mini-Spec) -> Stages 3–5 (Local Ollama) -> Stage 6 (Two-Tier Review)

---

## 1. Scope & Target Files

### In Scope
- Extend `SqliteDatabaseInitializer` schema with a `MarketDividendCache` table (and index on `Symbol`) to cache external historical dividend events in SQLite alongside existing `Securities.LastPrice` / `LastPriceUpdatedUtc` and `AppSettings`.
- Define market data contracts (`MarketDataProviderType`, `MarketDataSettingsDto`, `UpdateMarketDataSettingsRequest`, `MarketQuoteDto`, `ExternalDividendEventDto`, `MarketDataRefreshResultDto`, `DividendImportResultDto`), `IMarketDataProvider`, and `IMarketDataService` in `DividendTracker.Core/Features/MarketData/`.
- Implement three `HttpClient`-backed `IMarketDataProvider` providers using `System.Text.Json`:
  - `YahooFinanceMarketDataProvider` (default, zero API key required)
  - `FinnhubMarketDataProvider` (official free API key)
  - `FmpMarketDataProvider` (Financial Modeling Prep, official free API key)
- Implement `MarketDataService` using `ISqliteConnectionFactory`, `IEnumerable<IMarketDataProvider>`, and `TimeProvider`:
  - Persist/read provider settings (`MarketData.Provider`, `MarketData.ApiKey`, `MarketData.CacheTtlMinutes`) in `AppSettings`.
  - Refresh single or all security quotes with SQLite TTL caching (`Securities.LastPrice`, `Securities.LastPriceUpdatedUtc`) and `forceRefresh` bypass.
  - Fetch and cache historical dividend events in `MarketDividendCache` with TTL caching.
  - Import historical dividend payouts for a holding into `DividendPayments` using shares held on or before `ExDividendDate`, skipping duplicates on `(HoldingId, PaymentDate)` so imports are idempotent.
- Register `AddMarketDataFeature()` in `DividendTracker.Web` and `DividendTracker.Maui`.
- Create the `/settings` page (`Settings.razor` + `Settings.razor.css`) in `DividendTracker.Shared.UI` using pure CSS Grid/Flexbox and `app.css` design tokens to configure the market data provider, API key, cache TTL, and trigger a batch quote refresh.
- Add xUnit + FluentAssertions + NSubstitute unit/integration tests covering provider JSON parsing (via fake `HttpMessageHandler`, zero live network calls), SQLite caching & dividend import (`MarketDataServiceTests.cs`), and bUnit tests for `/settings` (`SettingsPageTests.cs`).

### Out of Scope
- Live network calls in automated tests (all HTTP tests must use a stubbed `HttpMessageHandler`)
- Background scheduled polling / timers (refresh is user-triggered)
- Per-row refresh buttons on `Securities.razor` or `Dividends.razor` (batch refresh lives on `/settings` in this slice)
- JSON backup export/import (Slice 09)

### Target Files
- **Create**:
  - `src/DividendTracker.Core/Features/MarketData/MarketDataDtos.cs`
  - `src/DividendTracker.Core/Features/MarketData/IMarketDataProvider.cs`
  - `src/DividendTracker.Core/Features/MarketData/IMarketDataService.cs`
  - `src/DividendTracker.Core/Features/MarketData/YahooFinanceMarketDataProvider.cs`
  - `src/DividendTracker.Core/Features/MarketData/FinnhubMarketDataProvider.cs`
  - `src/DividendTracker.Core/Features/MarketData/FmpMarketDataProvider.cs`
  - `src/DividendTracker.Core/Features/MarketData/MarketDataService.cs`
  - `src/DividendTracker.Core/Features/MarketData/MarketDataServiceCollectionExtensions.cs`
  - `src/DividendTracker.Shared.UI/Pages/Settings.razor`
  - `src/DividendTracker.Shared.UI/Pages/Settings.razor.css`
  - `tests/DividendTracker.Tests/Features/MarketData/MarketDataProviderTests.cs`
  - `tests/DividendTracker.Tests/Features/MarketData/MarketDataServiceTests.cs`
  - `tests/DividendTracker.Tests/Features/MarketData/SettingsPageTests.cs`
- **Modify**:
  - `src/DividendTracker.Core/Infrastructure/Persistence/SqliteDatabaseInitializer.cs` *(add `MarketDividendCache` table and `IX_MarketDividendCache_Symbol` index)*
  - `src/DividendTracker.Shared.UI/_Imports.razor` *(add `@using DividendTracker.Core.Features.MarketData`)*
  - `src/DividendTracker.Web/Program.cs` *(register `builder.Services.AddMarketDataFeature();`; preserve explicit `Program.Main`)*
  - `src/DividendTracker.Maui/MauiProgram.cs` *(register `builder.Services.AddMarketDataFeature();`)*

*(Note: `NavMenu.razor` already links `Settings` to `settings`.)*

---

## 2. Data Contracts / Interfaces / SQL Schema

### Required Namespaces & Packages
- Existing packages only (no new NuGet packages required).
- Key namespaces:
  - `Dapper`
  - `Microsoft.Data.Sqlite`
  - `Microsoft.Extensions.DependencyInjection`
  - `System.Globalization`
  - `System.Net.Http`
  - `System.Text.Json`
  - `DividendTracker.Core.Infrastructure.Persistence`
  - `DividendTracker.Core.Features.MarketData`
  - `Bunit`, `FluentAssertions`, `NSubstitute`, `Xunit`
- Analyzer & Dapper notes (`-warnaserror`, `AnalysisLevel=latest-recommended`):
  - Use `Uri.EscapeDataString(symbol)` and `Uri.EscapeDataString(apiKey)` when building request URLs.
  - Use `CultureInfo.InvariantCulture` for all `DateOnly` (`"yyyy-MM-dd"`), `DateTimeOffset` (`"O"`), and `decimal` conversions.
  - Pass `CommandDefinition` with `cancellationToken` to all Dapper calls.
  - Register a single shared `HttpClient` via `SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) }` in `AddMarketDataFeature()` (no extra `Microsoft.Extensions.Http` package required).
  - Never expose or log API keys in exception messages or UI error banners.
  - In SQLite integration tests using temp files, call `SqliteConnection.ClearAllPools()` before deleting the temp file in `Dispose()`.

### DTOs & Contracts
```csharp
namespace DividendTracker.Core.Features.MarketData;

public enum MarketDataProviderType
{
    YahooFinance = 0,
    Finnhub = 1,
    FinancialModelingPrep = 2,
}

public sealed record MarketDataSettingsDto(
    MarketDataProviderType Provider,
    string ApiKey,
    int CacheTtlMinutes);

public sealed record UpdateMarketDataSettingsRequest(
    MarketDataProviderType Provider,
    string? ApiKey,
    int CacheTtlMinutes);

public sealed record MarketQuoteDto(
    string Symbol,
    decimal Price,
    string? Name,
    string ProviderName,
    DateTimeOffset RetrievedUtc,
    bool FromCache);

public sealed record ExternalDividendEventDto(
    string Symbol,
    DateOnly ExDividendDate,
    DateOnly PaymentDate,
    decimal AmountPerShare,
    string ProviderName);

public sealed record MarketDataRefreshResultDto(
    int UpdatedCount,
    int CachedCount,
    int FailedCount,
    IReadOnlyList<string> Errors);

public sealed record DividendImportResultDto(
    int ImportedCount,
    int SkippedCount,
    IReadOnlyList<string> Errors);
```

### Interfaces & DI Extension
```csharp
namespace DividendTracker.Core.Features.MarketData;

public interface IMarketDataProvider
{
    MarketDataProviderType ProviderType { get; }
    string DisplayName { get; }
    bool RequiresApiKey { get; }

    Task<MarketQuoteDto> GetQuoteAsync(
        string symbol,
        string? apiKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExternalDividendEventDto>> GetDividendHistoryAsync(
        string symbol,
        DateOnly fromDate,
        DateOnly toDate,
        string? apiKey,
        CancellationToken cancellationToken = default);
}

public interface IMarketDataService
{
    Task<MarketDataSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task<MarketDataSettingsDto> SaveSettingsAsync(
        UpdateMarketDataSettingsRequest request,
        CancellationToken cancellationToken = default);

    Task<MarketQuoteDto> RefreshSecurityQuoteAsync(
        long securityId,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    Task<MarketDataRefreshResultDto> RefreshAllQuotesAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExternalDividendEventDto>> GetDividendHistoryAsync(
        long securityId,
        DateOnly fromDate,
        DateOnly toDate,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    Task<DividendImportResultDto> ImportDividendsForHoldingAsync(
        long holdingId,
        DateOnly fromDate,
        DateOnly toDate,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);
}

public static class MarketDataServiceCollectionExtensions
{
    public static IServiceCollection AddMarketDataFeature(this IServiceCollection services);
}
```

### SQL Schema Addition (`SqliteDatabaseInitializer.cs`)
```sql
CREATE TABLE IF NOT EXISTS MarketDividendCache (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Symbol TEXT NOT NULL,
    ExDividendDate TEXT NOT NULL,
    PaymentDate TEXT NOT NULL,
    AmountPerShare TEXT NOT NULL,
    ProviderName TEXT NOT NULL,
    FetchedUtc TEXT NOT NULL,
    UNIQUE (Symbol, PaymentDate, ProviderName)
);

CREATE INDEX IF NOT EXISTS IX_MarketDividendCache_Symbol ON MarketDividendCache(Symbol);
```

### Implementation Notes
- **Providers (`YahooFinanceMarketDataProvider`, `FinnhubMarketDataProvider`, `FmpMarketDataProvider`)**:
  - Constructor: `(HttpClient httpClient)` with `ArgumentNullException.ThrowIfNull(httpClient)`.
  - Validate `symbol` with `ArgumentException.ThrowIfNullOrWhiteSpace(symbol)` and normalize via `symbol.Trim().ToUpperInvariant()`.
  - Validate `fromDate <= toDate` (throw `ArgumentOutOfRangeException` if `toDate < fromDate`).
  - For providers where `RequiresApiKey == true` (Finnhub, FMP), throw `InvalidOperationException` if `string.IsNullOrWhiteSpace(apiKey)`.
  - **Yahoo Finance** (`DisplayName = "Yahoo Finance"`, `RequiresApiKey = false`):
    - Quote: `GET https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?range=1d&interval=1d` with header `User-Agent: ZeroCoolDividendTracker/1.0`. Parse `chart.result[0].meta.regularMarketPrice` (`decimal`) and optional `shortName` / `longName`.
    - Dividends: `GET https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?period1={fromUnix}&period2={toUnix}&interval=1d&events=div`. Parse `chart.result[0].events.dividends` entries (`amount`, `date` unix timestamp -> `DateOnly` for both `ExDividendDate` and `PaymentDate`). Return ordered by `PaymentDate ASC`.
  - **Finnhub** (`DisplayName = "Finnhub"`, `RequiresApiKey = true`):
    - Quote: `GET https://finnhub.io/api/v1/quote?symbol={symbol}&token={key}`. Parse `c` (`decimal`, must be `> 0m`).
    - Dividends: `GET https://finnhub.io/api/v1/stock/dividend?symbol={symbol}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&token={key}`. Parse JSON array (`amount`, `date` as `ExDividendDate`, `payDate` or fallback to `date` as `PaymentDate`).
  - **FMP** (`DisplayName = "Financial Modeling Prep"`, `RequiresApiKey = true`):
    - Quote: `GET https://financialmodelingprep.com/api/v3/quote-short/{symbol}?apikey={key}`. Parse `[0].price` (`decimal`).
    - Dividends: `GET https://financialmodelingprep.com/api/v3/historical-price-full/stock_dividend/{symbol}?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&apikey={key}`. Parse `historical` array (`dividend`, `date` as `ExDividendDate`, `paymentDate` or fallback to `date` as `PaymentDate`).
- **`MarketDataService`**:
  - Constructor: `(ISqliteConnectionFactory connectionFactory, IEnumerable<IMarketDataProvider> providers, TimeProvider? timeProvider = null)` (default `TimeProvider.System`).
  - **Settings** (`AppSettings` keys: `MarketData.Provider`, `MarketData.ApiKey`, `MarketData.CacheTtlMinutes`):
    - Defaults when keys are absent: `YahooFinance`, `""`, `15`.
    - `SaveSettingsAsync`: validate `Enum.IsDefined(request.Provider)` and `CacheTtlMinutes` in `[1, 1440]` (`ArgumentOutOfRangeException` otherwise). Trim `ApiKey` (`request.ApiKey?.Trim() ?? string.Empty`). Upsert all three keys in `AppSettings`.
  - **`RefreshSecurityQuoteAsync`**:
    - Validate `securityId > 0`; throw `KeyNotFoundException` if security does not exist.
    - If `!forceRefresh` and `LastPriceUpdatedUtc` parses and `(nowUtc - lastUpdatedUtc) < TimeSpan.FromMinutes(settings.CacheTtlMinutes)`, return `MarketQuoteDto(symbol, lastPrice, name, activeProvider.DisplayName, lastUpdatedUtc, FromCache: true)` without invoking the provider.
    - Otherwise call `activeProvider.GetQuoteAsync(symbol, settings.ApiKey, cancellationToken)`, update `Securities.LastPrice` and `Securities.LastPriceUpdatedUtc = nowUtc.ToString("O", CultureInfo.InvariantCulture)`, and return `MarketQuoteDto(..., FromCache: false)`.
  - **`RefreshAllQuotesAsync`**:
    - Load all security IDs ordered by `Symbol ASC`. For each, call `RefreshSecurityQuoteAsync(id, forceRefresh, cancellationToken)`, tallying `UpdatedCount` (`!FromCache`) or `CachedCount` (`FromCache`). Catch `HttpRequestException`, `InvalidOperationException`, and `JsonException` per security to increment `FailedCount` and append a sanitized `"{symbol}: {message}"` error without aborting remaining symbols.
  - **`GetDividendHistoryAsync`**:
    - Validate `securityId > 0` and `toDate >= fromDate`; throw `KeyNotFoundException` if security does not exist.
    - Check `MarketDividendCache` for `(Symbol, ProviderName)`: if `!forceRefresh` and the most recent `FetchedUtc` is within `CacheTtlMinutes`, return cached rows where `PaymentDate` is within `[fromDate, toDate]` ordered by `PaymentDate ASC`.
    - Otherwise call `activeProvider.GetDividendHistoryAsync(...)`, upsert each event into `MarketDividendCache`, and return the events in `[fromDate, toDate]` ordered by `PaymentDate ASC`.
  - **`ImportDividendsForHoldingAsync`**:
    - Validate `holdingId > 0` and `toDate >= fromDate`; throw `KeyNotFoundException` if holding does not exist.
    - Load holding's `SecurityId`, all `MarketBuys` for `holdingId`, and existing `DividendPayments.PaymentDate` values for `holdingId`.
    - Fetch events via `GetDividendHistoryAsync(securityId, fromDate, toDate, forceRefresh, cancellationToken)`.
    - For each event:
      - If `existingPaymentDates` already contains `evt.PaymentDate`, increment `SkippedCount` and continue.
      - Compute `sharesHeld = buys.Where(b => b.PurchaseDate <= evt.ExDividendDate).Sum(b => b.NumberOfShares)`.
      - Compute `amount = Math.Round(sharesHeld * evt.AmountPerShare, 2, MidpointRounding.AwayFromZero)`.
      - If `sharesHeld <= 0m || amount <= 0m`, increment `SkippedCount` and continue.
      - Insert into `DividendPayments` (`HoldingId`, `PaymentDate`, `ExDividendDate`, `Amount`, `IsReinvested = 0`, `Notes = $"Imported from {evt.ProviderName}"`), add `evt.PaymentDate` to `existingPaymentDates`, and increment `ImportedCount`.
- **`Settings.razor` & `Settings.razor.css`**:
  - `@page "/settings"`, `@inject IMarketDataService MarketDataService`.
  - Form for Provider `<select>`, API Key `<input type="password">` (with helper text noting Yahoo Finance requires no key), Cache TTL Minutes `<input type="number" min="1" max="1440">`, and Save button.
  - Market Data Refresh card with "Force refresh (bypass cache)" checkbox, "Refresh All Quotes" button, and result summary showing Updated, Cached, Failed counts and any errors.
  - Pure CSS Grid & Flexbox using `app.css` tokens (`--color-*`, `--space-*`, `--radius-*`), mobile-first, `min-height: 44px` on interactive controls.

---

## 3. Behavioral Acceptance Criteria (Gherkin)

```gherkin
Feature: External Market Data Fetching and Caching

  Scenario: Yahoo Finance provider parses quote and dividend history JSON without an API key
    Given an HttpClient returning valid Yahoo Finance v8 chart JSON for "SCHD" with price 82.45 and two dividend events
    When YahooFinanceMarketDataProvider.GetQuoteAsync and GetDividendHistoryAsync are called with no API key
    Then GetQuoteAsync returns Price 82.45m and Symbol "SCHD"
    And GetDividendHistoryAsync returns both ExternalDividendEventDto items ordered by PaymentDate ascending

  Scenario: Finnhub and FMP providers require an API key and parse quotes and dividends
    Given FinnhubMarketDataProvider and FmpMarketDataProvider backed by stubbed HttpMessageHandlers
    When GetQuoteAsync is called with a blank API key
    Then an InvalidOperationException is thrown
    When GetQuoteAsync and GetDividendHistoryAsync are called with a valid API key and valid JSON payloads
    Then each provider returns the parsed MarketQuoteDto and ExternalDividendEventDto list

  Scenario: RefreshSecurityQuoteAsync caches quotes in SQLite and respects TTL and forceRefresh
    Given a tracked security "O" and CacheTtlMinutes set to 15 with a controllable TimeProvider
    When RefreshSecurityQuoteAsync is called the first time
    Then the active IMarketDataProvider is invoked once, Securities.LastPrice is updated in SQLite, and FromCache is false
    When RefreshSecurityQuoteAsync is called again 5 minutes later with forceRefresh false
    Then the provider is not invoked again and FromCache is true
    When RefreshSecurityQuoteAsync is called with forceRefresh true or after 20 minutes
    Then the provider is invoked again and Securities.LastPrice is updated

  Scenario: RefreshAllQuotesAsync isolates per-symbol provider errors and reports batch totals
    Given two tracked securities "SCHD" and "BAD" where the provider succeeds for "SCHD" and throws HttpRequestException for "BAD"
    When RefreshAllQuotesAsync is called
    Then UpdatedCount is 1, FailedCount is 1, Errors contains an entry for "BAD", and "SCHD" is updated in SQLite

  Scenario: ImportDividendsForHoldingAsync computes payout from shares held on ex-date and skips duplicates
    Given a holding with 10 shares bought on 2026-01-10 and external dividends of 0.75/sh on 2025-12-15 (before purchase) and 0.80/sh on 2026-03-20
    When ImportDividendsForHoldingAsync is called for 2025-12-01 to 2026-03-31
    Then ImportedCount is 1 (Amount 8.00m on 2026-03-20) and SkippedCount is 1
    When ImportDividendsForHoldingAsync is called a second time for the same range
    Then ImportedCount is 0 and SkippedCount is 2 with no duplicate DividendPayments rows

  Scenario: Settings page loads and saves market data settings and triggers quote refresh
    Given IMarketDataService returns YahooFinance with CacheTtlMinutes 15
    When the Settings component is rendered, updated to Finnhub with an API key and TTL 30, and submitted
    Then IMarketDataService.SaveSettingsAsync is called with Provider Finnhub, the entered API key, and CacheTtlMinutes 30
    And clicking "Refresh All Quotes" calls IMarketDataService.RefreshAllQuotesAsync and displays Updated, Cached, and Failed counts
```

---

## 4. Implementation Subtasks

- [x] **Red**: Generate xUnit + FluentAssertions + NSubstitute + bUnit tests covering all Gherkin scenarios (`qwen2.5-coder:7b`)
- [ ] **Green**: Implement `MarketData` contracts, providers, `MarketDataService`, SQLite `MarketDividendCache` DDL, DI registration, and `/settings` UI (`qwen2.5-coder:7b`)
- [ ] **Verify**: Pass `dotnet build -warnaserror` and `dotnet test` (local fixes via `qwen3.5:9b`, max 2 retries)
- [ ] **Pass-1 Review**: Run local `qwen3.5:9b` review on `git diff --staged`
- [ ] **Pass-2 Audit**: Submit `git diff --staged` for Gemini architectural & security sign-off

