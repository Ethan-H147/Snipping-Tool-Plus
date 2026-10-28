using System.Drawing.Drawing2D;

namespace SnippingToolPlus;

internal sealed class SelectionOverlay : Form
{
    private readonly Bitmap desktop;
    private readonly CaptureMode mode;
    private readonly AppSettings preferences;
    private Point anchor;
    private Point pointer;
    private Point previousPointer;
    private bool dragging;
    private bool spaceDown;
    private bool keyboardPlacement;
    private readonly List<Point> points = [];
    private readonly ShapePicker shapePicker;
    private readonly Rectangle fullScreen;
    public CaptureShape Shape => (CaptureShape)shapePicker.SelectedIndex;
    public Rectangle Selection { get; private set; }
    public CaptureMode DestinationMode { get; private set; }
    public Point[] Polygon => points.Select(p => new Point(p.X - Selection.X, p.Y - Selection.Y)).ToArray();
    internal Rectangle PickerBounds => shapePicker.Bounds;
    private bool FixedSize => preferences.ExactSize && Shape == CaptureShape.Rectangle;

    public SelectionOverlay(Bitmap desktop, Rectangle bounds, CaptureMode mode, CaptureShape shape = CaptureShape.Rectangle,
        AppSettings? preferences = null, Rectangle? rememberedSelection = null)
    {
        this.desktop = desktop;
        this.mode = DestinationMode = mode;
        this.preferences = preferences ?? new AppSettings();
        Font = Theme.MakeFont();
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        Text = mode == CaptureMode.Copy ? "Snipping Tool+ — Copy snip" : "Snipping Tool+ — Save snip";
        AccessibleName = Text;
        Rectangle screen = Screen.FromPoint(Cursor.Position).Bounds;
        fullScreen = new Rectangle(screen.X - bounds.X, screen.Y - bounds.Y, screen.Width, screen.Height);
        int pickerWidth = (int)(252 * DeviceDpi / 96f);
        shapePicker = new ShapePicker { Size = new Size(pickerWidth, (int)(44 * DeviceDpi / 96f)),
            Location = new Point(fullScreen.Left + (fullScreen.Width - pickerWidth) / 2, fullScreen.Top + (int)(68 * DeviceDpi / 96f)),
            Font = Theme.MakeFont() };
        shapePicker.SelectedIndex = (int)shape;
        shapePicker.SelectedIndexChanged += (_, _) =>
        {
            points.Clear(); Selection = Rectangle.Empty;
            if (Shape == CaptureShape.FullScreen) { Selection = fullScreen; Complete(); }
            else { Focus(); Invalidate(); }
        };
        shapePicker.Popup.Closed += (_, _) => Invalidate();
        if (rememberedSelection is Rectangle remembered && shape == CaptureShape.Rectangle &&
            new Rectangle(Point.Empty, desktop.Size).Contains(remembered)) Selection = remembered;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e); Activate();
        pointer = previousPointer = PointToClient(Cursor.Position);
        if (Shape == CaptureShape.FullScreen) { Selection = fullScreen; DestinationMode = mode; DialogResult = DialogResult.OK; return; }
        if (FixedSize)
        {
            if (preferences.CaptureWidth > desktop.Width || preferences.CaptureHeight > desktop.Height)
            {
                MessageBox.Show(this, "The exact capture size is larger than the desktop. Choose a smaller size in settings.", "Snipping Tool+");
                DialogResult = DialogResult.Cancel; return;
            }
            Selection = PlaceFixed(pointer);
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImageUnscaled(desktop, Point.Empty);
        using var shade = new SolidBrush(Color.FromArgb(112, 10, 16, 30));
        e.Graphics.FillRectangle(shade, ClientRectangle);
        if (Selection.Width > 0 && Selection.Height > 0)
        {
            if (Shape == CaptureShape.Freeform && points.Count >= 3)
            {
                using var path = new GraphicsPath(); path.AddPolygon(points.ToArray());
                var state = e.Graphics.Save(); e.Graphics.SetClip(path);
                e.Graphics.DrawImageUnscaled(desktop, Point.Empty); e.Graphics.Restore(state);
                using var line = new Pen(Color.FromArgb(185, 180, 255), 2); e.Graphics.DrawPath(line, path);
            }
            else e.Graphics.DrawImage(desktop, Selection, Selection, GraphicsUnit.Pixel);
            using var outline = new Pen(Color.FromArgb(185, 180, 255), 2);
            if (Shape != CaptureShape.Freeform) e.Graphics.DrawRectangle(outline, Selection.X, Selection.Y, Selection.Width - 1, Selection.Height - 1);
            DrawPill(e.Graphics, $"{Selection.Width} × {Selection.Height}", Selection.Left,
                Selection.Top > 42 ? Selection.Top - 37 : Selection.Bottom + 10);
        }
        else
        {
            using var cross = new Pen(Color.FromArgb(110, Color.White));
            e.Graphics.DrawLine(cross, pointer.X - 14, pointer.Y, pointer.X + 14, pointer.Y);
            e.Graphics.DrawLine(cross, pointer.X, pointer.Y - 14, pointer.X, pointer.Y + 14);
        }
        string hint = EffectiveMode() == CaptureMode.Copy ? "COPY SNIP" : "SAVE + COPY";
        var measured = TextRenderer.MeasureText(hint, Font);
        DrawPill(e.Graphics, hint, fullScreen.X + (fullScreen.Width - measured.Width - 28) / 2, fullScreen.Y + 28);
        shapePicker.PaintOn(e.Graphics, shapePicker.Location, shapePicker.Bounds.Contains(pointer));
    }

