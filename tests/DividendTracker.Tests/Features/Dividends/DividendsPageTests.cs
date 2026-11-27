using Bunit;
using DividendTracker.Core.Features.Dividends;
using DividendTracker.Core.Features.Holdings;
using DividendTracker.Core.Features.Portfolios;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using DividendsPage = DividendTracker.Shared.UI.Pages.Dividends;

namespace DividendTracker.Tests.Features.Dividends;

public sealed class DividendsPageTests : BunitContext
{
    [Fact]
    public void DividendsPage_WhenRendered_DisplaysPayoutAmountAndDripDetails()
    {
        // Arrange
        var dividendService = Substitute.For<IDividendService>();
        var holdingService = Substitute.For<IHoldingService>();
        var portfolioService = Substitute.For<IPortfolioService>();

        portfolioService.GetPortfoliosAsync(Arg.Any<CancellationToken>())
            .Returns(new List<PortfolioDto>
            {
                new(1, "Core Income", "Primary", false, "2026-01-01T00:00:00Z", new List<AccountDto>
                {
                    new(10, 1, "Fidelity Roth", "Roth IRA", "2026-01-01T00:00:00Z")
                })
            });

        holdingService.GetHoldingsAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(new List<HoldingDto>
            {
                new(
                    1, 1, "Core Income", 10, "Fidelity Roth", 100, "SCHD", "Schwab US Dividend Equity ETF",
                    80.00m, true, 10.5m, 790.00m, 75.238m, 840.00m, Array.Empty<MarketBuyDto>())
            });

        dividendService.GetDividendsAsync(Arg.Any<long?>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(new List<DividendPaymentDto>
            {
                new(
                    1, 1, 1, "Core Income", 10, "Fidelity Roth", 100, "SCHD", "Schwab US Dividend Equity ETF",
                    new DateOnly(2026, 3, 25), new DateOnly(2026, 3, 18), 40.00m, true, "Q1 DRIP", 5, 0.5m, 80.00m)
            });

        Services.AddSingleton(dividendService);
        Services.AddSingleton(holdingService);
        Services.AddSingleton(portfolioService);

        // Act
        var cut = Render<DividendsPage>();

        // Assert
        cut.Markup.Should().Contain("SCHD");
        cut.Markup.Should().Contain("40.00");
        cut.Markup.Should().Contain("DRIP");
        cut.Markup.Should().Contain("0.5");
        cut.Markup.Should().Contain("80.00");
    }

    [Fact]
    public void DividendsPage_WhenRecordDividendFormSubmitted_CallsRecordDividendAsyncAndRefreshesDividends()
    {
        // Arrange
        var dividendService = Substitute.For<IDividendService>();
        var holdingService = Substitute.For<IHoldingService>();
        var portfolioService = Substitute.For<IPortfolioService>();

        portfolioService.GetPortfoliosAsync(Arg.Any<CancellationToken>())
            .Returns(new List<PortfolioDto>
            {
                new(1, "Core Income", "Primary", false, "2026-01-01T00:00:00Z", new List<AccountDto>
                {
                    new(10, 1, "Fidelity Roth", "Roth IRA", "2026-01-01T00:00:00Z")
                })
            });

        holdingService.GetHoldingsAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(new List<HoldingDto>
            {
                new(
                    1, 1, "Core Income", 10, "Fidelity Roth", 100, "SCHD", "Schwab US Dividend Equity ETF",
                    80.00m, true, 10.0m, 750.00m, 75.00m, 800.00m, Array.Empty<MarketBuyDto>())
            });

        var initialDividends = new List<DividendPaymentDto>();
        var refreshedDividends = new List<DividendPaymentDto>
        {
            new(
                1, 1, 1, "Core Income", 10, "Fidelity Roth", 100, "SCHD", "Schwab US Dividend Equity ETF",
                new DateOnly(2026, 3, 25), null, 40.00m, true, "Q1 DRIP", 5, 0.5m, 80.00m)
        };

        dividendService.GetDividendsAsync(Arg.Any<long?>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(initialDividends, refreshedDividends);
        dividendService.RecordDividendAsync(Arg.Any<RecordDividendRequest>(), Arg.Any<CancellationToken>())
            .Returns(refreshedDividends[0]);

        Services.AddSingleton(dividendService);
        Services.AddSingleton(holdingService);
        Services.AddSingleton(portfolioService);

        var cut = Render<DividendsPage>();

        // Act
        cut.Find("form.record-dividend-form select[name='holdingId']").Change("1");
        cut.Find("form.record-dividend-form input[name='paymentDate']").Change("2026-03-25");
        cut.Find("form.record-dividend-form input[name='amount']").Change("40");
        cut.Find("form.record-dividend-form input[name='notes']").Change("Q1 DRIP");
        cut.Find("form.record-dividend-form input[name='isReinvested']").Change(true);
        cut.Find("form.record-dividend-form input[name='dripPricePerShare']").Change("80");
        cut.Find("form.record-dividend-form").Submit();

        // Assert
        dividendService.Received(1).RecordDividendAsync(
            Arg.Is<RecordDividendRequest>(r =>
                r.HoldingId == 1 &&
                r.PaymentDate == new DateOnly(2026, 3, 25) &&
                r.Amount == 40m &&
                r.IsReinvested &&
                r.DripPricePerShare == 80m),
            Arg.Any<CancellationToken>());
        cut.Markup.Should().Contain("40.00");
        cut.Markup.Should().Contain("Q1 DRIP");
    }
}

