using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace SnippingToolPlus;

internal sealed record RecognizedWord(string Text, RectangleF Bounds, int Line);
internal sealed record TextRecognitionResult(string Text, IReadOnlyList<RecognizedWord> Words, string Language)
{
    public string SelectedText(IEnumerable<int> indices)
    {
        var selected = indices.Distinct().Order().Where(i => i >= 0 && i < Words.Count).Select(i => Words[i]);
        return string.Join(Environment.NewLine, selected.GroupBy(word => word.Line)
            .Select(line => string.Join(" ", line.Select(word => word.Text))));
    }

    public static string WithoutLineBreaks(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s*\r?\n\s*", " ");
}

internal static class TextRecognition
{
    public static Task<TextRecognitionResult> RecognizeAsync(Bitmap image, CancellationToken cancellationToken = default)
    {
        // Own the pixels before returning, so closing the UI cannot invalidate a running recognition.
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

                var limit = (int)OcrEngine.MaxImageDimension;
                Size size = RecognitionSize(snapshot.Size, limit);
                using var prepared = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(prepared))
                {
                    graphics.Clear(Color.White);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(snapshot, new Rectangle(Point.Empty, size));
                }
                using var encoded = new MemoryStream();
                prepared.Save(encoded, ImageFormat.Png);
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
                float scaleX = snapshot.Width / (float)size.Width, scaleY = snapshot.Height / (float)size.Height;
                var words = result.Lines.SelectMany((line, lineIndex) => line.Words.Select(word =>
                    new RecognizedWord(word.Text, new RectangleF((float)word.BoundingRect.X * scaleX,
                        (float)word.BoundingRect.Y * scaleY, (float)word.BoundingRect.Width * scaleX,
                        (float)word.BoundingRect.Height * scaleY), lineIndex))).ToArray();
                return new TextRecognitionResult(string.Join(Environment.NewLine, result.Lines.Select(line => line.Text)),
                    words, engine.RecognizerLanguage.NativeName);
            }
        });
    }

    internal static Size RecognitionSize(Size source, int limit)
    {
        if (source.Width < 1 || source.Height < 1 || limit < 1) throw new ArgumentOutOfRangeException(nameof(source));
        double scale = Math.Min(2, limit / (double)Math.Max(source.Width, source.Height));
        return new Size(Math.Max(1, (int)Math.Floor(source.Width * scale)), Math.Max(1, (int)Math.Floor(source.Height * scale)));
    }
}
