using Microsoft.Extensions.DependencyInjection;

namespace DividendTracker.Core.Features.Dividends;

public static class DividendServiceCollectionExtensions
{
    public static IServiceCollection AddDividendFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IDividendService, DividendService>();
        return services;
    }
}

