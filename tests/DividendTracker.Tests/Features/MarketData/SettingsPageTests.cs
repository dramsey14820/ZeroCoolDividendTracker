using Bunit;
using DividendTracker.Core.Features.MarketData;
using DividendTracker.Shared.UI.Pages;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace DividendTracker.Tests.Features.MarketData;

public sealed class SettingsPageTests : BunitContext
{
    [Fact]
    public void SettingsPage_LoadsSavesSettings_AndTriggersBatchQuoteRefresh()
    {
        // Arrange
        var marketDataService = Substitute.For<IMarketDataService>();
        marketDataService.GetSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new MarketDataSettingsDto(MarketDataProviderType.YahooFinance, "", 15));

        marketDataService.SaveSettingsAsync(Arg.Any<UpdateMarketDataSettingsRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var req = callInfo.Arg<UpdateMarketDataSettingsRequest>();
                return new MarketDataSettingsDto(req.Provider, req.ApiKey ?? string.Empty, req.CacheTtlMinutes);
            });

        marketDataService.RefreshAllQuotesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new MarketDataRefreshResultDto(2, 1, 0, Array.Empty<string>()));

        Services.AddSingleton(marketDataService);

        // Act — Render
        var cut = Render<Settings>();

        // Assert initial load
        cut.Find("#market-provider").GetAttribute("value").Should().NotBeNull();

        // Act — Update settings & submit
        cut.Find("#market-provider").Change(MarketDataProviderType.Finnhub.ToString());
        cut.Find("#market-api-key").Change("my-finnhub-key");
        cut.Find("#market-cache-ttl").Change("30");
        cut.Find("form").Submit();

        // Assert SaveSettingsAsync called
        marketDataService.Received(1).SaveSettingsAsync(
            Arg.Is<UpdateMarketDataSettingsRequest>(r =>
                r.Provider == MarketDataProviderType.Finnhub &&
                r.ApiKey == "my-finnhub-key" &&
                r.CacheTtlMinutes == 30),
            Arg.Any<CancellationToken>());

        // Act — Click Refresh All Quotes
        cut.Find("#refresh-all-quotes").Click();

        // Assert RefreshAllQuotesAsync called and summary displayed
        marketDataService.Received(1).RefreshAllQuotesAsync(false, Arg.Any<CancellationToken>());
        cut.Markup.Should().Contain("2");
        cut.Markup.Should().Contain("1");
    }
}

