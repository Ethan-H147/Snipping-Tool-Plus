using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SnippingToolPlus;

internal sealed class TextRecognitionCanvas : Control
{
    private readonly Bitmap image;
    private readonly HashSet<int> selected = [];
    private readonly System.Windows.Forms.Timer revealTimer = new() { Interval = 16 };
    private readonly Stopwatch revealClock = new();
    private TextRecognitionResult? result;
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

    public void SetResult(TextRecognitionResult value, bool animate = true)
    {
        result = value; selected.Clear(); anchor = hovered = -1;
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

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(Theme.IsDark ? Color.FromArgb(24, 24, 28) : Color.FromArgb(236, 236, 242));
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(image, ImageBounds);
        if (result == null) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var normalFill = new SolidBrush(Color.FromArgb((int)(34 * reveal), Theme.Accent));
        using var hoverFill = new SolidBrush(Color.FromArgb((int)(80 * reveal), Theme.Accent));
        using var selectedFill = new SolidBrush(Color.FromArgb((int)(140 * reveal), Theme.Accent));
        using var normalOutline = new Pen(Color.FromArgb((int)(110 * reveal), Theme.Accent));
        using var selectedOutline = new Pen(Color.FromArgb((int)(245 * reveal), Theme.Accent), 2);
        for (int i = 0; i < result.Words.Count; i++)
        {
            bool active = selected.Contains(i);
            RectangleF bounds = WordBounds(i);
            e.Graphics.FillRectangle(active ? selectedFill : i == hovered ? hoverFill : normalFill, bounds);
            e.Graphics.DrawRectangle(active ? selectedOutline : normalOutline, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }
    }

    private int HitWord(Point location)
    {
        if (result == null) return -1;
        for (int i = 0; i < result.Words.Count; i++)
            if (RectangleF.Inflate(WordBounds(i), 3, 3).Contains(location)) return i;
        return -1;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus(); anchor = HitWord(e.Location);
        selected.Clear();
        if (anchor >= 0) { selected.Add(anchor); Capture = true; }
        Invalidate(); SelectionChanged?.Invoke();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hit = HitWord(e.Location);
        if (hovered != hit) { hovered = hit; Cursor = hit >= 0 ? Cursors.IBeam : Cursors.Default; Invalidate(); }
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
            selected.Clear();
            for (int i = 0; i < result.Words.Count; i++) selected.Add(i);
            Invalidate(); SelectionChanged?.Invoke(); e.Handled = true; e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }

    protected override void Dispose(bool disposing) { if (disposing) revealTimer.Dispose(); base.Dispose(disposing); }
}
