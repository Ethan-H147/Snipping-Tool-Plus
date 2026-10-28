using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace QuietSnip;

internal sealed record CaptureResult(string? SavedPath, bool Copied, string? ClipboardError);

internal static class CaptureService
{
    public static Bitmap FreezeDesktop(Rectangle bounds)
    {
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    public static Rectangle SelectionBetween(Point start, Point end, Size size)
    {
        int left = Math.Clamp(Math.Min(start.X, end.X), 0, size.Width);
        int top = Math.Clamp(Math.Min(start.Y, end.Y), 0, size.Height);
        int right = Math.Clamp(Math.Max(start.X, end.X), 0, size.Width);
        int bottom = Math.Clamp(Math.Max(start.Y, end.Y), 0, size.Height);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    public static Bitmap Crop(Bitmap desktop, Rectangle selection)
    {
        if (selection.Width < 1 || selection.Height < 1 ||
            !new Rectangle(Point.Empty, desktop.Size).Contains(selection))
            throw new ArgumentException("Select an area inside the screen.");
        return desktop.Clone(selection, PixelFormat.Format32bppArgb);
    }

    public static Bitmap CropFreeform(Bitmap desktop, Point[] points, Rectangle bounds)
    {
        if (points.Length < 3 || bounds.Width < 1 || bounds.Height < 1)
            throw new ArgumentException("Draw an area to capture.");
        var image = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(image);
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddPolygon(points.Select(p => new Point(p.X - bounds.X, p.Y - bounds.Y)).ToArray());
        graphics.SetClip(path);
        graphics.DrawImage(desktop, new Rectangle(Point.Empty, bounds.Size), bounds, GraphicsUnit.Pixel);
        return image;
    }

    public static string SavePng(Bitmap bitmap, string directory, DateTime? now = null)
    {
        Directory.CreateDirectory(directory);
        string stem = "Screenshot " + (now ?? DateTime.Now).ToString("yyyy-MM-dd HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        for (int suffix = 0; suffix < 10000; suffix++)
        {
            string path = Path.Combine(directory, stem + (suffix == 0 ? "" : $"_{suffix}") + ".png");
            FileStream stream;
            try { stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
            catch (IOException) when (File.Exists(path)) { continue; }
            try
            {
                using (stream) bitmap.Save(stream, ImageFormat.Png);
                return path;
            }
            catch { stream.Dispose(); File.Delete(path); throw; }
        }
        throw new IOException("Couldn't create a unique screenshot filename.");
    }

    public static async Task<CaptureResult> DeliverAsync(Bitmap bitmap, CaptureMode mode, string directory)
    {
        // Save first so a busy clipboard cannot discard the requested file.
        string? savedPath = mode == CaptureMode.Save ? SavePng(bitmap, directory) : null;
        string? clipboardError = null;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                Clipboard.SetImage(bitmap);
                return new CaptureResult(savedPath, true, null);
            }
            catch (ExternalException exception)
            {
                clipboardError = exception.Message;
                await Task.Delay(60);
            }
        }
        return new CaptureResult(savedPath, false, clipboardError);
    }
}

