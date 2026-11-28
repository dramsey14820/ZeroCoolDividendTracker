using Bunit;
using DividendTracker.Core.Common;
using DividendTracker.Core.Features.Analytics;
using DividendTracker.Core.Features.Portfolios;
using DividendTracker.Shared.UI.Pages;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace DividendTracker.Tests.Features.Shell;

public sealed class HomeComponentTests : BunitContext
{
    [Fact]
    public void Home_WhenRendered_DisplaysApplicationMetadataFromIAppInfoService()
    {
        // Arrange
        var appInfoService = Substitute.For<IAppInfoService>();
        appInfoService.GetAppInfo().Returns(new AppInfoDto("ZeroCool Dividend Tracker", "Blazor Server", "1.0.0"));

        var portfolioService = Substitute.For<IPortfolioService>();
        portfolioService.GetPortfoliosAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<PortfolioDto>());

        var analyticsService = Substitute.For<IAnalyticsService>();
        analyticsService.GetDashboardAsync(Arg.Any<long?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
            .Returns(new DashboardAnalyticsDto(
                null,
                new DateOnly(2026, 3, 31),
                new DashboardSummaryDto(0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0),
                Array.Empty<HoldingAnalyticsDto>(),
                Array.Empty<AllocationItemDto>(),
                Array.Empty<AllocationItemDto>(),
                Array.Empty<AllocationItemDto>(),
                Array.Empty<MonthlyIncomePointDto>()));

        Services.AddSingleton(appInfoService);
        Services.AddSingleton(portfolioService);
        Services.AddSingleton(analyticsService);

        // Act
        var cut = Render<Home>();

        // Assert
        cut.Markup.Should().Contain("ZeroCool Dividend Tracker");
        cut.Markup.Should().Contain("Blazor Server");
        cut.Markup.Should().Contain("1.0.0");
    }
}