    private void DrawPill(Graphics graphics, string text, int x, int y)
    {
        Size measured = TextRenderer.MeasureText(text, Font);
        var rectangle = new Rectangle(Math.Clamp(x, 0, Math.Max(0, Width - measured.Width - 28)),
            Math.Clamp(y, 0, Math.Max(0, Height - measured.Height - 18)), measured.Width + 28, measured.Height + 18);
        CaptureChrome.DrawSurface(graphics, rectangle, 9, Color.FromArgb(245, CaptureChrome.Background));
        TextRenderer.DrawText(graphics, text, Font, rectangle, CaptureChrome.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private Rectangle PlaceFixed(Point position) => SelectionGeometry.Move(
        new Rectangle(position.X, position.Y, preferences.CaptureWidth, preferences.CaptureHeight), 0, 0, desktop.Size);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; return; }
        if (e.Button != MouseButtons.Left) return;
        if (shapePicker.Bounds.Contains(e.Location))
        {
            shapePicker.OpenMenuAt(this, new Point(shapePicker.Left, shapePicker.Bottom + 6)); Invalidate(); return;
        }
        if (FixedSize) { if (!keyboardPlacement) Selection = PlaceFixed(e.Location); Complete(); return; }
        anchor = previousPointer = e.Location;
        points.Clear(); points.Add(e.Location);
        Selection = Rectangle.Empty;
        dragging = true; Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        pointer = e.Location;
        if (dragging && spaceDown)
            MoveSelection(pointer.X - previousPointer.X, pointer.Y - previousPointer.Y);
        else if (dragging)
        {
            points.Add(new Point(Math.Clamp(pointer.X, 0, desktop.Width - 1), Math.Clamp(pointer.Y, 0, desktop.Height - 1)));
            Selection = Shape == CaptureShape.Freeform ? PolygonBounds() : CaptureService.SelectionBetween(anchor, pointer, desktop.Size);
        }
        else if (FixedSize && pointer != previousPointer && !shapePicker.Bounds.Contains(pointer))
        { keyboardPlacement = false; Selection = PlaceFixed(pointer); }
        previousPointer = pointer; Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!dragging || e.Button != MouseButtons.Left) return;
        dragging = false; Capture = false;
        if (!spaceDown && e.Location != pointer) Selection = Shape == CaptureShape.Freeform ? PolygonBounds() : CaptureService.SelectionBetween(anchor, e.Location, desktop.Size);
        if (Shape == CaptureShape.Freeform && points.Count < 3) { Selection = Rectangle.Empty; Invalidate(); return; }
        if (Selection.Width > 1 && Selection.Height > 1) Complete();
        else { Selection = Rectangle.Empty; Invalidate(); }
    }

    internal void MoveSelection(int dx, int dy)
    {
        Rectangle moved = SelectionGeometry.Move(Selection, dx, dy, desktop.Size);
        int actualX = moved.X - Selection.X, actualY = moved.Y - Selection.Y;
        Selection = moved;
        anchor.Offset(actualX, actualY);
        for (int i = 0; i < points.Count; i++) points[i] = new Point(points[i].X + actualX, points[i].Y + actualY);
    }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is
        Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Space or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; e.Handled = true; }
        else if (e.KeyCode == Keys.Space) { spaceDown = true; e.Handled = true; }
        else if (e.KeyCode == Keys.Enter && Selection.Width > 1 && Selection.Height > 1) { Complete(); e.Handled = true; }
        else if (e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down && Selection.Width > 0)
        {
            int step = e.Shift ? 10 : 1;
            MoveSelection(e.KeyCode == Keys.Left ? -step : e.KeyCode == Keys.Right ? step : 0,
                e.KeyCode == Keys.Up ? -step : e.KeyCode == Keys.Down ? step : 0);
            keyboardPlacement = true; e.Handled = true;
        }
        Invalidate(); base.OnKeyDown(e);
    }
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space) spaceDown = false;
        Invalidate(); base.OnKeyUp(e);
    }
    internal static CaptureMode ResolveMode(CaptureMode requested, bool alt) => alt ?
        (requested == CaptureMode.Copy ? CaptureMode.Save : CaptureMode.Copy) : requested;
    private CaptureMode EffectiveMode() => ResolveMode(mode, ModifierKeys.HasFlag(Keys.Alt));
    private void Complete() { DestinationMode = EffectiveMode(); DialogResult = DialogResult.OK; }
    private Rectangle PolygonBounds() => points.Count == 0 ? Rectangle.Empty :
        Rectangle.Intersect(new Rectangle(Point.Empty, desktop.Size), Rectangle.FromLTRB(
            points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X) + 1, points.Max(p => p.Y) + 1));
    public Bitmap CreateImage() => Shape == CaptureShape.Freeform
        ? CaptureService.CropFreeform(desktop, points.ToArray(), Selection) : CaptureService.Crop(desktop, Selection);
    protected override void WndProc(ref Message message)
    {
        if (message.Msg is 0x001a or 0x031a) { Theme.Refresh(); Invalidate(); }
        base.WndProc(ref message);
    }
    protected override void Dispose(bool disposing) { if (disposing) shapePicker.Dispose(); base.Dispose(disposing); }
}
