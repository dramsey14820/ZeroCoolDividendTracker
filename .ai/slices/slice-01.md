# Slice 01: Walking Skeleton & Multi-Host Shell

- **Status**: `In Progress`
- **Risk**: `High`
- **Owner / Stage**: Stage 2 (Gemini Mini-Spec) -> Stages 3–5 (Local Ollama) -> Stage 6 (Two-Tier Review)

---

## 1. Scope & Target Files

### In Scope
- Pin `.NET 10` LTS SDK in `global.json` (`10.0.404`, `rollForward: latestFeature`).
- Create `ZeroCoolDividendTracker.sln` with 5 projects:
  1. `src/DividendTracker.Core` (`net10.0` class library)
  2. `src/DividendTracker.Shared.UI` (`net10.0` Razor Class Library, pure CSS Grid/Flexbox mobile-first shell)
  3. `src/DividendTracker.Web` (`net10.0` Blazor Server host, explicit `public class Program` + `static void Main(string[] args)`)
  4. `src/DividendTracker.Maui` (`net10.0-windows10.0.19041.0;net10.0-android`, `<WindowsPackageType>None</WindowsPackageType>`, WebView2 `%LOCALAPPDATA%\ZeroCoolDividendTracker\WebView2` redirect)
  5. `tests/DividendTracker.Tests` (`net10.0` xUnit + FluentAssertions + NSubstitute + bUnit)
- Implement `IAppInfoService` + `AppInfoDto` so `Home.razor` displays app name, active platform host, and version via DI.
- Provide base design tokens (`wwwroot/css/app.css`) and responsive CSS Grid/Flexbox `MainLayout` + `NavMenu` + `Home` pages.

### Out of Scope
- SQLite database schema or repositories (Slice 02)
- Portfolio/Account/Holding CRUD (Slices 03–06)
- WiX `.msi` installer project (Slice 10)
- Authentication or external HTTP calls

### Target Files
- **Create**:
  - `global.json`
  - `ZeroCoolDividendTracker.sln`
  - `src/DividendTracker.Core/DividendTracker.Core.csproj`
  - `src/DividendTracker.Core/Common/AppInfoDto.cs`
  - `src/DividendTracker.Core/Common/IAppInfoService.cs`
  - `src/DividendTracker.Shared.UI/DividendTracker.Shared.UI.csproj`
  - `src/DividendTracker.Shared.UI/_Imports.razor`
  - `src/DividendTracker.Shared.UI/Routes.razor`
  - `src/DividendTracker.Shared.UI/Layout/MainLayout.razor`
  - `src/DividendTracker.Shared.UI/Layout/MainLayout.razor.css`
  - `src/DividendTracker.Shared.UI/Layout/NavMenu.razor`
  - `src/DividendTracker.Shared.UI/Layout/NavMenu.razor.css`
  - `src/DividendTracker.Shared.UI/Pages/Home.razor`
  - `src/DividendTracker.Shared.UI/Pages/Home.razor.css`
  - `src/DividendTracker.Shared.UI/wwwroot/css/app.css`
  - `src/DividendTracker.Web/DividendTracker.Web.csproj`
  - `src/DividendTracker.Web/Program.cs`
  - `src/DividendTracker.Web/Components/App.razor`
  - `src/DividendTracker.Web/Services/WebAppInfoService.cs`
  - `src/DividendTracker.Maui/DividendTracker.Maui.csproj`
  - `src/DividendTracker.Maui/MauiProgram.cs`
  - `src/DividendTracker.Maui/App.xaml`
  - `src/DividendTracker.Maui/App.xaml.cs`
  - `src/DividendTracker.Maui/MainPage.xaml`
  - `src/DividendTracker.Maui/MainPage.xaml.cs`
  - `src/DividendTracker.Maui/Services/MauiAppInfoService.cs`
  - `src/DividendTracker.Maui/wwwroot/index.html`
  - `tests/DividendTracker.Tests/DividendTracker.Tests.csproj`
  - `tests/DividendTracker.Tests/Features/Shell/HomeComponentTests.cs`
  - `tests/DividendTracker.Tests/Features/Shell/MainLayoutComponentTests.cs`
  - `tests/DividendTracker.Tests/Features/Shell/WebAppInfoServiceTests.cs`

---

## 2. Data Contracts / Interfaces / SQL Schema

