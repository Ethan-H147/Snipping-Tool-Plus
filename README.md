# Snipping Tool+

A lightweight Windows screen capture app with configurable global shortcuts, rectangle and freeform selections, full-screen capture, PNG saving, and clipboard copying.

The current application is named **QuietSnip** in its source, executable, and settings. This repository preserves that implementation as the starting point for Snipping Tool+.

## Build and run

Requires Windows and the .NET 10 SDK with Windows Desktop support.

```powershell
dotnet build .\QuietSnip-investigation\QuietSnip\QuietSnip.csproj -c Release
dotnet run --project .\QuietSnip-investigation\QuietSnip\QuietSnip.csproj -c Release
```

Default shortcuts:

- **Ctrl + Alt + S**: select and copy a snip.
- **Ctrl + Alt + D**: select, save, and copy a snip.

Use the tray menu to open settings and configure shortcuts, capture options, and save folders.

## Tests and diagnostics

Run the core tests with an isolated output directory:

```powershell
dotnet run --project .\QuietSnip-investigation\QuietSnip.Tests\QuietSnip.Tests.csproj -c Release -- .\test-results\core --investigation-core
dotnet build .\QuietSnip-investigation\QuietSnip.Diagnostics\QuietSnip.Diagnostics.csproj -c Release
```

The core test mode checks shortcuts, settings, geometry, PNG output, saved regions, placement, and icons without writing to the clipboard. Other test modes can interact with the desktop and clipboard; review their source before running them.

See the [diagnostics guide](QuietSnip-investigation/QuietSnip.Diagnostics/README.md) and [investigation notes](QuietSnip-investigation/INVESTIGATION.md) for the capture and HDR investigation. HDR behavior remains under investigation.

Generated builds, local screenshots, machine-specific reports, and test output are excluded from version control.
