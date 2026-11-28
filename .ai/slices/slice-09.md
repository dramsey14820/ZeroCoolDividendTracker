# Slice 09: JSON Backup Export & Import

- **Status**: `Ready`
- **Risk**: `High`
- **Owner / Stage**: Stage 2 (Gemini Mini-Spec) -> Stages 3–5 (Local Ollama) -> Stage 6 (Two-Tier Review)

---

## 1. Scope & Target Files

### In Scope
- Define versioned JSON backup contracts (`BackupImportMode`, `BackupDocumentDto`, table backup records, `BackupImportResultDto`) and `IBackupService` in `DividendTracker.Core/Features/Backup/`.
- Implement `BackupService` using `ISqliteConnectionFactory`, Dapper parameterized SQL, `System.Text.Json`, and `TimeProvider`:
  - **Export (`ExportJsonAsync`)**: Serialize all user data (`InvestmentTypes`, `IndustryTypes`, `SectorTypes`, `Portfolios`, `Accounts`, `Securities`, `Holdings`, `DividendPayments`, `MarketBuys`, and non-secret `AppSettings`) to a formatted JSON string with `SchemaVersion = 1` and `ExportedUtc`. Exclude `MarketData.ApiKey` and ephemeral `MarketDividendCache`.
  - **Import (`ImportJsonAsync`)**: Validate JSON structure, `SchemaVersion == 1`, and internal foreign-key references before mutating the database. Execute inside a single SQLite transaction supporting both:
    - `BackupImportMode.Replace`: Clear existing user data (preserving `MarketData.ApiKey` in `AppSettings`) and restore the full backup cleanly with remapped relational IDs.
    - `BackupImportMode.Merge`: Match existing lookups/portfolios/accounts/securities/holdings by natural keys, insert missing records, skip duplicate dividends/buys, and upsert non-secret `AppSettings`.
- Register `AddBackupFeature()` in `DividendTracker.Web` and `DividendTracker.Maui` (and remove the duplicate `AddMarketDataFeature()` call in both hosts).
- Extend `/settings` (`Settings.razor` + `Settings.razor.css`) with a **JSON Backup & Restore** card supporting export (JSON preview + download link) and import (file picker or pasted JSON + **Merge / Replace** mode toggle + result summary).
- Add xUnit + FluentAssertions + NSubstitute integration tests (`BackupServiceTests.cs`) and bUnit tests (`BackupSettingsPageTests.cs`), and update `SettingsPageTests.cs` to register a mocked `IBackupService`.

### Out of Scope
- Cloud sync or automatic scheduled backups
- Exporting or importing secret API keys (`MarketData.ApiKey`)
- Exporting ephemeral `MarketDividendCache` rows
- Legacy v0 schema migration (only `SchemaVersion == 1`)

### Target Files
- **Create**:
  - `src/DividendTracker.Core/Features/Backup/BackupDtos.cs`
  - `src/DividendTracker.Core/Features/Backup/IBackupService.cs`
  - `src/DividendTracker.Core/Features/Backup/BackupService.cs`
  - `src/DividendTracker.Core/Features/Backup/BackupServiceCollectionExtensions.cs`
  - `tests/DividendTracker.Tests/Features/Backup/BackupServiceTests.cs`
  - `tests/DividendTracker.Tests/Features/Backup/BackupSettingsPageTests.cs`
- **Modify**:
  - `src/DividendTracker.Shared.UI/_Imports.razor` *(add `@using DividendTracker.Core.Features.Backup`)*
  - `src/DividendTracker.Shared.UI/Pages/Settings.razor` *(inject `IBackupService` and add JSON Backup & Restore card)*
  - `src/DividendTracker.Shared.UI/Pages/Settings.razor.css` *(add styles for backup textarea, file input, mode toggle, and download link)*
  - `src/DividendTracker.Web/Program.cs` *(register `builder.Services.AddBackupFeature();` and remove duplicate `AddMarketDataFeature();`; preserve explicit `Program.Main`)*
  - `src/DividendTracker.Maui/MauiProgram.cs` *(register `builder.Services.AddBackupFeature();` and remove duplicate `AddMarketDataFeature();`)*
  - `tests/DividendTracker.Tests/Features/MarketData/SettingsPageTests.cs` *(register `Substitute.For<IBackupService>()` in DI)*

---

## 2. Data Contracts / Interfaces / SQL Schema

