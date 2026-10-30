using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SnippingToolPlus;

[Flags]
internal enum TextResizeEdges { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

internal sealed class ScreenTextOverlay : Form
{
    private readonly Bitmap desktop;
    private readonly Rectangle desktopBounds;
    private Bitmap image;
    private readonly TextRecognitionCanvas preview;
    private readonly FloatingTextToolbar toolbar;
    private readonly TextOverlayButton copyAll;
    private readonly Label information;
    private readonly ContextMenuStrip options;
    private readonly ToolStripMenuItem copySelected, removeLineBreaks, openInApp;
    private readonly ToolTip tips = new();
    private readonly System.Windows.Forms.Timer feedbackTimer = new() { Interval = 1100 };
    private CancellationTokenSource? recognition;
    private TextRecognitionResult? result;
    private Rectangle selection, resizeStart;
    private Point resizePointer;
    private TextResizeEdges resizeEdges;
    private bool copying, resourcesDisposed;

    public event Action? NewCaptureRequested;
    public event Action<Bitmap, TextRecognitionResult?>? OpenInAppRequested;
    public event Action<Bitmap, Rectangle>? ImageChanged;
    internal TextRecognitionCanvas Preview => preview;
    internal Rectangle SelectionBounds => selection;
    internal Rectangle SelectedScreenBounds => new(selection.X + desktopBounds.X, selection.Y + desktopBounds.Y, selection.Width, selection.Height);
    internal Rectangle ToolbarBounds => toolbar.Bounds;
    internal TextRecognitionResult? RecognitionResult => result;
    internal ContextMenuStrip Options => options;

    public ScreenTextOverlay(Bitmap desktop, Rectangle desktopBounds, Rectangle screenSelection, TextRecognitionResult? initialResult = null)
    {
        this.desktop = (Bitmap)desktop.Clone();
        this.desktopBounds = desktopBounds;
        selection = new Rectangle(screenSelection.X - desktopBounds.X, screenSelection.Y - desktopBounds.Y, screenSelection.Width, screenSelection.Height);
        if (!new Rectangle(Point.Empty, desktop.Size).Contains(selection) || selection.Width < 1 || selection.Height < 1)
        { this.desktop.Dispose(); throw new ArgumentException("The text region must be inside the captured desktop.", nameof(screenSelection)); }
        image = CaptureService.Crop(this.desktop, selection);
        Text = "Snipping Tool+ — Select text on screen";
        AccessibleName = Text;
        Font = Theme.MakeFont();
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = desktopBounds;
        ShowInTaskbar = false; TopMost = true; KeyPreview = true; DoubleBuffered = true;
        preview = new TextRecognitionCanvas(image) { NativePixels = true, Bounds = selection };
        preview.BeginFrameDrag = p => BeginResize(new Point(selection.X + p.X, selection.Y + p.Y));
        preview.FrameCursor = p => ResizeCursor(HitEdges(new Point(selection.X + p.X, selection.Y + p.Y)));
        Controls.Add(preview);

        toolbar = new FloatingTextToolbar();
        var back = Button("", "Select a new region", ToolbarGlyph.Back);
        copyAll = Button("Reading text…", "Copy all text", ToolbarGlyph.Copy);
        copyAll.Enabled = false; copyAll.Name = "CopyAllText";
        var more = Button("", "More text actions", ToolbarGlyph.More);
        var close = Button("", "Close text recognition", ToolbarGlyph.Close);
        toolbar.Controls.AddRange([back, copyAll, more, close]);
        back.Click += (_, _) => NewRegion();
        copyAll.Click += async (_, _) => await CopyAsync(false);
        close.Click += (_, _) => Close();
        options = new ContextMenuStrip { Font = Font, Renderer = new ThemeMenuRenderer() };
        copySelected = new ToolStripMenuItem("Copy selected") { ShortcutKeyDisplayString = "Ctrl+C", Enabled = false };
        copySelected.Click += async (_, _) => await CopyAsync(true);
        var selectAll = new ToolStripMenuItem("Select all") { ShortcutKeyDisplayString = "Ctrl+A" };
        selectAll.Click += (_, _) => preview.SelectAllWords();
        removeLineBreaks = new ToolStripMenuItem("Remove line breaks") { CheckOnClick = true };
        openInApp = new ToolStripMenuItem("Open in app");
        openInApp.Click += (_, _) => BeginInvoke((Action)OpenEditor);
        options.Items.AddRange([copySelected, selectAll, new ToolStripSeparator(), removeLineBreaks, new ToolStripSeparator(), openInApp]);
        options.Opening += (_, _) =>
        {
            options.BackColor = Theme.Surface; options.ForeColor = Theme.Ink;
            copySelected.Enabled = !copying && preview.SelectedIndices.Count > 0;
            selectAll.Enabled = result?.Words.Count > 0;
            openInApp.Enabled = !copying;
        };
        more.Click += (_, _) => options.Show(more, new Point(0, more.Height + S(6)));
        preview.ContextMenuStrip = options;
        preview.SelectionChanged += () => { copySelected.Enabled = !copying && preview.SelectedIndices.Count > 0; };
        information = new Label { Visible = false, AutoEllipsis = true, Padding = new Padding(S(10)), BackColor = Theme.Surface, ForeColor = Theme.Ink, Font = Font };
        Controls.Add(information); Controls.Add(toolbar);
        feedbackTimer.Tick += (_, _) => { feedbackTimer.Stop(); UpdateCopyState(); };
        Shown += async (_, _) => { Activate(); preview.Focus(); await RecognizeAsync(initialResult); };
        LayoutChrome();
    }