### Required Namespaces & Packages
- `DividendTracker.Core`: no external NuGet packages required.
- `DividendTracker.Shared.UI`: `Microsoft.AspNetCore.Components.Web` (`10.*`).
- `DividendTracker.Web`: `Microsoft.NET.Sdk.Web` (references `DividendTracker.Core`, `DividendTracker.Shared.UI`).
- `DividendTracker.Maui`: `Microsoft.Maui.Controls`, `Microsoft.AspNetCore.Components.WebView.Maui` (`10.*`), `<UseMaui>true</UseMaui>`, `<WindowsPackageType>None</WindowsPackageType>`.
- `DividendTracker.Tests`: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `FluentAssertions`, `NSubstitute`, `bunit`.
- Key namespaces: `Bunit`, `FluentAssertions`, `NSubstitute`, `Xunit`, `Microsoft.Extensions.DependencyInjection`.

### DTOs & Contracts
```csharp
namespace DividendTracker.Core.Common;

public sealed record AppInfoDto(
    string ApplicationName,
    string PlatformName,
    string Version);
```

### Interfaces
```csharp
namespace DividendTracker.Core.Common;

public interface IAppInfoService
{
    AppInfoDto GetAppInfo();
}
```

### Host & UI Notes
- `DividendTracker.Web/Program.cs`: explicit `public class Program` with `public static void Main(string[] args)` (no top-level statements). Registers `AddRazorComponents().AddInteractiveServerComponents()` and `builder.Services.AddSingleton<IAppInfoService, WebAppInfoService>()`.
- `DividendTracker.Web/Services/WebAppInfoService.cs`: returns `new AppInfoDto("ZeroCool Dividend Tracker", "Blazor Server", "1.0.0")`.
- `DividendTracker.Maui/MainPage.xaml.cs`: subscribes to `BlazorWebViewInitializing` on Windows and sets `e.UserDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZeroCoolDividendTracker", "WebView2")`.
- `DividendTracker.Shared.UI/Layout/MainLayout.razor`: CSS Grid shell (`header`/`sidebar` + `main`), mobile-first responsive `@media (min-width: 768px)` layout.
- `DividendTracker.Shared.UI/Layout/NavMenu.razor`: Flexbox nav with links to `/` (`Dashboard`), `/portfolios` (`Portfolios`), `/holdings` (`Holdings`), `/dividends` (`Dividends`), and `/settings` (`Settings`), with `min-height: 44px` touch targets.
- `DividendTracker.Shared.UI/Pages/Home.razor`: injects `IAppInfoService`, renders `ApplicationName`, `PlatformName`, `Version`, and a CSS Grid overview section.

### SQL Schema / Query Notes
- None in Slice 01 (SQLite persistence is introduced in Slice 02).

---

## 3. Behavioral Acceptance Criteria (Gherkin)

```gherkin
Feature: Walking Skeleton & Multi-Host Shell

  Scenario: Home page renders application metadata from IAppInfoService
    Given IAppInfoService returns "ZeroCool Dividend Tracker", "Blazor Server", and "1.0.0"
    When the Home component is rendered
    Then the markup displays "ZeroCool Dividend Tracker"
    And the markup displays "Blazor Server"
    And the markup displays "1.0.0"

  Scenario: NavMenu renders primary navigation links
    Given the shared UI navigation menu is mounted
    When the NavMenu component is rendered
    Then it contains navigation links for "Dashboard", "Portfolios", "Holdings", "Dividends", and "Settings"

  Scenario: MainLayout renders navigation and body content
    Given the MainLayout component is rendered with child content "Welcome Body"
    When the layout renders
    Then it displays the NavMenu and the child content "Welcome Body"

  Scenario: WebAppInfoService returns Blazor Server metadata
    Given an instance of WebAppInfoService
    When GetAppInfo is called
    Then ApplicationName is "ZeroCool Dividend Tracker"
    And PlatformName is "Blazor Server"
    And Version is not null or whitespace
```

---

## 4. Implementation Subtasks

- [ ] **Red**: Generate xUnit + FluentAssertions + NSubstitute + bUnit tests covering all Gherkin scenarios (`qwen2.5-coder:7b`)
- [ ] **Green**: Scaffold `.NET 10` solution, projects, contracts, shared CSS Grid/Flexbox UI, Web/MAUI hosts, and DI registrations (`qwen2.5-coder:7b`)
- [ ] **Verify**: Pass `dotnet build -warnaserror` and `dotnet test` (local fixes via `qwen3.5:9b`, max 2 retries)
- [ ] **Pass-1 Review**: Run local `qwen3.5:9b` review on `git diff --staged`
- [ ] **Pass-2 Audit**: Submit `git diff --staged` for Gemini architectural & security sign-off

