using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace SnippingToolPlus;

internal sealed record RecognizedWord(string Text, RectangleF Bounds, int Line, string Separator = " ");
internal sealed record TextRecognitionResult(string Text, IReadOnlyList<RecognizedWord> Words, string Language)
{
    public string SelectedText(IEnumerable<int> indices)
    {
        var selected = indices.Distinct().Order().Where(i => i >= 0 && i < Words.Count).Select(i => Words[i]);
        return string.Join(Environment.NewLine, selected.GroupBy(word => word.Line).Select(JoinLine));
    }

    internal static string JoinLine(IEnumerable<RecognizedWord> words)
    {
        var text = new System.Text.StringBuilder();
        foreach (var word in words)
        {
            if (text.Length > 0) text.Append(word.Separator);
            text.Append(word.Text);
        }
        return text.ToString();
    }

    internal TextRecognitionResult Format()
    {
        var words = Words.ToArray();
        for (int i = 1; i < words.Length; i++)
        {
            if (words[i].Line != words[i - 1].Line || string.IsNullOrEmpty(words[i].Text)
                || !string.IsNullOrWhiteSpace(words[i].Separator)) continue;
            if (words[i].Text.All(c => ".,;:!?%)]}’".Contains(c))
                || words[i - 1].Text.EndsWith('(') || words[i - 1].Text.EndsWith('['))
                words[i] = words[i] with { Separator = "" };
        }
        return this with
        {
            Words = words,
            Text = string.Join(Environment.NewLine, words.GroupBy(word => word.Line).Select(JoinLine))
        };
    }

    public static string WithoutLineBreaks(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s*\r?\n\s*", " ");
}

internal static class TextRecognition
{
    public static Task<TextRecognitionResult> RecognizeAsync(Bitmap image, CancellationToken cancellationToken = default)
    {
        var snapshot = (Bitmap)image.Clone();
        return Task.Run(async () =>
        {
            using (snapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var engine = OcrEngine.TryCreateFromUserProfileLanguages();
                if (engine == null)
                {
                    var language = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
                    if (language != null) engine = OcrEngine.TryCreateFromLanguage(language);
                }
                if (engine == null)
                    throw new InvalidOperationException("No text recognition language is installed. Add a language with text recognition in Windows Settings → Time & language → Language & region.");

                const int padding = 12;
                Size size = RecognitionSize(snapshot.Size, (int)OcrEngine.MaxImageDimension - 2 * padding);
                using var prepared = new Bitmap(size.Width + 2 * padding, size.Height + 2 * padding, PixelFormat.Format32bppArgb);
                using var source = new Bitmap(snapshot.Width, snapshot.Height, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(source))
                { graphics.Clear(Color.White); graphics.DrawImage(snapshot, new Rectangle(Point.Empty, source.Size), new Rectangle(Point.Empty, snapshot.Size), GraphicsUnit.Pixel); }
                NormalizeContrast(source);
                using (var graphics = Graphics.FromImage(prepared))
                {
                    graphics.Clear(Color.White);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.Half;
                    graphics.DrawImage(source, new Rectangle(padding, padding, size.Width, size.Height), new Rectangle(Point.Empty, source.Size), GraphicsUnit.Pixel);
                }
                var result = await ReadAsync(engine, prepared, cancellationToken).ConfigureAwait(false);
                var words = new List<RecognizedWord>();
                for (int lineIndex = 0; lineIndex < result.Lines.Count; lineIndex++)
                {
                    var line = result.Lines[lineIndex];
                    int cursor = 0;
                    foreach (var word in line.Words)
                    {
                        int offset = line.Text.IndexOf(word.Text, cursor, StringComparison.Ordinal);
                        string separator = offset >= cursor ? line.Text[cursor..offset] : " ";
                        if (offset >= 0) cursor = offset + word.Text.Length;
                        var bounds = MapBounds(new RectangleF((float)word.BoundingRect.X, (float)word.BoundingRect.Y,
                            (float)word.BoundingRect.Width, (float)word.BoundingRect.Height),
                            result.TextAngle ?? 0, prepared.Size, snapshot.Size, padding);
                        if (bounds.Width > 0 && bounds.Height > 0) words.Add(new(word.Text, bounds, lineIndex, separator));
                    }
                }
                var recognized = new TextRecognitionResult(string.Join(Environment.NewLine, result.Lines.Select(line => line.Text)),
                    words, engine.RecognizerLanguage.NativeName);
                var filtered = IconTextFilter.Apply(snapshot, recognized);
                var symbols = ArrowTextRecognition.Apply(snapshot, filtered);
                return DurationTextCorrection.Apply(VersionTextCorrection.Apply(symbols));
            }
        });
    }

