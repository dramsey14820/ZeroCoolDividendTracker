using DividendTracker.Core.Common;

namespace DividendTracker.Web.Services;

public sealed class WebAppInfoService : IAppInfoService
{
    public AppInfoDto GetAppInfo()
    {
        return new AppInfoDto("ZeroCool Dividend Tracker", "Blazor Server", "1.0.0");
    }
}

