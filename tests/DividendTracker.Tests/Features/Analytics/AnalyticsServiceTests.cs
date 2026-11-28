using DividendTracker.Core.Features.Analytics;
using DividendTracker.Core.Features.Dividends;
using DividendTracker.Core.Features.Holdings;
using DividendTracker.Core.Features.Portfolios;
using DividendTracker.Core.Features.Securities;
using DividendTracker.Core.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DividendTracker.Tests.Features.Analytics;

public sealed class AnalyticsServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"zcdt_analytics_tests_{Guid.NewGuid():N}");

    private async Task<(AnalyticsService Analytics, DividendService Dividends, HoldingService Holdings, PortfolioService Portfolios, SecurityService Securities)> CreateInitializedServicesAsync()
    {
        var dbPath = Path.Combine(_tempDirectory, $"{Guid.NewGuid():N}.db");
        var factory = new SqliteConnectionFactory(new SqliteOptions($"Data Source={dbPath}"));
        var initializer = new SqliteDatabaseInitializer(factory);
        await initializer.InitializeAsync();

        return (
            new AnalyticsService(factory),
            new DividendService(factory),
            new HoldingService(factory),
            new PortfolioService(factory, TimeProvider.System),
            new SecurityService(factory, TimeProvider.System));
    }

    private static async Task<SecurityDto> CreateSecurityAsync(
        SecurityService securityService,
        string symbol,
        string name,
        string sectorName,
        string industryName,
        string investmentTypeName,
        decimal lastPrice)
    {
        var lookups = await securityService.GetLookupsAsync();
        var sector = lookups.SectorTypes.FirstOrDefault(s => s.Name.Equals(sectorName, StringComparison.OrdinalIgnoreCase))
            ?? await securityService.CreateLookupAsync(new CreateLookupRequest(LookupCategory.SectorType, sectorName));
        var industry = lookups.IndustryTypes.FirstOrDefault(i => i.Name.Equals(industryName, StringComparison.OrdinalIgnoreCase))
            ?? await securityService.CreateLookupAsync(new CreateLookupRequest(LookupCategory.IndustryType, industryName));
        var investmentType = lookups.InvestmentTypes.FirstOrDefault(t => t.Name.Equals(investmentTypeName, StringComparison.OrdinalIgnoreCase))
            ?? await securityService.CreateLookupAsync(new CreateLookupRequest(LookupCategory.InvestmentType, investmentTypeName));

        return await securityService.CreateSecurityAsync(new CreateSecurityRequest(
            symbol,
            name,
            investmentType.Id,
            industry.Id,
            sector.Id,
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
    public async Task GetDashboardAsync_ComputesPortfolioAndPerHoldingValuationYieldAndYoC()
    {
        // Arrange
        var (analyticsService, dividendService, holdingService, portfolioService, securityService) = await CreateInitializedServicesAsync();
        var portfolio = await portfolioService.CreatePortfolioAsync(new CreatePortfolioRequest("Core Income", "Primary", false));
        var account = await portfolioService.CreateAccountAsync(new CreateAccountRequest(portfolio.Id, "Fidelity Roth", "Roth IRA"));

        var schd = await CreateSecurityAsync(securityService, "SCHD", "Schwab US Dividend Equity ETF", "Financials", "ETF", "ETF", 80.00m);
        var realtyIncome = await CreateSecurityAsync(securityService, "O", "Realty Income", "Real Estate", "REIT", "REIT", 60.00m);

        var schdHolding = await holdingService.CreateHoldingAsync(new CreateHoldingRequest(portfolio.Id, account.Id, schd.Id, false));
        var oHolding = await holdingService.CreateHoldingAsync(new CreateHoldingRequest(portfolio.Id, account.Id, realtyIncome.Id, false));

        await holdingService.CreateMarketBuyAsync(new CreateMarketBuyRequest(schdHolding.Id, 10m, 75.00m, new DateOnly(2025, 1, 10)));
        await holdingService.CreateMarketBuyAsync(new CreateMarketBuyRequest(oHolding.Id, 20m, 50.00m, new DateOnly(2025, 1, 10)));

        await dividendService.RecordDividendAsync(new RecordDividendRequest(schdHolding.Id, new DateOnly(2025, 9, 15), null, 15.00m, false));
        await dividendService.RecordDividendAsync(new RecordDividendRequest(schdHolding.Id, new DateOnly(2026, 3, 15), null, 15.00m, false));
        await dividendService.RecordDividendAsync(new RecordDividendRequest(oHolding.Id, new DateOnly(2025, 10, 15), null, 30.00m, false));
        await dividendService.RecordDividendAsync(new RecordDividendRequest(oHolding.Id, new DateOnly(2026, 3, 15), null, 30.00m, false));

        var asOf = new DateOnly(2026, 3, 31);

        // Act
        var dashboard = await analyticsService.GetDashboardAsync(portfolio.Id, asOf);

        // Assert
        dashboard.PortfolioId.Should().Be(portfolio.Id);
        dashboard.AsOfDate.Should().Be(asOf);

        dashboard.Summary.TotalMarketValue.Should().Be(2000.00m);
        dashboard.Summary.TotalCostBasis.Should().Be(1750.00m);
        dashboard.Summary.TotalUnrealizedGainLoss.Should().Be(250.00m);
        dashboard.Summary.TotalUnrealizedGainLossPercent.Should().Be(14.29m);
        dashboard.Summary.Trailing12MonthIncome.Should().Be(90.00m);
        dashboard.Summary.ProjectedAnnualIncome.Should().Be(90.00m);
        dashboard.Summary.ProjectedMonthlyIncome.Should().Be(7.50m);
        dashboard.Summary.LifetimeDividendIncome.Should().Be(90.00m);
        dashboard.Summary.PortfolioCurrentYieldPercent.Should().Be(4.50m);
        dashboard.Summary.PortfolioYieldOnCostPercent.Should().Be(5.14m);
        dashboard.Summary.HoldingCount.Should().Be(2);

        dashboard.Holdings.Should().HaveCount(2);
        dashboard.Holdings[0].SecuritySymbol.Should().Be("O");
        dashboard.Holdings[0].MarketValue.Should().Be(1200.00m);
        dashboard.Holdings[0].CurrentYieldPercent.Should().Be(5.00m);
        dashboard.Holdings[0].YieldOnCostPercent.Should().Be(6.00m);

        dashboard.Holdings[1].SecuritySymbol.Should().Be("SCHD");
        dashboard.Holdings[1].MarketValue.Should().Be(800.00m);
        dashboard.Holdings[1].CurrentYieldPercent.Should().Be(3.75m);
        dashboard.Holdings[1].YieldOnCostPercent.Should().Be(4.00m);
    }

    [Fact]
    public async Task GetDashboardAsync_ExcludesOlderThan12MonthsFromTtmButPreservesLifetimeAndMonthlyHistory()
    {
        // Arrange
        var (analyticsService, dividendService, holdingService, portfolioService, securityService) = await CreateInitializedServicesAsync();
        var portfolio = await portfolioService.CreatePortfolioAsync(new CreatePortfolioRequest("Core Income", null, false));
        var account = await portfolioService.CreateAccountAsync(new CreateAccountRequest(portfolio.Id, "Taxable", "Taxable"));
        var schd = await CreateSecurityAsync(securityService, "SCHD", "Schwab US Dividend Equity ETF", "Financials", "ETF", "ETF", 80.00m);
        var holding = await holdingService.CreateHoldingAsync(new CreateHoldingRequest(portfolio.Id, account.Id, schd.Id, true));
        await holdingService.CreateMarketBuyAsync(new CreateMarketBuyRequest(holding.Id, 10m, 75.00m, new DateOnly(2024, 12, 1)));

        await dividendService.RecordDividendAsync(new RecordDividendRequest(holding.Id, new DateOnly(2025, 1, 15), null, 20.00m, false));
        await dividendService.RecordDividendAsync(new RecordDividendRequest(holding.Id, new DateOnly(2025, 6, 15), null, 25.00m, false));
        await dividendService.RecordDividendAsync(new RecordDividendRequest(holding.Id, new DateOnly(2026, 3, 15), null, 35.00m, true, DripPricePerShare: 70.00m));

        // Act
        var dashboard = await analyticsService.GetDashboardAsync(portfolio.Id, new DateOnly(2026, 3, 31));

        // Assert
        dashboard.Summary.Trailing12MonthIncome.Should().Be(60.00m);
        dashboard.Summary.LifetimeDividendIncome.Should().Be(80.00m);

        dashboard.MonthlyIncomeHistory.Should().HaveCount(3);
        dashboard.MonthlyIncomeHistory[0].Should().BeEquivalentTo(new MonthlyIncomePointDto(2025, 1, "2025-01", 20.00m, 20.00m, 0m, 1));
        dashboard.MonthlyIncomeHistory[1].Should().BeEquivalentTo(new MonthlyIncomePointDto(2025, 6, "2025-06", 25.00m, 25.00m, 0m, 1));
        dashboard.MonthlyIncomeHistory[2].Should().BeEquivalentTo(new MonthlyIncomePointDto(2026, 3, "2026-03", 35.00m, 0m, 35.00m, 1));
    }

    [Fact]
    public async Task GetDashboardAsync_ComputesAllocationBreakdownsBySectorIndustryAndInvestmentType()
    {
        // Arrange
        var (analyticsService, _, holdingService, portfolioService, securityService) = await CreateInitializedServicesAsync();
        var portfolio = await portfolioService.CreatePortfolioAsync(new CreatePortfolioRequest("Core Income", null, false));
        var account = await portfolioService.CreateAccountAsync(new CreateAccountRequest(portfolio.Id, "Taxable", "Taxable"));

        var schd = await CreateSecurityAsync(securityService, "SCHD", "Schwab US Dividend Equity ETF", "Financials", "Index ETF", "ETF", 80.00m);
        var realtyIncome = await CreateSecurityAsync(securityService, "O", "Realty Income", "Real Estate", "Retail REIT", "REIT", 60.00m);

        var schdHolding = await holdingService.CreateHoldingAsync(new CreateHoldingRequest(portfolio.Id, account.Id, schd.Id, false));
        var oHolding = await holdingService.CreateHoldingAsync(new CreateHoldingRequest(portfolio.Id, account.Id, realtyIncome.Id, false));

        await holdingService.CreateMarketBuyAsync(new CreateMarketBuyRequest(schdHolding.Id, 10m, 75.00m, new DateOnly(2025, 1, 10)));
        await holdingService.CreateMarketBuyAsync(new CreateMarketBuyRequest(oHolding.Id, 20m, 50.00m, new DateOnly(2025, 1, 10)));

        // Act
        var dashboard = await analyticsService.GetDashboardAsync(portfolio.Id, new DateOnly(2026, 3, 31));

        // Assert
        dashboard.SectorAllocations.Should().HaveCount(2);
        dashboard.SectorAllocations[0].Name.Should().Be("Real Estate");
        dashboard.SectorAllocations[0].MarketValue.Should().Be(1200.00m);
        dashboard.SectorAllocations[0].AllocationPercent.Should().Be(60.00m);
        dashboard.SectorAllocations[1].Name.Should().Be("Financials");
        dashboard.SectorAllocations[1].MarketValue.Should().Be(800.00m);
        dashboard.SectorAllocations[1].AllocationPercent.Should().Be(40.00m);

        dashboard.IndustryAllocations.Sum(a => a.AllocationPercent).Should().Be(100.00m);
        dashboard.InvestmentTypeAllocations.Sum(a => a.AllocationPercent).Should().Be(100.00m);
    }

    [Fact]
    public async Task GetDashboardAsync_HandlesZeroValueHoldingsWithoutDivideByZeroAndRejectsInvalidPortfolioId()
    {
        // Arrange
        var (analyticsService, _, holdingService, portfolioService, securityService) = await CreateInitializedServicesAsync();
        var emptyPortfolio = await portfolioService.CreatePortfolioAsync(new CreatePortfolioRequest("Watchlist", null, true));
        var emptyAccount = await portfolioService.CreateAccountAsync(new CreateAccountRequest(emptyPortfolio.Id, "Model", "Model"));
        var activePortfolio = await portfolioService.CreatePortfolioAsync(new CreatePortfolioRequest("Active", null, false));
        var activeAccount = await portfolioService.CreateAccountAsync(new CreateAccountRequest(activePortfolio.Id, "Taxable", "Taxable"));

        var zeroPriceSecurity = await CreateSecurityAsync(securityService, "ZERO", "Zero Corp", "Technology", "General", "Stock", 0m);
        var activeSecurity = await CreateSecurityAsync(securityService, "SCHD", "Schwab US Dividend Equity ETF", "Financials", "General", "ETF", 80.00m);

        await holdingService.CreateHoldingAsync(new CreateHoldingRequest(emptyPortfolio.Id, emptyAccount.Id, zeroPriceSecurity.Id, false));
        var activeHolding = await holdingService.CreateHoldingAsync(new CreateHoldingRequest(activePortfolio.Id, activeAccount.Id, activeSecurity.Id, false));
        await holdingService.CreateMarketBuyAsync(new CreateMarketBuyRequest(activeHolding.Id, 10m, 75.00m, new DateOnly(2025, 1, 10)));

        // Act
        var dashboard = await analyticsService.GetDashboardAsync(emptyPortfolio.Id, new DateOnly(2026, 3, 31));
        var zeroIdAct = async () => await analyticsService.GetDashboardAsync(0);
        var negativeIdAct = async () => await analyticsService.GetDashboardAsync(-5);

        // Assert
        dashboard.Summary.TotalMarketValue.Should().Be(0m);
        dashboard.Summary.TotalCostBasis.Should().Be(0m);
        dashboard.Summary.PortfolioCurrentYieldPercent.Should().Be(0m);
        dashboard.Summary.PortfolioYieldOnCostPercent.Should().Be(0m);
        dashboard.SectorAllocations.Should().ContainSingle(a => a.AllocationPercent == 0m);

        await zeroIdAct.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await negativeIdAct.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}

