using System.Globalization;
using System.Text.Json;

namespace DividendTracker.Core.Features.MarketData;

public sealed class FmpMarketDataProvider : IMarketDataProvider
{
    private readonly HttpClient _httpClient;

    public FmpMarketDataProvider(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    public MarketDataProviderType ProviderType => MarketDataProviderType.FinancialModelingPrep;
    public string DisplayName => "Financial Modeling Prep";
    public bool RequiresApiKey => true;

    public async Task<MarketQuoteDto> GetQuoteAsync(
        string symbol,
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException($"{DisplayName} requires an API key.");
        }

        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        var requestUri = new Uri(
            $"https://financialmodelingprep.com/api/v3/quote-short/{Uri.EscapeDataString(normalizedSymbol)}?apikey={Uri.EscapeDataString(apiKey.Trim())}",
            UriKind.Absolute);

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (document.RootElement.ValueKind != JsonValueKind.Array ||
            document.RootElement.GetArrayLength() == 0 ||
            !document.RootElement[0].TryGetProperty("price", out var priceElement) ||
            !priceElement.TryGetDecimal(out var price) ||
            price <= 0m)
        {
            throw new InvalidOperationException($"Invalid quote price returned by {DisplayName} for '{normalizedSymbol}'.");
        }

        return new MarketQuoteDto(
            normalizedSymbol,
            price,
            Name: null,
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

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException($"{DisplayName} requires an API key.");
        }

        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        var fromText = fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toText = toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var requestUri = new Uri(
            $"https://financialmodelingprep.com/api/v3/historical-price-full/stock_dividend/{Uri.EscapeDataString(normalizedSymbol)}?from={fromText}&to={toText}&apikey={Uri.EscapeDataString(apiKey.Trim())}",
            UriKind.Absolute);

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("historical", out var historicalElement) ||
            historicalElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ExternalDividendEventDto>();
        }

        var items = new List<ExternalDividendEventDto>();
        foreach (var entry in historicalElement.EnumerateArray())
        {
            if (!entry.TryGetProperty("dividend", out var amountElement) ||
                !amountElement.TryGetDecimal(out var amount) ||
                amount <= 0m ||
                !entry.TryGetProperty("date", out var exDateElement) ||
                exDateElement.ValueKind != JsonValueKind.String ||
                !DateOnly.TryParseExact(exDateElement.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exDate))
            {
                continue;
            }

            var paymentDate = exDate;
            if (entry.TryGetProperty("paymentDate", out var payDateElement) &&
                payDateElement.ValueKind == JsonValueKind.String &&
                DateOnly.TryParseExact(payDateElement.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedPayDate))
            {
                paymentDate = parsedPayDate;
            }

            if (paymentDate < fromDate || paymentDate > toDate)
            {
                continue;
            }

            items.Add(new ExternalDividendEventDto(
                normalizedSymbol,
                exDate,
                paymentDate,
                amount,
                DisplayName));
        }

        return items
            .OrderBy(i => i.PaymentDate)
            .ToList();
    }
}
