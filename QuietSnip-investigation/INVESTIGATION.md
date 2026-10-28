# QuietSnip 0.2.6 investigation — first baseline

Date: October 28, 2026, America/Chicago.

Status: diagnostic tooling and the current-display SDR baseline are implemented. The HDR investigation remains open. No production capture, conversion, encoding, clipboard transport, shortcut, settings, startup, or UI changes have been made. Existing 0.3.1 source and application binaries are retained; there is no version downgrade.

## Findings

| Experiment | Observation | What it establishes |
| --- | --- | --- |
| Display inventory | AW2725D, 2560 × 1440, about 279.961 Hz, HDR active according to Advanced Color Info2, 10 bits/channel, SDR white 284 nits | The state of this run, not a universal HDR configuration |
| Validated visible SDR fixture on HDR display | All 294,912 acquired pixels, including all 32 patch centers, matched the generated SDR bitmap exactly after adding a fixture margin | No reproduced SDR code-value brightness error in this controlled GDI case |
| Crop and PNG roundtrip | Zero RGB/alpha code-value error for this fixture and synthetic RGB/alpha cases | Crop/PNG encoding did not create a brightness change in these tests |
| Opaque synthetic clipboard | Zero RGB/alpha error using Clipboard.GetImage after production save-plus-copy | This tested bitmap consumer preserved opaque values; other paste targets remain untested |
| Freeform synthetic clipboard | Saved PNG preserved alpha exactly; Bitmap/GetImage readback changed 138,368 alpha pixels and also changed RGB values in that representation | A separate clipboard transparency/representation compatibility issue; it does not explain the earlier HDR brightness reversal |
| Busy clipboard | PNG saved; copy failed as expected after about 9,348 ms | Save-first behavior works, but the user-visible contention delay is much larger than eight 60 ms waits |
| 100 GDI capture/crop/encode cycles, 768 × 384 | Capture-only median 6.04 ms, p95 9.07 ms; GDI/USER counts unchanged before/after | A small-region acquisition baseline, not full-screen, shortcut, or full-pipeline latency |
| 100 production overlay open/cancel cycles at 144 DPI | Median construction-to-Shown 16.78 ms, p95 24.65 ms; GDI 39→39 and USER 5→5 from cycle 50 to 100 | No sustained GUI-resource growth in this interval; private memory still rose and needs longer observation |
| Actual popup inspection | Saved and inspected full-overlay screenshots on light/dark backgrounds at 150%; no obvious square backing or visibly stepped corners | Current-scale visual baseline only; mixed-DPI and animated frame pacing remain open |

Both builds completed with zero compiler warnings/errors. Nine diagnostic self-test groups and seven existing core test groups passed. The full clipboard-writing application suite was not run against the user's normal state.

The sampled PNGs contained sRGB and gAMA chunks (gamma 0.45455). This is observed encoder output, not proof that every input was correctly converted to sRGB or that earlier clipboard attachments retained original metadata.

The legacy ICM lookup returned Windows error 2016 in this configuration. This is recorded as unavailable; it is not interpreted as absence of calibration or proof of any color space.

## Investigation controls and rejected runs

The initial SDR fixture placed pixels at the window edge. Sixteen differing pixels were confined to the two bottom corners, consistent with DWM window rounding. Test patches were inset; the repeated visible fixture then matched across the entire buffer. The original corner evidence remains available.

An elevated fixture run captured unrelated content. Its expected-vs-acquired result is invalid for color analysis. A visibility check was added, and screen capture was separated from synthetic clipboard delivery. The valid current-display result is `hdr-on-sdr-05`; do not use `hdr-on-sdr-04` as a color reference.

The initial overlay resource gate used cycle 10 as its warm baseline and flagged a 9-object increase. Counts plateaued by cycle 50. The revised report retains all three samples and gates the 50→100 interval. This does not justify claiming zero private-memory growth.

A 1.2-second clipboard lock expired before all production/framework retries exhausted; copying succeeded after roughly 1.3 seconds. A 15-second lock then reproduced save-with-copy-failure after 9.35 seconds. WinForms' SetImage calls SetDataObject, whose default retry budget is 10 retries at 100 ms, inside the application's eight attempts. Treat 480 ms as an incomplete estimate. The exact installed runtime behavior is established by the measured test; the tagged source corroborates the nested retry structure.

## Output contract before any capture replacement

Default target: shareable SDR PNG/clipboard with explicit sRGB interpretation. SDR UI content should keep intended sRGB code values after normalization. Genuine HDR content needs a declared white reference, gamut policy, and tone mapper with reproducible parameters. No arbitrary brightness or contrast adjustment is acceptable.

