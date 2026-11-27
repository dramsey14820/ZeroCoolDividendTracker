using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DividendTracker.Core.Features.Portfolios;

public static class PortfolioServiceCollectionExtensions
{
    public static IServiceCollection AddPortfolioFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPortfolioService, PortfolioService>();

        return services;
    }
}

