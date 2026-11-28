using Microsoft.Extensions.DependencyInjection;

namespace DividendTracker.Core.Features.MarketData;

public static class MarketDataServiceCollectionExtensions
{
    public static IServiceCollection AddMarketDataFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(_ =>
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            };
            return new HttpClient(handler, disposeHandler: true);
        });

        services.AddSingleton<IMarketDataProvider, YahooFinanceMarketDataProvider>();
        services.AddSingleton<IMarketDataProvider, FinnhubMarketDataProvider>();
        services.AddSingleton<IMarketDataProvider, FmpMarketDataProvider>();
        services.AddSingleton<IMarketDataService, MarketDataService>();

        return services;
    }
}
