using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SnippingToolPlus;

internal static class IconTextFilter
{
    // Only ambiguous round letters are candidates. A real C/O is normally one connected stroke.
    // Do not blanket-remove single characters, punctuation, or unfamiliar words.
    internal static bool IsIconLike(Bitmap source, RecognizedWord word)
    {
        if (word.Text.Length != 1 || !"OoCc".Contains(word.Text[0])) return false;
        Rectangle bounds = Rectangle.Intersect(Rectangle.FromLTRB((int)Math.Floor(word.Bounds.Left) - 1, (int)Math.Floor(word.Bounds.Top) - 1, (int)Math.Ceiling(word.Bounds.Right) + 1, (int)Math.Ceiling(word.Bounds.Bottom) + 1), new Rectangle(Point.Empty, source.Size));
        if (bounds.Width < 8 || bounds.Height < 8 || bounds.Width > 160 || bounds.Height > 160) return false;
        if (bounds.Width / (float)bounds.Height is < .5f or > 1.6f) return false;
        using var crop = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(crop))
        { graphics.Clear(Color.White); graphics.DrawImage(source, new Rectangle(Point.Empty, crop.Size), bounds, GraphicsUnit.Pixel); }
        var data = crop.LockBits(new Rectangle(Point.Empty, crop.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] pixels = new byte[data.Stride * crop.Height];
        try { Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); }
        finally { crop.UnlockBits(data); }
        int left = Math.Clamp((int)Math.Floor(word.Bounds.Left) - bounds.Left, 0, crop.Width - 1);
        int right = Math.Clamp((int)Math.Ceiling(word.Bounds.Right) - 1 - bounds.Left, 0, crop.Width - 1);
        int top = Math.Clamp((int)Math.Floor(word.Bounds.Top) - bounds.Top, 0, crop.Height - 1);
        int bottom = Math.Clamp((int)Math.Ceiling(word.Bounds.Bottom) - 1 - bounds.Top, 0, crop.Height - 1);
        int[] corners = [top * data.Stride + left * 4, top * data.Stride + right * 4,
            bottom * data.Stride + left * 4, bottom * data.Stride + right * 4];
        int Median(int channel) { var values = corners.Select(i => (int)pixels[i + channel]).Order().ToArray(); return (values[1] + values[2]) / 2; }
        int bgB = Median(0), bgG = Median(1), bgR = Median(2);
        var difference = new int[crop.Width * crop.Height];
        int peak = 0;
        for (int y = 0; y < crop.Height; y++)
        for (int x = 0; x < crop.Width; x++)
        {
            int i = y * data.Stride + x * 4;
            int delta = (Math.Abs(pixels[i] - bgB) + Math.Abs(pixels[i + 1] - bgG) + Math.Abs(pixels[i + 2] - bgR)) / 3;
            difference[y * crop.Width + x] = delta; peak = Math.Max(peak, delta);
        }
        if (peak < 24) return false;
        int threshold = Math.Max(18, (int)(peak * .4));
        var foreground = difference.Select(d => d >= threshold).ToArray();
        int ink = foreground.Count(value => value);
        if (ink < 10 || ink > foreground.Length * .65) return false;
        var areas = new List<int>();
        var pending = new Stack<int>();
        for (int i = 0; i < foreground.Length; i++)
        {
            if (!foreground[i]) continue;
            foreground[i] = false; pending.Push(i);
            int area = 0;
            while (pending.Count > 0)
            {
                int current = pending.Pop(); area++;
                int px = current % crop.Width, py = current / crop.Width;
                for (int y = Math.Max(0, py - 1); y <= Math.Min(crop.Height - 1, py + 1); y++)
                for (int x = Math.Max(0, px - 1); x <= Math.Min(crop.Width - 1, px + 1); x++)
                {
                    int next = y * crop.Width + x;
                    if (!foreground[next]) continue;
                    foreground[next] = false; pending.Push(next);
                }
            }
            if (area >= Math.Max(3, ink * .12)) areas.Add(area);
        }
        return areas.Count >= 2;
    }

    internal static TextRecognitionResult Apply(Bitmap source, TextRecognitionResult raw)
    {
        var rejected = raw.Words.Select(word => IsIconLike(source, word)).ToArray();
        if (!rejected.Any(value => value)) return raw;
        string[] originalLines = raw.Text.Replace("\r", "").Split('\n');
        var lines = new List<string>();
        var words = new List<RecognizedWord>();
        foreach (var group in raw.Words.Select((word, index) => (word, index)).GroupBy(item => item.word.Line))
        {
            string line = group.Key < originalLines.Length ? originalLines[group.Key] : string.Join(" ", group.Select(item => item.word.Text));
            var removals = new List<(int Start, int Length)>();
            int cursor = 0;
            foreach (var item in group)
            {
                int offset = line.IndexOf(item.word.Text, cursor, StringComparison.Ordinal);
                if (offset >= 0) { cursor = offset + item.word.Text.Length; if (rejected[item.index]) removals.Add((offset, item.word.Text.Length)); }
            }
            var retained = group.Where(item => !rejected[item.index]).ToArray();
            if (retained.Length == 0) continue;
            foreach (var removal in removals.OrderByDescending(r => r.Start)) line = line.Remove(removal.Start, removal.Length);
            line = System.Text.RegularExpressions.Regex.Replace(line, @"[ \t]{2,}", " ").Trim();
            int lineNumber = lines.Count; lines.Add(line);
            words.AddRange(retained.Select(item => item.word with { Line = lineNumber }));
        }
        return raw with { Text = string.Join(Environment.NewLine, lines), Words = words };
    }
}
