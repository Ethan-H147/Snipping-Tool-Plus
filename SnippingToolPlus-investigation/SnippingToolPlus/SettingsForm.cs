namespace SnippingToolPlus;

internal sealed class SettingsForm : Form
{
    private readonly AppSettings original;
    private readonly Func<AppSettings, bool, string?> apply;
    private readonly Action<AppAction>? runAction;
    private readonly Func<AppAction, string?>? availability;
    private readonly Dictionary<AppAction, ActionButton> actionButtons = new();
    private readonly Icon windowIcon;
    internal IReadOnlyDictionary<AppAction, ActionButton> ActionButtons => actionButtons;
    private readonly ToolTip tips = new();
    private readonly Dictionary<AppAction, HotkeyBox> hotkeyBoxes = new();
    private readonly Dictionary<AppAction, CheckBox> optionalActions = new();
    private readonly List<Panel> pages = [];
    private readonly List<ActionButton> tabs = [];
    private readonly ShapePicker shape;
    private readonly TextBox folder;
    private readonly TextBox projectFolder;
    private readonly CheckBox startup;
    private readonly CheckBox remember;
    private readonly CheckBox confirmation;
    private readonly CheckBox exact;
    private readonly TextBox captureWidth;
    private readonly TextBox captureHeight;
    private readonly Label status;
    private readonly ActionButton save;
    private bool ready;
    private int currentPage;

