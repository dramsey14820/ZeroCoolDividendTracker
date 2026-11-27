using DividendTracker.Core.Common;

namespace DividendTracker.Maui.Services;

public sealed class MauiAppInfoService : IAppInfoService
{
    public AppInfoDto GetAppInfo()
    {
        var platform = DeviceInfo.Current.Platform.ToString();
        return new AppInfoDto("ZeroCool Dividend Tracker", $"MAUI ({platform})", "1.0.0");
    }
}

