using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SnippingToolPlus;

internal static class ArrowTextRecognition
{
    private sealed record Shape(Rectangle Bounds, bool[] Pixels)
    {
        internal int Width => Bounds.Width;
        internal int Height => Bounds.Height;
    }
    private static List<Shape> Components(Bitmap image, Rectangle bounds)
    {
        using var crop = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(crop))
        { graphics.Clear(Color.White); graphics.DrawImage(image, new Rectangle(Point.Empty, crop.Size), bounds, GraphicsUnit.Pixel); }
        var data = crop.LockBits(new Rectangle(Point.Empty, crop.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] pixels = new byte[data.Stride * crop.Height];
        try { Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); }
        finally { crop.UnlockBits(data); }
        int Gray(int i) => (pixels[i + 2] * 77 + pixels[i + 1] * 150 + pixels[i] * 29) >> 8;
        var border = new List<int>();
        for (int x = 0; x < crop.Width; x++)
        {
            border.Add(Gray(x * 4));
            border.Add(Gray((crop.Height - 1) * data.Stride + x * 4));
        }
        for (int y = 0; y < crop.Height; y++)
        { border.Add(Gray(y * data.Stride)); border.Add(Gray(y * data.Stride + (crop.Width - 1) * 4)); }
        border.Sort(); int background = border[border.Count / 2];
        var contrast = new int[crop.Width * crop.Height];
        int peak = 0;
        for (int y = 0; y < crop.Height; y++)
        for (int x = 0; x < crop.Width; x++)
        {
            int i = y * data.Stride + x * 4;
            int gray = (pixels[i + 2] * 77 + pixels[i + 1] * 150 + pixels[i] * 29) >> 8;
            int value = Math.Abs(gray - background);
            contrast[y * crop.Width + x] = value; peak = Math.Max(peak, value);
        }
        var found = new List<Shape>();
        if (peak < 32) return found;
        int threshold = Math.Max(24, (int)(peak * .4));
        var foreground = contrast.Select(value => value >= threshold).ToArray();
        var pending = new Stack<int>();
        var members = new List<int>();
        for (int i = 0; i < foreground.Length; i++)
        {
            if (!foreground[i]) continue;
            foreground[i] = false; pending.Push(i); members.Clear();
            int left = crop.Width, right = 0, top = crop.Height, bottom = 0;
            while (pending.Count > 0)
            {
                int current = pending.Pop(); members.Add(current);
                int px = current % crop.Width, py = current / crop.Width;
                left = Math.Min(left, px); right = Math.Max(right, px);
                top = Math.Min(top, py); bottom = Math.Max(bottom, py);
                for (int y = Math.Max(0, py - 1); y <= Math.Min(crop.Height - 1, py + 1); y++)
                for (int x = Math.Max(0, px - 1); x <= Math.Min(crop.Width - 1, px + 1); x++)
                {
                    int next = y * crop.Width + x;
                    if (!foreground[next]) continue;
                    foreground[next] = false; pending.Push(next);
                }
            }
            int width = right - left + 1, height = bottom - top + 1;
            if (width < 7 || height < 3 || width > 100 || height > 60 || width / (float)height is < 1.15f or > 5f) continue;
            var mask = new bool[width * height];
            foreach (int member in members) mask[(member / crop.Width - top) * width + member % crop.Width - left] = true;
            found.Add(new(new Rectangle(bounds.X + left, bounds.Y + top, width, height), mask));
        }
        return found;
    }

    private static string? Arrow(Shape shape)
    {
        int width = shape.Width, height = shape.Height;
        var shaft = new List<int>();
        for (int y = 0; y < height; y++)
        {
            int run = 0, longest = 0;
            for (int x = 0; x < width; x++)
            {
                run = shape.Pixels[y * width + x] ? run + 1 : 0;
                longest = Math.Max(longest, run);
            }
            if (longest >= width * .85) shaft.Add(y);
        }
        if (shaft.Count == 0 || shaft.Count > height * .45) return null;
        int first = shaft[0], last = shaft[^1];
        if (first == 0 || last == height - 1 || last - first + 1 != shaft.Count
            || (first + last) / 2f < height * .25f || (first + last) / 2f > height * .75f) return null;
        if (Math.Abs(first - (height - 1 - last)) > Math.Max(1, height * .25)) return null;

        bool HasChevron(bool right)
        {
            double Outer(int y)
            {
                var xs = Enumerable.Range(0, width).Where(x => shape.Pixels[y * width + x])
                    .Select(x => right ? x : width - 1 - x).ToArray();
                return xs.Length == 0 ? -1 : xs.Average();
            }
            for (int y = 0; y < height; y++)
            {
                if (y >= first && y <= last) continue;
                for (int x = 0; x < width; x++)
                    if (shape.Pixels[y * width + x] && (right ? x : width - 1 - x) < width * .45) return false;
            }
            double top = Outer(0), bottom = Outer(height - 1);
            double nearTop = Outer(first - 1), nearBottom = Outer(last + 1);
            return top >= width * .45 && bottom >= width * .45
                && top <= width - 2 && bottom <= width - 2
                && nearTop >= top && nearBottom >= bottom
                && nearTop >= width * .7 && nearBottom >= width * .7;
        }
        // A long central shaft plus a symmetric diagonal head distinguishes an arrow
        // from dashes, letters, and T-shaped mathematical symbols in the source pixels.
        if (HasChevron(true)) return "→";
        if (HasChevron(false)) return "←";
        return null;
    }

    internal static TextRecognitionResult Apply(Bitmap image, TextRecognitionResult raw)
    {
        var output = new List<RecognizedWord>();
        bool changed = false;
        foreach (var line in raw.Words.GroupBy(word => word.Line))
        {
            var words = line.ToList();
            var union = Rectangle.Ceiling(words.Select(word => word.Bounds).Aggregate(RectangleF.Union));
            union.Inflate(4, 4); union.Intersect(new Rectangle(Point.Empty, image.Size));
            if (words.Count < 2 || union.Width < 1 || union.Height < 1) { output.AddRange(words); continue; }
            foreach (var shape in Components(image, union))
            {
                string? symbol = Arrow(shape);
                if (symbol == null) continue;
                var area = RectangleF.Inflate(shape.Bounds, 2, 2);
                var overlapping = words.Where(word => word.Bounds.IntersectsWith(shape.Bounds)).ToArray();
                if (overlapping.Any(word => word.Bounds.Left < area.Left || word.Bounds.Right > area.Right)) continue;
                if (!words.Any(word => word.Bounds.Right <= area.Left) || !words.Any(word => word.Bounds.Left >= area.Right)) continue;
                words.RemoveAll(word => overlapping.Contains(word));
                int insert = words.FindIndex(word => word.Bounds.Left >= shape.Bounds.Right);
                if (insert < 0) continue;
                words.Insert(insert, new(symbol, shape.Bounds, line.Key));
                changed = true;
            }
            output.AddRange(words);
        }
        return changed ? (raw with { Words = output }).Format() : raw;
    }
}
