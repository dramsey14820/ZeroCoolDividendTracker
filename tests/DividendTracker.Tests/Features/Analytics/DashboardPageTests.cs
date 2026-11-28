using Bunit;
using DividendTracker.Core.Common;
using DividendTracker.Core.Features.Analytics;
using DividendTracker.Core.Features.Portfolios;
using DividendTracker.Shared.UI.Pages;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace DividendTracker.Tests.Features.Analytics;

public sealed class DashboardPageTests : BunitContext
{
    [Fact]
    public void Dashboard_WhenRendered_DisplaysAnalyticsKpisAllocationsHistoryAndFiltersByPortfolio()
    {
        // Arrange
        var appInfoService = Substitute.For<IAppInfoService>();
        appInfoService.GetAppInfo().Returns(new AppInfoDto("ZeroCool Dividend Tracker", "Blazor Server", "1.0.0"));

        var portfolioService = Substitute.For<IPortfolioService>();
        portfolioService.GetPortfoliosAsync(Arg.Any<CancellationToken>())
            .Returns(new List<PortfolioDto>
            {
                new(1, "Core Income", "Primary", false, "2026-01-01T00:00:00Z", Array.Empty<AccountDto>()),
                new(2, "Watchlist", "Modelled", true, "2026-01-01T00:00:00Z", Array.Empty<AccountDto>())
            });

        var analyticsService = Substitute.For<IAnalyticsService>();
        var sampleDashboard = new DashboardAnalyticsDto(
            PortfolioId: null,
            AsOfDate: new DateOnly(2026, 3, 31),
            Summary: new DashboardSummaryDto(
                TotalMarketValue: 2000.00m,
                TotalCostBasis: 1750.00m,
                TotalUnrealizedGainLoss: 250.00m,
                TotalUnrealizedGainLossPercent: 14.29m,
                Trailing12MonthIncome: 90.00m,
                ProjectedAnnualIncome: 90.00m,
                ProjectedMonthlyIncome: 7.50m,
                LifetimeDividendIncome: 120.00m,
                PortfolioCurrentYieldPercent: 4.50m,
                PortfolioYieldOnCostPercent: 5.14m,
                HoldingCount: 2),
            Holdings: new List<HoldingAnalyticsDto>
            {
                new(1, 1, "Core Income", 10, "Fidelity Roth", 100, "O", "Realty Income", "Real Estate", "REIT", "REIT", 60.00m, 20m, 1000.00m, 1200.00m, 200.00m, 20.00m, 60.00m, 60.00m, 5.00m, 80.00m, 5.00m, 6.00m),
                new(2, 1, "Core Income", 10, "Fidelity Roth", 101, "SCHD", "Schwab US Dividend Equity ETF", "Financials", "ETF", "ETF", 80.00m, 10m, 750.00m, 800.00m, 50.00m, 6.67m, 30.00m, 30.00m, 2.50m, 40.00m, 3.75m, 4.00m)
            },
            SectorAllocations: new List<AllocationItemDto>
            {
                new("Real Estate", 1200.00m, 1000.00m, 60.00m, 60.00m, 1),
                new("Financials", 800.00m, 750.00m, 40.00m, 30.00m, 1)
            },
            IndustryAllocations: new List<AllocationItemDto>
            {
                new("REIT", 1200.00m, 1000.00m, 60.00m, 60.00m, 1),
                new("ETF", 800.00m, 750.00m, 40.00m, 30.00m, 1)
            },
            InvestmentTypeAllocations: new List<AllocationItemDto>
            {
                new("REIT", 1200.00m, 1000.00m, 60.00m, 60.00m, 1),
                new("ETF", 800.00m, 750.00m, 40.00m, 30.00m, 1)
            },
            MonthlyIncomeHistory: new List<MonthlyIncomePointDto>
            {
                new(2026, 3, "2026-03", 90.00m, 60.00m, 30.00m, 2)
            });

        analyticsService.GetDashboardAsync(Arg.Any<long?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
            .Returns(sampleDashboard);

        Services.AddSingleton(appInfoService);
        Services.AddSingleton(portfolioService);
        Services.AddSingleton(analyticsService);

        // Act
        var cut = Render<Home>();

        // Assert initial render
        cut.Markup.Should().Contain("ZeroCool Dividend Tracker");
        cut.Markup.Should().Contain("2,000.00");
        cut.Markup.Should().Contain("90.00");
        cut.Markup.Should().Contain("4.50%");
        cut.Markup.Should().Contain("5.14%");
        cut.Markup.Should().Contain("Real Estate");
        cut.Markup.Should().Contain("2026-03");
        cut.Markup.Should().Contain("SCHD");

        // Act filter change
        cut.Find("select[name='portfolioFilter']").Change("2");

        // Assert filter reload
        analyticsService.Received(1).GetDashboardAsync(2, Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>());
    }
}

