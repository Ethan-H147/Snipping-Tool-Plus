# Snipping Tool+

A lightweight Windows screen capture app with configurable global shortcuts, rectangle and freeform selections, full-screen capture, PNG saving, and clipboard copying.

The app and executable are named **Snipping Tool+**. Source projects use `SnippingToolPlus` as their code identifier.

## Build and run

Requires Windows 10 version 2004 or later and the .NET 10 SDK with Windows Desktop support. The published app requires the .NET 10 Desktop Runtime.

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

## Text recognition

Start your normal snip, click **Text** in the capture toolbar, and select a region. The selected box stays exactly where you drew it on the frozen screen. Recognized words are highlighted inside the box; drag across them to select text, or press Ctrl+A to select everything. Ctrl+C copies the selected words, or all recognized text if nothing is selected.

A compact floating toolbar provides **Back**, **Copy all text**, **More**, and **Close**. Back starts a fresh region selection; Close or Escape dismisses OCR. Drag the box edges or corners to resize the region and recognize it again. Resizing cancels stale recognition work so older results cannot overwrite the new selection.

The More menu contains **Copy selected**, **Select all**, **Remove line breaks**, and **Open in app**. The larger editable text window opens only when you choose Open in app or explicitly use **Text actions for last snip** from the tray. The normal app window is never the default result of a text capture. Optional shortcuts for fresh text capture and last-snip text actions are available under Settings → Shortcuts.

Recognition uses Windows OCR locally, with an installed recognition language matching your Windows preferences where available. It sends no images to a server. Text mode does not save a PNG or replace the clipboard until you choose a copy action. Accuracy depends on text size, contrast, language, and image quality. Missing recognition languages can be added in Windows Settings → Time & language → Language & region.

Word highlights ease in over 160 ms and respect Windows' animation setting. Capture shortcuts and toolbar changes remain immediate.


## Tests and diagnostics

Run the core tests with an isolated output directory:

```powershell
dotnet run --project .\SnippingToolPlus-investigation\SnippingToolPlus.Tests\SnippingToolPlus.Tests.csproj -c Release -- .\test-results\core --investigation-core
dotnet run --project .\SnippingToolPlus-investigation\SnippingToolPlus.Tests\SnippingToolPlus.Tests.csproj -c Release -- .\test-results\rapid-capture --rapid-capture
dotnet run --project .\SnippingToolPlus-investigation\SnippingToolPlus.Tests\SnippingToolPlus.Tests.csproj -c Release -- .\test-results\ocr --ocr-tests
dotnet run --project .\SnippingToolPlus-investigation\SnippingToolPlus.Tests\SnippingToolPlus.Tests.csproj -c Release -- .\test-results\ocr-ui --ocr-ui
dotnet run --project .\SnippingToolPlus-investigation\SnippingToolPlus.Tests\SnippingToolPlus.Tests.csproj -c Release -- .\test-results\ocr-overlay --ocr-overlay
dotnet build .\SnippingToolPlus-investigation\SnippingToolPlus.Diagnostics\SnippingToolPlus.Diagnostics.csproj -c Release
```

The core test mode checks shortcuts, settings, geometry, PNG output, saved regions, placement, and icons without writing to the clipboard. Other test modes can interact with the desktop and clipboard; review their source before running them.

The rapid capture checks open and cancel overlays to verify that an earlier confirmation popup cannot close the next capture. They leave the clipboard and saved settings unchanged.

See the [diagnostics guide](SnippingToolPlus-investigation/SnippingToolPlus.Diagnostics/README.md) and [investigation notes](SnippingToolPlus-investigation/INVESTIGATION.md) for the capture and HDR investigation. HDR behavior remains under investigation.

Generated builds, local screenshots, machine-specific reports, and test output are excluded from version control.