    public SettingsForm(AppSettings settings, Func<AppSettings, bool, string?> apply, string? initialMessage,
        Action<AppAction>? runAction = null, Func<AppAction, string?>? availability = null)
    {
        SuspendLayout();
        original = settings;
        this.apply = apply;
        this.runAction = runAction;
        this.availability = availability;
        Text = "Snipping Tool+";
        windowIcon = AppIcons.LoadWindow();
        Icon = windowIcon;
        Font = Theme.MakeFont();
        ClientSize = new Size(660, 598);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Ink;

        string[] names = ["Capture", "Shortcuts", "Folders"];
        for (int index = 0; index < names.Length; index++)
        {
            int pageIndex = index;
            var tab = new ActionButton { Text = names[index], Bounds = new Rectangle(24 + index * 206, 22, 200, 40), TabIndex = index, Tag = "navigation" };
            tab.Click += (_, _) => ShowPage(pageIndex);
            tabs.Add(tab); Controls.Add(tab);
            var page = new Panel { Bounds = new Rectangle(24, 82, 612, 416), AutoScroll = true, TabIndex = 3 + index, Visible = index == 0 };
            pages.Add(page); Controls.Add(page);
        }

        var capture = pages[0];
        LabelAt(capture, "Capture mode", 4, 10, 280, 30, true);
        shape = new ShapePicker { Bounds = new Rectangle(324, 2, 284, 44), SelectedIndex = (int)settings.Shape };
        shape.SelectedIndexChanged += (_, _) => Changed(); capture.Controls.Add(shape);
        Divider(capture, 65);
        exact = Check(capture, "Use an exact rectangle size", 4, 87, settings.ExactSize);
        captureWidth = Number(capture, settings.CaptureWidth, 28, 130, "Capture width in pixels");
        LabelAt(capture, "×", 158, 133, 32, 30);
        captureHeight = Number(capture, settings.CaptureHeight, 194, 130, "Capture height in pixels");
        LabelAt(capture, "pixels", 329, 133, 100, 30);
        tips.SetToolTip(exact, "The dimensions are physical image pixels. Position the rectangle and click to capture.");
        exact.CheckedChanged += (_, _) => { captureWidth.Enabled = captureHeight.Enabled = exact.Checked; Changed(); };
        captureWidth.Enabled = captureHeight.Enabled = exact.Checked;
        remember = Check(capture, "Remember the last region on each monitor", 4, 196, settings.RememberRegions);
        confirmation = Check(capture, "Show a brief capture confirmation", 4, 242, settings.ShowConfirmation);
        startup = Check(capture, "Start with Windows", 4, 288, StartupManager.IsEnabled());
        LabelAt(capture, "Hold Alt when completing a capture to switch copy / save.", 4, 350, 596, 40, false, true);

        var shortcuts = pages[1];
        shortcuts.AutoScroll = true;
        int row = 0;
        foreach (AppAction action in Enum.GetValues<AppAction>())
        {
            int y = row++ * 49;
            bool required = action is AppAction.CopySnip or AppAction.SaveSnip;
            bool enabled = required || settings.ExtraHotkeys.ContainsKey(action);
            Hotkey value = action switch
            {
                AppAction.CopySnip => settings.CopyHotkey,
                AppAction.SaveSnip => settings.SaveHotkey,
                _ => settings.ExtraHotkeys.GetValueOrDefault(action,
                    new Hotkey(Keys.F1 + ((int)action - 2), HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift))
            };
            if (required) LabelAt(shortcuts, ActionNames.Label(action), 6, y + 9, 254, 30, true);
            else
            {
                var enabledBox = Check(shortcuts, ActionNames.Label(action), 6, y + 9, enabled);
                optionalActions[action] = enabledBox;
                enabledBox.CheckedChanged += (_, _) => { hotkeyBoxes[action].Enabled = enabledBox.Checked; Changed(); };
                if (action is AppAction.CopyAsFile or AppAction.CopyPath)
                    tips.SetToolTip(enabledBox, "Saves the last capture to your normal folder first, if it hasn't been saved.");
            }
            var frame = new FieldFrame { Bounds = new Rectangle(258, y + 4, 226, 38), Tag = "field" };
            var input = new HotkeyBox(value) { Bounds = new Rectangle(6, 9, 214, 24), Enabled = enabled,
                AccessibleName = ActionNames.Label(action) + " shortcut" };
            hotkeyBoxes[action] = input;
            frame.Controls.Add(input); shortcuts.Controls.Add(frame);
            input.ValueChanged += Changed;
            tips.SetToolTip(input, "Click, then press Ctrl or Alt with your chosen key. Changes apply when you save.");
            var run = new ActionButton { Text = ActionNames.ButtonLabel(action),
                Bounds = new Rectangle(494, y + 4, 90, 38), AccessibleName = "Run " + ActionNames.Label(action), Tag = action };
            run.Click += (_, _) => RunAction(action);
            actionButtons[action] = run;
            shortcuts.Controls.Add(run);
            Divider(shortcuts, y + 48);
        }
        shortcuts.AutoScrollMinSize = new Size(0, row * 49 + 4);

        var folders = pages[2];
        LabelAt(folders, "Screenshot folder", 4, 4, 590, 30, true);
        folder = FolderField(folders, settings.SaveFolder, 46, "Screenshot folder");
        LabelAt(folders, "Project folder", 4, 116, 590, 30, true);
        projectFolder = FolderField(folders, settings.ProjectFolder, 158, "Project folder");
        LabelAt(folders, "The project shortcut saves here and also copies the image.", 4, 219, 590, 42, false, true);

        status = LabelAt(this, initialMessage ?? "", 28, 510, 604, 40, false, true);
        status.AccessibleName = "Settings status";
        if (initialMessage != null) status.ForeColor = Color.IndianRed;
        save = new ActionButton { Text = "Save settings", Primary = true, Bounds = new Rectangle(468, 548, 168, 40), TabIndex = 10 };
        save.Click += (_, _) => SaveSettings(); Controls.Add(save); AcceptButton = save;
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(true);
        ready = true;
        Shown += (_, _) => { Theme.Apply(this); PaintTabState(); RefreshActions(); ActiveControl = tabs[0]; };
        Activated += (_, _) => RefreshActions();
        Theme.Apply(this); ShowPage(0);
        RefreshActions();
    }

