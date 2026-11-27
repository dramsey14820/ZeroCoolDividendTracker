# Slice 02: SQLite Persistence Foundation & Schema Bootstrap

- **Status**: `Ready`
- **Risk**: `High`
- **Owner / Stage**: Stage 2 (Gemini Mini-Spec) -> Stages 3–5 (Local Ollama) -> Stage 6 (Two-Tier Review)

---

## 1. Scope & Target Files

### In Scope
- Add `Microsoft.Data.Sqlite`, `Dapper`, and `Microsoft.Extensions.DependencyInjection.Abstractions` to `DividendTracker.Core`.
- Implement `SqliteOptions`, `ISqliteConnectionFactory` / `SqliteConnectionFactory` (enforces `PRAGMA foreign_keys = ON;` on every opened connection and creates parent directories for file-backed databases).
- Implement `DecimalTextTypeHandler` (Dapper `SqlMapper.TypeHandler<decimal>`) so currency amounts and fractional shares stored as SQLite `TEXT` round-trip with exact decimal precision.
- Implement `IDatabaseInitializer` / `SqliteDatabaseInitializer` to idempotently create all v1 SQLite tables, indexes, and default lookup seed rows (`InvestmentTypes`, `SectorTypes`, `IndustryTypes`).
- Provide `AddSqlitePersistence(string databasePath)` DI extension and wire it into `DividendTracker.Web` (`App_Data/dividend-tracker.db`, initialized at startup) and `DividendTracker.Maui` (`%LOCALAPPDATA%\ZeroCoolDividendTracker\dividend-tracker.db` on Windows, `FileSystem.AppDataDirectory/dividend-tracker.db` on Android).

### Out of Scope
- Feature-specific CRUD repositories, services, or Razor UI pages (Slices 03–07)
- External market data HTTP clients (Slice 08)
- JSON export/import serialization (Slice 09)
- Migration framework beyond idempotent `CREATE TABLE IF NOT EXISTS` v1 bootstrap

### Target Files
- **Create**:
  - `src/DividendTracker.Core/Infrastructure/Persistence/SqliteOptions.cs`
  - `src/DividendTracker.Core/Infrastructure/Persistence/ISqliteConnectionFactory.cs`
  - `src/DividendTracker.Core/Infrastructure/Persistence/SqliteConnectionFactory.cs`
  - `src/DividendTracker.Core/Infrastructure/Persistence/DecimalTextTypeHandler.cs`
  - `src/DividendTracker.Core/Infrastructure/Persistence/IDatabaseInitializer.cs`
  - `src/DividendTracker.Core/Infrastructure/Persistence/SqliteDatabaseInitializer.cs`
  - `src/DividendTracker.Core/Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs`
  - `tests/DividendTracker.Tests/Infrastructure/Persistence/SqliteConnectionFactoryTests.cs`
  - `tests/DividendTracker.Tests/Infrastructure/Persistence/SqliteDatabaseInitializerTests.cs`
- **Modify**:
  - `src/DividendTracker.Core/DividendTracker.Core.csproj`
  - `src/DividendTracker.Web/Program.cs` *(preserve explicit `public class Program` and `Main`; use `public static async Task Main(string[] args)` so `InitializeAsync` is awaited without blocking)*
  - `src/DividendTracker.Maui/MauiProgram.cs`

---

## 2. Data Contracts / Interfaces / SQL Schema

### Required Namespaces & Packages
- `DividendTracker.Core.csproj` packages:
  - `Microsoft.Data.Sqlite` (`10.0.*`)
  - `Dapper` (`2.1.*`)
  - `Microsoft.Extensions.DependencyInjection.Abstractions` (`10.0.*`)
- Key namespaces: `System.Data`, `System.Globalization`, `Dapper`, `Microsoft.Data.Sqlite`, `Microsoft.Extensions.DependencyInjection`.
- Analyzer notes (`-warnaserror`, `AnalysisLevel=latest-recommended`):
  - Use `CultureInfo.InvariantCulture` for all `decimal.Parse` / `decimal.ToString` conversions (`CA1305`).
  - Use `ArgumentException.ThrowIfNullOrWhiteSpace(...)` and `ArgumentNullException.ThrowIfNull(...)`.
  - Use `await using` for `SqliteConnection` and dispose connections cleanly; in tests using temp files, call `SqliteConnection.ClearAllPools()` before deleting the temp file in `Dispose()`.

