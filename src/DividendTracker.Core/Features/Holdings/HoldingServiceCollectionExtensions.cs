using Microsoft.Extensions.DependencyInjection;

namespace DividendTracker.Core.Features.Holdings;

public static class HoldingServiceCollectionExtensions
{
    public static IServiceCollection AddHoldingFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IHoldingService, HoldingService>();
        return services;
    }
}

