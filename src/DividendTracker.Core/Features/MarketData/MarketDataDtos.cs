namespace DividendTracker.Core.Features.MarketData;

public enum MarketDataProviderType
{
    YahooFinance = 0,
    Finnhub = 1,
    FinancialModelingPrep = 2,
}

public sealed record MarketDataSettingsDto(
    MarketDataProviderType Provider,
    string ApiKey,
    int CacheTtlMinutes);

public sealed record UpdateMarketDataSettingsRequest(
    MarketDataProviderType Provider,
    string? ApiKey,
    int CacheTtlMinutes);

public sealed record MarketQuoteDto(
    string Symbol,
    decimal Price,
    string? Name,
    string ProviderName,
    DateTimeOffset RetrievedUtc,
    bool FromCache);

public sealed record ExternalDividendEventDto(
    string Symbol,
    DateOnly ExDividendDate,
    DateOnly PaymentDate,
    decimal AmountPerShare,
    string ProviderName);

public sealed record MarketDataRefreshResultDto(
    int UpdatedCount,
    int CachedCount,
    int FailedCount,
    IReadOnlyList<string> Errors);

public sealed record DividendImportResultDto(
    int ImportedCount,
    int SkippedCount,
    IReadOnlyList<string> Errors);
