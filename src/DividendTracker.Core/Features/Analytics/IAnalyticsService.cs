namespace DividendTracker.Core.Features.Analytics;

public interface IAnalyticsService
{
    Task<DashboardAnalyticsDto> GetDashboardAsync(
        long? portfolioId = null,
        DateOnly? asOfDate = null,
        CancellationToken cancellationToken = default);
}