### DTOs & Contracts
```csharp
namespace DividendTracker.Core.Infrastructure.Persistence;

public sealed record SqliteOptions(string ConnectionString);
```

### Interfaces & DI Extension
```csharp
namespace DividendTracker.Core.Infrastructure.Persistence;

public interface ISqliteConnectionFactory
{
    Task<SqliteConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);
}

public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddSqlitePersistence(
        this IServiceCollection services,
        string databasePath);
}
```

### Implementation Notes
- `DecimalTextTypeHandler : SqlMapper.TypeHandler<decimal>`:
  - `SetValue(IDbDataParameter parameter, decimal value)`: sets `parameter.Value = value.ToString(CultureInfo.InvariantCulture);` and `parameter.DbType = DbType.String;`.
  - `Parse(object value)`: parses `decimal`, `string` (`CultureInfo.InvariantCulture`), `double`, `long`, or `int` into `decimal`.
  - Register via `SqlMapper.AddTypeHandler(new DecimalTextTypeHandler());` in `SqliteDatabaseInitializer` (idempotent static guard).
- `SqliteConnectionFactory`:
  - Validates `options.ConnectionString` is not null/whitespace.
  - Parses `new SqliteConnectionStringBuilder(options.ConnectionString)`. If `DataSource` is not `:memory:` and has a non-empty directory via `Path.GetDirectoryName(...)`, calls `Directory.CreateDirectory(directory)`.
  - Opens the connection asynchronously and runs `PRAGMA foreign_keys = ON;`.
- `SqliteDatabaseInitializer`:
  - Opens a connection via `ISqliteConnectionFactory`, runs `PRAGMA journal_mode = WAL;`, executes the idempotent DDL below, and inserts default seed rows with `INSERT OR IGNORE`.
- `DividendTracker.Maui/MauiProgram.cs`:
  - Uses `#if WINDOWS` to resolve `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZeroCoolDividendTracker", "dividend-tracker.db")`, and `#else` to resolve `Path.Combine(FileSystem.AppDataDirectory, "dividend-tracker.db")`.

### SQL Schema (v1 Bootstrap DDL)
```sql
CREATE TABLE IF NOT EXISTS Portfolios (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Description TEXT NOT NULL DEFAULT '',
    IsModelled INTEGER NOT NULL DEFAULT 0 CHECK (IsModelled IN (0, 1)),
    CreatedUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Accounts (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PortfolioId INTEGER NOT NULL,
    Name TEXT NOT NULL,
    AccountType TEXT NOT NULL,
    CreatedUtc TEXT NOT NULL,
    FOREIGN KEY (PortfolioId) REFERENCES Portfolios(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS InvestmentTypes (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS IndustryTypes (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS SectorTypes (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS Securities (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Symbol TEXT NOT NULL UNIQUE,
    Name TEXT NOT NULL,
    InvestmentTypeId INTEGER NOT NULL,
    IndustryTypeId INTEGER NOT NULL,
    SectorTypeId INTEGER NOT NULL,
    LastPrice TEXT NOT NULL DEFAULT '0',
    LastPriceUpdatedUtc TEXT NULL,
    FOREIGN KEY (InvestmentTypeId) REFERENCES InvestmentTypes(Id) ON DELETE RESTRICT,
    FOREIGN KEY (IndustryTypeId) REFERENCES IndustryTypes(Id) ON DELETE RESTRICT,
    FOREIGN KEY (SectorTypeId) REFERENCES SectorTypes(Id) ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS Holdings (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PortfolioId INTEGER NOT NULL,
    AccountId INTEGER NOT NULL,
    SecurityId INTEGER NOT NULL,
    IsDripEnabled INTEGER NOT NULL DEFAULT 0 CHECK (IsDripEnabled IN (0, 1)),
    UNIQUE (PortfolioId, AccountId, SecurityId),
    FOREIGN KEY (PortfolioId) REFERENCES Portfolios(Id) ON DELETE CASCADE,
    FOREIGN KEY (AccountId) REFERENCES Accounts(Id) ON DELETE CASCADE,
    FOREIGN KEY (SecurityId) REFERENCES Securities(Id) ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS DividendPayments (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    HoldingId INTEGER NOT NULL,
    PaymentDate TEXT NOT NULL,
    ExDividendDate TEXT NULL,
    Amount TEXT NOT NULL,
    IsReinvested INTEGER NOT NULL DEFAULT 0 CHECK (IsReinvested IN (0, 1)),
    Notes TEXT NOT NULL DEFAULT '',
    FOREIGN KEY (HoldingId) REFERENCES Holdings(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS MarketBuys (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    HoldingId INTEGER NOT NULL,
    NumberOfShares TEXT NOT NULL,
    PricePerShare TEXT NOT NULL,
    PurchaseDate TEXT NOT NULL,
    IsDrip INTEGER NOT NULL DEFAULT 0 CHECK (IsDrip IN (0, 1)),
    DividendPaymentId INTEGER NULL,
    FOREIGN KEY (HoldingId) REFERENCES Holdings(Id) ON DELETE CASCADE,
    FOREIGN KEY (DividendPaymentId) REFERENCES DividendPayments(Id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS AppSettings (
    Key TEXT PRIMARY KEY,
    Value TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_Accounts_PortfolioId ON Accounts(PortfolioId);
CREATE INDEX IF NOT EXISTS IX_Holdings_PortfolioId ON Holdings(PortfolioId);
CREATE INDEX IF NOT EXISTS IX_Holdings_AccountId ON Holdings(AccountId);
CREATE INDEX IF NOT EXISTS IX_DividendPayments_HoldingId ON DividendPayments(HoldingId);
CREATE INDEX IF NOT EXISTS IX_MarketBuys_HoldingId ON MarketBuys(HoldingId);
```

