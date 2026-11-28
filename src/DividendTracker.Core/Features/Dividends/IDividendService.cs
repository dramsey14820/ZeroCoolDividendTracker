namespace DividendTracker.Core.Features.Dividends;

public interface IDividendService
{
    Task<IReadOnlyList<DividendPaymentDto>> GetDividendsAsync(
        long? portfolioId = null,
        long? holdingId = null,
        CancellationToken cancellationToken = default);

    Task<DividendPaymentDto> RecordDividendAsync(
        RecordDividendRequest request,
        CancellationToken cancellationToken = default);

    Task<DividendPaymentDto> UpdateDividendAsync(
        UpdateDividendRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteDividendAsync(
        long dividendPaymentId,
        CancellationToken cancellationToken = default);
}

