using Microsoft.AspNetCore.Components.WebView;

namespace DividendTracker.Maui;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        blazorWebView.BlazorWebViewInitializing += OnBlazorWebViewInitializing;
    }

    private static void OnBlazorWebViewInitializing(object? sender, BlazorWebViewInitializingEventArgs e)
    {
#if WINDOWS
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userDataFolder = Path.Combine(localAppData, "ZeroCoolDividendTracker", "WebView2");
        Directory.CreateDirectory(userDataFolder);
        e.UserDataFolder = userDataFolder;
#endif
    }
}

