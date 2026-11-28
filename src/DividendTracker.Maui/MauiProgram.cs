using DividendTracker.Core.Common;
using DividendTracker.Core.Features.Analytics;
using DividendTracker.Core.Features.Dividends;
using DividendTracker.Core.Features.Holdings;
using DividendTracker.Core.Features.Portfolios;
using DividendTracker.Core.Features.Securities;
using DividendTracker.Core.Infrastructure.Persistence;
using DividendTracker.Maui.Services;

namespace DividendTracker.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddSingleton<IAppInfoService, MauiAppInfoService>();

#if WINDOWS
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZeroCoolDividendTracker",
            "dividend-tracker.db");
#else
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "dividend-tracker.db");
#endif

        builder.Services.AddSqlitePersistence(dbPath);
        builder.Services.AddPortfolioFeature();
        builder.Services.AddSecurityFeature();
        builder.Services.AddHoldingFeature();
        builder.Services.AddDividendFeature();
        builder.Services.AddAnalyticsFeature();

        return builder.Build();
    }
}