HDR preservation would be a separate later file-export contract, retaining high precision and declared color meaning. It cannot be implemented by retagging an already clipped 8-bit GDI image. An HDR screenshot represents captured desktop composition, not necessarily source-media pixels or physical monitor output.

Keep GDI for supported SDR cases if it continues to pass. Defer the production backend decision until actual HDR and white-level experiments below identify the failure stage. Windows.Graphics.Capture FP16 and DXGI DuplicateOutput1 remain candidates for a diagnostic reference path; neither has been added to production. Include WGC capture-border/package-capability constraints and portable deployment in that evaluation.

## Remaining experiment matrix

| Case | Required input/control | Status |
| --- | --- | --- |
| SDR patches, HDR on at current white level | Known fixture, full pixel comparison | Complete for current display state |
| SDR patches, HDR off | Temporarily change HDR only; record and restore display state | Pending authorization/controlled run |
| SDR patches, HDR on at two additional white levels | Record exact per-output white nits; restore 284 nits after testing | Pending controlled run |
| Actual HDR content | Verified HDR renderer/video/game; known luminance/primaries or independently validated reference | Pending source selection |
| Matched Snipping Tool originals | Same static fixture and exact coordinates; original saved files, tool version, separate clipboard snapshot | Pending |
| Viewer behavior | Open identical original files in named color-managed viewers on HDR and SDR displays | Pending |
| Mixed SDR/HDR monitors and DPI | Cross-boundary regions, negative origins, monitor movement | Pending hardware/configuration coverage |
| Global input and rendering | Measured WM_HOTKEY-to-visible latency, pointer frame pacing, 100/125/200% overlay inspection | Pending; no claims from component previews |

The earlier darker/washed-out reversal is still unexplained. Existing attachment metadata is not accepted as original-capture evidence. No conclusion is drawn that QuietSnip or Snipping Tool is globally brighter, darker, or more accurate.

For each run retain OS/GPU/driver, monitor and HDR state, SDR white nits, profile/calibration lookup status, source app/content mode, capture API/format, original file hashes, coordinates, tool/viewer versions, and clipboard formats. Compare capture → crop → conversion → encoding → paste separately. Use the same original file when comparing viewers. Use static content and avoid JPEG, chat uploads, screenshots-of-screenshots, or resized regions as the numerical reference.

## Release gates

- Production capture code remains unchanged until the failing stage is reproduced and attributed.
- Current custom shortcuts, extra shortcuts, folders, dimensions, shape, confirmation preference, saved regions, and startup command are preserved. Verification uses hashes; no migration/reset is needed.
- Encoding must reproduce the intended decoded buffer exactly, including freeform alpha in PNG.
- Known SDR patch centers should remain within 2/255 per channel in supported display configurations. Current run passed exactly; other configurations have not passed by inference.
- HDR ramps must remain monotonic, neutral ramps neutral, and distinguishable highlights retained through the declared range. Compare against an independent implementation of the chosen transform before considering a pipeline change.
- Define clipboard support by tested receiving applications. Do not generalize the Bitmap/GetImage transparency result to all formats or all paste targets.
- Preserve save-first behavior under contention. Review the 9.35-second retry delay as a separate follow-up; do not silently change transport during this investigation.
- Keep a bounded capture session lifetime and lazy resource allocation. No background capture loop or OCR/model dependencies are introduced.

## Primary references checked

- [Microsoft screen capture guidance, including FP16 HDR acquisition](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture)
- [Advanced Color capture, composition, and SDR white reference](https://learn.microsoft.com/en-us/windows/win32/direct3darticles/high-dynamic-range)
- [DISPLAYCONFIG_SDR_WHITE_LEVEL conversion to nits](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-displayconfig_sdr_white_level)
- [Clipboard.SetImage Bitmap format](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.clipboard.setimage?view=windowsdesktop-10.0)
- [WinForms 10.0 Clipboard implementation and internal retry defaults](https://github.com/dotnet/winforms/blob/v10.0.0/src/System.Windows.Forms/System/Windows/Forms/OLE/Clipboard.cs)
- [ICC behavior under Advanced Color](https://learn.microsoft.com/en-us/windows/win32/wcs/advanced-color-icc-profiles)
- [DWM rounding is a preference, not a guarantee](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-rounded-corners)

Native DisplayConfig layouts and Advanced Color Info2 flag meanings were also checked against the installed Microsoft Windows SDK 10.0.26100.0 `wingdi.h`. Query errors are retained rather than replacing unavailable Info2 with a false HDR assertion.
