namespace SnippingToolPlus;

internal sealed class CaptureNotice : Form
{
    private readonly string message;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 750 };
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var parameters = base.CreateParams; parameters.ExStyle |= 0x08000000 | 0x80; return parameters; }
    }
    public CaptureNotice(string message, Rectangle selection)
    {
        this.message = message;
        Font = Theme.MakeFont();
        FormBorderStyle = FormBorderStyle.None; TopMost = true; ShowInTaskbar = false;
        Enabled = false;
        SetStyle(ControlStyles.Selectable, false);
        StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
        var textSize = TextRenderer.MeasureText(message, Font);
        Size = new Size(textSize.Width + 34, textSize.Height + 22);
        BackColor = Theme.Surface; ForeColor = Theme.Ink; DoubleBuffered = true;
        Rectangle workArea = Screen.FromRectangle(selection).WorkingArea;
        int y = selection.Bottom + 10;
        if (y + Height > workArea.Bottom) y = selection.Top - Height - 10;
        Location = new Point(Math.Clamp(selection.Right - Width, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width)),
            Math.Clamp(y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height)));
        timer.Tick += (_, _) => Close();
        Shown += (_, _) => timer.Start();
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int rounding = 2, noBorder = unchecked((int)0xfffffffe);
        _ = SetAttribute(Handle, 33, ref rounding, 4); _ = SetAttribute(Handle, 34, ref noBorder, 4);
    }
    protected override void OnPaint(PaintEventArgs e) => TextRenderer.DrawText(e.Graphics, message, Font, ClientRectangle,
        Theme.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    [System.Runtime.InteropServices.DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static extern int SetAttribute(IntPtr window, int attribute, ref int value, int size);
    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
}