### Default Seed Data (`INSERT OR IGNORE`)
- `InvestmentTypes`: `Stock`, `ETF`, `REIT`, `BDC`, `Preferred Stock`
- `SectorTypes`: `Technology`, `Financials`, `Healthcare`, `Consumer Staples`, `Consumer Discretionary`, `Energy`, `Utilities`, `Real Estate`, `Industrials`, `Communication Services`, `Materials`
- `IndustryTypes`: `General`

---

## 3. Behavioral Acceptance Criteria (Gherkin)

```gherkin
Feature: SQLite Persistence Foundation & Schema Bootstrap

  Scenario: Connection factory opens SQLite connection with foreign keys enabled
    Given a SqliteConnectionFactory configured with a temporary database file path
    When CreateOpenConnectionAsync is called
    Then the returned connection is open
    And PRAGMA foreign_keys returns 1
    And the parent directory of the database file exists

  Scenario: Connection factory rejects blank connection string
    Given a SqliteOptions instance with a whitespace connection string
    When SqliteConnectionFactory is constructed
    Then an ArgumentException is thrown

  Scenario: Database initializer creates all v1 tables and seeds default taxonomies
    Given an empty temporary SQLite database
    When InitializeAsync is called
    Then sqlite_master contains tables Portfolios, Accounts, InvestmentTypes, IndustryTypes, SectorTypes, Securities, Holdings, DividendPayments, MarketBuys, and AppSettings
    And InvestmentTypes contains "Stock", "ETF", "REIT", "BDC", and "Preferred Stock"
    And SectorTypes contains 11 default sectors
    And IndustryTypes contains "General"

  Scenario: Database initializer is idempotent across repeated runs
    Given a temporary SQLite database that has already been initialized
    When InitializeAsync is called a second time
    Then it completes without error
    And InvestmentTypes, SectorTypes, and IndustryTypes row counts remain unchanged

  Scenario: Foreign key constraints and exact decimal precision are enforced
    Given an initialized temporary SQLite database
    When a decimal value "123.456789" is stored and read back via Dapper in Securities.LastPrice
    Then the exact decimal 123.456789m is returned
    And inserting an Account referencing a non-existent PortfolioId fails with SqliteException
```

---

## 4. Implementation Subtasks

- [ ] **Red**: Generate xUnit + FluentAssertions tests covering all Gherkin scenarios (`qwen2.5-coder:7b`)
- [ ] **Green**: Add SQLite/Dapper packages, implement persistence contracts/bootstrapper, and wire DI in Web & MAUI hosts (`qwen2.5-coder:7b`)
- [ ] **Verify**: Pass `dotnet build -warnaserror` and `dotnet test` (local fixes via `qwen3.5:9b`, max 2 retries)
- [ ] **Pass-1 Review**: Run local `qwen3.5:9b` review on `git diff --staged`
- [ ] **Pass-2 Audit**: Submit `git diff --staged` for Gemini architectural & security sign-off