    private TextOverlayButton Button(string text, string description, ToolbarGlyph glyph)
    {
        var button = new TextOverlayButton { Text = text, AccessibleName = description, Glyph = glyph };
        tips.SetToolTip(button, description);
        return button;
    }

    private int S(int value) => Math.Max(1, (int)Math.Round(value * DeviceDpi / 96f));

    private void LayoutChrome()
    {
        if (toolbar == null || preview == null) return;
        Rectangle screen = Screen.FromRectangle(SelectedScreenBounds).Bounds;
        var localScreen = new Rectangle(screen.X - desktopBounds.X, screen.Y - desktopBounds.Y, screen.Width, screen.Height);
        var work = Rectangle.Intersect(new Rectangle(Point.Empty, desktop.Size), localScreen);
        int width = Math.Min(S(326), Math.Max(1, work.Width - S(16))), height = S(44);
        var preferred = new Rectangle(work.Left + (work.Width - width) / 2, work.Top + S(18), width, height);
        // Prefer the monitor's top center; move below the region if that would cover its text.
        if (Rectangle.Inflate(selection, S(8), S(8)).IntersectsWith(preferred) && selection.Bottom + S(12) + height <= work.Bottom)
            preferred.Y = selection.Bottom + S(12);
        toolbar.Bounds = preferred;
        int[] left = [4, 44, 242, 282], sizes = [36, 194, 36, 36];
        for (int i = 0; i < toolbar.Controls.Count; i++)
            toolbar.Controls[i].Bounds = new Rectangle(S(left[i]), S(4), S(sizes[i]), S(36));
        int infoWidth = Math.Min(S(460), work.Width - S(16));
        information.Bounds = new Rectangle(work.Left + (work.Width - infoWidth) / 2,
            Math.Min(toolbar.Bottom + S(8), Math.Max(work.Top, work.Bottom - S(90))), infoWidth, S(80));
        preview.Bounds = selection;
        toolbar.BringToFront(); information.BringToFront();
        Invalidate();
    }

    private async Task RecognizeAsync(TextRecognitionResult? supplied = null)
    {
        recognition?.Cancel();
        recognition?.Dispose();
        var current = new CancellationTokenSource();
        recognition = current;
        result = null; copying = false;
        feedbackTimer.Stop(); information.Visible = false;
        copyAll.Text = "Reading text…"; copyAll.Enabled = false;
        try
        {
            var recognized = supplied ?? await TextRecognition.RecognizeAsync(image, current.Token);
            if (IsDisposed || Disposing || !ReferenceEquals(recognition, current) || current.IsCancellationRequested) return;
            result = recognized; preview.SetResult(recognized); UpdateCopyState();
            if (recognized.Words.Count == 0) ShowInformation("No text found. Select a clearer or larger region using the back arrow.");
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!IsDisposed && !Disposing && ReferenceEquals(recognition, current))
            { copyAll.Text = "Text unavailable"; ShowInformation(error.Message); }
        }
    }

    private void ShowInformation(string text)
    { information.Text = text; information.AccessibleDescription = text; information.Visible = true; }

    private void UpdateCopyState()
    {
        copyAll.Text = result?.Words.Count > 0 ? "Copy all text" : result == null ? "Reading text…" : "No text found";
        copyAll.Enabled = !copying && !string.IsNullOrWhiteSpace(result?.Text);
        copySelected.Enabled = !copying && preview.SelectedIndices.Count > 0;
    }

    internal string CopyText(bool selected)
    {
        string text = selected ? result?.SelectedText(preview.SelectedIndices) ?? "" : result?.Text ?? "";
        return removeLineBreaks.Checked ? TextRecognitionResult.WithoutLineBreaks(text) : text;
    }

