using Dapper;
using DividendTracker.Core.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DividendTracker.Tests.Infrastructure.Persistence;

public sealed class SqliteDatabaseInitializerTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"zcdt_init_tests_{Guid.NewGuid():N}");

    private string CreateDatabasePath() =>
        Path.Combine(_tempDirectory, $"{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task InitializeAsync_CreatesAllTablesAndSeedsDefaultTaxonomies()
    {
        // Arrange
        var options = new SqliteOptions($"Data Source={CreateDatabasePath()}");
        var factory = new SqliteConnectionFactory(options);
        var initializer = new SqliteDatabaseInitializer(factory);

        // Act
        await initializer.InitializeAsync();

        // Assert
        await using var connection = await factory.CreateOpenConnectionAsync();
        var tables = (await connection.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type = 'table';")).ToList();

        tables.Should().Contain([
            "Portfolios",
            "Accounts",
            "InvestmentTypes",
            "IndustryTypes",
            "SectorTypes",
            "Securities",
            "Holdings",
            "DividendPayments",
            "MarketBuys",
            "AppSettings"
        ]);

        var investmentTypes = (await connection.QueryAsync<string>(
            "SELECT Name FROM InvestmentTypes;")).ToList();
        investmentTypes.Should().Contain(["Stock", "ETF", "REIT", "BDC", "Preferred Stock"]);

        var sectorTypes = (await connection.QueryAsync<string>(
            "SELECT Name FROM SectorTypes;")).ToList();
        sectorTypes.Should().HaveCount(11);

        var industryTypes = (await connection.QueryAsync<string>(
            "SELECT Name FROM IndustryTypes;")).ToList();
        industryTypes.Should().Contain("General");
    }

    [Fact]
    public async Task InitializeAsync_CalledTwice_IsIdempotent()
    {
        // Arrange
        var options = new SqliteOptions($"Data Source={CreateDatabasePath()}");
        var factory = new SqliteConnectionFactory(options);
        var initializer = new SqliteDatabaseInitializer(factory);

        // Act
        await initializer.InitializeAsync();
        await initializer.InitializeAsync();

        // Assert
        await using var connection = await factory.CreateOpenConnectionAsync();
        var investmentTypeCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM InvestmentTypes;");
        var sectorTypeCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SectorTypes;");
        var industryTypeCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM IndustryTypes;");

        investmentTypeCount.Should().Be(5);
        sectorTypeCount.Should().Be(11);
        industryTypeCount.Should().Be(1);
    }

    [Fact]
    public async Task InitializeAsync_EnforcesForeignKeysAndExactDecimalPrecision()
    {
        // Arrange
        var options = new SqliteOptions($"Data Source={CreateDatabasePath()}");
        var factory = new SqliteConnectionFactory(options);
        var initializer = new SqliteDatabaseInitializer(factory);
        await initializer.InitializeAsync();

        await using var connection = await factory.CreateOpenConnectionAsync();
        var investmentTypeId = await connection.ExecuteScalarAsync<long>("SELECT Id FROM InvestmentTypes LIMIT 1;");
        var industryTypeId = await connection.ExecuteScalarAsync<long>("SELECT Id FROM IndustryTypes LIMIT 1;");
        var sectorTypeId = await connection.ExecuteScalarAsync<long>("SELECT Id FROM SectorTypes LIMIT 1;");

        // Act
        await connection.ExecuteAsync(
            """
            INSERT INTO Securities (Symbol, Name, InvestmentTypeId, IndustryTypeId, SectorTypeId, LastPrice)
            VALUES (@Symbol, @Name, @InvestmentTypeId, @IndustryTypeId, @SectorTypeId, @LastPrice);
            """,
            new
            {
                Symbol = "AAPL",
                Name = "Apple Inc.",
                InvestmentTypeId = investmentTypeId,
                IndustryTypeId = industryTypeId,
                SectorTypeId = sectorTypeId,
                LastPrice = 123.456789m
            });

        var lastPrice = await connection.QuerySingleAsync<decimal>(
            "SELECT LastPrice FROM Securities WHERE Symbol = @Symbol;",
            new { Symbol = "AAPL" });

        var invalidInsertAct = async () => await connection.ExecuteAsync(
            """
            INSERT INTO Accounts (PortfolioId, Name, AccountType, CreatedUtc)
            VALUES (@PortfolioId, @Name, @AccountType, @CreatedUtc);
            """,
            new
            {
                PortfolioId = 999999,
                Name = "Orphan Account",
                AccountType = "Taxable",
                CreatedUtc = "2026-01-01T00:00:00Z"
            });

        // Assert
        lastPrice.Should().Be(123.456789m);
        await invalidInsertAct.Should().ThrowAsync<SqliteException>();
    }
}

