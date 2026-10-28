using System.Drawing.Imaging;
using System.Text.Json;
using QuietSnip;

namespace QuietSnip.Diagnostics;

internal static class SelfTests
{
    internal static int Run(string output)
    {
        var results = new List<object>();
        void Test(string name, Action body)
        {
            body(); results.Add(new { Name = name, Passed = true }); Console.WriteLine("PASS " + name);
        }
        try
        {
            Test("Pixel comparison detects channel and alpha changes", () => {
                using var a = new Bitmap(4, 3); using var b = new Bitmap(4, 3);
                using (var g = Graphics.FromImage(a)) g.Clear(Color.FromArgb(255, 10, 20, 30));
                using (var g = Graphics.FromImage(b)) g.Clear(Color.FromArgb(255, 10, 20, 30));
                b.SetPixel(2, 1, Color.FromArgb(128, 17, 18, 30));
                var result = PixelEvidence.Compare(a, b);
                Check(result.SameSize && result.DifferentRgbPixels == 1 && result.MaxRgbCodeError == 7 &&
                    result.DifferentAlphaPixels == 1 && result.MaxAlphaCodeError == 127, "Wrong difference metrics");
                using var otherSize = new Bitmap(2, 2);
                Check(!PixelEvidence.Compare(a, otherSize).SameSize, "Different dimensions accepted");
            });
            Test("Crop and PNG preserve full generated RGB pattern", () => {
                using var pattern = new Bitmap(256, 128, PixelFormat.Format32bppArgb);
                for (int y = 0; y < pattern.Height; y++)
                    for (int x = 0; x < pattern.Width; x++) pattern.SetPixel(x, y, Color.FromArgb(255, x, y * 2, (x + y) % 256));
                using var crop = CaptureService.Crop(pattern, new Rectangle(16, 20, 200, 100));
                Check(crop.GetPixel(0, 0) == pattern.GetPixel(16, 20), "Crop coordinate mismatch");
                string path = Program.SaveStage(crop, output, "rgb");
                using var decoded = new Bitmap(path);
                Check(PixelEvidence.Compare(crop, decoded).MaxRgbCodeError == 0, "PNG changed RGB values");
                _ = PixelEvidence.Inspect(path);
            });
            Test("Freeform PNG retains alpha and interior colors", () => {
                using var pattern = FixtureForm.MakePattern();
                Point[] points = [new(0, 0), new(95, 0), new(0, 95)];
                using var crop = CaptureService.CropFreeform(pattern, points, new Rectangle(0, 0, 96, 96));
                Check(crop.GetPixel(80, 80).A == 0 && crop.GetPixel(10, 10).A == 255, "Freeform alpha mismatch");
                string path = Program.SaveStage(crop, output, "freeform"); using var decoded = new Bitmap(path);
                var comparison = PixelEvidence.Compare(crop, decoded);
                Check(comparison.MaxAlphaCodeError == 0 && comparison.MaxRgbCodeError == 0, "Freeform encoding changed pixels");
            });
            Test("Concurrent same-second saves never overwrite", () => {
                string folder = Path.Combine(output, "collisions");
                var files = new System.Collections.Concurrent.ConcurrentBag<string>();
                Parallel.For(0, 20, i => {
                    using var bitmap = new Bitmap(4, 4); bitmap.SetPixel(0, 0, Color.FromArgb(255, i, 0, 0));
                    files.Add(CaptureService.SavePng(bitmap, folder, new DateTime(2026, 10, 28, 1, 41, 11)));
                });
                Check(files.Distinct().Count() == 20 && Directory.GetFiles(folder).Length == 20, "Collision lost an image");
                Check(files.All(f => Path.GetFileName(f).StartsWith("Screenshot 2026-10-28 014111")), "Filename convention changed");
            });
            Test("Invalid save destination fails without changing blocker", () => {
                string path = Path.Combine(output, "destination-is-file"); File.WriteAllText(path, "unchanged");
                using var bitmap = new Bitmap(2, 2);
                Expect<IOException>(() => CaptureService.SavePng(bitmap, path));
                Check(File.ReadAllText(path) == "unchanged", "Blocker changed");
            });
            Test("Existing custom settings roundtrip independently", () => {
                string folder = Path.Combine(output, "isolated-settings"); Directory.CreateDirectory(folder);
                var store = new SettingsStore(folder);
                var expected = new AppSettings { CopyHotkey = new(Keys.C, HotkeyModifiers.Control | HotkeyModifiers.Alt),
                    SaveHotkey = new(Keys.S, HotkeyModifiers.Control | HotkeyModifiers.Alt),
                    ExtraHotkeys = new() { [AppAction.SaveProject] = new(Keys.F6, HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift) },
                    SaveFolder = Path.Combine(folder, "custom screenshots"), RememberRegions = false, ShowConfirmation = false, ExactSize = true,
                    CaptureWidth = 923, CaptureHeight = 521 };
                store.Save(expected);
                Check(JsonSerializer.Serialize(expected) == JsonSerializer.Serialize(store.Load()), "Custom preferences lost");
                Check(!File.Exists(store.FilePath + ".tmp"), "Temporary settings file remains");
                File.WriteAllText(store.FilePath, "{broken"); Expect<JsonException>(() => store.Load());
            });
            Test("PNG evidence rejects corruption and truncation", () => {
                using var bitmap = new Bitmap(3, 3); using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png);
                byte[] good = stream.ToArray(); Check(PixelEvidence.PngChunks(good).Count >= 3, "No chunks");
                byte[] bad = good.ToArray(); bad[29] ^= 1;
                Expect<InvalidDataException>(() => PixelEvidence.PngChunks(bad));
                Expect<InvalidDataException>(() => PixelEvidence.PngChunks(good[..^1]));
                Expect<InvalidDataException>(() => PixelEvidence.PngChunks([1, 2, 3]));
            });
            Test("Command validation requires explicit capture bounds", () => {
                Expect<ArgumentException>(() => Options.Parse(["--capture", "--output", output]));
                Expect<ArgumentException>(() => Options.Parse(["--fixture", "--cycles", "0", "--output", output]));
                Expect<ArgumentException>(() => Options.Parse(["--self-test", "--clipboard-roundtrip", "--output", output]));
                var options = Options.Parse(["--capture", "--bounds", "-1920,-20,800,600", "--output", output]);
                Check(options.Bounds == new Rectangle(-1920, -20, 800, 600), "Negative coordinates lost");
            });
            Test("Native display structures match SDK layouts", DisplayEvidence.ValidateNativeLayouts);
            Program.WriteJson(Path.Combine(output, "self-test.json"), new { Passed = results.Count, Failed = 0, Results = results });
            return 0;
        }
        catch (Exception error)
        {
            Program.WriteJson(Path.Combine(output, "self-test.json"), new { Passed = results.Count, Failed = 1, Results = results, Error = error.ToString() });
            throw;
        }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Expect<T>(Action body) where T : Exception
    {
        try { body(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name);
    }
}