### Required Namespaces & Packages
- Existing packages only (no new NuGet packages required).
- Key namespaces:
  - `Dapper`
  - `Microsoft.AspNetCore.Components.Forms` *(for `InputFile` / `InputFileChangeEventArgs` in `Settings.razor`)*
  - `Microsoft.Data.Sqlite`
  - `Microsoft.Extensions.DependencyInjection`
  - `System.Globalization`
  - `System.Text.Json`
  - `DividendTracker.Core.Infrastructure.Persistence`
  - `DividendTracker.Core.Features.Backup`
  - `Bunit`, `FluentAssertions`, `NSubstitute`, `Xunit`
- Analyzer & Dapper notes (`-warnaserror`, `AnalysisLevel=latest-recommended`):
  - Pass `CommandDefinition` with `transaction` and `cancellationToken` to all Dapper calls inside the import transaction.
  - Use `CultureInfo.InvariantCulture` for all `DateOnly` (`"yyyy-MM-dd"`), `DateTimeOffset` (`"O"`), and `decimal` conversions.
  - Use `JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true }`.
  - Cap `InputFile` reads to a safe limit (e.g., `5 * 1024 * 1024` bytes / 5 MB).
  - Never export or overwrite `MarketData.ApiKey`.
  - In SQLite integration tests using temp files, call `SqliteConnection.ClearAllPools()` before deleting the temp file in `Dispose()`.

### DTOs & Contracts
```csharp
namespace DividendTracker.Core.Features.Backup;

public enum BackupImportMode
{
    Merge = 0,
    Replace = 1,
}

public sealed record BackupLookupItemDto(long Id, string Name);

public sealed record BackupPortfolioDto(
    long Id,
    string Name,
    string Description,
    bool IsModelled,
    DateTimeOffset CreatedUtc);

public sealed record BackupAccountDto(
    long Id,
    long PortfolioId,
    string Name,
    string AccountType,
    DateTimeOffset CreatedUtc);

public sealed record BackupSecurityDto(
    long Id,
    string Symbol,
    string Name,
    long InvestmentTypeId,
    long IndustryTypeId,
    long SectorTypeId,
    decimal LastPrice,
    DateTimeOffset? LastPriceUpdatedUtc);

public sealed record BackupHoldingDto(
    long Id,
    long PortfolioId,
    long AccountId,
    long SecurityId,
    bool IsDripEnabled);

public sealed record BackupDividendPaymentDto(
    long Id,
    long HoldingId,
    DateOnly PaymentDate,
    DateOnly? ExDividendDate,
    decimal Amount,
    bool IsReinvested,
    string Notes);

public sealed record BackupMarketBuyDto(
    long Id,
    long HoldingId,
    decimal NumberOfShares,
    decimal PricePerShare,
    DateOnly PurchaseDate,
    bool IsDrip,
    long? DividendPaymentId);

public sealed record BackupAppSettingDto(
    string Key,
    string Value,
    DateTimeOffset UpdatedUtc);

public sealed record BackupDocumentDto(
    int SchemaVersion,
    DateTimeOffset ExportedUtc,
    IReadOnlyList<BackupLookupItemDto> InvestmentTypes,
    IReadOnlyList<BackupLookupItemDto> IndustryTypes,
    IReadOnlyList<BackupLookupItemDto> SectorTypes,
    IReadOnlyList<BackupPortfolioDto> Portfolios,
    IReadOnlyList<BackupAccountDto> Accounts,
    IReadOnlyList<BackupSecurityDto> Securities,
    IReadOnlyList<BackupHoldingDto> Holdings,
    IReadOnlyList<BackupDividendPaymentDto> DividendPayments,
    IReadOnlyList<BackupMarketBuyDto> MarketBuys,
    IReadOnlyList<BackupAppSettingDto> AppSettings);

public sealed record BackupImportResultDto(
    BackupImportMode Mode,
    int PortfoliosImported,
    int AccountsImported,
    int SecuritiesImported,
    int HoldingsImported,
    int DividendPaymentsImported,
    int MarketBuysImported,
    int SettingsImported,
    int SkippedCount);
```

### Interfaces & DI Extension
```csharp
namespace DividendTracker.Core.Features.Backup;

public interface IBackupService
{
    Task<string> ExportJsonAsync(CancellationToken cancellationToken = default);

    Task<BackupImportResultDto> ImportJsonAsync(
        string json,
        BackupImportMode mode,
        CancellationToken cancellationToken = default);
}

public static class BackupServiceCollectionExtensions
{
    public static IServiceCollection AddBackupFeature(this IServiceCollection services);
}
```