    internal static RectangleF MapBounds(RectangleF word, double angle, Size prepared, Size original, int padding)
    {
        // Windows returns boxes in its deskewed coordinate space. Rotate them back about the image center.
        double radians = angle * Math.PI / 180;
        double cos = Math.Cos(radians), sin = Math.Sin(radians);
        float cx = prepared.Width / 2f, cy = prepared.Height / 2f;
        float sx = original.Width / (float)(prepared.Width - 2 * padding);
        float sy = original.Height / (float)(prepared.Height - 2 * padding);
        PointF[] corners = [new(word.Left, word.Top), new(word.Right, word.Top), new(word.Left, word.Bottom), new(word.Right, word.Bottom)];
        var mapped = corners.Select(p => new PointF((float)((p.X - cx) * cos - (p.Y - cy) * sin + cx - padding) * sx,
            (float)((p.X - cx) * sin + (p.Y - cy) * cos + cy - padding) * sy)).ToArray();
        return RectangleF.Intersect(new RectangleF(0, 0, original.Width, original.Height),
            RectangleF.FromLTRB(mapped.Min(p => p.X), mapped.Min(p => p.Y), mapped.Max(p => p.X), mapped.Max(p => p.Y)));
    }

    private static async Task<OcrResult> ReadAsync(OcrEngine engine, Bitmap image, CancellationToken cancellationToken)
    {
        using var encoded = new MemoryStream();
        image.Save(encoded, ImageFormat.Png);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(encoded.ToArray());
            await writer.StoreAsync().AsTask(cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
            writer.DetachStream();
        }
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore)
            .AsTask(cancellationToken).ConfigureAwait(false);
        var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static void NormalizeContrast(Bitmap image)
    {
        var data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            byte[] pixels = new byte[data.Stride * image.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            var histogram = new int[256];
            long borderSum = 0; int borderCount = 0;
            for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                int i = y * data.Stride + x * 4;
                int gray = (pixels[i + 2] * 77 + pixels[i + 1] * 150 + pixels[i] * 29) >> 8;
                histogram[gray]++;
                if (x < 3 || y < 3 || x >= image.Width - 3 || y >= image.Height - 3) { borderSum += gray; borderCount++; }
            }
            int low = 0, high = 255, count = 0, tail = Math.Max(1, image.Width * image.Height / 1000);
            for (int i = 0; i < 256; i++) { count += histogram[i]; if (count >= tail) { low = i; break; } }
            count = 0;
            for (int i = 255; i >= 0; i--) { count += histogram[i]; if (count >= tail) { high = i; break; } }
            bool invert = borderCount > 0 && borderSum / borderCount < 128;
            bool stretch = high - low is >= 8 and < 128;
            if (!stretch && !invert) return;
            int range = stretch ? high - low : 255;
            int floor = stretch ? low : 0;
            for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                int i = y * data.Stride + x * 4;
                int gray = (pixels[i + 2] * 77 + pixels[i + 1] * 150 + pixels[i] * 29) >> 8;
                int value = Math.Clamp((gray - floor) * 255 / range, 0, 255);
                if (invert) value = 255 - value;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = (byte)value; pixels[i + 3] = 255;
            }
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally { image.UnlockBits(data); }
    }

    internal static Size RecognitionSize(Size source, int limit, double upscale = 4)
    {
        if (source.Width < 1 || source.Height < 1 || limit < 1) throw new ArgumentOutOfRangeException(nameof(source));
        double scale = Math.Min(upscale, limit / (double)Math.Max(source.Width, source.Height));
        return new Size(Math.Max(1, (int)Math.Floor(source.Width * scale)), Math.Max(1, (int)Math.Floor(source.Height * scale)));
    }
}