    private Label LabelAt(Control parent, string text, int x, int y, int w, int h, bool bold = false, bool muted = false)
    {
        var label = new Label { Text = text, Bounds = new Rectangle(x, y, w, h), Font = Theme.MakeFont(10, bold),
            ForeColor = muted ? Theme.Muted : Theme.Ink, BackColor = Color.Transparent, Tag = muted ? "muted" : null };
        parent.Controls.Add(label); return label;
    }
    private void Divider(Control parent, int y) => parent.Controls.Add(new Panel { Bounds = new Rectangle(4, y, 580, 1), BackColor = Theme.Border, Tag = "divider" });
    private CheckBox Check(Control parent, string text, int x, int y, bool value)
    {
        var check = new CheckBox { Text = text, Location = new Point(x, y), AutoSize = true, Checked = value, ForeColor = Theme.Ink };
        check.CheckedChanged += (_, _) => Changed(); parent.Controls.Add(check); return check;
    }
    private TextBox Number(Control parent, int value, int x, int y, string name)
    {
        var frame = new FieldFrame { Bounds = new Rectangle(x, y, 124, 38), Tag = "input" };
        var input = new TextBox { Bounds = new Rectangle(10, 8, 104, 24), Text = value.ToString(),
            AccessibleName = name, BorderStyle = BorderStyle.None, TextAlign = HorizontalAlignment.Center };
        input.TextChanged += (_, _) => Changed();
        input.KeyPress += (_, args) => { if (!char.IsControl(args.KeyChar) && !char.IsDigit(args.KeyChar)) args.Handled = true; };
        frame.Controls.Add(input); parent.Controls.Add(frame); return input;
    }
    private TextBox FolderField(Control parent, string value, int y, string name)
    {
        var frame = new FieldFrame { Bounds = new Rectangle(4, y, 470, 42), Tag = "input" };
        var input = new TextBox { Text = value, Bounds = new Rectangle(12, 10, 444, 26), BorderStyle = BorderStyle.None,
            AccessibleName = name, BackColor = Theme.Surface, ForeColor = Theme.Ink };
        frame.Controls.Add(input); parent.Controls.Add(frame);
        var browse = new ActionButton { Text = "Browse…", Bounds = new Rectangle(486, y, 116, 42), AccessibleName = "Browse " + name.ToLowerInvariant() };
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = name, UseDescriptionForTitle = true, SelectedPath = input.Text };
            if (dialog.ShowDialog(this) == DialogResult.OK) input.Text = dialog.SelectedPath;
        };
        input.TextChanged += (_, _) => Changed(); parent.Controls.Add(browse); return input;
    }
    private void Changed() { if (ready) { status.Text = "Unsaved changes"; status.ForeColor = Theme.Muted; } }
    private void ShowPage(int index)
    {
        currentPage = index;
        for (int i = 0; i < pages.Count; i++) pages[i].Visible = i == index;
        PaintTabState();
    }
    private void PaintTabState()
    {
        for (int i = 0; i < tabs.Count; i++) { tabs[i].Selected = i == currentPage; tabs[i].Invalidate(); }
    }
    public void RefreshTheme() { Theme.Apply(this); PaintTabState(); }
    public void RefreshActions()
    {
        foreach (var pair in actionButtons)
        {
            string? reason = availability?.Invoke(pair.Key);
            pair.Value.Enabled = runAction != null && reason == null;
            tips.SetToolTip(pair.Value, reason ?? (pair.Key is AppAction.CopyAsFile or AppAction.CopyPath
                ? "Saves to your screenshot folder first if needed. Uses saved settings."
                : "Run this action using your saved settings."));
            pair.Value.Invalidate();
        }
    }
    private void RunAction(AppAction action)
    {
        if (runAction == null) return;
        string? reason = availability?.Invoke(action);
        if (reason != null) { status.Text = reason; status.ForeColor = Theme.Muted; RefreshActions(); return; }
        runAction(action);
    }
    public bool RecordActiveShortcut(Hotkey value)
    {
        var active = hotkeyBoxes.Values.FirstOrDefault(input => input.Focused);
        if (active == null) return false;
        active.SetHotkey(value); return true;
    }
    internal void SaveSettings()
    {
        if (!int.TryParse(captureWidth.Text, out int width) || !int.TryParse(captureHeight.Text, out int height))
        { status.Text = "Enter a valid width and height in pixels."; status.ForeColor = Color.IndianRed; return; }
        var extras = optionalActions.Where(pair => pair.Value.Checked).ToDictionary(pair => pair.Key, pair => hotkeyBoxes[pair.Key].Value);
        var settings = original with { CopyHotkey = hotkeyBoxes[AppAction.CopySnip].Value, SaveHotkey = hotkeyBoxes[AppAction.SaveSnip].Value,
            ExtraHotkeys = extras, Shape = (CaptureShape)shape.SelectedIndex, SaveFolder = folder.Text.Trim(), ProjectFolder = projectFolder.Text.Trim(),
            ExactSize = exact.Checked, CaptureWidth = width, CaptureHeight = height,
            RememberRegions = remember.Checked, ShowConfirmation = confirmation.Checked };
        string? error = apply(settings, startup.Checked);
        if (error == null) Close();
        else { status.Text = error; status.ForeColor = Color.IndianRed; }
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg is 0x001a or 0x031a) { Theme.Refresh(); RefreshTheme(); }
        base.WndProc(ref message);
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void Dispose(bool disposing)
    { if (disposing) { tips.Dispose(); windowIcon.Dispose(); } base.Dispose(disposing); }
}
