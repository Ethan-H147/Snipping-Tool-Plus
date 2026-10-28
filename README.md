# Snipping Tool+

A lightweight Windows screen capture app with configurable global shortcuts, rectangle and freeform selections, full-screen capture, PNG saving, and clipboard copying.

The app and executable are named **Snipping Tool+**. Source projects use `SnippingToolPlus` as their code identifier.

## Build and run

Requires Windows and the .NET 10 SDK with Windows Desktop support.

```powershell
dotnet build .\SnippingToolPlus-investigation\SnippingToolPlus\SnippingToolPlus.csproj -c Release
dotnet run --project .\SnippingToolPlus-investigation\SnippingToolPlus\SnippingToolPlus.csproj -c Release
```

Default shortcuts:

- **Ctrl + Alt + S**: select and copy a snip.
- **Ctrl + Alt + D**: select, save, and copy a snip.

Use the tray menu to open settings and configure shortcuts, capture options, and save folders.

The local runnable app lives directly in this project's main folder as `Snipping Tool+.exe`, alongside its runtime files, `settings.json`, and `regions.json`. To rebuild it there:

```powershell
dotnet publish .\SnippingToolPlus-investigation\SnippingToolPlus\SnippingToolPlus.csproj -c Release --self-contained false -o .
```

To update an existing QuietSnip installation, close the old app and place the new build in the same folder, keeping `settings.json` and `regions.json`. Existing shortcuts, save folders, and saved regions are retained. If startup was enabled, save settings once in the new app to update the Windows startup entry. New installations save screenshots to `Pictures\Snipping Tool+` by default.

## Tests and diagnostics

Run the core tests with an isolated output directory:

```powershell
dotnet run --project .\SnippingToolPlus-investigation\SnippingToolPlus.Tests\SnippingToolPlus.Tests.csproj -c Release -- .\test-results\core --investigation-core
dotnet run --project .\SnippingToolPlus-investigation\SnippingToolPlus.Tests\SnippingToolPlus.Tests.csproj -c Release -- .\test-results\rapid-capture --rapid-capture
dotnet build .\SnippingToolPlus-investigation\SnippingToolPlus.Diagnostics\SnippingToolPlus.Diagnostics.csproj -c Release
```

The core test mode checks shortcuts, settings, geometry, PNG output, saved regions, placement, and icons without writing to the clipboard. Other test modes can interact with the desktop and clipboard; review their source before running them.

The rapid capture checks open and cancel overlays to verify that an earlier confirmation popup cannot close the next capture. They leave the clipboard and saved settings unchanged.

See the [diagnostics guide](SnippingToolPlus-investigation/SnippingToolPlus.Diagnostics/README.md) and [investigation notes](SnippingToolPlus-investigation/INVESTIGATION.md) for the capture and HDR investigation. HDR behavior remains under investigation.

Generated builds, local screenshots, machine-specific reports, and test output are excluded from version control.
