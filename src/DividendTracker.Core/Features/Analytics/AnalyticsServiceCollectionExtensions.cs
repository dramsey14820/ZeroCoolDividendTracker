using Microsoft.Extensions.DependencyInjection;

namespace DividendTracker.Core.Features.Analytics;

public static class AnalyticsServiceCollectionExtensions
{
    public static IServiceCollection AddAnalyticsFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IAnalyticsService, AnalyticsService>();
        return services;
    }
}

