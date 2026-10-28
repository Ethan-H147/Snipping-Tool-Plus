using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace QuietSnip;

internal sealed class ShapePicker : Control
{
    private readonly ContextMenuStrip menu;
    private int selectedIndex;
    private bool hovering;
    private bool pressed;
    private static readonly string[] Names = ["Rectangle mode", "Free-form mode", "Full-screen mode"];

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (value < 0 || value >= Names.Length) throw new ArgumentOutOfRangeException(nameof(value));
            bool changed = selectedIndex != value;
            selectedIndex = value;
            Text = Names[value];
            AccessibleDescription = Text + ". Press Space or Down to choose a capture mode.";
            Invalidate();
            if (changed) SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? SelectedIndexChanged;
    internal ContextMenuStrip Popup => menu;

    public ShapePicker()
    {
        Text = Names[0];
        AccessibleName = "Capture mode";
        AccessibleRole = AccessibleRole.PushButton;
        TabStop = true;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
            ControlStyles.StandardClick | ControlStyles.Selectable, true);
        menu = new RoundedModeMenu
        {
            ShowImageMargin = false, ShowCheckMargin = false,
            BackColor = CaptureChrome.Background, ForeColor = CaptureChrome.Text, Padding = new Padding(5),
            Renderer = new ModeMenuRenderer(() => selectedIndex)
        };
        for (int index = 0; index < Names.Length; index++)
        {
            int modeIndex = index;
            var item = new ToolStripMenuItem(Names[index]) { Tag = index };
            item.Click += (_, _) => SelectedIndex = modeIndex;
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) => { pressed = false; Invalidate(); };
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        ShowMenu();
    }

    public void PerformClick() => OnClick(EventArgs.Empty);

    internal void OpenMenuAt(Control owner, Point location) => ShowMenu(owner, location);

    private void ShowMenu(Control? owner = null, Point? location = null)
    {
        if (menu.Visible) { menu.Close(); return; }
        float scale = DeviceDpi / 96f;
        menu.Font = Font;
        int popupWidth = Math.Max(Width, Names.Max(name => TextRenderer.MeasureText(name, Font).Width) + (int)(76 * scale));
        int rowHeight = (int)(38 * scale);
        menu.AutoSize = false;
        menu.Size = new Size(popupWidth, rowHeight * Names.Length + menu.Padding.Vertical);
        foreach (ToolStripItem item in menu.Items)
        {
            item.AutoSize = false;
            item.Size = new Size(popupWidth - menu.Padding.Horizontal, rowHeight);
        }
        pressed = true;
        Invalidate();
        menu.Show(owner ?? this, location ?? new Point(0, Height + (int)(5 * scale)));
        menu.Items[selectedIndex].Select();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Down or Keys.Up or Keys.Space or Keys.Enter)
        {
            ShowMenu(); e.Handled = true; e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Down or Keys.Up or Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnMouseEnter(EventArgs e) { hovering = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovering = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (Parent == null) { base.OnPaintBackground(e); return; }
        var state = e.Graphics.Save();
        e.Graphics.TranslateTransform(-Left, -Top);
        var parentPaint = new PaintEventArgs(e.Graphics, new Rectangle(Location, Size));
        InvokePaintBackground(Parent, parentPaint);
        InvokePaint(Parent, parentPaint);
        e.Graphics.Restore(state);
    }

    internal void PaintOn(Graphics graphics, Point position, bool hover)
    {
        hovering = hover;
        var state = graphics.Save();
        graphics.TranslateTransform(position.X, position.Y);
        OnPaint(new PaintEventArgs(graphics, ClientRectangle));
        graphics.Restore(state);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        float scale = DeviceDpi / 96f;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        CaptureChrome.DrawSurface(g, new Rectangle(0, 0, Width - 1, Height - 1), (int)(9 * scale),
            pressed || (Focused && ShowFocusCues) ? CaptureChrome.Selected : hovering ? CaptureChrome.Hover : CaptureChrome.Background);
        CaptureGlyph.Draw(g, (CaptureShape)selectedIndex,
            new RectangleF(13 * scale, (Height - 20 * scale) / 2, 20 * scale, 20 * scale), CaptureChrome.Text);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var textBrush = new SolidBrush(CaptureChrome.Text);
        using var format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString(Text, Font, textBrush,
            new RectangleF(43 * scale, 0, Width - 76 * scale, Height), format);
        using var pen = new Pen(CaptureChrome.Muted, 1.3f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float x = Width - 22 * scale, y = Height / 2f - 2 * scale;
        g.DrawLines(pen, new PointF[] { new(x - 3 * scale, y), new(x, y + 3 * scale), new(x + 3 * scale, y) });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) menu.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class RoundedModeMenu : ContextMenuStrip
{
    public RoundedModeMenu() => DropShadowEnabled = false;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Let the desktop compositor draw antialiased corners. A window Region
        // only provides a binary mask, which creates visibly stair-stepped edges.
        int preference = 2;
        try
        {
            _ = SetCornerPreference(Handle, 33, ref preference, sizeof(int));
            int noBorder = unchecked((int)0xfffffffe);
            _ = SetCornerPreference(Handle, 34, ref noBorder, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static extern int SetCornerPreference(IntPtr window, int attribute, ref int value, int size);
}

internal sealed class ModeMenuRenderer(Func<int> selectedIndex) : ToolStripProfessionalRenderer
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(CaptureChrome.Background);

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        // The popup has a flat, borderless surface, like the capture hint.
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var g = e.Graphics;
        float scale = e.Item.Height / 38f;
        bool active = e.Item.Tag is int index && index == selectedIndex();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundedRectangle(new Rectangle(0, 1, e.Item.Width - 1, e.Item.Height - 2), (int)(5 * scale));
        using var fill = new SolidBrush(e.Item.Selected ? CaptureChrome.Hover
            : active ? CaptureChrome.Selected : CaptureChrome.Background);
        g.FillPath(fill, path);
        if (active)
        {
            using var indicator = new Pen(CaptureChrome.Focus, 3 * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(indicator, 4 * scale, 11 * scale, 4 * scale, e.Item.Height - 11 * scale);
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        float scale = e.Item.Height / 38f;
        if (e.Item.Tag is int index)
            CaptureGlyph.Draw(e.Graphics, (CaptureShape)index,
                new RectangleF(15 * scale, (e.Item.Height - 20 * scale) / 2, 20 * scale, 20 * scale), CaptureChrome.Text);
        TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont,
            new Rectangle((int)(46 * scale), 0, e.Item.Width - (int)(54 * scale), e.Item.Height), CaptureChrome.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

internal static class CaptureGlyph
{
    // Draw at a 24-unit size so the same light strokes stay sharp at any DPI.
    public static void Draw(Graphics graphics, CaptureShape shape, RectangleF bounds, Color color)
    {
        var state = graphics.Save();
        graphics.TranslateTransform(bounds.X, bounds.Y);
        graphics.ScaleTransform(bounds.Width / 24, bounds.Height / 24);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, 1.25f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        if (shape == CaptureShape.Freeform)
        {
            using var path = new GraphicsPath();
            path.AddBezier(17, 18, 11, 24, 3, 20, 3, 13);
            path.AddBezier(3, 13, 0, 4, 9, 0, 14, 4);
            path.AddBezier(14, 4, 17, 2, 22, 7, 19, 14);
            graphics.DrawPath(pen, path);
        }
        else
        {
            graphics.DrawLines(pen, [new(15, 19), new(3, 19), new(3, 4), new(21, 4), new(21, 14)]);
            if (shape == CaptureShape.FullScreen)
            {
                graphics.DrawLine(pen, 21, 14, 21, 19); graphics.DrawLine(pen, 21, 19, 15, 19);
                graphics.DrawLines(pen, [new(9, 7), new(6, 7), new(6, 10)]);
                graphics.DrawLine(pen, 6, 7, 10, 11);
                graphics.DrawLines(pen, [new(15, 16), new(18, 16), new(18, 13)]);
                graphics.DrawLine(pen, 18, 16, 14, 12);
            }
        }
        if (shape != CaptureShape.FullScreen)
        {
            graphics.DrawLine(pen, 19, 16, 19, 23);
            graphics.DrawLine(pen, 15.5f, 19.5f, 22.5f, 19.5f);
        }
        graphics.Restore(state);
    }
}

