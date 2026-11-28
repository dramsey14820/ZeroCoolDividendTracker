# Repository Map (Auto-Generated)

> Compact signature-only index for Stage 2 slice planning. Regenerate after commits via `.\.ai\update-repo-map.ps1`.

## C# Types & Interfaces
- `src/DividendTracker.Core/Common/AppInfoDto.cs`: public sealed record AppInfoDto(
- `src/DividendTracker.Core/Common/IAppInfoService.cs`: public interface IAppInfoService
- `src/DividendTracker.Core/Features/Analytics/AnalyticsDtos.cs`: public sealed record DashboardSummaryDto(; public sealed record HoldingAnalyticsDto(; public sealed record AllocationItemDto(; public sealed record MonthlyIncomePointDto(; public sealed record DashboardAnalyticsDto(
- `src/DividendTracker.Core/Features/Analytics/AnalyticsService.cs`: public sealed class AnalyticsService : IAnalyticsService
- `src/DividendTracker.Core/Features/Analytics/AnalyticsServiceCollectionExtensions.cs`: public static class AnalyticsServiceCollectionExtensions
- `src/DividendTracker.Core/Features/Analytics/IAnalyticsService.cs`: public interface IAnalyticsService
- `src/DividendTracker.Core/Features/Dividends/DividendDtos.cs`: public sealed record DividendPaymentDto(; public sealed record RecordDividendRequest(; public sealed record UpdateDividendRequest(
- `src/DividendTracker.Core/Features/Dividends/DividendService.cs`: public sealed class DividendService : IDividendService
- `src/DividendTracker.Core/Features/Dividends/DividendServiceCollectionExtensions.cs`: public static class DividendServiceCollectionExtensions
- `src/DividendTracker.Core/Features/Dividends/IDividendService.cs`: public interface IDividendService
- `src/DividendTracker.Core/Features/Holdings/HoldingDtos.cs`: public sealed record MarketBuyDto(; public sealed record HoldingDto(; public sealed record CreateHoldingRequest(; public sealed record UpdateHoldingRequest(; public sealed record CreateMarketBuyRequest(; public sealed record UpdateMarketBuyRequest(
- `src/DividendTracker.Core/Features/Holdings/HoldingService.cs`: public sealed class HoldingService : IHoldingService
- `src/DividendTracker.Core/Features/Holdings/HoldingServiceCollectionExtensions.cs`: public static class HoldingServiceCollectionExtensions
- `src/DividendTracker.Core/Features/Holdings/IHoldingService.cs`: public interface IHoldingService
- `src/DividendTracker.Core/Features/MarketData/FinnhubMarketDataProvider.cs`: public sealed class FinnhubMarketDataProvider : IMarketDataProvider
- `src/DividendTracker.Core/Features/MarketData/FmpMarketDataProvider.cs`: public sealed class FmpMarketDataProvider : IMarketDataProvider
- `src/DividendTracker.Core/Features/MarketData/IMarketDataProvider.cs`: public interface IMarketDataProvider
- `src/DividendTracker.Core/Features/MarketData/IMarketDataService.cs`: public interface IMarketDataService
- `src/DividendTracker.Core/Features/MarketData/MarketDataDtos.cs`: public enum MarketDataProviderType; public sealed record MarketDataSettingsDto(; public sealed record UpdateMarketDataSettingsRequest(; public sealed record MarketQuoteDto(; public sealed record ExternalDividendEventDto(; public sealed record MarketDataRefreshResultDto(; public sealed record DividendImportResultDto(
- `src/DividendTracker.Core/Features/MarketData/MarketDataService.cs`: public sealed class MarketDataService : IMarketDataService
- `src/DividendTracker.Core/Features/MarketData/MarketDataServiceCollectionExtensions.cs`: public static class MarketDataServiceCollectionExtensions
- `src/DividendTracker.Core/Features/MarketData/YahooFinanceMarketDataProvider.cs`: public sealed class YahooFinanceMarketDataProvider : IMarketDataProvider
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
- `tests/DividendTracker.Tests/Features/Analytics/AnalyticsServiceTests.cs`: public sealed class AnalyticsServiceTests : IDisposable
- `tests/DividendTracker.Tests/Features/Analytics/DashboardPageTests.cs`: public sealed class DashboardPageTests : BunitContext
- `tests/DividendTracker.Tests/Features/Dividends/DividendServiceTests.cs`: public sealed class DividendServiceTests : IDisposable
- `tests/DividendTracker.Tests/Features/Dividends/DividendsPageTests.cs`: public sealed class DividendsPageTests : BunitContext
- `tests/DividendTracker.Tests/Features/Holdings/HoldingServiceTests.cs`: public sealed class HoldingServiceTests : IDisposable
- `tests/DividendTracker.Tests/Features/Holdings/HoldingsPageTests.cs`: public sealed class HoldingsPageTests : BunitContext
- `tests/DividendTracker.Tests/Features/MarketData/MarketDataProviderTests.cs`: public sealed class MarketDataProviderTests
- `tests/DividendTracker.Tests/Features/MarketData/MarketDataServiceTests.cs`: public sealed class MarketDataServiceTests : IDisposable
- `tests/DividendTracker.Tests/Features/MarketData/SettingsPageTests.cs`: public sealed class SettingsPageTests : BunitContext
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
- `src/DividendTracker.Shared.UI/Pages/Dividends.razor`: @page "/dividends" | @inject IDividendService DividendService | @inject IHoldingService HoldingService | @inject IPortfolioService PortfolioService
- `src/DividendTracker.Shared.UI/Pages/Holdings.razor`: @page "/holdings" | @inject IHoldingService HoldingService | @inject IPortfolioService PortfolioService | @inject ISecurityService SecurityService
- `src/DividendTracker.Shared.UI/Pages/Home.razor`: @page "/" | @inject IAppInfoService AppInfoService | @inject IAnalyticsService AnalyticsService | @inject IPortfolioService PortfolioService
- `src/DividendTracker.Shared.UI/Pages/Portfolios.razor`: @page "/portfolios" | @inject IPortfolioService PortfolioService
- `src/DividendTracker.Shared.UI/Pages/Securities.razor`: @page "/securities" | @inject ISecurityService SecurityService
- `src/DividendTracker.Shared.UI/Pages/Settings.razor`: @page "/settings" | @inject IMarketDataService MarketDataService
- `src/DividendTracker.Shared.UI/Routes.razor`
- `src/DividendTracker.Web/Components/App.razor`

## SQL Schema
- *(none yet)*

