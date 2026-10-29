namespace SnippingToolPlus;

internal sealed class TextActionsForm : Form
{
    private readonly Bitmap image;
    private readonly Icon windowIcon;
    private readonly CancellationTokenSource recognitionCancellation = new();
    private readonly TextRecognitionCanvas preview;
    private readonly TextBox editor;
    private readonly ActionButton copyAll, copySelected;
    private readonly CheckBox removeLineBreaks;
    private readonly Label status;
    private readonly ProgressBar progress;
    private readonly TextRecognitionResult? initialResult;
    private TextRecognitionResult? result;
    private bool copying, resourcesDisposed;
    public event Action? NewCaptureRequested;
    internal TextRecognitionCanvas Preview => preview;
    internal TextBox Editor => editor;

    public TextActionsForm(Bitmap image, TextRecognitionResult? initialResult = null)
    {
        SuspendLayout();
        this.image = (Bitmap)image.Clone();
        this.initialResult = initialResult;
        Text = "Snipping Tool+ — Text actions";
        AccessibleName = Text;
        windowIcon = AppIcons.LoadWindow(); Icon = windowIcon;
        Font = Theme.MakeFont();
        ClientSize = new Size(1020, 680);
        MinimumSize = new Size(800, 540);
        StartPosition = FormStartPosition.CenterScreen;

        KeyPreview = true;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(20), BackColor = Theme.Background };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        var header = new Panel { Dock = DockStyle.Fill };
        header.Controls.Add(new Label { Text = "Text actions", Font = Theme.MakeFont(17, true), AutoSize = true, Location = new Point(0, 0) });
        header.Controls.Add(new Label { Text = "Select words in your capture, or edit the extracted text.", AutoSize = true,
            ForeColor = Theme.Muted, Tag = "muted", Location = new Point(1, 36) });
        layout.Controls.Add(header, 0, 0);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        copyAll = new ActionButton { Text = "Copy all text", Primary = true, Size = new Size(144, 36), Enabled = false, Margin = new Padding(0, 0, 8, 0) };
        copySelected = new ActionButton { Text = "Copy selected", Size = new Size(132, 36), Enabled = false, Margin = new Padding(0, 0, 16, 0) };
        removeLineBreaks = new CheckBox { Text = "Remove line breaks", AutoSize = true, Margin = new Padding(0, 9, 0, 0) };
        toolbar.Controls.AddRange([copyAll, copySelected, removeLineBreaks]);
        layout.Controls.Add(toolbar, 0, 1);

        var contents = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        contents.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        contents.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63));
        contents.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
        preview = new TextRecognitionCanvas(this.image) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0) };
        contents.Controls.Add(preview, 0, 0);
        var textCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(14), Margin = Padding.Empty };
        editor = new TextBox { Multiline = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical,
            Font = Theme.MakeFont(11), AccessibleName = "Extracted text, editable", ReadOnly = true, BackColor = Theme.Surface, ForeColor = Theme.Ink };
        textCard.Controls.Add(editor);
        contents.Controls.Add(textCard, 1, 0);
        layout.Controls.Add(contents, 0, 2);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 10, 0, 0) };
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        var feedback = new Panel { Dock = DockStyle.Fill };
        status = new Label { Text = "Recognizing text…", AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Tag = "muted" };
        progress = new ProgressBar { Dock = DockStyle.Bottom, Height = 3, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 24 };
        feedback.Controls.Add(status); feedback.Controls.Add(progress);
        var newCapture = new ActionButton { Text = "New region", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };
        newCapture.Click += (_, _) => { Close(); NewCaptureRequested?.Invoke(); };
        footer.Controls.Add(feedback, 0, 0); footer.Controls.Add(newCapture, 1, 0);
        layout.Controls.Add(footer, 0, 3);
        Controls.Add(layout);
        copyAll.Click += async (_, _) => await CopyAsync(false);
        copySelected.Click += async (_, _) => await CopyAsync(true);
        editor.TextChanged += (_, _) => RefreshCopyButtons();
        editor.MouseUp += (_, _) => RefreshCopyButtons();
        editor.KeyUp += (_, _) => RefreshCopyButtons();
        preview.SelectionChanged += () => { editor.Select(0, 0); RefreshCopyButtons(); };
        Shown += async (_, _) => await RecognizeAsync();
        Theme.Apply(this);
        editor.BackColor = Theme.Surface;
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(true);
    }

    private async Task RecognizeAsync()
    {
        try
        {
            var recognized = initialResult ?? await TextRecognition.RecognizeAsync(image, recognitionCancellation.Token);
            if (IsDisposed || Disposing || recognitionCancellation.IsCancellationRequested) return;
            result = recognized;
            preview.SetResult(recognized);
            editor.ReadOnly = false; editor.Text = recognized.Text;
            status.Text = recognized.Words.Count == 0 ? "No text found. Try a clearer or larger region."
                : recognized.Words.Count + " words · " + recognized.Language + " · On device";
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!IsDisposed && !Disposing)
            {
                status.Text = "Text recognition is unavailable";
                editor.Text = error.Message;
            }
        }
        finally { if (!IsDisposed && !Disposing) { progress.Visible = false; RefreshCopyButtons(); } }
    }

    private void RefreshCopyButtons()
    {
        copyAll.Enabled = !copying && result != null && !string.IsNullOrWhiteSpace(editor.Text);
        copySelected.Enabled = !copying && result != null && (editor.SelectionLength > 0 || preview.SelectedIndices.Count > 0);
    }

    internal string CopyText(bool selected)
    {
        string text = selected ? editor.SelectionLength > 0 ? editor.SelectedText
            : result?.SelectedText(preview.SelectedIndices) ?? "" : editor.Text;
        return removeLineBreaks.Checked ? TextRecognitionResult.WithoutLineBreaks(text) : text;
    }

    private async Task CopyAsync(bool selected)
    {
        string text = CopyText(selected);
        if (string.IsNullOrWhiteSpace(text) || copying) return;
        copying = true; RefreshCopyButtons();
        try
        {
            // Retry briefly without blocking the UI or replacing the clipboard with an empty result.
            for (int attempt = 0; ; attempt++)
            {
                try { Clipboard.SetDataObject(text, true, 0, 0); break; }
                catch (System.Runtime.InteropServices.ExternalException) when (attempt < 5)
                { await Task.Delay(60, recognitionCancellation.Token); }
            }
            if (!IsDisposed) status.Text = selected ? "Selected text copied" : "All text copied";
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!IsDisposed) status.Text = "The clipboard is busy. Try copying again."; }
        finally { copying = false; if (!IsDisposed) RefreshCopyButtons(); }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        if (keyData == (Keys.Control | Keys.C) && preview.Focused)
        { _ = CopyAsync(preview.SelectedIndices.Count > 0); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    { base.OnFormClosing(e); if (!e.Cancel) recognitionCancellation.Cancel(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true;
            recognitionCancellation.Cancel(); base.Dispose(true);
            recognitionCancellation.Dispose(); image.Dispose(); windowIcon.Dispose();
        }
        else base.Dispose(disposing);
    }
}
