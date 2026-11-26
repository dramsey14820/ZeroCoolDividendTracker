# Blazor Server & .NET MAUI Blazor Hybrid UI Standards

Load this rule file whenever a vertical slice creates or modifies UI components, CSS, platform hosts, or desktop packaging.

## 1. Shared UI & Host Architecture

- **Single Shared UI (`*.Shared.UI` Razor Class Library)**:
  - All pages, reusable components (`.razor`), code-behind files (`.razor.cs`), and isolated stylesheets (`.razor.css`) live in the shared RCL.
  - Both hosts render the exact same components:
    - **`*.Web`**: ASP.NET Core Blazor Server (`InteractiveServer`).
    - **`*.Maui`**: `.NET MAUI Blazor Hybrid` (`BlazorWebView`) targeting **Windows Desktop** and **Android**.
- **No MAUI XAML Feature Screens**:
  - Keep MAUI XAML strictly limited to the minimal `MainPage.xaml` / `BlazorWebView` host shell. Never build feature screens in XAML.
- **Strict Service Abstraction**:
  - Razor components MUST inject `I*Service` interfaces (registered per host in DI).
  - Never inject `DbContext`, `IDbConnection`, Dapper, raw `HttpClient`, or Windows/Android platform APIs directly into `.razor` components.
  - Keep components thin: delegate business logic and state orchestration to services or slice handlers.

## 2. CSS Grid & Flexbox Styling Standards

- **Layout Primitives**:
  - Use **CSS Grid** (`display: grid`) for 2D page shells, dashboards, forms, and card grids (`grid-template-columns`, `gap`, `auto-fit` / `minmax()`).
  - Use **CSS Flexbox** (`display: flex`) for 1D alignment: navbars, toolbars, button groups, and vertical/horizontal stacks.
- **CSS Isolation & Design Tokens**:
  - Co-locate component styles in `ComponentName.razor.css` (Blazor CSS isolation).
  - Define shared colors, spacing, typography, and radii as CSS custom properties (`--color-*`, `--space-*`) in the shared stylesheet (`wwwroot/css/app.css`).
  - Avoid Bootstrap, Tailwind, or inline `style="..."` attributes unless explicitly required by a slice.
- **Mobile-First Responsive Design (Android + Desktop)**:
  - Design layouts mobile-first so every screen works cleanly on narrow Android portrait viewports and scales up cleanly to wide Windows desktop monitors using `@media` queries and responsive Grid tracks.
  - Respect safe touch targets (`min-height: 44px`) for interactive controls on Android.

## 3. Windows Desktop (WiX MSI) & Android Guardrails

- **WiX-Compatible Unpackaged Windows Build**:
  - Configure the MAUI project for unpackaged Windows deployment (`<WindowsPackageType>None</WindowsPackageType>`) so output can be harvested cleanly into a **WiX Toolset** MSI installer.
  - **Never** call MSIX-only APIs (e.g., `Windows.ApplicationModel.Package.Current`, `ApplicationData.Current`).
  - **Never** write logs, databases, or user settings into the installation directory (`Program Files`). Always use `Environment.SpecialFolder.LocalApplicationData` or `CommonApplicationData`.
- **Android Compatibility**:
  - Avoid Windows-only file path assumptions (use `Path.Combine`), and wrap any platform-specific capability (file picker, camera, notifications) behind a shared interface in `*.Shared.UI` with per-host implementations.

## 4. Razor Component Testing (`bUnit`)

- Use **`bUnit`** alongside `xUnit`, `FluentAssertions`, and `NSubstitute` to test Razor components in `*.Shared.UI`.
- Register mocked `I*Service` dependencies via `Services.AddSingleton(mockService)` and assert rendered markup, user interactions, and service calls without requiring a running browser or emulator.

