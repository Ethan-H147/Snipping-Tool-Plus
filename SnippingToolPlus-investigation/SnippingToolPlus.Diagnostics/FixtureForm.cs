using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SnippingToolPlus;

namespace SnippingToolPlus.Diagnostics;

internal sealed class FixtureForm : Form
{
    private readonly Options options;
    private readonly string output;
    private readonly Bitmap pattern = MakePattern();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 4000 };
    internal Exception? Failure { get; private set; }
    internal static readonly Color[] Colors = [
        Color.Black, Color.FromArgb(8,8,8), Color.FromArgb(16,16,16), Color.FromArgb(32,32,32),
        Color.FromArgb(64,64,64), Color.FromArgb(128,128,128), Color.FromArgb(192,192,192), Color.White,
        Color.Red, Color.Lime, Color.Blue, Color.Cyan, Color.Magenta, Color.Yellow, Color.FromArgb(255,128,0), Color.FromArgb(128,0,255),
        Color.FromArgb(17,34,51), Color.FromArgb(100,149,237), Color.FromArgb(240,128,128), Color.FromArgb(46,139,87),
        Color.FromArgb(250,240,230), Color.FromArgb(24,30,48), Color.FromArgb(91,79,207), Color.FromArgb(246,247,251),
        Color.FromArgb(200,32,32), Color.FromArgb(32,200,32), Color.FromArgb(32,32,200), Color.FromArgb(80,120,160),
        Color.FromArgb(220,220,220), Color.FromArgb(235,235,235), Color.FromArgb(245,245,245), Color.FromArgb(254,254,254)
    ];

    internal FixtureForm(Options options, string output)
    {
        this.options = options; this.output = output;
        Text = "Snipping Tool+ diagnostics — SDR fixture";
        AutoScaleMode = AutoScaleMode.None; ClientSize = new Size(800, 480);
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        TopMost = true; StartPosition = FormStartPosition.Manual;
        var screens = Screen.AllScreens;
        Screen monitor = options.Monitor is int index
            ? index >= 0 && index < screens.Length ? screens[index] : throw new ArgumentException("Monitor index out of range.")
            : Screen.FromPoint(Cursor.Position);
        Location = new Point(monitor.WorkingArea.Left + (monitor.WorkingArea.Width - Width) / 2,
            monitor.WorkingArea.Top + (monitor.WorkingArea.Height - Height) / 2);
        DoubleBuffered = true;
        timer.Tick += (_, _) => Collect();
        Shown += (_, _) => timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.White);
        TextRenderer.DrawText(e.Graphics, "SDR fixture · " + options.Label + " · capture starts after 4 seconds", Font,
            new Rectangle(12, 12, 740, 40), Color.Black);
        // Keep test pixels away from DWM's rounded client corners.
        e.Graphics.DrawImageUnscaled(pattern, 16, 80);
    }

    internal static Bitmap MakePattern()
    {
        var bitmap = new Bitmap(768, 384, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        for (int i = 0; i < Colors.Length; i++)
        {
            using var brush = new SolidBrush(Colors[i]);
            graphics.FillRectangle(brush, i % 8 * 96, i / 8 * 96, 96, 96);
        }
        return bitmap;
    }

    internal static object[] PatchSamples(Bitmap captured) => Colors.Select((color, i) => {
        int x = i % 8 * 96 + 48, y = i / 8 * 96 + 48;
        Color actual = captured.GetPixel(x, y);
        return (object)new { Index = i, X = x, Y = y, ExpectedRgb = new[] { (int)color.R, color.G, color.B },
            ActualRgb = new[] { (int)actual.R, actual.G, actual.B },
            MaxCodeError = new[] { Math.Abs(color.R - actual.R), Math.Abs(color.G - actual.G), Math.Abs(color.B - actual.B) }.Max() };
    }).ToArray();

    private void Collect()
    {
        timer.Stop();
        try
        {
            Refresh(); DwmFlush();
            Rectangle bounds = new(PointToScreen(new Point(16, 80)), pattern.Size);
            if (GetAncestor(WindowFromPoint(new Point(bounds.Left + 48, bounds.Top + 48)), 2) != Handle ||
                GetAncestor(WindowFromPoint(new Point(bounds.Right - 48, bounds.Bottom - 48)), 2) != Handle)
                throw new InvalidOperationException("Fixture is occluded or not visible on the capture desktop. This run is invalid; bring the fixture to the foreground and run again.");
            var before = Resources();
            var timings = new List<double>();
            object? stages = null;
            for (int i = 0; i < options.Cycles; i++)
            {
                var clock = Stopwatch.StartNew();
                using var captured = CaptureService.FreezeDesktop(bounds);
                timings.Add(clock.Elapsed.TotalMilliseconds);
                if (i == 0) stages = Program.RunStages(captured, output, options with { Bounds = bounds }, pattern);
                else
                {
                    using var crop = CaptureService.Crop(captured, new Rectangle(8, 8, 752, 368));
                    using var stream = new MemoryStream();
                    crop.Save(stream, ImageFormat.Png); stream.Position = 0;
                    using var reloaded = new Bitmap(stream);
                    var comparison = PixelEvidence.Compare(crop, reloaded);
                    if (comparison.MaxRgbCodeError != 0 || comparison.MaxAlphaCodeError != 0)
                        throw new InvalidDataException("PNG roundtrip changed pixels at cycle " + i);
                }
            }
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            timings.Sort();
            Program.WriteJson(Path.Combine(output, "capture.json"), stages!);
            Program.WriteJson(Path.Combine(output, "stress.json"), new {
                options.Cycles, CaptureOnlyMedianMs = timings[timings.Count / 2],
                CaptureOnlyP95Ms = timings[(int)Math.Ceiling(timings.Count * .95) - 1],
                Before = before, AfterCollection = Resources(),
                Scope = "768x384 fixture acquisition/crop/PNG; not shortcut latency, user cancellation, or full-overlay frame pacing."
            });
            Console.WriteLine("Evidence saved: " + output);
        }
        catch (Exception error) { Failure = error; Program.WriteJson(Path.Combine(output, "failure.json"), new { Error = error.ToString() }); }
        finally { if (!options.Hold || Failure != null) Close(); }
    }

    private static object Resources()
    {
        using var process = Process.GetCurrentProcess(); process.Refresh();
        return new { process.WorkingSet64, process.PrivateMemorySize64, process.HandleCount,
            GdiObjects = GetGuiResources(process.Handle, 0), UserObjects = GetGuiResources(process.Handle, 1) };
    }
    protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); pattern.Dispose(); } base.Dispose(disposing); }
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
}
