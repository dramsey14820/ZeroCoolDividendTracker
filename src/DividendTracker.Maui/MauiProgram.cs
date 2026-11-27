using DividendTracker.Core.Common;
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

        return builder.Build();
    }
}

