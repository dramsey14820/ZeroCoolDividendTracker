# WiX v7 + .NET MAUI Blazor Hybrid (Unpackaged Windows) Packaging Standard

Reusable pipeline guide for packaging an unpackaged `.NET 10+` MAUI Blazor Hybrid Windows desktop application into a single-file per-machine `.msi` installer using **WiX Toolset v7 SDK**.

---

## 1. WiX Project Settings (`Installer.wixproj`)

- **WiX SDK**: Use `Sdk="WixToolset.Sdk/7.0.1"`.
- **Output Type**: `<OutputType>Package</OutputType>` to produce an `.msi`.
- **Target Architecture**: `<InstallerPlatform>x64</InstallerPlatform>` to match the `win-x64` publish output and 64-bit Program Files.

```xml
<Project Sdk="WixToolset.Sdk/7.0.1">
  <PropertyGroup>
    <OutputType>Package</OutputType>
    <InstallerPlatform>x64</InstallerPlatform>
  </PropertyGroup>
</Project>
```

---

## 2. WiX Package Definition (`Package.wxs`)

- **Schema**: `xmlns="http://wixtoolset.org/schemas/v4/wxs"`.
- **Package Metadata**: Set `Id`, `Name`, `Manufacturer`, `Version` (e.g., `1.0.0.0`), and `Scope="perMachine"`.
- **In-Place Upgrades**: Include `<MajorUpgrade DowngradeErrorMessage="..." />` to support clean upgrades and block downgrades.
- **Single-File MSI**: Include `<MediaTemplate EmbedCab="yes" />` so the `.cab` is embedded directly inside the `.msi`.
- **Shortcut Icon**: Reference the MAUI-generated `.ico` from resizetizer:
  ```xml
  <Icon Id="<AppId>Icon"
        SourceFile="..\<App>.Maui\obj\Release\net10.0-windows10.0.19041.0\win-x64\resizetizer\r\appicon.ico" />
  ```
- **Install & Start Menu Directories**:
  - `ProgramFiles64Folder` -> `INSTALLFOLDER` (`<App Display Name>`)
  - `ProgramMenuFolder` -> `AppStartMenuFolder` (`<App Display Name>`)
- **File Harvesting & Exclusions**:
  - Harvest `..\<App>.Maui\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\**` into `INSTALLFOLDER`.
  - **Exclude `**\*.mui`** to avoid duplicate/conflicting localized `.mui` resource files during harvesting.
  - **Exclude `<App>.Maui.exe.WebView2\**`** so local WebView2 runtime cache/profile files are never packaged into the MSI.
  - **Explicitly re-include `publish\en-us\**`** into `Subdirectory="en-us"` so English `.mui` resources are installed cleanly.
  - *(Optional bundled read-only data)*: Harvest `..\src\Data\**\*.json` (or app seed files) into `INSTALLFOLDER\Data`.
- **Start Menu Shortcut Component**:
  - `<Shortcut>` targeting `[INSTALLFOLDER]<App>.Maui.exe` with `WorkingDirectory="INSTALLFOLDER"`, `Icon="<AppId>Icon"`, `IconIndex="0"`.
  - `<RemoveFolder Id="RemoveStartMenuFolder" On="uninstall" />` for clean uninstall.
  - `<RegistryValue Root="HKCU" Key="Software\<AppId>" Name="Installed" Type="integer" Value="1" KeyPath="yes" />` to satisfy MSI validation (**ICE38 / ICE43**) requiring a registry `KeyPath` on a user-profile Start Menu shortcut.

---

## 3. MAUI Project Settings (`<App>.Maui.csproj`)

- **Unpackaged Windows Mode**: Set `<WindowsPackageType>None</WindowsPackageType>` so MAUI builds an unpackaged Win32/WinUI app suitable for MSI distribution instead of MSIX.
- **Prevent Duplicate Bundled Data Files in Publish** *(when WiX harvests data directly)*: Set `CopyToPublishDirectory="Never"` while keeping `CopyToOutputDirectory="PreserveNewest"` on bundled data files (e.g., `..\src\Data\**\*.json`) so `dotnet publish` does not duplicate files already harvested by `Package.wxs`.

---

## 4. Runtime Settings Required for Per-Machine (`Program Files`) Installs

Because `Scope="perMachine"` installs into **read-only** `C:\Program Files\<App Display Name>\`, the application MUST never write runtime state next to the `.exe`:

1. **Writable WebView2 User Data Folder (`MainPage.xaml.cs`)**:
   - Handle `BlazorWebViewInitializing` and redirect `e.UserDataFolder` to `%LOCALAPPDATA%\<AppId>\WebView2` (using `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)`).
   - Without this, WebView2 attempts to create `<App>.Maui.exe.WebView2` inside `Program Files`, failing with a blank window.
2. **Writable App Data / SQLite / Seeded Files (`MauiProgram.cs`)**:
   - Store all writable data (SQLite database, user settings, imported/exported state) under `FileSystem.AppDataDirectory` or `%LOCALAPPDATA%\<AppId>\`.
   - If the installer ships read-only seed files under `Path.Combine(AppContext.BaseDirectory, "Data")`, copy them on first startup into the writable app data directory rather than opening them for write in place.

