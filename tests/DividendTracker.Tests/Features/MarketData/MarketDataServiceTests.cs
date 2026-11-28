using DividendTracker.Core.Features.Dividends;
using DividendTracker.Core.Features.Holdings;
using DividendTracker.Core.Features.MarketData;
using DividendTracker.Core.Features.Portfolios;
using DividendTracker.Core.Features.Securities;
using DividendTracker.Core.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using NSubstitute;
using Xunit;

namespace DividendTracker.Tests.Features.MarketData;

public sealed class MarketDataServiceTests : IDisposable
{
    private sealed class ControllableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ControllableTimeProvider(DateTimeOffset startUtc)
        {
            _utcNow = startUtc;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }

    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"zcdt_marketdata_tests_{Guid.NewGuid():N}");

    private async Task<(
        MarketDataService MarketData,
        IMarketDataProvider YahooProvider,
        ControllableTimeProvider Clock,
        SecurityService Securities,
        PortfolioService Portfolios,
        HoldingService Holdings,
        DividendService Dividends)> CreateContextAsync()
    {
        var dbPath = Path.Combine(_tempDirectory, $"{Guid.NewGuid():N}.db");
        var factory = new SqliteConnectionFactory(new SqliteOptions($"Data Source={dbPath}"));
        var initializer = new SqliteDatabaseInitializer(factory);
        await initializer.InitializeAsync();

        var clock = new ControllableTimeProvider(new DateTimeOffset(2026, 3, 31, 12, 0, 0, TimeSpan.Zero));
        var yahooProvider = Substitute.For<IMarketDataProvider>();
        yahooProvider.ProviderType.Returns(MarketDataProviderType.YahooFinance);
        yahooProvider.DisplayName.Returns("Yahoo Finance");
        yahooProvider.RequiresApiKey.Returns(false);

        var marketDataService = new MarketDataService(factory, [yahooProvider], clock);

        return (
            marketDataService,
            yahooProvider,
            clock,
            new SecurityService(factory, clock),
            new PortfolioService(factory, clock),
            new HoldingService(factory),
            new DividendService(factory));
    }

    private static async Task<SecurityDto> CreateSecurityAsync(
        SecurityService securityService,
        string symbol,
        string name,
        decimal lastPrice = 0m)
    {
        var lookups = await securityService.GetLookupsAsync();
        return await securityService.CreateSecurityAsync(new CreateSecurityRequest(
            symbol,
            name,
            lookups.InvestmentTypes[0].Id,
            lookups.IndustryTypes[0].Id,
            lookups.SectorTypes[0].Id,
            lastPrice));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RefreshSecurityQuoteAsync_CachesInSqlite_AndRespectsTtlAndForceRefresh()
    {
        // Arrange
        var (marketData, provider, clock, securities, _, _, _) = await CreateContextAsync();
        var security = await CreateSecurityAsync(securities, "O", "Realty Income", 50.00m);

        provider.GetQuoteAsync("O", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => new MarketQuoteDto("O", 58.25m, "Realty Income", "Yahoo Finance", clock.GetUtcNow(), false),
                _ => new MarketQuoteDto("O", 59.10m, "Realty Income", "Yahoo Finance", clock.GetUtcNow(), false),
                _ => new MarketQuoteDto("O", 60.00m, "Realty Income", "Yahoo Finance", clock.GetUtcNow(), false));

        // Act 1 — Initial refresh hits provider
        var first = await marketData.RefreshSecurityQuoteAsync(security.Id);

        // Assert 1
        first.Price.Should().Be(58.25m);
        first.FromCache.Should().BeFalse();
        await provider.Received(1).GetQuoteAsync("O", Arg.Any<string?>(), Arg.Any<CancellationToken>());
        (await securities.GetSecurityByIdAsync(security.Id))!.LastPrice.Should().Be(58.25m);

        // Act 2 — 5 minutes later (within default 15 min TTL) returns cached
        clock.Advance(TimeSpan.FromMinutes(5));
        var cached = await marketData.RefreshSecurityQuoteAsync(security.Id, forceRefresh: false);

        // Assert 2
        cached.Price.Should().Be(58.25m);
        cached.FromCache.Should().BeTrue();
        await provider.Received(1).GetQuoteAsync("O", Arg.Any<string?>(), Arg.Any<CancellationToken>());

        // Act 3 — forceRefresh bypasses cache
        var forced = await marketData.RefreshSecurityQuoteAsync(security.Id, forceRefresh: true);

        // Assert 3
        forced.Price.Should().Be(59.10m);
        forced.FromCache.Should().BeFalse();
        await provider.Received(2).GetQuoteAsync("O", Arg.Any<string?>(), Arg.Any<CancellationToken>());

        // Act 4 — 20 minutes later (past 15 min TTL) refreshes automatically
        clock.Advance(TimeSpan.FromMinutes(20));
        var expired = await marketData.RefreshSecurityQuoteAsync(security.Id, forceRefresh: false);

        // Assert 4
        expired.Price.Should().Be(60.00m);
        expired.FromCache.Should().BeFalse();
        await provider.Received(3).GetQuoteAsync("O", Arg.Any<string?>(), Arg.Any<CancellationToken>());
        (await securities.GetSecurityByIdAsync(security.Id))!.LastPrice.Should().Be(60.00m);
    }

    [Fact]
    public async Task RefreshAllQuotesAsync_IsolatesPerSymbolErrors_AndReportsTotals()
    {
        // Arrange
        var (marketData, provider, clock, securities, _, _, _) = await CreateContextAsync();
        var schd = await CreateSecurityAsync(securities, "SCHD", "Schwab US Dividend Equity ETF", 75.00m);
        _ = await CreateSecurityAsync(securities, "BAD", "Broken Ticker", 10.00m);

        provider.GetQuoteAsync("SCHD", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new MarketQuoteDto("SCHD", 82.45m, "Schwab US Dividend Equity ETF", "Yahoo Finance", clock.GetUtcNow(), false));
        provider.GetQuoteAsync("BAD", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<MarketQuoteDto>(_ => throw new HttpRequestException("404 Not Found"));

        // Act
        var result = await marketData.RefreshAllQuotesAsync();

        // Assert
        result.UpdatedCount.Should().Be(1);
        result.CachedCount.Should().Be(0);
        result.FailedCount.Should().Be(1);
        result.Errors.Should().ContainSingle(e => e.Contains("BAD", StringComparison.OrdinalIgnoreCase));
        (await securities.GetSecurityByIdAsync(schd.Id))!.LastPrice.Should().Be(82.45m);
    }

    [Fact]
    public async Task ImportDividendsForHoldingAsync_UsesSharesHeldOnExDate_AndSkipsDuplicates()
    {
        // Arrange
        var (marketData, provider, _, securities, portfolios, holdings, dividends) = await CreateContextAsync();
        var portfolio = await portfolios.CreatePortfolioAsync(new CreatePortfolioRequest("Core", "Main", false));
        var account = await portfolios.CreateAccountAsync(new CreateAccountRequest(portfolio.Id, "Taxable", "Taxable"));
        var security = await CreateSecurityAsync(securities, "SCHD", "Schwab US Dividend Equity ETF", 80.00m);
        var holding = await holdings.CreateHoldingAsync(new CreateHoldingRequest(portfolio.Id, account.Id, security.Id, false));
        await holdings.CreateMarketBuyAsync(new CreateMarketBuyRequest(holding.Id, 10m, 75.00m, new DateOnly(2026, 1, 10)));

        provider.GetDividendHistoryAsync(
                "SCHD",
                new DateOnly(2025, 12, 1),
                new DateOnly(2026, 3, 31),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<ExternalDividendEventDto>
            {
                new("SCHD", new DateOnly(2025, 12, 15), new DateOnly(2025, 12, 15), 0.75m, "Yahoo Finance"),
                new("SCHD", new DateOnly(2026, 3, 20), new DateOnly(2026, 3, 20), 0.80m, "Yahoo Finance")
            });

        // Act 1 — First import
        var firstImport = await marketData.ImportDividendsForHoldingAsync(
            holding.Id,
            new DateOnly(2025, 12, 1),
            new DateOnly(2026, 3, 31));

        // Assert 1
        firstImport.ImportedCount.Should().Be(1);
        firstImport.SkippedCount.Should().Be(1);
        firstImport.Errors.Should().BeEmpty();

        var savedDividends = await dividends.GetDividendsForHoldingAsync(holding.Id);
        savedDividends.Should().ContainSingle();
        savedDividends[0].PaymentDate.Should().Be(new DateOnly(2026, 3, 20));
        savedDividends[0].Amount.Should().Be(8.00m);

        // Act 2 — Second import is idempotent
        var secondImport = await marketData.ImportDividendsForHoldingAsync(
            holding.Id,
            new DateOnly(2025, 12, 1),
            new DateOnly(2026, 3, 31));

        // Assert 2
        secondImport.ImportedCount.Should().Be(0);
        secondImport.SkippedCount.Should().Be(2);
        (await dividends.GetDividendsForHoldingAsync(holding.Id)).Should().ContainSingle();
    }
}

