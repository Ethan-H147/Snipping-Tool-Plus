using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using QuietSnip;

namespace QuietSnip.Diagnostics;

internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true };

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        try
        {
            if (args.Length == 2 && args[0] == "--hold-clipboard") return ClipboardEvidence.HoldForTest(args[1]);
            if (args.Length == 3 && args[0] == "--hdr-watchdog") return DisplayEvidence.HdrWatchdog(args[1], args[2]);
            if (args.Contains("--help")) { Console.WriteLine(Help); return 0; }
            if (args.Length == 0) args = ["--fixture", "--cycles", "100", "--hold", "--label", "current-display-sdr-content",
                "--output", Path.Combine(AppContext.BaseDirectory, "results", "fixture-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6])];
            var options = Options.Parse(args);
            string output = Path.GetFullPath(options.Output);
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                throw new ArgumentException("Use an empty output folder; existing evidence is never overwritten.");
            Directory.CreateDirectory(output);
            if (options.Command == "--self-test") return SelfTests.Run(output);
            WriteJson(Path.Combine(output, "environment.json"), DisplayEvidence.Read());
            if (options.Command == "--environment") return 0;
            if (options.Command is "--hdr-cycle" or "--hdr-window") return DisplayEvidence.RunHdrCycle(options, output);
            if (options.Command == "--inspect")
            {
                WriteJson(Path.Combine(output, "image.json"), PixelEvidence.Inspect(options.File!));
                return 0;
            }
            if (options.Command == "--compare")
            {
                using var first = new Bitmap(options.File!);
                using var second = new Bitmap(options.Other!);
                WriteJson(Path.Combine(output, "comparison.json"), new {
                    First = PixelEvidence.Inspect(options.File!), Second = PixelEvidence.Inspect(options.Other!),
                    Pixels = PixelEvidence.Compare(first, second),
                    Interpretation = "Decoded code-value comparison, without ICC conversion. Matching coordinates required; this is not a perceptual HDR accuracy score."
                });
                return 0;
            }
            if (options.Command == "--clipboard-snapshot")
            {
                WriteJson(Path.Combine(output, "clipboard.json"), ClipboardEvidence.Save(output));
                return 0;
            }
            if (options.Command == "--fixture")
            {
                using var fixture = new FixtureForm(options, output);
                Application.Run(fixture);
                return fixture.Failure == null ? 0 : throw new InvalidOperationException("Fixture failed", fixture.Failure);
            }
            if (options.Command == "--delivery-test")
            {
                using var synthetic = FixtureForm.MakePattern();
                object opaque = RunStages(synthetic, Path.Combine(output, "opaque"), options, synthetic);
                using var freeform = CaptureService.CropFreeform(synthetic,
                    [new Point(0, 0), new Point(767, 0), new Point(0, 383)], new Rectangle(0, 0, 768, 384));
                object transparent = RunStages(freeform, Path.Combine(output, "freeform"), options, null);
                WriteJson(Path.Combine(output, "delivery.json"), new { Opaque = opaque, Freeform = transparent,
                    Interpretation = "Synthetic input; no screen acquisition. Clipboard alpha differences are findings, not a reason to adjust brightness." });
                return 0;
            }
            if (options.Command == "--contention-test") return ContentionTest.Run(output);
            if (options.Command == "--capture")
            {
                Rectangle bounds = options.Bounds!.Value;
                if (!SystemInformation.VirtualScreen.Contains(bounds)) throw new ArgumentException("Bounds must be inside the virtual desktop.");
                using var desktop = CaptureService.FreezeDesktop(bounds);
                WriteJson(Path.Combine(output, "capture.json"), RunStages(desktop, output, options, null));
                return 0;
            }
            throw new ArgumentException("Unknown command.");
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    internal static object RunStages(Bitmap captured, string output, Options options, Bitmap? expected)
    {
        string capture = SaveStage(captured, output, "01-acquired-gdi");
        Rectangle region = new(8, 8, captured.Width - 16, captured.Height - 16);
        if (region.Width < 1 || region.Height < 1) throw new ArgumentException("Capture must be at least 17 × 17 pixels.");
        using var crop = CaptureService.Crop(captured, region);
        using var referenceCrop = captured.Clone(region, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var timer = Stopwatch.StartNew();
        string png = SaveStage(crop, output, "02-cropped-png");
        double encodeMs = timer.Elapsed.TotalMilliseconds;
        using var decoded = new Bitmap(png);
        var encoding = PixelEvidence.Compare(crop, decoded);
        object? clipboard = null;
        if (options.ClipboardRoundtrip)
        {
            Console.WriteLine("Testing clipboard delivery; supported previous clipboard formats will be retained in memory and restored unless another app changes the clipboard.");
            using var lease = new ClipboardLease();
            timer.Restart();
            var delivery = Wait(CaptureService.DeliverAsync(crop, CaptureMode.Save, Path.Combine(output, "03-production-delivery")));
            lease.MarkWritten();
            double deliveryMs = timer.Elapsed.TotalMilliseconds;
            object evidence = ClipboardEvidence.Save(Path.Combine(output, "04-clipboard"));
            using var clipboardImage = Clipboard.GetImage();
            using var clipboardBitmap = clipboardImage == null ? null : new Bitmap(clipboardImage);
            clipboard = new { Delivery = delivery, DeliveryMs = deliveryMs, Evidence = evidence,
                DecodedVsCrop = clipboardBitmap == null ? null : PixelEvidence.Compare(crop, clipboardBitmap),
                Restore = lease.Restore() };
        }
        string? expectedFile = expected == null ? null : SaveStage(expected, output, "00-expected-sdr");
        return new {
            options.Label, CapturedUtc = DateTimeOffset.UtcNow, options.Bounds,
            Source = options.Command == "--delivery-test" ? "Synthetic bitmap; no screen acquisition" : "Unmodified production Graphics.CopyFromScreen -> Format32bppRgb",
            Content = options.Command == "--delivery-test" ? "Synthetic SDR/alpha test" : expected == null ? "User-specified region; content type not automatically classified" : "Known SDR GDI fixture; NOT actual HDR content",
            ExpectedFile = expectedFile, Acquired = PixelEvidence.Inspect(capture),
            ExpectedVsAcquired = expected == null ? null : PixelEvidence.Compare(expected, captured),
            PatchCenters = expected == null ? null : FixtureForm.PatchSamples(captured),
            CropRegion = region, CropVsSource = PixelEvidence.Compare(referenceCrop, crop),
            Encoded = PixelEvidence.Inspect(png), EncodeMs = encodeMs, EncodeRoundtrip = encoding, Clipboard = clipboard,
            DisplayAfter = DisplayEvidence.Read(),
            Interpretation = "PNG equality tests encoding only. Fixture errors can originate in display composition, occlusion, or GDI acquisition. No HDR tone mapping or brightness adjustment has been applied."
        };
    }

    internal static string SaveStage(Bitmap image, string root, string stage) =>
        CaptureService.SavePng(image, Path.Combine(root, stage));

    internal static T Wait<T>(Task<T> task)
    {
        while (!task.IsCompleted) { Application.DoEvents(); Thread.Sleep(5); }
        return task.GetAwaiter().GetResult();
    }

    internal static void WriteJson(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    internal static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private const string Help = """
QuietSnip Diagnostics — separate portable investigation tool; no settings, startup, or hotkey writes.
Requires the same .NET 10 Desktop Runtime as QuietSnip. No new runtime packages.

Commands (choose one; use a NEW output folder for each run):
  No arguments: open/hold an SDR fixture and collect 100 cycles under results beside the executable.
  --self-test --output <folder>                 Synthetic tests; no desktop/clipboard capture
  --environment --output <folder>               Read-only display/white-level/profile inventory
  --fixture --output <folder> --label <case>     SDR patches; capture automatically after 4 seconds
  --capture --bounds x,y,width,height --output <folder> --label <case>
  --clipboard-snapshot --output <folder>        Read CURRENT image clipboard, preserving offered PNG/DIB bytes
  --inspect --file <original.png> --output <folder>
  --compare --file <first.png> --other <second.png> --output <folder>
  --delivery-test --clipboard-roundtrip --output <folder>   Synthetic bitmap to PNG/clipboard; no screen capture
  --contention-test --output <folder>            Hold clipboard in a child process; verify save survives retry exhaustion
  --hdr-cycle --output <folder>                 EXPLICIT DISPLAY CHANGE: HDR off fixture, restore, HDR on fixture
  --hdr-window --output <folder>                EXPLICIT DISPLAY CHANGE: hold HDR off for 25s, then restore

Fixture options:
  --cycles 100                  Capture/crop/encode stress, default 1; clipboard runs only once
  --clipboard-roundtrip         Test production save+copy; retain/restore supported clipboard formats in memory
  --monitor 0                   Index from environment.json, default monitor under pointer
  --hold                        Keep fixture open after capture for a matched Snipping Tool original

The fixture is SDR even when Windows HDR is enabled. HDR content needs a separately verified HDR source.
Native clipboard snapshots contain local image data. Nothing is uploaded. Pasted/chat attachments are not originals.
""";
}

internal sealed record Options(string Command, string Output, string Label, string? File, string? Other,
    Rectangle? Bounds, int Cycles, bool ClipboardRoundtrip, int? Monitor, bool Hold)
{
    internal static Options Parse(string[] args)
    {
        var commands = new[] { "--self-test", "--environment", "--fixture", "--capture", "--clipboard-snapshot", "--inspect", "--compare", "--delivery-test", "--contention-test", "--hdr-cycle", "--hdr-window" };
        var found = args.Where(commands.Contains).ToArray();
        if (found.Length != 1) throw new ArgumentException("Choose exactly one command.");
        var values = new Dictionary<string, string>();
        bool clipboard = false, hold = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (commands.Contains(args[i])) continue;
            if (args[i] == "--clipboard-roundtrip") { clipboard = true; continue; }
            if (args[i] == "--hold") { hold = true; continue; }
            if (!new[] { "--output", "--label", "--file", "--other", "--bounds", "--cycles", "--monitor" }.Contains(args[i]))
                throw new ArgumentException("Unknown option: " + args[i]);
            if (i + 1 >= args.Length || !values.TryAdd(args[i], args[++i])) throw new ArgumentException("Missing or repeated option value.");
        }
        if (!values.TryGetValue("--output", out var output)) throw new ArgumentException("--output is required.");
        Rectangle? bounds = null;
        if (values.TryGetValue("--bounds", out var rectangle))
        {
            var parts = rectangle.Split(',').Select(int.Parse).ToArray();
            if (parts.Length != 4 || parts[2] < 17 || parts[3] < 17 || parts[2] > 20000 || parts[3] > 20000)
                throw new ArgumentException("Bounds must be x,y,width,height; size 17..20000.");
            bounds = new Rectangle(parts[0], parts[1], parts[2], parts[3]);
        }
        int cycles = values.TryGetValue("--cycles", out var count) ? int.Parse(count) : 1;
        if (cycles is < 1 or > 1000) throw new ArgumentException("Cycles must be 1..1000.");
        string command = found[0];
        if (command == "--capture" && bounds == null) throw new ArgumentException("--capture requires explicit --bounds.");
        if ((command is "--inspect" or "--compare") && !values.ContainsKey("--file")) throw new ArgumentException("--file is required.");
        if (command == "--compare" && !values.ContainsKey("--other")) throw new ArgumentException("--other is required.");
        if (clipboard && command is not ("--fixture" or "--capture" or "--delivery-test")) throw new ArgumentException("Clipboard roundtrip requires fixture, capture, or delivery test.");
        if (command == "--delivery-test" && !clipboard) throw new ArgumentException("Delivery test requires explicit --clipboard-roundtrip.");
        if (hold && command != "--fixture") throw new ArgumentException("--hold requires --fixture.");
        return new(command, output, values.GetValueOrDefault("--label", "unlabelled"), values.GetValueOrDefault("--file"),
            values.GetValueOrDefault("--other"), bounds, cycles, clipboard,
            values.TryGetValue("--monitor", out var monitor) ? int.Parse(monitor) : null, hold);
    }
}
