namespace DividendTracker.Core.Features.MarketData;

public interface IMarketDataProvider
{
    MarketDataProviderType ProviderType { get; }
    string DisplayName { get; }
    bool RequiresApiKey { get; }

    Task<MarketQuoteDto> GetQuoteAsync(
        string symbol,
        string? apiKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExternalDividendEventDto>> GetDividendHistoryAsync(
        string symbol,
        DateOnly fromDate,
        DateOnly toDate,
        string? apiKey,
        CancellationToken cancellationToken = default);
}
