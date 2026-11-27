using DividendTracker.Core.Features.Dividends;
using DividendTracker.Core.Features.Holdings;
using DividendTracker.Core.Features.Portfolios;
using DividendTracker.Core.Features.Securities;
using DividendTracker.Core.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DividendTracker.Tests.Features.Dividends;

public sealed class DividendServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"zcdt_dividend_tests_{Guid.NewGuid():N}");

    private async Task<(DividendService Dividends, HoldingService Holdings, PortfolioService Portfolios, SecurityService Securities)> CreateInitializedServicesAsync()
    {
        var dbPath = Path.Combine(_tempDirectory, $"{Guid.NewGuid():N}.db");
        var factory = new SqliteConnectionFactory(new SqliteOptions($"Data Source={dbPath}"));
        var initializer = new SqliteDatabaseInitializer(factory);
        await initializer.InitializeAsync();

        return (
            new DividendService(factory),
            new HoldingService(factory),
            new PortfolioService(factory, TimeProvider.System),
            new SecurityService(factory, TimeProvider.System));
    }

    private static async Task<SecurityDto> CreateSecurityAsync(
        SecurityService securityService,
        string symbol,
        string name,
        decimal lastPrice)
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
    public async Task RecordDividendAsync_CashPayout_OrdersDescendingAndFiltersByPortfolioAndHolding()
    {
        // Arrange
        var (dividendService, holdingService, portfolioService, securityService) = await CreateInitializedServicesAsync();
        var portfolio1 = await portfolioService.CreatePortfolioAsync(new CreatePortfolioRequest("Core Income", null, false));
        var account1 = await portfolioService.CreateAccountAsync(new CreateAccountRequest(portfolio1.Id, "Fidelity Roth", "Roth IRA"));
        var portfolio2 = await portfolioService.CreatePortfolioAsync(new CreatePortfolioRequest("Taxable", null, false));
        var account2 = await portfolioService.CreateAccountAsync(new CreateAccountRequest(portfolio2.Id, "Schwab", "Taxable"));

        var schd = await CreateSecurityAsync(securityService, "SCHD", "Schwab US Dividend Equity ETF", 80.00m);
        var realtyIncome = await CreateSecurityAsync(securityService, "O", "Realty Income", 60.00m);

        var holding1 = await holdingService.CreateHoldingAsync(new CreateHoldingRequest(portfolio1.Id, account1.Id, schd.Id, false));
        var holding2 = await holdingService.CreateHoldingAsync(new CreateHoldingRequest(portfolio2.Id, account2.Id, realtyIncome.Id, false));

        // Act
        var div1 = await dividendService.RecordDividendAsync(new RecordDividendRequest(
            holding1.Id,
            new DateOnly(2026, 3, 15),
            new DateOnly(2026, 3, 1),
            25.50m,
            false,
            "Q1 payout"));

        var div2 = await dividendService.RecordDividendAsync(new RecordDividendRequest(
            holding2.Id,
            new DateOnly(2026, 3, 20),
            null,
            12.00m,
            false,
            "March monthly"));

        var allDividends = await dividendService.GetDividendsAsync();
        var byPortfolio = await dividendService.GetDividendsAsync(portfolioId: portfolio1.Id);
        var byHolding = await dividendService.GetDividendsAsync(holdingId: holding1.Id);

        // Assert
        allDividends.Should().HaveCount(2);
        allDividends[0].Id.Should().Be(div2.Id);
        allDividends[1].Id.Should().Be(div1.Id);

        byPortfolio.Should().ContainSingle();
        byPortfolio[0].Id.Should().Be(div1.Id);
        byPortfolio[0].Amount.Should().Be(25.50m);
        byPortfolio[0].ExDividendDate.Should().Be(new DateOnly(2026, 3, 1));
        byPortfolio[0].Notes.Should().Be("Q1 payout");
        byPortfolio[0].IsReinvested.Should().BeFalse();
        byPortfolio[0].DripMarketBuyId.Should().BeNull();

        byHolding.Should().ContainSingle(d => d.Id == div1.Id);
    }

    [Fact]
    public async Task RecordDividendAsync_WhenReinvestedWithNullShares_CreatesLinkedDripMarketBuyAndComputesShares()
    {
        // Arrange
        var (dividendService, holdingService, portfolioService, securityService) = await CreateInitializedServicesAsync();
        var portfolio = await portfolioService.CreatePortfolioAsync(new CreatePortfolioRequest("Core Income", null, false));
        var account = await portfolioService.CreateAccountAsync(new CreateAccountRequest(portfolio.Id, "Fidelity Roth", "Roth IRA"));
        var schd = await CreateSecurityAsync(securityService, "SCHD", "Schwab US Dividend Equity ETF", 80.00m);
        var holding = await holdingService.CreateHoldingAsync(new CreateHoldingRequest(portfolio.Id, account.Id, schd.Id, true));
        await holdingService.CreateMarketBuyAsync(new CreateMarketBuyRequest(holding.Id, 10.0m, 75.00m, new DateOnly(2026, 1, 15)));

        // Act
        var dividend = await dividendService.RecordDividendAsync(new RecordDividendRequest(
            holding.Id,
            new DateOnly(2026, 3, 25),
            new DateOnly(2026, 3, 18),
            40.00m,
            true,
            "Q1 DRIP",
            DripShares: null,
            DripPricePerShare: 80.00m));

        var holdings = await holdingService.GetHoldingsAsync(portfolio.Id);

        // Assert
        dividend.IsReinvested.Should().BeTrue();
        dividend.DripMarketBuyId.Should().NotBeNull();
        dividend.DripShares.Should().Be(0.5m);
        dividend.DripPricePerShare.Should().Be(80.00m);

        holdings.Should().ContainSingle();
        holdings[0].TotalShares.Should().Be(10.5m);
        holdings[0].MarketBuys.Should().ContainSingle(b =>
            b.Id == dividend.DripMarketBuyId &&
            b.IsDrip &&
            b.DividendPaymentId == dividend.Id &&
            b.NumberOfShares == 0.5m &&
            b.PricePerShare == 80.00m);
    }

    [Fact]
    public async Task UpdateDividendAsync_TogglingDripOnAndOff_SynchronizesLinkedDripMarketBuy()
    {
        // Arrange
        var (dividendService, holdingService, portfolioService, securityService) = await CreateInitializedServicesAsync();
        var portfolio = await portfolioService.CreatePortfolioAsync(new CreatePortfolioRequest("Core Income", null, false));
        var account = await portfolioService.CreateAccountAsync(new CreateAccountRequest(portfolio.Id, "Fidelity Roth", "Roth IRA"));
        var schd = await CreateSecurityAsync(securityService, "SCHD", "Schwab US Dividend Equity ETF", 80.00m);
        var holding = await holdingService.CreateHoldingAsync(new CreateHoldingRequest(portfolio.Id, account.Id, schd.Id, false));

        var cashDividend = await dividendService.RecordDividendAsync(new RecordDividendRequest(
            holding.Id,
            new DateOnly(2026, 3, 25),
            null,
            30.00m,
            false,
            "Initial cash"));

        // Act - Toggle DRIP ON
        var reinvested = await dividendService.UpdateDividendAsync(new UpdateDividendRequest(
            cashDividend.Id,
            new DateOnly(2026, 3, 25),
            null,
            30.00m,
            true,
            "Switched to DRIP",
            DripShares: 0.4m,
            DripPricePerShare: 75.00m));

        var holdingsAfterDripOn = await holdingService.GetHoldingsAsync(portfolio.Id);

        // Act - Toggle DRIP OFF
        var revertedToCash = await dividendService.UpdateDividendAsync(new UpdateDividendRequest(
            cashDividend.Id,
            new DateOnly(2026, 3, 25),
            null,
            30.00m,
            false,
            "Back to cash"));

        var holdingsAfterDripOff = await holdingService.GetHoldingsAsync(portfolio.Id);

        // Assert
        reinvested.IsReinvested.Should().BeTrue();
        reinvested.DripMarketBuyId.Should().NotBeNull();
        reinvested.DripShares.Should().Be(0.4m);
        reinvested.DripPricePerShare.Should().Be(75.00m);
        holdingsAfterDripOn[0].MarketBuys.Should().ContainSingle(b => b.DividendPaymentId == cashDividend.Id && b.IsDrip);

        revertedToCash.IsReinvested.Should().BeFalse();
        revertedToCash.DripMarketBuyId.Should().BeNull();
        revertedToCash.DripShares.Should().BeNull();
        holdingsAfterDripOff[0].MarketBuys.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordDividendAsync_WithInvalidInputsOrMissingHolding_ThrowsExpectedExceptionAndWritesNothing()
    {
        // Arrange
        var (dividendService, holdingService, portfolioService, securityService) = await CreateInitializedServicesAsync();
        var portfolio = await portfolioService.CreatePortfolioAsync(new CreatePortfolioRequest("Core Income", null, false));
        var account = await portfolioService.CreateAccountAsync(new CreateAccountRequest(portfolio.Id, "Fidelity Roth", "Roth IRA"));
        var schd = await CreateSecurityAsync(securityService, "SCHD", "Schwab US Dividend Equity ETF", 80.00m);
        var holding = await holdingService.CreateHoldingAsync(new CreateHoldingRequest(portfolio.Id, account.Id, schd.Id, true));

        // Act
        var zeroAmountAct = async () => await dividendService.RecordDividendAsync(
            new RecordDividendRequest(holding.Id, new DateOnly(2026, 3, 25), null, 0m, false));

        var missingDripPriceAct = async () => await dividendService.RecordDividendAsync(
            new RecordDividendRequest(holding.Id, new DateOnly(2026, 3, 25), null, 20.00m, true, DripPricePerShare: null));

        var zeroDripPriceAct = async () => await dividendService.RecordDividendAsync(
            new RecordDividendRequest(holding.Id, new DateOnly(2026, 3, 25), null, 20.00m, true, DripPricePerShare: 0m));

        var negativeDripSharesAct = async () => await dividendService.RecordDividendAsync(
            new RecordDividendRequest(holding.Id, new DateOnly(2026, 3, 25), null, 20.00m, true, DripShares: -1m, DripPricePerShare: 80.00m));

        var missingHoldingAct = async () => await dividendService.RecordDividendAsync(
            new RecordDividendRequest(999999, new DateOnly(2026, 3, 25), null, 20.00m, false));

        // Assert
        await zeroAmountAct.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await missingDripPriceAct.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await zeroDripPriceAct.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await negativeDripSharesAct.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await missingHoldingAct.Should().ThrowAsync<KeyNotFoundException>();

        var dividends = await dividendService.GetDividendsAsync();
        var holdings = await holdingService.GetHoldingsAsync();
        dividends.Should().BeEmpty();
        holdings[0].MarketBuys.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteDividendAsync_DeletesDripDividendAndLinkedMarketBuyAtomically()
    {
        // Arrange
        var (dividendService, holdingService, portfolioService, securityService) = await CreateInitializedServicesAsync();
        var portfolio = await portfolioService.CreatePortfolioAsync(new CreatePortfolioRequest("Core Income", null, false));
        var account = await portfolioService.CreateAccountAsync(new CreateAccountRequest(portfolio.Id, "Fidelity Roth", "Roth IRA"));
        var schd = await CreateSecurityAsync(securityService, "SCHD", "Schwab US Dividend Equity ETF", 80.00m);
        var holding = await holdingService.CreateHoldingAsync(new CreateHoldingRequest(portfolio.Id, account.Id, schd.Id, true));

        var dripDividend = await dividendService.RecordDividendAsync(new RecordDividendRequest(
            holding.Id,
            new DateOnly(2026, 3, 25),
            null,
            40.00m,
            true,
            "Q1 DRIP",
            DripShares: 0.5m,
            DripPricePerShare: 80.00m));

        // Act
        var deleted = await dividendService.DeleteDividendAsync(dripDividend.Id);
        var dividends = await dividendService.GetDividendsAsync();
        var holdings = await holdingService.GetHoldingsAsync();

        // Assert
        deleted.Should().BeTrue();
        dividends.Should().BeEmpty();
        holdings.Should().ContainSingle();
        holdings[0].MarketBuys.Should().BeEmpty();
    }
}

