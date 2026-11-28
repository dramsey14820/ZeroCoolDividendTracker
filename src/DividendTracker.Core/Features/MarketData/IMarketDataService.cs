namespace DividendTracker.Core.Features.MarketData;

public interface IMarketDataService
{
    Task<MarketDataSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task<MarketDataSettingsDto> SaveSettingsAsync(
        UpdateMarketDataSettingsRequest request,
        CancellationToken cancellationToken = default);

    Task<MarketQuoteDto> RefreshSecurityQuoteAsync(
        long securityId,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    Task<MarketDataRefreshResultDto> RefreshAllQuotesAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExternalDividendEventDto>> GetDividendHistoryAsync(
        long securityId,
        DateOnly fromDate,
        DateOnly toDate,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    Task<DividendImportResultDto> ImportDividendsForHoldingAsync(
        long holdingId,
        DateOnly fromDate,
        DateOnly toDate,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);
}
