using System.Drawing.Drawing2D;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace QuietSnip;

internal static class Theme
{
    public static bool Dark { get; private set; } = ReadDark();
    internal static bool? TestDark { get; set; }
    public static bool IsDark => TestDark ?? Dark;
    public const string FontFamily = "Segoe UI Variable Text";
    public static Color Background => IsDark ? Color.FromArgb(28, 28, 30) : Color.FromArgb(246, 246, 248);
    public static Color Surface => IsDark ? Color.FromArgb(38, 38, 41) : Color.White;
    public static Color Field => IsDark ? Color.FromArgb(47, 47, 51) : Color.FromArgb(240, 240, 244);
    public static Color Ink => IsDark ? Color.FromArgb(244, 244, 247) : Color.FromArgb(28, 28, 32);
    public static Color Muted => IsDark ? Color.FromArgb(177, 177, 188) : Color.FromArgb(99, 99, 112);
    public static Color Accent => IsDark ? Color.FromArgb(184, 174, 255) : Color.FromArgb(91, 71, 199);
    public static Color Primary => Color.FromArgb(91, 71, 199);
    public static Color Border => IsDark ? Color.FromArgb(64, 64, 69) : Color.FromArgb(222, 222, 229);
    public static Font MakeFont(float size = 10, bool bold = false) => new(
        bold ? "Segoe UI Variable Text Semibold" : FontFamily, size, FontStyle.Regular);
    private static bool ReadDark()
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0; }
        catch { return false; }
    }
    public static void Refresh() => Dark = ReadDark();
    public static void Apply(Control control)
    {
        control.ForeColor = control.Tag as string == "muted" ? Muted : Ink;
        control.BackColor = control switch
        {
            HotkeyBox => Field, TextBox or NumericUpDown => Surface,
            _ => Background
        };
        if (control is ShapePicker) control.BackColor = Color.Transparent;
        if (control is FieldFrame) control.BackColor = Background;
        else if (control is TextBox && control.Parent?.Tag as string == "field") control.BackColor = Field;
        if (control.Tag as string is "divider") control.BackColor = Border;
        foreach (Control child in control.Controls) Apply(child);
        if (control is Form form && form.IsHandleCreated)
        {
            int dark = IsDark ? 1 : 0;
            _ = DwmSetWindowAttribute(form.Handle, 20, ref dark, 4);
        }
        control.Invalidate(true);
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    public static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        int diameter = radius * 2;
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class FieldFrame : Panel
{
    public FieldFrame() { DoubleBuffered = true; BackColor = Theme.Background; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        CaptureChrome.DrawSurface(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1),
            Math.Max(1, (int)(6 * DeviceDpi / 96f)), Tag as string == "field" ? Theme.Field : Theme.Surface);
    }
}

internal static class CaptureChrome
{
    public static Color Background => Theme.Surface;
    public static Color Hover => Theme.IsDark ? Color.FromArgb(58, 58, 63) : Color.FromArgb(233, 231, 242);
    public static Color Selected => Theme.IsDark ? Color.FromArgb(49, 47, 60) : Color.FromArgb(242, 239, 252);
    public static Color Border => Theme.Border;
    public static Color Text => Theme.Ink;
    public static Color Muted => Theme.Muted;
    public static Color Focus => Theme.Accent;

    public static void DrawSurface(Graphics graphics, Rectangle bounds, int radius, Color? fill = null, Color? border = null)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundedRectangle(bounds, radius);
        using var brush = new SolidBrush(fill ?? Background);
        graphics.FillPath(brush, path);
        if (border is Color line)
        {
            using var pen = new Pen(line);
            graphics.DrawPath(pen, path);
        }
    }
}

internal sealed class Card : Panel
{
    public Card()
    {
        DoubleBuffered = true;
        BackColor = Theme.Background;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), 12);
        using var brush = new SolidBrush(Theme.Surface);
        using var pen = new Pen(Theme.Border);
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(pen, path);
    }
}

internal sealed class ActionButton : Button
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Primary { get; init; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected { get; set; }

    public ActionButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        bool hover = ClientRectangle.Contains(PointToClient(Cursor.Position));
        Color background = Primary ? (hover ? Color.FromArgb(76, 60, 177) : Theme.Primary)
            : (Selected ? CaptureChrome.Selected : hover ? CaptureChrome.Hover : Theme.Surface);
        if (!Enabled) background = Theme.Field;
        using var path = Theme.RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), 8);
        using var brush = new SolidBrush(background);
        e.Graphics.FillPath(brush, path);
        if (!Primary)
        {
            using var border = new Pen(Theme.Border);
            e.Graphics.DrawPath(border, path);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, !Enabled ? Theme.Muted : Primary ? Color.White : Selected ? Theme.Accent : Theme.Ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (Focused && ShowFocusCues)
        {
            using var focusPen = new Pen(Primary ? Color.FromArgb(222, 215, 255) : Theme.Accent, 2);
            using var focusPath = Theme.RoundedRectangle(Rectangle.Inflate(ClientRectangle, -4, -4), 5);
            e.Graphics.DrawPath(focusPen, focusPath);
        }
    }
}

internal sealed class HotkeyBox : TextBox
{
    public Hotkey Value { get; private set; }
    public event Action? ValueChanged;

    public HotkeyBox(Hotkey hotkey)
    {
        Value = hotkey;
        Text = hotkey.ToString();
        ReadOnly = true;
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Field;
        ForeColor = Theme.Accent;
        TextAlign = HorizontalAlignment.Center;
        Font = Theme.MakeFont(9, true);
        Cursor = Cursors.Hand;
        ShortcutsEnabled = false;
    }

    public void SetHotkey(Hotkey hotkey)
    {
        Value = hotkey;
        Text = hotkey.ToString();
        Select(0, 0);
        ValueChanged?.Invoke();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        if (key is Keys.Tab or Keys.Escape) return base.ProcessCmdKey(ref msg, keyData);
        HotkeyModifiers modifiers = HotkeyModifiers.None;
        if (keyData.HasFlag(Keys.Control)) modifiers |= HotkeyModifiers.Control;
        if (keyData.HasFlag(Keys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (keyData.HasFlag(Keys.Shift)) modifiers |= HotkeyModifiers.Shift;
        var value = new Hotkey(key, modifiers);
        if (value.IsValid)
        {
            SetHotkey(value);
        }
        return true;
    }
}