    private async Task CopyAsync(bool selected)
    {
        if (copying || string.IsNullOrWhiteSpace(CopyText(selected))) return;
        string text = CopyText(selected);
        var current = recognition;
        copying = true; UpdateCopyState();
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                try { Clipboard.SetDataObject(text, true, 0, 0); break; }
                catch (ExternalException) when (attempt < 5) { await Task.Delay(60, current?.Token ?? CancellationToken.None); }
            }
            if (!IsDisposed && ReferenceEquals(recognition, current))
            { copyAll.Text = selected ? "Selection copied" : "Copied"; feedbackTimer.Stop(); feedbackTimer.Start(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!IsDisposed && ReferenceEquals(recognition, current)) ShowInformation("The clipboard is busy. Try copying again."); }
        finally
        {
            if (!IsDisposed && ReferenceEquals(recognition, current))
            { copying = false; copyAll.Enabled = result?.Words.Count > 0; copySelected.Enabled = preview.SelectedIndices.Count > 0; }
        }
    }

    internal void OpenEditor()
    {
        if (resourcesDisposed || IsDisposed || Disposing) return;
        using var snapshot = (Bitmap)image.Clone();
        var recognized = result;
        Close();
        OpenInAppRequested?.Invoke(snapshot, recognized);
    }

    internal void NewRegion() { Close(); NewCaptureRequested?.Invoke(); }

    private TextResizeEdges HitEdges(Point p)
    {
        int grip = S(6);
        if (!Rectangle.Inflate(selection, grip, grip).Contains(p)) return TextResizeEdges.None;
        TextResizeEdges edges = TextResizeEdges.None;
        if (Math.Abs(p.X - selection.Left) <= grip) edges |= TextResizeEdges.Left;
        else if (Math.Abs(p.X - selection.Right) <= grip) edges |= TextResizeEdges.Right;
        if (Math.Abs(p.Y - selection.Top) <= grip) edges |= TextResizeEdges.Top;
        else if (Math.Abs(p.Y - selection.Bottom) <= grip) edges |= TextResizeEdges.Bottom;
        return edges;
    }

    private static Cursor? ResizeCursor(TextResizeEdges edges) => edges switch
    {
        TextResizeEdges.Left or TextResizeEdges.Right => Cursors.SizeWE,
        TextResizeEdges.Top or TextResizeEdges.Bottom => Cursors.SizeNS,
        TextResizeEdges.Left | TextResizeEdges.Top or TextResizeEdges.Right | TextResizeEdges.Bottom => Cursors.SizeNWSE,
        TextResizeEdges.Right | TextResizeEdges.Top or TextResizeEdges.Left | TextResizeEdges.Bottom => Cursors.SizeNESW,
        _ => null
    };

    private bool BeginResize(Point p)
    {
        resizeEdges = HitEdges(p);
        if (resizeEdges == TextResizeEdges.None) return false;
        recognition?.Cancel(); feedbackTimer.Stop();
        result = null; preview.UpdateImage(image); copyAll.Enabled = false; copySelected.Enabled = false; copyAll.Text = "Adjust region…";
        resizeStart = selection; resizePointer = p; preview.Visible = false;
        Capture = true; Cursor = ResizeCursor(resizeEdges) ?? Cursors.Default;
        return true;
    }

    internal static Rectangle ResizeRegion(Rectangle start, TextResizeEdges edges, int dx, int dy, Size desktopSize)
    {
        int minimumX = Math.Min(16, desktopSize.Width), minimumY = Math.Min(16, desktopSize.Height);
        int left = start.Left, top = start.Top, right = start.Right, bottom = start.Bottom;
        if (edges.HasFlag(TextResizeEdges.Left)) left = Math.Clamp(start.Left + dx, 0, Math.Max(0, right - minimumX));
        if (edges.HasFlag(TextResizeEdges.Right)) right = Math.Clamp(start.Right + dx, Math.Min(desktopSize.Width, left + minimumX), desktopSize.Width);
        if (edges.HasFlag(TextResizeEdges.Top)) top = Math.Clamp(start.Top + dy, 0, Math.Max(0, bottom - minimumY));
        if (edges.HasFlag(TextResizeEdges.Bottom)) bottom = Math.Clamp(start.Bottom + dy, Math.Min(desktopSize.Height, top + minimumY), desktopSize.Height);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    internal void ReplaceRegion(Rectangle value)
    {
        if (!new Rectangle(Point.Empty, desktop.Size).Contains(value) || value.Width < 1 || value.Height < 1) throw new ArgumentOutOfRangeException(nameof(value));
        selection = value;
        var previous = image;
        image = CaptureService.Crop(desktop, selection);
        preview.UpdateImage(image); previous.Dispose();
        preview.Visible = true;
        LayoutChrome();
        ImageChanged?.Invoke(image, SelectedScreenBounds);
        _ = RecognizeAsync();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) BeginResize(e.Location); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (resizeEdges != TextResizeEdges.None && Capture)
        { selection = ResizeRegion(resizeStart, resizeEdges, e.X - resizePointer.X, e.Y - resizePointer.Y, desktop.Size); Invalidate(); }
        else Cursor = ResizeCursor(HitEdges(e.Location)) ?? Cursors.Default;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || resizeEdges == TextResizeEdges.None) return;
        resizeEdges = TextResizeEdges.None; Capture = false; Cursor = Cursors.Default;
        ReplaceRegion(selection); preview.Focus();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture && resizeEdges != TextResizeEdges.None && !IsDisposed && !Disposing && !resourcesDisposed)
        {
            resizeEdges = TextResizeEdges.None; Cursor = Cursors.Default;
            ReplaceRegion(selection);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImageUnscaled(desktop, Point.Empty);
        var state = e.Graphics.Save();
        e.Graphics.ExcludeClip(selection);
        using (var shade = new SolidBrush(Color.FromArgb(66, 10, 13, 20))) e.Graphics.FillRectangle(shade, ClientRectangle);
        e.Graphics.Restore(state);
        using var edge = new Pen(Color.White, Math.Max(1, S(1))) { DashStyle = DashStyle.Dot };
        e.Graphics.DrawRectangle(edge, selection.X - 1, selection.Y - 1, selection.Width + 1, selection.Height + 1);
        using var handle = new Pen(Color.White, S(3)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        int length = S(13);
        foreach (var corner in new[] { new Point(selection.Left - 2, selection.Top - 2), new Point(selection.Right + 2, selection.Top - 2),
            new Point(selection.Left - 2, selection.Bottom + 2), new Point(selection.Right + 2, selection.Bottom + 2) })
        {
            int dx = corner.X < selection.Left ? length : -length, dy = corner.Y < selection.Top ? length : -length;
            e.Graphics.DrawLines(handle, [new Point(corner.X + dx, corner.Y), corner, new Point(corner.X, corner.Y + dy)]);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { if (options.Visible) options.Close(); else Close(); return true; }
        if (keyData == (Keys.Control | Keys.A)) { preview.SelectAllWords(); return true; }
        if (keyData == (Keys.Control | Keys.C)) { _ = CopyAsync(preview.SelectedIndices.Count > 0); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    { base.OnFormClosing(e); if (!e.Cancel) recognition?.Cancel(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true; recognition?.Cancel(); feedbackTimer.Dispose(); options.Dispose(); tips.Dispose();
            base.Dispose(true);
            recognition?.Dispose(); image.Dispose(); desktop.Dispose();
        }
        else base.Dispose(disposing);
    }
}

internal sealed class FloatingTextToolbar : Panel
{
    public FloatingTextToolbar()
    { SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true); BackColor = Color.Transparent; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        CaptureChrome.DrawSurface(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), Math.Max(2, (int)(9 * DeviceDpi / 96f)),
            Color.FromArgb(246, Theme.Surface), Theme.Border);
        using var separator = new Pen(Theme.Border);
        float scale = DeviceDpi / 96f;
        foreach (int x in new[] { 42, 240, 280 })
            e.Graphics.DrawLine(separator, x * scale, 12 * scale, x * scale, 32 * scale);
    }
}

internal sealed class TextOverlayButton : Button
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal ToolbarGlyph Glyph { get; init; }
    public TextOverlayButton()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false; Cursor = Cursors.Hand;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        bool hover = Enabled && ClientRectangle.Contains(PointToClient(Cursor.Position));
        if (hover || Focused)
            CaptureChrome.DrawSurface(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1), Math.Max(2, (int)(6 * DeviceDpi / 96f)), CaptureChrome.Hover);
        Rectangle textBounds = ClientRectangle;
        float scale = DeviceDpi / 96f;
        float iconSize = 20 * scale;
        var iconBounds = new RectangleF(Glyph == ToolbarGlyph.Copy ? 12 * scale : (Width - iconSize) / 2,
            (Height - iconSize) / 2, iconSize, iconSize);
        ToolbarGlyphs.Draw(e.Graphics, Glyph, iconBounds, Enabled ? Theme.Ink : Theme.Muted);
        if (Glyph == ToolbarGlyph.Copy)
        { textBounds.X += (int)(26 * scale); textBounds.Width -= (int)(26 * scale); }
        if (!string.IsNullOrEmpty(Text))
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Enabled ? Theme.Ink : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4), Theme.Accent, Theme.Surface);
    }
}
