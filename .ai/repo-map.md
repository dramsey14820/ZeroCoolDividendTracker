# Repository Map (Auto-Generated)

> Compact signature-only index for Stage 2 slice planning. Regenerate after commits via `.\.ai\update-repo-map.ps1`.

## C# Types & Interfaces
- `src/DividendTracker.Core/Common/AppInfoDto.cs`: public sealed record AppInfoDto(
- `src/DividendTracker.Core/Common/IAppInfoService.cs`: public interface IAppInfoService
- `src/DividendTracker.Core/Features/Holdings/HoldingDtos.cs`: public sealed record MarketBuyDto(; public sealed record HoldingDto(; public sealed record CreateHoldingRequest(; public sealed record UpdateHoldingRequest(; public sealed record CreateMarketBuyRequest(; public sealed record UpdateMarketBuyRequest(
- `src/DividendTracker.Core/Features/Holdings/HoldingService.cs`: public sealed class HoldingService : IHoldingService
- `src/DividendTracker.Core/Features/Holdings/HoldingServiceCollectionExtensions.cs`: public static class HoldingServiceCollectionExtensions
- `src/DividendTracker.Core/Features/Holdings/IHoldingService.cs`: public interface IHoldingService
- `src/DividendTracker.Core/Features/Portfolios/IPortfolioService.cs`: public interface IPortfolioService
- `src/DividendTracker.Core/Features/Portfolios/PortfolioDtos.cs`: public sealed record AccountDto(; public sealed record PortfolioDto(; public sealed record CreatePortfolioRequest(; public sealed record UpdatePortfolioRequest(; public sealed record CreateAccountRequest(; public sealed record UpdateAccountRequest(
- `src/DividendTracker.Core/Features/Portfolios/PortfolioService.cs`: public sealed class PortfolioService : IPortfolioService
- `src/DividendTracker.Core/Features/Portfolios/PortfolioServiceCollectionExtensions.cs`: public static class PortfolioServiceCollectionExtensions
- `src/DividendTracker.Core/Features/Securities/ISecurityService.cs`: public interface ISecurityService
- `src/DividendTracker.Core/Features/Securities/SecurityDtos.cs`: public enum LookupCategory; public sealed record LookupItemDto(; public sealed record LookupCatalogDto(; public sealed record CreateLookupRequest(; public sealed record UpdateLookupRequest(; public sealed record SecurityDto(; public sealed record CreateSecurityRequest(; public sealed record UpdateSecurityRequest(
- `src/DividendTracker.Core/Features/Securities/SecurityService.cs`: public sealed class SecurityService : ISecurityService
- `src/DividendTracker.Core/Features/Securities/SecurityServiceCollectionExtensions.cs`: public static class SecurityServiceCollectionExtensions
- `src/DividendTracker.Core/Infrastructure/Persistence/DecimalTextTypeHandler.cs`: public sealed class DecimalTextTypeHandler : SqlMapper.TypeHandler<decimal>
- `src/DividendTracker.Core/Infrastructure/Persistence/IDatabaseInitializer.cs`: public interface IDatabaseInitializer
- `src/DividendTracker.Core/Infrastructure/Persistence/ISqliteConnectionFactory.cs`: public interface ISqliteConnectionFactory
- `src/DividendTracker.Core/Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs`: public static class PersistenceServiceCollectionExtensions
- `src/DividendTracker.Core/Infrastructure/Persistence/SqliteConnectionFactory.cs`: public sealed class SqliteConnectionFactory : ISqliteConnectionFactory
- `src/DividendTracker.Core/Infrastructure/Persistence/SqliteDatabaseInitializer.cs`: public sealed class SqliteDatabaseInitializer : IDatabaseInitializer
- `src/DividendTracker.Core/Infrastructure/Persistence/SqliteOptions.cs`: public sealed record SqliteOptions(string ConnectionString);
- `src/DividendTracker.Maui/App.xaml.cs`: public partial class App : Application
- `src/DividendTracker.Maui/MainPage.xaml.cs`: public partial class MainPage : ContentPage
- `src/DividendTracker.Maui/MauiProgram.cs`: public static class MauiProgram
- `src/DividendTracker.Maui/Platforms/Android/MainActivity.cs`: public class MainActivity : MauiAppCompatActivity
- `src/DividendTracker.Maui/Platforms/Android/MainApplication.cs`: public class MainApplication : MauiApplication
- `src/DividendTracker.Maui/Platforms/Windows/App.xaml.cs`: public partial class App : MauiWinUIApplication
- `src/DividendTracker.Maui/Services/MauiAppInfoService.cs`: public sealed class MauiAppInfoService : IAppInfoService
- `src/DividendTracker.Web/Program.cs`: public class Program
- `src/DividendTracker.Web/Services/WebAppInfoService.cs`: public sealed class WebAppInfoService : IAppInfoService
- `tests/DividendTracker.Tests/Features/Holdings/HoldingServiceTests.cs`: public sealed class HoldingServiceTests : IDisposable
- `tests/DividendTracker.Tests/Features/Holdings/HoldingsPageTests.cs`: public sealed class HoldingsPageTests : BunitContext
- `tests/DividendTracker.Tests/Features/Portfolios/PortfolioServiceTests.cs`: public sealed class PortfolioServiceTests : IDisposable
- `tests/DividendTracker.Tests/Features/Portfolios/PortfoliosPageTests.cs`: public sealed class PortfoliosPageTests : BunitContext
- `tests/DividendTracker.Tests/Features/Securities/SecuritiesPageTests.cs`: public sealed class SecuritiesPageTests : BunitContext
- `tests/DividendTracker.Tests/Features/Securities/SecurityServiceTests.cs`: public sealed class SecurityServiceTests : IDisposable
- `tests/DividendTracker.Tests/Features/Shell/HomeComponentTests.cs`: public sealed class HomeComponentTests : BunitContext
- `tests/DividendTracker.Tests/Features/Shell/MainLayoutComponentTests.cs`: public sealed class MainLayoutComponentTests : BunitContext
- `tests/DividendTracker.Tests/Features/Shell/WebAppInfoServiceTests.cs`: public sealed class WebAppInfoServiceTests
- `tests/DividendTracker.Tests/Infrastructure/Persistence/SqliteConnectionFactoryTests.cs`: public sealed class SqliteConnectionFactoryTests : IDisposable
- `tests/DividendTracker.Tests/Infrastructure/Persistence/SqliteDatabaseInitializerTests.cs`: public sealed class SqliteDatabaseInitializerTests : IDisposable

## Razor Components
- `src/DividendTracker.Shared.UI/_Imports.razor`
- `src/DividendTracker.Shared.UI/Layout/MainLayout.razor`
- `src/DividendTracker.Shared.UI/Layout/NavMenu.razor`
- `src/DividendTracker.Shared.UI/Pages/Holdings.razor`: @page "/holdings" | @inject IHoldingService HoldingService | @inject IPortfolioService PortfolioService | @inject ISecurityService SecurityService
- `src/DividendTracker.Shared.UI/Pages/Home.razor`: @page "/" | @inject IAppInfoService AppInfoService
- `src/DividendTracker.Shared.UI/Pages/Portfolios.razor`: @page "/portfolios" | @inject IPortfolioService PortfolioService
- `src/DividendTracker.Shared.UI/Pages/Securities.razor`: @page "/securities" | @inject ISecurityService SecurityService
- `src/DividendTracker.Shared.UI/Routes.razor`
- `src/DividendTracker.Web/Components/App.razor`

## SQL Schema
- *(none yet)*

