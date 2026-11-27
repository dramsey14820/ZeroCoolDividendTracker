using Bunit;
using DividendTracker.Core.Features.Holdings;
using DividendTracker.Core.Features.Portfolios;
using DividendTracker.Core.Features.Securities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using HoldingsPage = DividendTracker.Shared.UI.Pages.Holdings;

namespace DividendTracker.Tests.Features.Holdings;

public sealed class HoldingsPageTests : BunitContext
{
    [Fact]
    public void HoldingsPage_WhenRendered_DisplaysHoldingDetailsAndFormattedMarketValue()
    {
        // Arrange
        var holdingService = Substitute.For<IHoldingService>();
        var portfolioService = Substitute.For<IPortfolioService>();
        var securityService = Substitute.For<ISecurityService>();

        portfolioService.GetPortfoliosAsync(Arg.Any<CancellationToken>())
            .Returns(new List<PortfolioDto>
            {
                new(1, "Core Income", "Primary", false, "2026-01-01T00:00:00Z", new List<AccountDto>
                {
                    new(10, 1, "Fidelity Roth", "Roth IRA", "2026-01-01T00:00:00Z")
                })
            });

        securityService.GetSecuritiesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SecurityDto>
            {
                new(100, "SCHD", "Schwab US Dividend Equity ETF", 1, "ETF", 1, "General", 1, "Financials", 80.00m, "2026-01-01T00:00:00Z")
            });

        holdingService.GetHoldingsAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(new List<HoldingDto>
            {
                new(
                    1,
                    1,
                    "Core Income",
                    10,
                    "Fidelity Roth",
                    100,
                    "SCHD",
                    "Schwab US Dividend Equity ETF",
                    80.00m,
                    true,
                    10.0m,
                    750.00m,
                    75.00m,
                    800.00m,
                    new List<MarketBuyDto>
                    {
                        new(1, 1, 10.0m, 75.00m, new DateOnly(2026, 1, 15), false, null)
                    })
            });

        Services.AddSingleton(holdingService);
        Services.AddSingleton(portfolioService);
        Services.AddSingleton(securityService);

        // Act
        var cut = Render<HoldingsPage>();

        // Assert
        cut.Markup.Should().Contain("SCHD");
        cut.Markup.Should().Contain("Core Income");
        cut.Markup.Should().Contain("Fidelity Roth");
        cut.Markup.Should().Contain("800.00");
    }

    [Fact]
    public void HoldingsPage_WhenInlineMarketBuySubmitted_CallsCreateMarketBuyAsyncAndRefreshesHoldings()
    {
        // Arrange
        var holdingService = Substitute.For<IHoldingService>();
        var portfolioService = Substitute.For<IPortfolioService>();
        var securityService = Substitute.For<ISecurityService>();

        portfolioService.GetPortfoliosAsync(Arg.Any<CancellationToken>())
            .Returns(new List<PortfolioDto>
            {
                new(1, "Core Income", "Primary", false, "2026-01-01T00:00:00Z", new List<AccountDto>
                {
                    new(10, 1, "Fidelity Roth", "Roth IRA", "2026-01-01T00:00:00Z")
                })
            });

        securityService.GetSecuritiesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SecurityDto>
            {
                new(100, "SCHD", "Schwab US Dividend Equity ETF", 1, "ETF", 1, "General", 1, "Financials", 80.00m, "2026-01-01T00:00:00Z")
            });

        var initialHoldings = new List<HoldingDto>
        {
            new(
                1, 1, "Core Income", 10, "Fidelity Roth", 100, "SCHD", "Schwab US Dividend Equity ETF",
                80.00m, true, 10.0m, 750.00m, 75.00m, 800.00m,
                new List<MarketBuyDto>
                {
                    new(1, 1, 10.0m, 75.00m, new DateOnly(2026, 1, 15), false, null)
                })
        };

        var refreshedHoldings = new List<HoldingDto>
        {
            new(
                1, 1, "Core Income", 10, "Fidelity Roth", 100, "SCHD", "Schwab US Dividend Equity ETF",
                80.00m, true, 15.0m, 1140.00m, 76.00m, 1200.00m,
                new List<MarketBuyDto>
                {
                    new(2, 1, 5.0m, 78.00m, new DateOnly(2026, 2, 15), false, null),
                    new(1, 1, 10.0m, 75.00m, new DateOnly(2026, 1, 15), false, null)
                })
        };

        holdingService.GetHoldingsAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(initialHoldings, refreshedHoldings);
        holdingService.CreateMarketBuyAsync(Arg.Any<CreateMarketBuyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MarketBuyDto(2, 1, 5.0m, 78.00m, new DateOnly(2026, 2, 15), false, null));

        Services.AddSingleton(holdingService);
        Services.AddSingleton(portfolioService);
        Services.AddSingleton(securityService);

        var cut = Render<HoldingsPage>();

        // Act
        cut.Find("form.market-buy-form input[name='purchaseDate']").Change("2026-02-15");
        cut.Find("form.market-buy-form input[name='numberOfShares']").Change("5");
        cut.Find("form.market-buy-form input[name='pricePerShare']").Change("78");
        cut.Find("form.market-buy-form").Submit();

        // Assert
        holdingService.Received(1).CreateMarketBuyAsync(
            Arg.Is<CreateMarketBuyRequest>(r =>
                r.HoldingId == 1 &&
                r.NumberOfShares == 5m &&
                r.PricePerShare == 78m &&
                r.PurchaseDate == new DateOnly(2026, 2, 15)),
            Arg.Any<CancellationToken>());
        cut.Markup.Should().Contain("1200.00");
    }
}

