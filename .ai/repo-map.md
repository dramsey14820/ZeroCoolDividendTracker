# Repository Map (Auto-Generated)

> Compact signature-only index for Stage 2 slice planning. Regenerate after commits via `.\.ai\update-repo-map.ps1`.

## C# Types & Interfaces
- `src/DividendTracker.Core/Common/AppInfoDto.cs`: public sealed record AppInfoDto(
- `src/DividendTracker.Core/Common/IAppInfoService.cs`: public interface IAppInfoService
- `src/DividendTracker.Maui/App.xaml.cs`: public partial class App : Application
- `src/DividendTracker.Maui/MainPage.xaml.cs`: public partial class MainPage : ContentPage
- `src/DividendTracker.Maui/MauiProgram.cs`: public static class MauiProgram
- `src/DividendTracker.Maui/Platforms/Android/MainActivity.cs`: public class MainActivity : MauiAppCompatActivity
- `src/DividendTracker.Maui/Platforms/Android/MainApplication.cs`: public class MainApplication : MauiApplication
- `src/DividendTracker.Maui/Platforms/Windows/App.xaml.cs`: public partial class App : MauiWinUIApplication
- `src/DividendTracker.Maui/Services/MauiAppInfoService.cs`: public sealed class MauiAppInfoService : IAppInfoService
- `src/DividendTracker.Web/Program.cs`: public class Program
- `src/DividendTracker.Web/Services/WebAppInfoService.cs`: public sealed class WebAppInfoService : IAppInfoService
- `tests/DividendTracker.Tests/Features/Shell/HomeComponentTests.cs`: public sealed class HomeComponentTests : BunitContext
- `tests/DividendTracker.Tests/Features/Shell/MainLayoutComponentTests.cs`: public sealed class MainLayoutComponentTests : BunitContext
- `tests/DividendTracker.Tests/Features/Shell/WebAppInfoServiceTests.cs`: public sealed class WebAppInfoServiceTests

## Razor Components
- `src/DividendTracker.Shared.UI/_Imports.razor`
- `src/DividendTracker.Shared.UI/Layout/MainLayout.razor`
- `src/DividendTracker.Shared.UI/Layout/NavMenu.razor`
- `src/DividendTracker.Shared.UI/Pages/Home.razor`: @page "/" | @inject IAppInfoService AppInfoService
- `src/DividendTracker.Shared.UI/Routes.razor`
- `src/DividendTracker.Web/Components/App.razor`

## SQL Schema
- *(none yet)*

