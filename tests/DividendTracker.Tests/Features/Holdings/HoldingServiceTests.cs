using DividendTracker.Core.Features.Holdings;
using DividendTracker.Core.Features.Portfolios;
using DividendTracker.Core.Features.Securities;
using DividendTracker.Core.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DividendTracker.Tests.Features.Holdings;

public sealed class HoldingServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"zcdt_holding_tests_{Guid.NewGuid():N}");

    private async Task<(HoldingService Holdings, PortfolioService Portfolios, SecurityService Securities)> CreateInitializedServicesAsync()
    {
        var dbPath = Path.Combine(_tempDirectory, $"{Guid.NewGuid():N}.db");
        var factory = new SqliteConnectionFactory(new SqliteOptions($"Data Source={dbPath}"));
        var initializer = new SqliteDatabaseInitializer(factory);
        await initializer.InitializeAsync();

        return (
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
    public async Task CreateHoldingAndMarketBuysAsync_ComputesExactPositionTotalsAndOrdersBuysDescending()
    {
        // Arrange
        var (holdingService, portfolioService, securityService) = await CreateInitializedServicesAsync();
        var portfolio = await portfolioService.CreatePortfolioAsync(
            new CreatePortfolioRequest("Core Income", "Primary dividend portfolio", false));
        var account = await portfolioService.CreateAccountAsync(
            new CreateAccountRequest(portfolio.Id, "Fidelity Roth", "Roth IRA"));
        var security = await CreateSecurityAsync(securityService, "SCHD", "Schwab US Dividend Equity ETF", 80.00m);

        // Act
        var createdHolding = await holdingService.CreateHoldingAsync(
            new CreateHoldingRequest(portfolio.Id, account.Id, security.Id, true));
        var buy1 = await holdingService.CreateMarketBuyAsync(
            new CreateMarketBuyRequest(createdHolding.Id, 10.5m, 75.00m, new DateOnly(2026, 1, 15)));
        var buy2 = await holdingService.CreateMarketBuyAsync(
            new CreateMarketBuyRequest(createdHolding.Id, 4.5m, 78.00m, new DateOnly(2026, 2, 15)));

        var holdings = await holdingService.GetHoldingsAsync();

        // Assert
        holdings.Should().ContainSingle();
        var holding = holdings[0];
        holding.Id.Should().Be(createdHolding.Id);
        holding.PortfolioName.Should().Be("Core Income");
        holding.AccountName.Should().Be("Fidelity Roth");
        holding.SecuritySymbol.Should().Be("SCHD");
        holding.IsDripEnabled.Should().BeTrue();
        holding.TotalShares.Should().Be(15.0m);
        holding.TotalCostBasis.Should().Be(1138.50m);
        holding.AverageCostPerShare.Should().Be(75.90m);
        holding.MarketValue.Should().Be(1200.00m);
        holding.MarketBuys.Should().HaveCount(2);
        holding.MarketBuys[0].Id.Should().Be(buy2.Id);
        holding.MarketBuys[0].PurchaseDate.Should().Be(new DateOnly(2026, 2, 15));
        holding.MarketBuys[1].Id.Should().Be(buy1.Id);
        holding.MarketBuys[1].PurchaseDate.Should().Be(new DateOnly(2026, 1, 15));
    }

    [Fact]
    public async Task FilterByPortfolioAndUpdateHoldingAndBuyAsync_ReturnsFilteredAndRecalculatedValues()
    {
        // Arrange
        var (holdingService, portfolioService, securityService) = await CreateInitializedServicesAsync();
        var portfolio1 = await portfolioService.CreatePortfolioAsync(new CreatePortfolioRequest("Core Income", null, false));
        var account1 = await portfolioService.CreateAccountAsync(new CreateAccountRequest(portfolio1.Id, "Fidelity Roth", "Roth IRA"));
        var portfolio2 = await portfolioService.CreatePortfolioAsync(new CreatePortfolioRequest("Watchlist", null, true));
        var account2 = await portfolioService.CreateAccountAsync(new CreateAccountRequest(portfolio2.Id, "Model Account", "Model"));

        var schd = await CreateSecurityAsync(securityService, "SCHD", "Schwab ETF", 80.00m);
        var realtyIncome = await CreateSecurityAsync(securityService, "O", "Realty Income", 60.00m);

        var holding1 = await holdingService.CreateHoldingAsync(new CreateHoldingRequest(portfolio1.Id, account1.Id, schd.Id, true));
        var buy1 = await holdingService.CreateMarketBuyAsync(new CreateMarketBuyRequest(holding1.Id, 10.0m, 75.00m, new DateOnly(2026, 1, 10)));

        var holding2 = await holdingService.CreateHoldingAsync(new CreateHoldingRequest(portfolio2.Id, account2.Id, realtyIncome.Id, false));
        await holdingService.CreateMarketBuyAsync(new CreateMarketBuyRequest(holding2.Id, 5.0m, 55.00m, new DateOnly(2026, 1, 12)));

        // Act
        var filtered = await holdingService.GetHoldingsAsync(portfolio1.Id);
        var updatedHolding = await holdingService.UpdateHoldingAsync(new UpdateHoldingRequest(holding1.Id, false));
        await holdingService.UpdateMarketBuyAsync(new UpdateMarketBuyRequest(buy1.Id, 20.0m, 75.00m, new DateOnly(2026, 1, 10), false));
        var refreshed = await holdingService.GetHoldingsAsync(portfolio1.Id);

        // Assert
        filtered.Should().ContainSingle(h => h.Id == holding1.Id);
        updatedHolding.IsDripEnabled.Should().BeFalse();
        refreshed.Should().ContainSingle();
        refreshed[0].IsDripEnabled.Should().BeFalse();
        refreshed[0].TotalShares.Should().Be(20.0m);
        refreshed[0].TotalCostBasis.Should().Be(1500.00m);
        refreshed[0].MarketValue.Should().Be(1600.00m);
    }

    [Fact]
    public async Task CreateHoldingAsync_WithMismatchedAccountOrDuplicate_ThrowsExpectedException()
    {
        // Arrange
        var (holdingService, portfolioService, securityService) = await CreateInitializedServicesAsync();
        var portfolioA = await portfolioService.CreatePortfolioAsync(new CreatePortfolioRequest("Portfolio A", null, false));
        var accountA1 = await portfolioService.CreateAccountAsync(new CreateAccountRequest(portfolioA.Id, "Account A1", "Taxable"));
        var portfolioB = await portfolioService.CreatePortfolioAsync(new CreatePortfolioRequest("Portfolio B", null, false));
        var accountB1 = await portfolioService.CreateAccountAsync(new CreateAccountRequest(portfolioB.Id, "Account B1", "IRA"));
        var security = await CreateSecurityAsync(securityService, "O", "Realty Income", 58.00m);

        // Act
        var mismatchAct = async () => await holdingService.CreateHoldingAsync(
            new CreateHoldingRequest(portfolioA.Id, accountB1.Id, security.Id, false));

        await holdingService.CreateHoldingAsync(
            new CreateHoldingRequest(portfolioA.Id, accountA1.Id, security.Id, false));
        var duplicateAct = async () => await holdingService.CreateHoldingAsync(
            new CreateHoldingRequest(portfolioA.Id, accountA1.Id, security.Id, true));

        // Assert
        await mismatchAct.Should().ThrowAsync<KeyNotFoundException>();
        await duplicateAct.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(0, 75.00)]
    [InlineData(-2.5, 75.00)]
    [InlineData(5.0, -5.00)]
    public async Task CreateMarketBuyAsync_WithInvalidQuantityOrPrice_ThrowsArgumentOutOfRangeExceptionAndInsertsNothing(
        decimal shares,
        decimal price)
    {
        // Arrange
        var (holdingService, portfolioService, securityService) = await CreateInitializedServicesAsync();
        var portfolio = await portfolioService.CreatePortfolioAsync(new CreatePortfolioRequest("Core Income", null, false));
        var account = await portfolioService.CreateAccountAsync(new CreateAccountRequest(portfolio.Id, "Fidelity Roth", "Roth IRA"));
        var security = await CreateSecurityAsync(securityService, "SCHD", "Schwab ETF", 80.00m);
        var holding = await holdingService.CreateHoldingAsync(new CreateHoldingRequest(portfolio.Id, account.Id, security.Id, false));

        // Act
        var act = async () => await holdingService.CreateMarketBuyAsync(
            new CreateMarketBuyRequest(holding.Id, shares, price, new DateOnly(2026, 3, 1)));

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        var holdings = await holdingService.GetHoldingsAsync();
        holdings.Should().ContainSingle();
        holdings[0].MarketBuys.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteHoldingAsync_DeletesHoldingAndCascadesMarketBuys()
    {
        // Arrange
        var (holdingService, portfolioService, securityService) = await CreateInitializedServicesAsync();
        var portfolio = await portfolioService.CreatePortfolioAsync(new CreatePortfolioRequest("Core Income", null, false));
        var account = await portfolioService.CreateAccountAsync(new CreateAccountRequest(portfolio.Id, "Fidelity Roth", "Roth IRA"));
        var security = await CreateSecurityAsync(securityService, "SCHD", "Schwab ETF", 80.00m);
        var holding = await holdingService.CreateHoldingAsync(new CreateHoldingRequest(portfolio.Id, account.Id, security.Id, true));

        await holdingService.CreateMarketBuyAsync(new CreateMarketBuyRequest(holding.Id, 5.0m, 75.00m, new DateOnly(2026, 1, 15)));
        await holdingService.CreateMarketBuyAsync(new CreateMarketBuyRequest(holding.Id, 5.0m, 78.00m, new DateOnly(2026, 2, 15)));

        // Act
        var deleted = await holdingService.DeleteHoldingAsync(holding.Id);
        var holdings = await holdingService.GetHoldingsAsync();

        // Assert
        deleted.Should().BeTrue();
        holdings.Should().BeEmpty();
    }
}

