# Snipping Tool+ diagnostics

This portable companion investigates the existing capture pipeline. It compiles the unchanged `CaptureService.cs` and `AppSettings.cs` sources into a separate executable. It does not instantiate the tray app, register shortcuts, read or save application preferences, change startup, switch HDR, apply tone mapping, or adjust brightness.

The working app is already 0.3.1. The name **0.2.6 investigation** refers to the agreed roadmap milestone, not a version downgrade.

The executable needs the same .NET 10 Desktop Runtime as Snipping Tool+. No additional NuGet packages, installation, package identity, service, or network connection are required. Keep the four published runtime files together in a writable folder.

## Start

Double-click `SnippingToolPlus.Diagnostics.exe` to open a known SDR patch fixture. It collects 100 capture/crop/PNG cycles after four seconds, writes a new directory under `results` beside the executable, and stays open for comparison captures. Close the fixture when finished.

The fixture uses physical pixels and checks whether its sample area is visible on its desktop before capture. Run normally in an unlocked interactive session, not with administrator elevation. A failed visibility check invalidates that run. Do not classify unrelated/occluded content as a color error. Avoid overlays or moving windows during capture.

## Command line

Run from the folder containing the diagnostics executable. Use a fresh, empty output directory for each invocation. Existing evidence is never overwritten.

```powershell
.\SnippingToolPlus.Diagnostics.exe --self-test --output .\results\synthetic
.\SnippingToolPlus.Diagnostics.exe --environment --output .\results\display
.\SnippingToolPlus.Diagnostics.exe --fixture --cycles 100 --hold --label hdr-on-sdr --output .\results\hdr-on-sdr
.\SnippingToolPlus.Diagnostics.exe --capture --bounds 896,541,768,384 --label verified-hdr-source --output .\results\hdr-region
.\SnippingToolPlus.Diagnostics.exe --inspect --file .\original.png --output .\results\inspect-original
.\SnippingToolPlus.Diagnostics.exe --compare --file .\first.png --other .\second.png --output .\results\compare
.\SnippingToolPlus.Diagnostics.exe --clipboard-snapshot --output .\results\clipboard-read-only
.\SnippingToolPlus.Diagnostics.exe --delivery-test --clipboard-roundtrip --output .\results\delivery
.\SnippingToolPlus.Diagnostics.exe --contention-test --output .\results\contention
```

`--monitor 0` chooses a fixture monitor from the indices in `environment.json`. Otherwise the fixture opens on the monitor under the pointer. Arbitrary desktop capture requires explicit bounds; there is no automatic background screen capture.

`--self-test` uses synthetic images and isolated settings under its output directory. It does not read the desktop or clipboard. `--environment` reads display paths, Advanced Color mode, channel depth, SDR white level, and legacy ICM profile lookup status. It never changes them. Unsupported queries retain their Windows error codes rather than reporting false/zero values.

## Evidence boundaries

- `00-expected-sdr`: generated SDR code values, not an HDR source.
- `01-acquired-gdi`: encoding of the unmodified production GDI acquisition buffer.
- `02-cropped-png`: production crop and PNG encoding. Compare decoded pixels, not compressed file hashes, to test encoding fidelity.
- `03-production-delivery`: production save-plus-copy output when explicitly requested.
- `04-clipboard`: original HGLOBAL image representations where available, plus a clearly labelled diagnostic `REENCODED` bitmap PNG. CF_BITMAP is a GDI handle and is not misread as an HGLOBAL byte array.
- JSON reports record decoded pixel differences, alpha differences, first differing coordinates, patch centers, chunk CRCs, metadata, hashes, timing, and resource samples.

The image comparer compares decoded code values without applying ICC transforms. Inputs must have matching coordinates and dimensions. It is not a perceptual HDR accuracy score and does not automatically align different snips. A file hash identifies exact bytes; a decoded-pixel hash identifies the canonical BGRA decoder output.

The PNG inspector records metadata from the file being inspected only. A bitmap re-encoded from clipboard data does not establish the metadata of the original capture. The native-format files retain the offered clipboard allocation bytes; these can include allocation padding and can themselves originate from an earlier conversion.

## Clipboard testing

The default fixture leaves the clipboard untouched. The separate `--delivery-test --clipboard-roundtrip` explicitly tests opaque and freeform synthetic images through production save-plus-copy. It retains supported prior clipboard data in memory, aborts before writing if a format cannot be retained, and restores supported logical formats afterward. This is not a byte-for-byte backup of arbitrary private clipboard formats. If another application changes the clipboard during the test, its newer contents are preserved.

`--clipboard-snapshot` reads the current image clipboard without replacing it. Use it immediately after a controlled copy operation; label the origin and avoid unrelated clipboard changes.

`--contention-test` holds the clipboard in a hidden child process for 15 seconds. It measures retry exhaustion and verifies that the requested PNG survives. The hold duration covers WinForms' internal retries as well as Snipping Tool+'s loop. It does not alter app settings or write to the clipboard while locked.

## Build and app integration checks

From the `work` directory:

```powershell
dotnet build SnippingToolPlus.Diagnostics\SnippingToolPlus.Diagnostics.csproj -c Release
dotnet publish SnippingToolPlus.Diagnostics\SnippingToolPlus.Diagnostics.csproj -c Release --self-contained false -o diagnostics-publish
dotnet run --project SnippingToolPlus.Tests\SnippingToolPlus.Tests.csproj -c Release -- investigation-results\core --investigation-core
dotnet run --project SnippingToolPlus.Tests\SnippingToolPlus.Tests.csproj -c Release -- investigation-results\overlay --overlay-baseline
```

The added test modes use explicit isolated result directories. `--investigation-core` avoids the clipboard-writing full suite. `--overlay-baseline` opens the actual production overlay over synthetic light/dark desktop backgrounds, captures its open popup, dispatches Escape through its key handler, and checks 100 modal open/cancel cycles. It measures form construction-to-Shown rather than global-shortcut-to-visible latency. Review the full-overlay PNGs; isolated component snapshots are insufficient.

GUI-resource counts are sampled after cycles 10, 50, and 100. The plateau check compares 50 with 100 because the initial font/menu caches were still warming at cycle 10. Private-memory measurements are observations, not proof of a leak or proof that all memory is reclaimed.
