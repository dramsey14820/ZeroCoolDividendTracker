using Dapper;

namespace DividendTracker.Core.Infrastructure.Persistence;

public sealed class SqliteDatabaseInitializer : IDatabaseInitializer
{
    private static readonly Lock TypeHandlerLock = new();
    private static bool _typeHandlersRegistered;

    private const string SchemaSql = """
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
        """;

    private const string SeedSql = """
        INSERT OR IGNORE INTO InvestmentTypes (Name)
        VALUES ('Stock'), ('ETF'), ('REIT'), ('BDC'), ('Preferred Stock');

        INSERT OR IGNORE INTO SectorTypes (Name)
        VALUES
            ('Technology'),
            ('Financials'),
            ('Healthcare'),
            ('Consumer Staples'),
            ('Consumer Discretionary'),
            ('Energy'),
            ('Utilities'),
            ('Real Estate'),
            ('Industrials'),
            ('Communication Services'),
            ('Materials');

        INSERT OR IGNORE INTO IndustryTypes (Name)
        VALUES ('General');
        """;

    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteDatabaseInitializer(ISqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        EnsureTypeHandlersRegistered();

        await using var connection = await _connectionFactory
            .CreateOpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition("PRAGMA journal_mode = WAL;", cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition(SchemaSql, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition(SeedSql, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private static void EnsureTypeHandlersRegistered()
    {
        if (_typeHandlersRegistered)
        {
            return;
        }

        lock (TypeHandlerLock)
        {
            if (_typeHandlersRegistered)
            {
                return;
            }

            SqlMapper.AddTypeHandler(new DecimalTextTypeHandler());
            _typeHandlersRegistered = true;
        }
    }
}