### SQL Schema & Transaction Notes
- No new tables are added; `BackupService` operates on the existing SQLite tables created by `SqliteDatabaseInitializer`:
  - `InvestmentTypes`, `IndustryTypes`, `SectorTypes`
  - `Portfolios`, `Accounts`, `Securities`, `Holdings`
  - `DividendPayments`, `MarketBuys`
  - `AppSettings` *(excluding `Key = 'MarketData.ApiKey'`)*
- **Validation before write**:
  - Throw `ArgumentException` if `json` is null or whitespace.
  - Throw `ArgumentOutOfRangeException` if `!Enum.IsDefined(mode)`.
  - Throw `InvalidDataException` if JSON fails to deserialize, `SchemaVersion != 1`, any required collection is null, or any foreign-key reference (`Account.PortfolioId`, `Security.*TypeId`, `Holding.PortfolioId/AccountId/SecurityId`, `DividendPayment.HoldingId`, `MarketBuy.HoldingId`, non-null `MarketBuy.DividendPaymentId`) does not exist in the backup document.
- **Transactional execution**:
  - Open a connection via `ISqliteConnectionFactory.CreateOpenConnectionAsync(cancellationToken)` and start `await using var transaction = await connection.BeginTransactionAsync(cancellationToken)`.
  - **`Replace` mode**: Delete existing rows in child-to-parent order (`MarketBuys` -> `DividendPayments` -> `Holdings` -> `Securities` -> `Accounts` -> `Portfolios` -> `InvestmentTypes` -> `IndustryTypes` -> `SectorTypes`, plus `DELETE FROM AppSettings WHERE Key <> 'MarketData.ApiKey'`), then insert all backup rows in parent-to-child order while mapping each backup `Id` to the newly inserted `last_insert_rowid()`.
  - **`Merge` mode**:
    - Lookups (`InvestmentTypes`, `IndustryTypes`, `SectorTypes`): look up existing by `LOWER(Name) = LOWER(@Name)`; if found, reuse ID; else insert.
    - `Portfolios`: look up existing by `LOWER(Name) = LOWER(@Name)`; if found, reuse ID and increment `SkippedCount`; else insert and increment `PortfoliosImported`.
    - `Accounts`: look up existing by `PortfolioId = @PortfolioId AND LOWER(Name) = LOWER(@Name)`; if found, reuse ID and increment `SkippedCount`; else insert and increment `AccountsImported`.
    - `Securities`: normalize `Symbol = symbol.Trim().ToUpperInvariant()`; look up existing by `UPPER(Symbol) = @Symbol`; if found, reuse ID and increment `SkippedCount`; else insert and increment `SecuritiesImported`.
    - `Holdings`: look up existing by `(PortfolioId, AccountId, SecurityId)`; if found, reuse ID and increment `SkippedCount`; else insert and increment `HoldingsImported`.
    - `DividendPayments`: look up existing by `(HoldingId, PaymentDate, Amount)`; if found, reuse ID and increment `SkippedCount`; else insert and increment `DividendPaymentsImported`.
    - `MarketBuys`: look up existing by `(HoldingId, PurchaseDate, NumberOfShares, PricePerShare, IsDrip)`; if found, increment `SkippedCount`; else insert (using mapped `DividendPaymentId`) and increment `MarketBuysImported`.
    - `AppSettings`: skip `Key == "MarketData.ApiKey"` (case-insensitive); otherwise upsert (`INSERT INTO AppSettings ... ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value, UpdatedUtc = excluded.UpdatedUtc`) and increment `SettingsImported`.
  - Commit transaction on success; any exception rolls back automatically so existing data remains intact.

