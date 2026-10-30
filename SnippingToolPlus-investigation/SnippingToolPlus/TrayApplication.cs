using System.Diagnostics;

namespace SnippingToolPlus;

internal sealed class TrayApplication : ApplicationContext
{
    private readonly SettingsStore store = new(AppContext.BaseDirectory);
    private readonly HotkeyWindow hotkeys;
    private readonly NotifyIcon tray;
    private Icon trayIcon;
    private readonly ContextMenuStrip menu;
    private readonly CaptureMemory memory = new();
    private RegionMemory regions;
    private AppSettings settings;
    private SettingsForm? settingsForm;
    private CaptureNotice? confirmation;
    private TextActionsForm? textActions;
    private ScreenTextOverlay? screenText;
    private bool busy;
    private string? startupMessage;
    private Rectangle lastSelection = Rectangle.Empty;

    public TrayApplication(string messageName)
    {
        Theme.Refresh();
        try { settings = store.Load(); }
        catch (Exception exception) { settings = new AppSettings(); startupMessage = "Couldn't load settings. " + exception.Message; }
        regions = new RegionMemory(settings.RememberRegions ? AppContext.BaseDirectory : null);
        hotkeys = new HotkeyWindow(messageName);
        hotkeys.ActionRequested += action =>
        {
            var shortcut = settings.Shortcuts().FirstOrDefault(pair => pair.Key == action).Value;
            if (settingsForm is { IsDisposed: false } && settingsForm.RecordActiveShortcut(shortcut)) return;
            Execute(action);
        };
        hotkeys.SettingsRequested += ShowSettings;
        hotkeys.QuitRequested += ExitThread;
        hotkeys.ThemeChanged += RefreshTheme;
        try { hotkeys.Apply(settings); } catch (Exception exception) { startupMessage = exception.Message; }
        menu = new ContextMenuStrip { Font = Theme.MakeFont(), Renderer = new ThemeMenuRenderer() };
        foreach (AppAction action in Enum.GetValues<AppAction>())
        {
            var entry = new ToolStripMenuItem(ActionNames.Label(action)) { Tag = action };
            entry.Click += (_, _) => Execute(action); menu.Items.Add(entry);
            if (action is AppAction.SaveSnip or AppAction.CopyPath) menu.Items.Add(new ToolStripSeparator());
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add("Open screenshot folder", null, (_, _) => OpenSaveFolder());
        menu.Items.Add("Quit Snipping Tool+", null, (_, _) => ExitThread());
        menu.Opening += (_, _) =>
        {
            Theme.Refresh(); menu.BackColor = Theme.Surface; menu.ForeColor = Theme.Ink;
            foreach (ToolStripItem item in menu.Items)
            {
                if (item.Tag is AppAction action)
                {
                    item.Enabled = UnavailableReason(action) == null;
                    string? keys = settings.Shortcuts().Where(pair => pair.Key == action).Select(pair => pair.Value.ToString()).FirstOrDefault();
                    ((ToolStripMenuItem)item).ShortcutKeyDisplayString = keys ?? "";
                    item.ToolTipText = action is AppAction.CopyAsFile or AppAction.CopyPath ? "Saves to your screenshot folder first, if needed." : "";
                }
            }
        };
        menu.ShowItemToolTips = true;
        trayIcon = AppIcons.Load(AppIcons.TrayPixels);
        tray = new NotifyIcon { Icon = trayIcon, Text = "Snipping Tool+", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowSettings();
        Application.Idle += FirstIdle;
    }
    private void RefreshTheme()
    {
        Theme.Refresh();
        var nextIcon = AppIcons.Load(AppIcons.TrayPixels);
        tray.Icon = nextIcon;
        trayIcon.Dispose();
        trayIcon = nextIcon;
        if (settingsForm is { IsDisposed: false }) settingsForm.RefreshTheme();
        foreach (Form form in Application.OpenForms) { form.Invalidate(true); }
        menu.Invalidate(true);
    }
    private void FirstIdle(object? sender, EventArgs e)
    {
        Application.Idle -= FirstIdle;
        if (!Environment.GetCommandLineArgs().Contains("--background") || startupMessage != null) ShowSettings();
    }
    private void ShowSettings()
    {
        if (busy) return;
        if (settingsForm is { IsDisposed: false })
        { settingsForm.WindowState = FormWindowState.Normal; settingsForm.Show(); settingsForm.Activate(); return; }
        settingsForm = new SettingsForm(settings, ApplySettings, startupMessage, Execute, UnavailableReason);
        startupMessage = null; settingsForm.Show();
    }
    private string? ApplySettings(AppSettings proposed, bool startWithWindows)
    {
        AppSettings previous = settings;
        bool previousStartup;
        try { previousStartup = StartupManager.IsEnabled(); } catch (Exception error) { return error.Message; }
        bool applied = false, written = false;
        try
        {
            proposed.Validate(); hotkeys.Apply(proposed); applied = true;
            store.Save(proposed); written = true; StartupManager.SetEnabled(startWithWindows);
            settings = proposed;
            if (previous.RememberRegions != proposed.RememberRegions) regions = new RegionMemory(proposed.RememberRegions ? AppContext.BaseDirectory : null);
            return null;
        }
        catch (Exception exception)
        {
            var errors = new List<string> { exception.Message };
            if (applied) { try { hotkeys.Apply(previous); } catch (Exception error) { errors.Add(error.Message); } }
            if (written) { try { store.Save(previous); } catch { errors.Add("Couldn't restore saved settings."); } }
            try { StartupManager.SetEnabled(previousStartup); } catch { errors.Add("Couldn't restore the startup preference."); }
            return string.Join(" ", errors);
        }
    }
    private async void Execute(AppAction action)
    {
        if (busy) return;
        busy = true;
        try
        {
            switch (action)
            {
                case AppAction.ExtractText: await Capture(CaptureMode.Text, settings.SaveFolder, false); break;
                case AppAction.TextActions:
                    if (memory.Image == null) throw new InvalidOperationException("Take a screenshot first.");
                    ShowTextActions(memory.Image); break;
                case AppAction.CopySnip: await Capture(CaptureMode.Copy, settings.SaveFolder, false); break;
                case AppAction.SaveSnip: await Capture(CaptureMode.Save, settings.SaveFolder, false); break;
                case AppAction.SaveProject:
                    if (string.IsNullOrWhiteSpace(settings.ProjectFolder)) throw new InvalidOperationException("Choose a project folder in Settings → Folders first.");
                    await Capture(CaptureMode.Save, settings.ProjectFolder, false); break;
                case AppAction.RepeatRegion: await Capture(CaptureMode.Copy, settings.SaveFolder, true); break;
                case AppAction.SaveLast:
                    memory.EnsureSaved(settings.SaveFolder); Confirm("Saved"); break;
                case AppAction.CopyAsFile:
                    memory.CopyAsFile(settings.SaveFolder); Confirm("File copied"); break;
                case AppAction.CopyPath:
                    Clipboard.SetText(memory.EnsureSaved(settings.SaveFolder)); Confirm("Path copied"); break;
                case AppAction.RevealLast:
                    if (memory.LastSavedPath == null || !File.Exists(memory.LastSavedPath)) throw new InvalidOperationException("No saved screenshot is available yet.");
                    var reveal = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                    reveal.ArgumentList.Add("/select," + memory.LastSavedPath); Process.Start(reveal); break;
            }
        }
        catch (Exception exception)
        { MessageBox.Show(exception.Message, "Snipping Tool+", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally
        {
            busy = false;
            if (settingsForm is { IsDisposed: false }) settingsForm.RefreshActions();
        }
    }
    private string? UnavailableReason(AppAction action)
    {
        if (busy) return "Finish the current action first.";
        if (action is AppAction.SaveLast or AppAction.CopyAsFile or AppAction.CopyPath or AppAction.TextActions && memory.Image == null)
            return "Take a screenshot first.";
        if (action == AppAction.RevealLast && (memory.LastSavedPath == null || !File.Exists(memory.LastSavedPath)))
            return "Save a screenshot first.";
        if (action == AppAction.SaveProject && string.IsNullOrWhiteSpace(settings.ProjectFolder))
            return "Choose a project folder under Folders, then save settings.";
        if (action == AppAction.RepeatRegion)
        {
            Screen screen = Screen.FromPoint(Cursor.Position);
            if (regions.Recall(screen.DeviceName, screen.Bounds, SystemInformation.VirtualScreen) == null)
                return "Capture a region on this monitor first.";
        }
        return null;
    }
    private async Task Capture(CaptureMode requestedMode, string directory, bool repeat)
    {
        DismissConfirmation();
        textActions?.Close();
        screenText?.Close();
        bool restoreSettings = settingsForm is { IsDisposed: false, Visible: true };
        settingsForm?.Hide();
        try
        {
            await Task.Delay(120);
            Rectangle desktopBounds = SystemInformation.VirtualScreen;
            Screen monitor = Screen.FromPoint(Cursor.Position);
            var remembered = regions.Recall(monitor.DeviceName, monitor.Bounds, desktopBounds);
            using var desktop = CaptureService.FreezeDesktop(desktopBounds);
            Bitmap image;
            Rectangle screenSelection;
            CaptureShape shape;
            Point[] polygon;
            CaptureMode mode = requestedMode;
            if (repeat)
            {
                if (remembered == null) throw new InvalidOperationException("Capture a region on this monitor first.");
                screenSelection = remembered.OnMonitor(monitor.Bounds);
                var local = new Rectangle(screenSelection.X - desktopBounds.X, screenSelection.Y - desktopBounds.Y, screenSelection.Width, screenSelection.Height);
                shape = remembered.Shape; polygon = remembered.Polygon;
                image = shape == CaptureShape.Freeform
                    ? CaptureService.CropFreeform(desktop, polygon.Select(p => new Point(p.X + local.X, p.Y + local.Y)).ToArray(), local)
                    : CaptureService.Crop(desktop, local);
            }
            else
            {
                Rectangle? initial = null;
                if (settings.RememberRegions && remembered?.Shape == CaptureShape.Rectangle && settings.Shape == CaptureShape.Rectangle)
                {
                    Rectangle rectangle = remembered.OnMonitor(monitor.Bounds);
                    initial = new Rectangle(rectangle.X - desktopBounds.X, rectangle.Y - desktopBounds.Y, rectangle.Width, rectangle.Height);
                }
                using var overlay = new SelectionOverlay(desktop, desktopBounds, mode, settings.Shape, settings, initial);
                // A timed confirmation must never become the modal overlay's owner.
                if (overlay.ShowDialog(hotkeys) != DialogResult.OK) return;
                image = overlay.CreateImage(); mode = overlay.DestinationMode; shape = overlay.Shape; polygon = overlay.Polygon;
                screenSelection = new Rectangle(overlay.Selection.X + desktopBounds.X, overlay.Selection.Y + desktopBounds.Y, overlay.Selection.Width, overlay.Selection.Height);
            }
            using (image)
            {
                lastSelection = screenSelection;
                memory.Replace(image, null);
                if (mode == CaptureMode.Text)
                {
                    restoreSettings = false;
                    ShowScreenText(desktop, desktopBounds, screenSelection);
                    return;
                }
                var result = await CaptureService.DeliverAsync(image, mode, directory);
                memory.MarkSaved(result.SavedPath);
                Screen selectedMonitor = Screen.FromRectangle(screenSelection);
                regions.Remember(selectedMonitor.DeviceName, selectedMonitor.Bounds, screenSelection, shape, polygon);
                if (result.Copied) Confirm(result.SavedPath == null ? "Copied" : "Saved + copied");
                else MessageBox.Show(result.SavedPath == null ? "The clipboard is busy. Your capture is retained; try Copy last as file from the tray."
                    : "The PNG was saved, but the clipboard is busy.", "Snipping Tool+", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally { if (restoreSettings && settingsForm is { IsDisposed: false }) settingsForm.Show(); }
    }
    private void ShowScreenText(Bitmap desktop, Rectangle desktopBounds, Rectangle screenSelection)
    {
        screenText?.Close();
        var window = new ScreenTextOverlay(desktop, desktopBounds, screenSelection);
        screenText = window;
        window.FormClosed += (_, _) => { if (ReferenceEquals(screenText, window)) screenText = null; };
        window.NewCaptureRequested += () => Execute(AppAction.ExtractText);
        window.OpenInAppRequested += (image, result) => ShowTextActions(image, result);
        window.ImageChanged += (image, bounds) => { memory.Replace(image, null); lastSelection = bounds; };
        window.Show(hotkeys);
    }
    private void ShowTextActions(Bitmap image, TextRecognitionResult? result = null)
    {
        DismissConfirmation();
        screenText?.Close();
        textActions?.Close();
        var window = new TextActionsForm(image, result);
        textActions = window;
        window.FormClosed += (_, _) => { if (ReferenceEquals(textActions, window)) textActions = null; };
        window.NewCaptureRequested += () => Execute(AppAction.ExtractText);
        window.Show();
    }
    private void Confirm(string text)
    {
        DismissConfirmation();
        if (!settings.ShowConfirmation) return;
        Rectangle location = lastSelection.IsEmpty ? new Rectangle(Cursor.Position, new Size(1, 1)) : lastSelection;
        var notice = new CaptureNotice(text, location);
        confirmation = notice;
        notice.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(confirmation, notice)) confirmation = null;
            notice.Dispose();
        };
        notice.Show();
    }
    private void DismissConfirmation()
    {
        var notice = confirmation;
        confirmation = null;
        notice?.Close();
    }
    private void OpenSaveFolder()
    {
        try { Directory.CreateDirectory(settings.SaveFolder); Process.Start(new ProcessStartInfo(settings.SaveFolder) { UseShellExecute = true }); }
        catch (Exception exception) { MessageBox.Show(exception.Message, "Snipping Tool+"); }
    }
    protected override void ExitThreadCore() { screenText?.Close(); textActions?.Close(); DismissConfirmation(); tray.Visible = false; settingsForm?.Close(); base.ExitThreadCore(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        { Application.Idle -= FirstIdle; screenText?.Dispose(); textActions?.Dispose(); DismissConfirmation(); settingsForm?.Dispose(); tray.Visible = false; tray.Dispose(); trayIcon.Dispose(); menu.Dispose(); hotkeys.Dispose(); memory.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed class ThemeMenuRenderer : ToolStripProfessionalRenderer
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(Theme.Surface);
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected) return;
        using var brush = new SolidBrush(CaptureChrome.Hover); e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    { e.TextColor = e.Item.Enabled ? Theme.Ink : Theme.Muted; base.OnRenderItemText(e); }
}
