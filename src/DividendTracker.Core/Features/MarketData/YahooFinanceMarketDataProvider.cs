using System.Globalization;
using System.Text.Json;

namespace DividendTracker.Core.Features.MarketData;

public sealed class YahooFinanceMarketDataProvider : IMarketDataProvider
{
    private const string UserAgentHeader = "ZeroCoolDividendTracker/1.0";
    private readonly HttpClient _httpClient;

    public YahooFinanceMarketDataProvider(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    public MarketDataProviderType ProviderType => MarketDataProviderType.YahooFinance;
    public string DisplayName => "Yahoo Finance";
    public bool RequiresApiKey => false;

    public async Task<MarketQuoteDto> GetQuoteAsync(
        string symbol,
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        var escapedSymbol = Uri.EscapeDataString(normalizedSymbol);
        var requestUri = new Uri(
            $"https://query1.finance.yahoo.com/v8/finance/chart/{escapedSymbol}?range=1d&interval=1d",
            UriKind.Absolute);

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.UserAgent.ParseAdd(UserAgentHeader);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("chart", out var chart) ||
            !chart.TryGetProperty("result", out var results) ||
            results.ValueKind != JsonValueKind.Array ||
            results.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"No quote data returned by {DisplayName} for '{normalizedSymbol}'.");
        }

        var firstResult = results[0];
        if (!firstResult.TryGetProperty("meta", out var meta) ||
            !meta.TryGetProperty("regularMarketPrice", out var priceElement) ||
            !priceElement.TryGetDecimal(out var price) ||
            price <= 0m)
        {
            throw new InvalidOperationException($"Invalid quote price returned by {DisplayName} for '{normalizedSymbol}'.");
        }

        string? name = null;
        if (meta.TryGetProperty("shortName", out var shortName) && shortName.ValueKind == JsonValueKind.String)
        {
            name = shortName.GetString();
        }
        else if (meta.TryGetProperty("longName", out var longName) && longName.ValueKind == JsonValueKind.String)
        {
            name = longName.GetString();
        }

        return new MarketQuoteDto(
            normalizedSymbol,
            price,
            name,
            DisplayName,
            DateTimeOffset.UtcNow,
            FromCache: false);
    }

    public async Task<IReadOnlyList<ExternalDividendEventDto>> GetDividendHistoryAsync(
        string symbol,
        DateOnly fromDate,
        DateOnly toDate,
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        if (toDate < fromDate)
        {
            throw new ArgumentOutOfRangeException(nameof(toDate), "End date must be on or after start date.");
        }

        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        var escapedSymbol = Uri.EscapeDataString(normalizedSymbol);
        var period1 = new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var period2 = new DateTimeOffset(toDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var requestUri = new Uri(
            string.Create(
                CultureInfo.InvariantCulture,
                $"https://query1.finance.yahoo.com/v8/finance/chart/{escapedSymbol}?period1={period1}&period2={period2}&interval=1d&events=div"),
            UriKind.Absolute);

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.UserAgent.ParseAdd(UserAgentHeader);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("chart", out var chart) ||
            !chart.TryGetProperty("result", out var results) ||
            results.ValueKind != JsonValueKind.Array ||
            results.GetArrayLength() == 0)
        {
            return Array.Empty<ExternalDividendEventDto>();
        }

        var firstResult = results[0];
        if (!firstResult.TryGetProperty("events", out var eventsElement) ||
            !eventsElement.TryGetProperty("dividends", out var dividendsElement) ||
            dividendsElement.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<ExternalDividendEventDto>();
        }

        var items = new List<ExternalDividendEventDto>();
        foreach (var property in dividendsElement.EnumerateObject())
        {
            var entry = property.Value;
            if (!entry.TryGetProperty("amount", out var amountElement) ||
                !amountElement.TryGetDecimal(out var amount) ||
                amount <= 0m ||
                !entry.TryGetProperty("date", out var dateElement) ||
                !dateElement.TryGetInt64(out var unixSeconds))
            {
                continue;
            }

            var eventDate = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime);
            if (eventDate < fromDate || eventDate > toDate)
            {
                continue;
            }

            items.Add(new ExternalDividendEventDto(
                normalizedSymbol,
                eventDate,
                eventDate,
                amount,
                DisplayName));
        }

        return items
            .OrderBy(i => i.PaymentDate)
            .ToList();
    }
}
