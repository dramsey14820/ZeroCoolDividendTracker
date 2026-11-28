using System.Net;
using System.Text;
using DividendTracker.Core.Features.MarketData;
using FluentAssertions;
using Xunit;

namespace DividendTracker.Tests.Features.MarketData;

public sealed class MarketDataProviderTests
{
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(_responder(request));
        }
    }

    private static HttpClient CreateJsonClient(string json, out StubHttpMessageHandler handler)
    {
        handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

        return new HttpClient(handler);
    }

    [Fact]
    public async Task YahooFinanceProvider_GetQuoteAndDividendHistory_ParsesValidJsonWithoutApiKey()
    {
        // Arrange — Quote
        const string quoteJson = """
            {
              "chart": {
                "result": [
                  {
                    "meta": {
                      "regularMarketPrice": 82.45,
                      "shortName": "Schwab US Dividend Equity ETF"
                    }
                  }
                ]
              }
            }
            """;

        using var quoteClient = CreateJsonClient(quoteJson, out _);
        var yahooQuoteProvider = new YahooFinanceMarketDataProvider(quoteClient);

        // Act — Quote
        var quote = await yahooQuoteProvider.GetQuoteAsync("schd", null);

        // Assert — Quote
        yahooQuoteProvider.ProviderType.Should().Be(MarketDataProviderType.YahooFinance);
        yahooQuoteProvider.RequiresApiKey.Should().BeFalse();
        quote.Symbol.Should().Be("SCHD");
        quote.Price.Should().Be(82.45m);
        quote.Name.Should().Be("Schwab US Dividend Equity ETF");
        quote.ProviderName.Should().Be("Yahoo Finance");
        quote.FromCache.Should().BeFalse();

        // Arrange — Dividends (2025-12-15 = 1765756800, 2026-03-20 = 1773964800)
        const string divJson = """
            {
              "chart": {
                "result": [
                  {
                    "events": {
                      "dividends": {
                        "1773964800": { "amount": 0.80, "date": 1773964800 },
                        "1765756800": { "amount": 0.75, "date": 1765756800 }
                      }
                    }
                  }
                ]
              }
            }
            """;

        using var divClient = CreateJsonClient(divJson, out _);
        var yahooDivProvider = new YahooFinanceMarketDataProvider(divClient);

        // Act — Dividends
        var dividends = await yahooDivProvider.GetDividendHistoryAsync(
            "SCHD",
            new DateOnly(2025, 12, 1),
            new DateOnly(2026, 3, 31),
            null);

        // Assert — Dividends ordered by PaymentDate ascending
        dividends.Should().HaveCount(2);
        dividends[0].ExDividendDate.Should().Be(new DateOnly(2025, 12, 15));
        dividends[0].PaymentDate.Should().Be(new DateOnly(2025, 12, 15));
        dividends[0].AmountPerShare.Should().Be(0.75m);
        dividends[0].ProviderName.Should().Be("Yahoo Finance");

        dividends[1].ExDividendDate.Should().Be(new DateOnly(2026, 3, 20));
        dividends[1].PaymentDate.Should().Be(new DateOnly(2026, 3, 20));
        dividends[1].AmountPerShare.Should().Be(0.80m);
    }

    [Fact]
    public async Task FinnhubProvider_RequiresApiKey_AndParsesQuoteAndDividends()
    {
        // Arrange
        using var emptyClient = CreateJsonClient("{}", out _);
        var finnhubNoKey = new FinnhubMarketDataProvider(emptyClient);

        // Act & Assert — missing key
        finnhubNoKey.RequiresApiKey.Should().BeTrue();
        var missingKeyAction = () => finnhubNoKey.GetQuoteAsync("SCHD", "   ");
        await missingKeyAction.Should().ThrowAsync<InvalidOperationException>();

        // Arrange — valid quote
        using var quoteClient = CreateJsonClient("""{"c": 82.45}""", out _);
        var finnhubQuote = new FinnhubMarketDataProvider(quoteClient);

        var quote = await finnhubQuote.GetQuoteAsync("SCHD", "demo-key");
        quote.Symbol.Should().Be("SCHD");
        quote.Price.Should().Be(82.45m);
        quote.ProviderName.Should().Be("Finnhub");

        // Arrange — valid dividends
        const string divJson = """
            [
              { "amount": 0.80, "date": "2026-03-18", "payDate": "2026-03-25" },
              { "amount": 0.75, "date": "2025-12-15", "payDate": "2025-12-22" }
            ]
            """;
        using var divClient = CreateJsonClient(divJson, out _);
        var finnhubDiv = new FinnhubMarketDataProvider(divClient);

        var dividends = await finnhubDiv.GetDividendHistoryAsync(
            "SCHD",
            new DateOnly(2025, 12, 1),
            new DateOnly(2026, 3, 31),
            "demo-key");

        dividends.Should().HaveCount(2);
        dividends[0].ExDividendDate.Should().Be(new DateOnly(2025, 12, 15));
        dividends[0].PaymentDate.Should().Be(new DateOnly(2025, 12, 22));
        dividends[0].AmountPerShare.Should().Be(0.75m);
        dividends[1].ExDividendDate.Should().Be(new DateOnly(2026, 3, 18));
        dividends[1].PaymentDate.Should().Be(new DateOnly(2026, 3, 25));
        dividends[1].AmountPerShare.Should().Be(0.80m);
    }

    [Fact]
    public async Task FmpProvider_RequiresApiKey_AndParsesQuoteAndDividends()
    {
        // Arrange
        using var emptyClient = CreateJsonClient("[]", out _);
        var fmpNoKey = new FmpMarketDataProvider(emptyClient);

        // Act & Assert — missing key
        fmpNoKey.RequiresApiKey.Should().BeTrue();
        var missingKeyAction = () => fmpNoKey.GetQuoteAsync("O", "");
        await missingKeyAction.Should().ThrowAsync<InvalidOperationException>();

        // Arrange — valid quote
        using var quoteClient = CreateJsonClient("""[{"symbol": "O", "price": 58.90}]""", out _);
        var fmpQuote = new FmpMarketDataProvider(quoteClient);

        var quote = await fmpQuote.GetQuoteAsync("O", "demo-key");
        quote.Symbol.Should().Be("O");
        quote.Price.Should().Be(58.90m);
        quote.ProviderName.Should().Be("Financial Modeling Prep");

        // Arrange — valid dividends
        const string divJson = """
            {
              "symbol": "O",
              "historical": [
                { "date": "2026-03-01", "paymentDate": "2026-03-15", "dividend": 0.265 },
                { "date": "2026-02-01", "paymentDate": "2026-02-15", "dividend": 0.264 }
              ]
            }
            """;
        using var divClient = CreateJsonClient(divJson, out _);
        var fmpDiv = new FmpMarketDataProvider(divClient);

        var dividends = await fmpDiv.GetDividendHistoryAsync(
            "O",
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 3, 31),
            "demo-key");

        dividends.Should().HaveCount(2);
        dividends[0].ExDividendDate.Should().Be(new DateOnly(2026, 2, 1));
        dividends[0].PaymentDate.Should().Be(new DateOnly(2026, 2, 15));
        dividends[0].AmountPerShare.Should().Be(0.264m);
        dividends[1].ExDividendDate.Should().Be(new DateOnly(2026, 3, 1));
        dividends[1].PaymentDate.Should().Be(new DateOnly(2026, 3, 15));
        dividends[1].AmountPerShare.Should().Be(0.265m);
    }
}

