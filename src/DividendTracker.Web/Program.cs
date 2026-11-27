using DividendTracker.Core.Common;
using DividendTracker.Core.Features.Portfolios;
using DividendTracker.Core.Features.Securities;
using DividendTracker.Core.Infrastructure.Persistence;
using DividendTracker.Web.Components;
using DividendTracker.Web.Services;

namespace DividendTracker.Web;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddSingleton<IAppInfoService, WebAppInfoService>();

        var dbPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "dividend-tracker.db");
        builder.Services.AddSqlitePersistence(dbPath);
        builder.Services.AddPortfolioFeature();
        builder.Services.AddSecurityFeature();

        var app = builder.Build();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseAntiforgery();
        app.MapStaticAssets();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode()
            .AddAdditionalAssemblies(typeof(DividendTracker.Shared.UI.Routes).Assembly);

        await app.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();

        await app.RunAsync();
    }
}
