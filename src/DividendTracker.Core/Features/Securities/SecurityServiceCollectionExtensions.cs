using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DividendTracker.Core.Features.Securities;

public static class SecurityServiceCollectionExtensions
{
    public static IServiceCollection AddSecurityFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ISecurityService, SecurityService>();

        return services;
    }
}