### UI Notes (`Settings.razor` & `Settings.razor.css`)
- Add `@inject IBackupService BackupService` to `Settings.razor`.
- Add a **JSON Backup & Restore** `<section class="settings-card backup-card">` inside `.settings-grid` (spanning full width on desktop `grid-column: 1 / -1`):
  - **Export**: Button `#export-backup-btn` ("Export JSON Backup") calls `BackupService.ExportJsonAsync()`, populates a read-only `<textarea id="backup-export-json">`, and renders a `<a id="download-backup-link" download="dividend-tracker-backup.json" href="data:application/json;charset=utf-8,...">` download link.
  - **Import**:
    - Mode selector (`#backup-import-mode`, `<select>` with `Merge` and `Replace` options, defaulting to `Merge`) plus a warning hint when `Replace` is selected.
    - `<InputFile id="backup-file-input" accept=".json,application/json" OnChange="HandleBackupFileSelectedAsync" />` that loads selected `.json` file content into `_importJson` (max 5 MB).
    - `<textarea id="backup-import-json">` bound to `_importJson` so users can inspect or paste backup JSON directly.
    - Button `#import-backup-btn` ("Import JSON Backup") calls `BackupService.ImportJsonAsync(_importJson, _importMode)` and displays `BackupImportResultDto` counts (or a clear error banner on `InvalidDataException` / `ArgumentException`).
- Pure CSS Grid & Flexbox using `app.css` tokens, mobile-first, `min-height: 44px` on interactive controls.

---

## 3. Behavioral Acceptance Criteria (Gherkin)

```gherkin
Feature: JSON Backup Export and Import

  Scenario: ExportJsonAsync serializes all portfolio data and excludes secret API keys
    Given a SQLite database with a portfolio, account, security, holding, dividend payment, DRIP market buy, and AppSettings containing both MarketData.Provider and MarketData.ApiKey
    When BackupService.ExportJsonAsync is called
    Then the returned JSON has schemaVersion 1 and contains the portfolio, account, security, holding, dividend payment, and DRIP market buy
    And AppSettings includes MarketData.Provider but excludes MarketData.ApiKey

  Scenario: ImportJsonAsync in Replace mode restores a full backup into a clean database and preserves DRIP links
    Given a valid JSON backup exported from a populated database and a target database containing different pre-existing data plus a saved MarketData.ApiKey
    When BackupService.ImportJsonAsync is called with BackupImportMode.Replace
    Then all pre-existing portfolios, accounts, securities, holdings, dividends, and buys are replaced with the backup data
    And MarketBuys.DividendPaymentId points to the newly inserted DividendPayments row
    And the target database's MarketData.ApiKey setting is preserved

  Scenario: ImportJsonAsync in Merge mode adds new records and skips existing duplicates
    Given a database containing portfolio "Core" with security "SCHD" and one dividend payment
    When BackupService.ImportJsonAsync is called with BackupImportMode.Merge using a backup containing both "Core"/"SCHD" (same dividend) and a new portfolio "Income" with security "O"
    Then the existing "Core" and "SCHD" records are not duplicated and SkippedCount is greater than 0
    And the new "Income" portfolio and "O" security are inserted

  Scenario: ImportJsonAsync rolls back and leaves existing data intact on invalid JSON or unsupported schema version
    Given a database with an existing portfolio "Core"
    When BackupService.ImportJsonAsync is called with invalid JSON or schemaVersion 99 or a broken Holding.SecurityId reference
    Then an InvalidDataException is thrown
    And the existing "Core" portfolio remains unchanged in SQLite

  Scenario: Settings page exports JSON backup and imports JSON with selectable Merge or Replace mode
    Given IBackupService returns valid backup JSON on ExportJsonAsync and a BackupImportResultDto on ImportJsonAsync
    When the Settings component is rendered and "Export JSON Backup" is clicked
    Then IBackupService.ExportJsonAsync is called and the exported JSON and download link are displayed
    When the user selects Replace mode, enters backup JSON, and clicks "Import JSON Backup"
    Then IBackupService.ImportJsonAsync is called with the JSON and BackupImportMode.Replace and the import summary is displayed
```

---

## 4. Implementation Subtasks

- [ ] **Red**: Generate xUnit + FluentAssertions + NSubstitute + bUnit tests covering all Gherkin scenarios (`qwen2.5-coder:7b`)
- [ ] **Green**: Implement `Backup` contracts, `BackupService`, DI registration, and `/settings` Backup & Restore UI (`qwen2.5-coder:7b`)
- [ ] **Verify**: Pass `dotnet build -warnaserror` and `dotnet test` (local fixes via `qwen3.5:9b`, max 2 retries)
- [ ] **Pass-1 Review**: Run local `qwen3.5:9b` review on `git diff --staged`
- [ ] **Pass-2 Audit**: Submit `git diff --staged` for Gemini architectural & security sign-off
