using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SnippingToolPlus;

internal sealed class TextRecognitionCanvas : Control
{
    private Bitmap image;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool NativePixels { get; init; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<Point, bool>? BeginFrameDrag { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<Point, Cursor?>? FrameCursor { get; set; }
    private readonly HashSet<int> selected = [];
    private readonly System.Windows.Forms.Timer revealTimer = new() { Interval = 16 };
    private readonly Stopwatch revealClock = new();
    private TextRecognitionResult? result;
    private int[][] lines = [];
    private float reveal = 1;
    private int anchor = -1, hovered = -1;
    public event Action? SelectionChanged;
    internal IReadOnlyCollection<int> SelectedIndices => selected;
    internal TextRecognitionResult? Result => result;

    public TextRecognitionCanvas(Bitmap image)
    {
        this.image = image;
        DoubleBuffered = true;
        ResizeRedraw = true;
        TabStop = true;
        AccessibleName = "Screenshot with recognized text. Drag across words to select them.";
        SetStyle(ControlStyles.Selectable, true);
        revealTimer.Tick += (_, _) =>
        {
            float t = Math.Min(1, (float)revealClock.Elapsed.TotalMilliseconds / 160);
            reveal = 1 - MathF.Pow(1 - t, 3);
            Invalidate();
            if (t >= 1) revealTimer.Stop();
        };
    }

    internal void UpdateImage(Bitmap value)
    {
        image = value; result = null; lines = []; selected.Clear(); anchor = hovered = -1; revealTimer.Stop();
        Invalidate(); SelectionChanged?.Invoke();
    }

    internal void SelectAllWords()
    {
        selected.Clear();
        if (result != null) for (int i = 0; i < result.Words.Count; i++) selected.Add(i);
        Invalidate(); SelectionChanged?.Invoke();
    }

    public void SetResult(TextRecognitionResult value, bool animate = true)
    {
        result = value; selected.Clear(); anchor = hovered = -1;
        lines = value.Words.Select((word, index) => (word.Line, index))
            .GroupBy(item => item.Line).Select(line => line.Select(item => item.index).ToArray()).ToArray();
        revealTimer.Stop();
        reveal = 1;
        if (animate && MotionEnabled)
        {
            reveal = 0; revealClock.Restart(); revealTimer.Start();
        }
        AccessibleDescription = value.Words.Count == 0 ? "No text found" : value.Text;
        Invalidate(); SelectionChanged?.Invoke();
    }

    private static bool MotionEnabled => !SystemInformation.HighContrast
        && SystemParametersInfo(0x1042, 0, out bool enabled, 0) && enabled;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);

    internal RectangleF ImageBounds
    {
        get
        {
            if (NativePixels) return new RectangleF(0, 0, image.Width, image.Height);
            float availableWidth = Math.Max(1, ClientSize.Width - 32), availableHeight = Math.Max(1, ClientSize.Height - 32);
            float scale = Math.Min(availableWidth / image.Width, availableHeight / image.Height);
            return new RectangleF((Width - image.Width * scale) / 2, (Height - image.Height * scale) / 2,
                image.Width * scale, image.Height * scale);
        }
    }

    internal RectangleF WordBounds(int index)
    {
        var target = ImageBounds;
        var word = result!.Words[index].Bounds;
        return new RectangleF(target.X + word.X * target.Width / image.Width,
            target.Y + word.Y * target.Height / image.Height,
            word.Width * target.Width / image.Width, word.Height * target.Height / image.Height);
    }

    private RectangleF BoundsFor(IEnumerable<int> indices)
    {
        var bounds = indices.Select(WordBounds).Aggregate(RectangleF.Union);
        float scale = ImageBounds.Height / image.Height;
        float padding = Math.Max(2 * scale, bounds.Height * .15f);
        return RectangleF.Intersect(RectangleF.Inflate(bounds, Math.Max(2 * scale, bounds.Height * .12f), padding), ImageBounds);
    }

    internal IReadOnlyList<RectangleF> LineBounds => lines.Select(BoundsFor).ToArray();

    internal IReadOnlyList<RectangleF> SelectionBounds
    {
        get
        {
            var spans = new List<RectangleF>();
            foreach (var line in lines)
            {
                var lineBounds = BoundsFor(line);
                RectangleF? span = null;
                foreach (int index in line)
                {
                    if (selected.Contains(index))
                    {
                        var word = WordBounds(index);
                        word.Y = lineBounds.Y; word.Height = lineBounds.Height;
                        span = span is RectangleF previous ? RectangleF.Union(previous, word) : word;
                    }
                    else if (span is RectangleF previous) { spans.Add(previous); span = null; }
                }
                if (span is RectangleF remaining) spans.Add(remaining);
            }
            return spans;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(Theme.IsDark ? Color.FromArgb(24, 24, 28) : Color.FromArgb(236, 236, 242));
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        if (NativePixels) e.Graphics.DrawImage(image, new Rectangle(Point.Empty, image.Size), new Rectangle(Point.Empty, image.Size), GraphicsUnit.Pixel);
        else e.Graphics.DrawImage(image, ImageBounds);
        if (NativePixels)
        {
            using var frame = new Pen(Color.White) { DashStyle = DashStyle.Dot };
            e.Graphics.DrawRectangle(frame, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        }
        if (result == null) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var normalFill = new SolidBrush(Color.FromArgb((int)((NativePixels ? 24 : 18) * reveal), NativePixels ? Color.White : Theme.Accent));
        using var hoverFill = new SolidBrush(Color.FromArgb((int)(48 * reveal), NativePixels ? Color.White : Theme.Accent));
        using var selectedFill = new SolidBrush(Color.FromArgb((int)(120 * reveal), Theme.Accent));
        using var normalOutline = new Pen(Color.FromArgb((int)(75 * reveal), NativePixels ? Color.White : Theme.Accent));
        foreach (var line in lines)
        {
            using var path = HighlightPath(BoundsFor(line));
            e.Graphics.FillPath(line.Contains(hovered) ? hoverFill : normalFill, path);
            e.Graphics.DrawPath(normalOutline, path);
        }
        foreach (var bounds in SelectionBounds)
        {
            using var path = HighlightPath(bounds);
            e.Graphics.FillPath(selectedFill, path);
        }
    }

    private GraphicsPath HighlightPath(RectangleF bounds)
    {
        var pixels = Rectangle.FromLTRB((int)Math.Floor(bounds.Left), (int)Math.Floor(bounds.Top),
            (int)Math.Ceiling(bounds.Right), (int)Math.Ceiling(bounds.Bottom));
        pixels.Width = Math.Max(1, pixels.Width); pixels.Height = Math.Max(1, pixels.Height);
        if (pixels.Width < 2 || pixels.Height < 2)
        {
            var path = new GraphicsPath(); path.AddRectangle(pixels); return path;
        }
        return Theme.RoundedRectangle(pixels, Math.Min(Math.Min(pixels.Width, pixels.Height) / 2, Math.Max(1, (int)(3 * DeviceDpi / 96f))));
    }

    private int HitWord(Point location)
    {
        if (result == null) return -1;
        foreach (var line in lines)
        {
            if (!RectangleF.Inflate(BoundsFor(line), 3, 3).Contains(location)) continue;
            // Keep spaces within a line selectable, using the closest word in reading order.
            return line.MinBy(index =>
            {
                var bounds = WordBounds(index);
                return Math.Max(0, Math.Max(bounds.Left - location.X, location.X - bounds.Right));
            });
        }
        return -1;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (BeginFrameDrag?.Invoke(e.Location) == true) return;
        Focus(); anchor = HitWord(e.Location);
        selected.Clear();
        if (anchor >= 0) { selected.Add(anchor); Capture = true; }
        Invalidate(); SelectionChanged?.Invoke();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var frameCursor = FrameCursor?.Invoke(e.Location);
        if (frameCursor != null && !Capture) { Cursor = frameCursor; return; }
        int hit = HitWord(e.Location);
        Cursor = hit >= 0 ? Cursors.IBeam : Cursors.Default;
        if (hovered != hit) { hovered = hit; Invalidate(); }
        if (!Capture || anchor < 0 || hit < 0 || result == null) return;
        selected.Clear();
        for (int i = Math.Min(anchor, hit); i <= Math.Max(anchor, hit); i++) selected.Add(i);
        Invalidate(); SelectionChanged?.Invoke();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left) { Capture = false; anchor = -1; }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = -1; Invalidate(); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.A && result != null)
        {
            SelectAllWords(); e.Handled = true; e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }

    protected override void Dispose(bool disposing) { if (disposing) revealTimer.Dispose(); base.Dispose(disposing); }
}
