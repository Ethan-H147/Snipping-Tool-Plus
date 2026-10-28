using System.Drawing.Imaging;
using System.Text.Json;
using QuietSnip;

internal static class Program
{
    private static int passed;
    private static string root = "";

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        root = Path.GetFullPath(args.Length > 0 ? args[0] : "test-results");
        Directory.CreateDirectory(root);
        try
        {
            if (args.Contains("--investigation-core"))
            {
                Run("Shortcut validation and formatting", TestShortcuts);
                Run("Settings persistence and invalid-file handling", TestSettings);
                Run("Selection geometry", TestSelection);
                Run("PNG pixels and filename collisions", TestImages);
                Run("Region persistence", TestRegionMemory);
                Run("Placement and destination override", TestPlacement);
                Run("Native icon frames", TestIcons);
                return 0;
            }
            if (args.Contains("--overlay-baseline"))
            {
                Run("Actual overlay menu and 100 cancel cycles", TestOverlayBaseline);
                return 0;
            }
            if (args.Contains("--ui-only"))
            {
                Run("Shortcut row action buttons", TestActionButtons);
                Run("Native-size icon frames and alpha edges", TestIcons);
                Run("Light/dark layout and capture menu", TestLayout);
                return 0;
            }
            if (args.Contains("--layout-only"))
            {
                Run("Settings layout at the desktop's display scale", TestLayout);
                return 0;
            }
            Run("Shortcut validation and formatting", TestShortcuts);
            Run("Settings persistence and invalid-file handling", TestSettings);
            Run("Selection geometry in all drag directions and at edges", TestSelection);
            Run("PNG pixel fidelity and filename collision handling", TestImages);
            Run("Freeform transparency and full-screen selection", TestCaptureShapes);
            Run("Copy-only writes no file; save mode writes and copies", TestDelivery);
            Run("Real desktop capture dimensions", TestDesktop);
            Run("Native hotkey conflicts preserve previous registrations", TestHotkeyRollback);
            Run("Last capture is retained once and saved lazily; file copy and path", TestCaptureMemory);
            Run("Monitor-specific regions survive restarts and reject changed sizes", TestRegionMemory);
            Run("Space reposition, arrow nudges, exact size, and Alt override", TestPlacement);
            Run("Optional shortcut registration and collision rollback", TestOptionalHotkeys);
            Run("Save closes settings on success; errors keep it open", TestSaveAndClose);
            Run("Capture confirmation is near the selection and dismisses itself", TestConfirmation);
            Run("Shortcut row buttons run each action independently of hotkey enablement", TestActionButtons);
            Run("Tray/taskbar icon contains native sizes and smooth alpha edges", TestIcons);
            Run("Settings layout at the desktop's display scale", TestLayout);
            Console.WriteLine($"PASS: {passed} checks");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    private static void Run(string name, Action test)
    {
        test();
        passed++;
        Console.WriteLine("PASS " + name);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private static void TestShortcuts()
    {
        Assert(new AppSettings().CopyHotkey.ToString() == "Ctrl + Alt + S", "Default shortcut");
        Assert(!new Hotkey(Keys.D, HotkeyModifiers.None).IsValid, "Plain typing must not become a global shortcut");
        Assert(!new Hotkey(Keys.ShiftKey, HotkeyModifiers.Control).IsValid, "Modifier-only shortcut accepted");
        Assert(!new Hotkey((Keys)900, HotkeyModifiers.Control).IsValid, "Unknown key accepted");
        Assert(!new Hotkey(Keys.F24, (HotkeyModifiers)8).IsValid, "Windows-key flag accepted");
        Assert(new Hotkey(Keys.F24, HotkeyModifiers.Control | HotkeyModifiers.Shift).IsValid, "Function shortcut rejected");
        Throws<ArgumentException>(() => new AppSettings { SaveHotkey = new AppSettings().CopyHotkey }.Validate());
    }

    private static void TestSettings()
    {
        var store = new SettingsStore(root);
        var settings = new AppSettings { SaveFolder = Path.Combine(root, "Screenshots"),
            CopyHotkey = new Hotkey(Keys.F12, HotkeyModifiers.Alt) };
        store.Save(settings);
        Assert(JsonSerializer.Serialize(store.Load()) == JsonSerializer.Serialize(settings), "Settings did not round-trip");
        Assert(!File.Exists(store.FilePath + ".tmp"), "Temporary settings file left behind");
        File.WriteAllText(store.FilePath, "{broken");
        Throws<JsonException>(() => store.Load());
        store.Save(settings);
        Throws<ArgumentException>(() => (settings with { SaveFolder = "relative" }).Validate());
        Throws<ArgumentException>(() => (settings with { SaveFolder = store.FilePath }).Validate());
    }

    private static void TestSelection()
    {
        var size = new Size(3840, 2160);
        var expected = new Rectangle(100, 200, 300, 500);
        Assert(CaptureService.SelectionBetween(new Point(400, 700), new Point(100, 200), size) == expected, "Reverse drag");
        Assert(CaptureService.SelectionBetween(new Point(100, 200), new Point(400, 700), size) == expected, "Forward drag");
        Assert(CaptureService.SelectionBetween(new Point(100, 700), new Point(400, 200), size) == expected, "Mixed drag");
        Assert(CaptureService.SelectionBetween(new Point(-10, -20), new Point(5000, 3000), size)
            == new Rectangle(Point.Empty, size), "Out-of-screen pointer not clamped");
        var virtualBounds = new Rectangle(-1920, -400, 3840, 2160);
        Point screenPoint = new(-1820, -200);
        var local = new Point(screenPoint.X - virtualBounds.X, screenPoint.Y - virtualBounds.Y);
        Assert(CaptureService.SelectionBetween(local, new Point(400, 700), virtualBounds.Size) == expected,
            "Negative desktop origin conversion");
    }

    private static void TestImages()
    {
        using var bitmap = new Bitmap(80, 60);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.Coral);
        bitmap.SetPixel(20, 10, Color.Blue);
        using var crop = CaptureService.Crop(bitmap, new Rectangle(20, 10, 30, 25));
        Assert(crop.Size == new Size(30, 25) && crop.GetPixel(0, 0).ToArgb() == Color.Blue.ToArgb(), "Crop lost pixels");
        Throws<ArgumentException>(() => CaptureService.Crop(bitmap, new Rectangle(-1, 0, 2, 2)));
        string images = Path.Combine(root, "png-tests");
        DateTime sameTime = new(2026, 10, 28, 12, 30, 0, 123);
        string first = CaptureService.SavePng(crop, images, sameTime);
        string second = CaptureService.SavePng(crop, images, sameTime);
        Assert(Path.GetFileName(first).StartsWith("Screenshot 2026-10-28 123000"), "Screenshot naming format incorrect");
        Assert(first != second && File.Exists(first), "Saved image overwritten");
        using var loaded = new Bitmap(second);
        Assert(loaded.RawFormat.Guid == ImageFormat.Png.Guid && loaded.Size == crop.Size, "PNG not readable");
        Assert(loaded.GetPixel(0, 0).ToArgb() == Color.Blue.ToArgb(), "PNG lost pixels");
    }

    private static void TestCaptureShapes()
    {
        using var desktop = new Bitmap(100, 100);
        using (var graphics = Graphics.FromImage(desktop)) graphics.Clear(Color.Coral);
        Point[] triangle = [new(10, 10), new(90, 10), new(10, 90)];
        using var cutout = CaptureService.CropFreeform(desktop, triangle, new Rectangle(10, 10, 81, 81));
        Assert(cutout.GetPixel(5, 5).ToArgb() == Color.Coral.ToArgb(), "Freeform interior changed color");
        Assert(cutout.GetPixel(75, 75).A == 0, "Freeform exterior is not transparent");
        string png = CaptureService.SavePng(cutout, Path.Combine(root, "freeform"));
        using var reloaded = new Bitmap(png);
        Assert(reloaded.GetPixel(75, 75).A == 0, "PNG lost freeform transparency");
        Rectangle bounds = SystemInformation.VirtualScreen;
        using var screen = CaptureService.FreezeDesktop(bounds);
        using var overlay = new SelectionOverlay(screen, bounds, CaptureMode.Copy, CaptureShape.FullScreen);
        Assert(overlay.ShowDialog() == DialogResult.OK, "Full-screen selection failed");
        using var full = overlay.CreateImage();
        Assert(full.Size == Screen.FromPoint(Cursor.Position).Bounds.Size, "Full-screen dimensions incorrect");
    }

    private static void TestDelivery()
    {
        using var bitmap = new Bitmap(42, 27);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.MediumPurple);
        string destination = Path.Combine(root, "delivery-" + Guid.NewGuid().ToString("N"));
        var copy = Wait(CaptureService.DeliverAsync(bitmap, CaptureMode.Copy, destination));
        Assert(copy.Copied && copy.SavedPath == null && !Directory.Exists(destination), "Copy mode created a folder or failed");
        using (var clip = Clipboard.GetImage())
            Assert(clip?.Size == bitmap.Size, "Clipboard image missing");
        var save = Wait(CaptureService.DeliverAsync(bitmap, CaptureMode.Save, destination));
        Assert(save.Copied && File.Exists(save.SavedPath) && Directory.GetFiles(destination).Length == 1, "Save mode failed");
        using var saved = new Bitmap(save.SavedPath!);
        Assert(saved.Size == bitmap.Size, "Saved dimensions incorrect");
    }

    private static T Wait<T>(Task<T> task)
    {
        while (!task.IsCompleted) { Application.DoEvents(); Thread.Sleep(10); }
        return task.GetAwaiter().GetResult();
    }

    private static void TestDesktop()
    {
        Rectangle bounds = SystemInformation.VirtualScreen;
        using var desktop = CaptureService.FreezeDesktop(bounds);
        Assert(desktop.Size == bounds.Size, "Desktop capture dimensions incorrect");
        Console.WriteLine($"  Desktop bounds: {bounds}");
    }

    private static void TestHotkeyRollback()
    {
        using var original = new HotkeyWindow("QuietSnip.Tests.Original");
        var combinations = new List<Hotkey>();
        for (Keys key = Keys.F13; key <= Keys.F24; key++)
        {
            var candidate = new Hotkey(key, HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift);
            if (NativeMethods.RegisterHotKey(IntPtr.Zero, 200, (uint)candidate.Modifiers, (uint)candidate.Key))
            {
                NativeMethods.UnregisterHotKey(IntPtr.Zero, 200);
                combinations.Add(candidate);
                if (combinations.Count == 5) break;
            }
        }
        Assert(combinations.Count == 5, "Not enough free test shortcuts");
        var old = new AppSettings { CopyHotkey = combinations[0], SaveHotkey = combinations[1] };
        original.Apply(old);
        using var other = new HotkeyWindow("QuietSnip.Tests.Other");
        other.Apply(old with { CopyHotkey = combinations[2], SaveHotkey = combinations[3] });
        Throws<InvalidOperationException>(() => original.Apply(old with { CopyHotkey = combinations[4], SaveHotkey = combinations[2] }));
        Assert(!NativeMethods.RegisterHotKey(IntPtr.Zero, 201, (uint)old.CopyHotkey.Modifiers, (uint)old.CopyHotkey.Key),
            "Original copy shortcut was not restored after a conflict");
        Assert(!NativeMethods.RegisterHotKey(IntPtr.Zero, 202, (uint)old.SaveHotkey.Modifiers, (uint)old.SaveHotkey.Key),
            "Original save shortcut was not restored after a conflict");
        Assert(NativeMethods.RegisterHotKey(IntPtr.Zero, 203, (uint)combinations[4].Modifiers, (uint)combinations[4].Key),
            "Partial new registration was leaked");
        NativeMethods.UnregisterHotKey(IntPtr.Zero, 203);
    }

    private static void TestLayout()
    {
        foreach (bool dark in new[] { false, true })
        {
            Theme.TestDark = dark;
            using var themed = new SettingsForm(new AppSettings(), (_, _) => null, null, _ => { });
            themed.Show(); Application.DoEvents(); themed.RefreshTheme();
            string mode = dark ? "dark" : "light";
            foreach (string page in new[] { "Capture", "Shortcuts", "Folders" })
            {
                Descendants(themed).OfType<ActionButton>().Single(b => b.Tag as string == "navigation" && b.Text == page).PerformClick();
                Application.DoEvents();
                using var render = new Bitmap(themed.Width, themed.Height);
                themed.DrawToBitmap(render, new Rectangle(Point.Empty, render.Size));
                render.Save(Path.Combine(root, $"settings-{mode}-{page.ToLowerInvariant()}.png"), ImageFormat.Png);
            }
            foreach (Control item in Descendants(themed))
                Assert(item.Font.FontFamily.Name.StartsWith("Segoe UI Variable"), $"Wrong font: {item.GetType().Name} {item.Font.Name}");
            themed.Close();
        }
        Theme.TestDark = null;
        using var form = new SettingsForm(new AppSettings(), (_, _) => null, null);
        form.Show();
        Application.DoEvents();
        Console.WriteLine($"  Settings: DPI {form.DeviceDpi}, client {form.ClientSize}");
        using var preview = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size));
        preview.Save(Path.Combine(root, "settings-layout.png"), ImageFormat.Png);
        var picker = Descendants(form).OfType<ShapePicker>().Single();
        picker.PerformClick();
        Application.DoEvents();
        Assert(picker.Region == null, "Capture button uses a jagged binary clipping region");
        Assert(picker.Popup.Region == null, "Capture menu uses a jagged binary clipping region");
        using (var onScreen = CaptureService.FreezeDesktop(picker.Popup.Bounds))
            onScreen.Save(Path.Combine(root, "capture-menu-onscreen.png"), ImageFormat.Png);
        using (var popup = new Bitmap(picker.Popup.Width, picker.Popup.Height))
        {
            picker.Popup.DrawToBitmap(popup, new Rectangle(Point.Empty, popup.Size));
            popup.Save(Path.Combine(root, "capture-menu.png"), ImageFormat.Png);
        }
        ((ToolStripMenuItem)picker.Popup.Items[1]).PerformClick();
        Assert(picker.SelectedIndex == 1, "Freeform selection did not update");
        picker.Popup.Close();
        // Exercise the actual capture overlay, not just the settings window.
        Rectangle desktopBounds = SystemInformation.VirtualScreen;
        using (var background = new Bitmap(desktopBounds.Width, desktopBounds.Height))
        {
            using (var graphics = Graphics.FromImage(background)) graphics.Clear(Color.LightGoldenrodYellow);
            using var overlay = new SelectionOverlay(background, desktopBounds, CaptureMode.Copy);
            overlay.Show();
            Application.DoEvents();
            using var rendered = new Bitmap(overlay.Width, overlay.Height);
            overlay.DrawToBitmap(rendered, new Rectangle(Point.Empty, rendered.Size));
            Rectangle button = overlay.PickerBounds;
            Assert(rendered.GetPixel(button.Left, button.Top).ToArgb() == rendered.GetPixel(button.Left - 2, button.Top).ToArgb(),
                "Overlay picker has an opaque rectangular background outside its rounded surface");
            using var detail = rendered.Clone(Rectangle.Inflate(button, 12, 12), PixelFormat.Format32bppArgb);
            detail.Save(Path.Combine(root, "overlay-picker.png"), ImageFormat.Png);
            overlay.Close();
        }
        foreach (var label in Descendants(form).OfType<Label>())
        {
            Assert(label.Height >= label.Font.Height, $"Clipped label '{label.Text}': height {label.Height}, font {label.Font.Height}");
        }
        form.Close();
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static void TestCaptureMemory()
    {
        string destination = Path.Combine(root, "memory-" + Guid.NewGuid().ToString("N"));
        using var memory = new CaptureMemory();
        using (var bitmap = new Bitmap(27, 19))
        {
            using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.SteelBlue);
            memory.Replace(bitmap, null);
        }
        Assert(!Directory.Exists(destination) && memory.Image?.Size == new Size(27, 19), "Capture was not retained independently in memory");
        string file = memory.EnsureSaved(destination);
        Assert(File.Exists(file) && memory.EnsureSaved(destination) == file, "Save last duplicated an already saved image");
        memory.CopyAsFile(destination);
        Assert(Clipboard.GetFileDropList().Contains(file), "Clipboard file drop missing");
        Clipboard.SetText(file);
        Assert(Clipboard.GetText() == file, "Path copy incorrect");
        using var replacement = new Bitmap(12, 10);
        memory.Replace(replacement, null);
        Assert(memory.Image?.Width == 12 && memory.SavedPath == null && memory.LastSavedPath == file, "Last capture state was not replaced");
        string second = memory.EnsureSaved(destination);
        Assert(second != file && Directory.GetFiles(destination).Length == 2, "Replacement did not save separately");
    }

    private static void TestRegionMemory()
    {
        string directory = Path.Combine(root, "regions"); Directory.CreateDirectory(directory);
        var memory = new RegionMemory(directory);
        Rectangle firstMonitor = new(-1920, 0, 1920, 1080), secondMonitor = new(0, 0, 2560, 1440);
        Rectangle selection = new(-1800, 100, 300, 200);
        Point[] polygon = [new(0, 0), new(299, 0), new(0, 199)];
        memory.Remember("left", firstMonitor, selection, CaptureShape.Freeform, polygon);
        memory.Remember("right", secondMonitor, new Rectangle(200, 300, 800, 600), CaptureShape.Rectangle, []);
        var restored = new RegionMemory(directory);
        var first = restored.Recall("left", firstMonitor, new Rectangle(-1920, 0, 4480, 1440));
        Assert(first?.OnMonitor(firstMonitor) == selection && first.Polygon.SequenceEqual(polygon), "Saved freeform region did not round-trip");
        Assert(restored.Recall("right", secondMonitor, new Rectangle(-1920, 0, 4480, 1440))?.Width == 800, "Monitor regions mixed");
        Assert(restored.Recall("left", new Rectangle(-1280, 0, 1280, 720), new Rectangle(-1920, 0, 4480, 1440)) == null, "Changed resolution was not rejected");
        Assert(restored.Recall("missing", secondMonitor, secondMonitor) == null, "Missing monitor has a region");
    }

    private static void OverlayEvent(SelectionOverlay overlay, string method, EventArgs args) =>
        typeof(SelectionOverlay).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(overlay, new object[] { args });

    private static void TestPlacement()
    {
        using var desktop = new Bitmap(1000, 800);
        using var overlay = new SelectionOverlay(desktop, new Rectangle(0, 0, 1000, 800), CaptureMode.Copy);
        OverlayEvent(overlay, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 100, 150, 0));
        OverlayEvent(overlay, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 300, 350, 0));
        Assert(overlay.Selection == new Rectangle(100, 150, 200, 200), "Initial region");
        OverlayEvent(overlay, "OnKeyDown", new KeyEventArgs(Keys.Space));
        OverlayEvent(overlay, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 340, 370, 0));
        Assert(overlay.Selection == new Rectangle(140, 170, 200, 200), "Space changed selection size");
        OverlayEvent(overlay, "OnKeyUp", new KeyEventArgs(Keys.Space));
        OverlayEvent(overlay, "OnKeyDown", new KeyEventArgs(Keys.Right | Keys.Shift));
        Assert(overlay.Selection.X == 150 && overlay.Selection.Width == 200, "Shift+arrow did not move 10 pixels");
        OverlayEvent(overlay, "OnKeyDown", new KeyEventArgs(Keys.Left));
        Assert(overlay.Selection.X == 149, "Arrow did not move 1 pixel");
        Assert(SelectionGeometry.Move(new Rectangle(900, 700, 100, 100), 50, 50, desktop.Size) == new Rectangle(900, 700, 100, 100), "Movement escaped desktop");
        Assert(SelectionOverlay.ResolveMode(CaptureMode.Copy, true) == CaptureMode.Save &&
            SelectionOverlay.ResolveMode(CaptureMode.Save, true) == CaptureMode.Copy &&
            SelectionOverlay.ResolveMode(CaptureMode.Copy, false) == CaptureMode.Copy, "Destination override incorrect");
        using var fixedOverlay = new SelectionOverlay(desktop, new Rectangle(0, 0, 1000, 800), CaptureMode.Copy,
            CaptureShape.Rectangle, new AppSettings { ExactSize = true, CaptureWidth = 320, CaptureHeight = 240 });
        OverlayEvent(fixedOverlay, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 900, 700, 0));
        Assert(fixedOverlay.Selection.Size == new Size(320, 240) && fixedOverlay.Selection.Right == 1000 && fixedOverlay.Selection.Bottom == 800,
            "Exact-size selection was resized instead of clamped");
        using var image = fixedOverlay.CreateImage();
        Assert(image.Size == new Size(320, 240), "Exact-size output incorrect");
    }

    private static void TestOptionalHotkeys()
    {
        using var window = new HotkeyWindow("QuietSnip.OptionalTests");
        var settings = new AppSettings { CopyHotkey = new(Keys.F19, HotkeyModifiers.Control | HotkeyModifiers.Shift),
            SaveHotkey = new(Keys.F20, HotkeyModifiers.Control | HotkeyModifiers.Shift),
            ExtraHotkeys = new() { [AppAction.SaveLast] = new(Keys.F21, HotkeyModifiers.Control | HotkeyModifiers.Shift) } };
        settings.Validate(); window.Apply(settings);
        Assert(!NativeMethods.RegisterHotKey(IntPtr.Zero, 240, 6, (uint)Keys.F21), "Optional shortcut wasn't registered");
        Throws<ArgumentException>(() => (settings with { ExtraHotkeys = new() { [AppAction.CopyPath] = settings.CopyHotkey } }).Validate());
        var proposed = settings with { ExtraHotkeys = new() { [AppAction.CopyPath] = new(Keys.F22, HotkeyModifiers.Control | HotkeyModifiers.Shift) } };
        Assert(NativeMethods.RegisterHotKey(IntPtr.Zero, 241, 6, (uint)Keys.F22), "Test shortcut is occupied");
        try
        {
            Throws<InvalidOperationException>(() => window.Apply(proposed));
            Assert(!NativeMethods.RegisterHotKey(IntPtr.Zero, 242, 6, (uint)Keys.F21), "Original optional shortcut wasn't restored");
        }
        finally { NativeMethods.UnregisterHotKey(IntPtr.Zero, 241); }
    }

    private static void TestSaveAndClose()
    {
        using (var success = new SettingsForm(new AppSettings(), (_, _) => null, null))
        { success.Show(); Application.DoEvents(); Descendants(success).OfType<ActionButton>().Single(b => b.Text == "Save settings").PerformClick(); Assert(!success.Visible, "Successful save did not return to tray"); }
        using var failed = new SettingsForm(new AppSettings(), (_, _) => "Shortcut is occupied", null);
        failed.Show(); Application.DoEvents(); failed.SaveSettings();
        Assert(failed.Visible, "Failed save hid the error"); failed.Close();
    }

    private static void TestActionButtons()
    {
        var called = new List<AppAction>();
        bool hasCapture = false;
        using var form = new SettingsForm(new AppSettings(), (_, _) => null, null, called.Add,
            action => action == AppAction.SaveLast && !hasCapture ? "Capture first" : null);
        form.Show(); Application.DoEvents();
        Descendants(form).OfType<ActionButton>().Single(b => b.Tag as string == "navigation" && b.Text == "Shortcuts").PerformClick();
        Assert(form.ActionButtons.Count == Enum.GetValues<AppAction>().Length, "An action is missing its button");
        Assert(!form.ActionButtons[AppAction.SaveLast].Enabled, "Unavailable action isn't disabled");
        hasCapture = true; form.RefreshActions();
        foreach (var pair in form.ActionButtons)
        {
            Assert(pair.Value.Enabled, "Button incorrectly depends on hotkey enablement: " + pair.Key);
            pair.Value.PerformClick();
        }
        Assert(called.SequenceEqual(Enum.GetValues<AppAction>()), "Buttons did not dispatch the corresponding actions");
        foreach (var input in Descendants(form).OfType<HotkeyBox>().Where(input => input.AccessibleName != "Copy to clipboard shortcut" && input.AccessibleName != "Save + copy shortcut"))
            Assert(!input.Enabled, "Running an action enabled its global hotkey");
        form.Close();
    }

    private static void TestIcons()
    {
        using var data = typeof(CaptureMemory).Assembly.GetManifestResourceStream("QuietSnip.app.ico")!;
        using var reader = new BinaryReader(data);
        Assert(reader.ReadUInt16() == 0 && reader.ReadUInt16() == 1, "Invalid ICO header");
        int count = reader.ReadUInt16();
        var sizes = new List<int>();
        for (int index = 0; index < count; index++)
        {
            int width = reader.ReadByte(); sizes.Add(width == 0 ? 256 : width);
            data.Position += 15;
        }
        foreach (int size in new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 })
        {
            Assert(sizes.Contains(size), "Missing native icon size: " + size);
            using var icon = AppIcons.Load(size);
            Assert(icon.Width == size && icon.Height == size, $"Icon requested {size}px but loaded {icon.Width} × {icon.Height}");
            using var bitmap = icon.ToBitmap();
            Assert(bitmap.GetPixel(0, 0).A == 0, "Icon has an opaque square background");
            bool antialiasing = false;
            for (int y = 0; y < size && !antialiasing; y++)
                for (int x = 0; x < size; x++)
                { int alpha = bitmap.GetPixel(x, y).A; if (alpha > 0 && alpha < 255) { antialiasing = true; break; } }
            Assert(antialiasing, "Icon is using a hard binary edge at " + size + " pixels");
        }
        Assert(sizes.Contains(AppIcons.TrayPixels), "The taskbar DPI has no native tray icon frame");
        Console.WriteLine($"  Native tray icon size: {AppIcons.TrayPixels}px");
    }

    private static void TestConfirmation()
    {
        using var owner = new Form { Text = "QuietSnip confirmation check", Size = new Size(360, 180), ShowInTaskbar = false };
        owner.Show(); owner.Activate(); Application.DoEvents();
        IntPtr foreground = GetForegroundWindow();
        Rectangle work = Screen.PrimaryScreen!.WorkingArea;
        var selection = new Rectangle(work.Left + 100, work.Top + 100, 300, 200);
        using var notice = new CaptureNotice("Copied", selection);
        notice.Show(); Application.DoEvents();
        Assert(work.Contains(notice.Bounds) && notice.Top >= selection.Bottom, "Confirmation isn't beside the capture");
        Assert(GetForegroundWindow() != notice.Handle, "Confirmation became the foreground window");
        DateTime deadline = DateTime.UtcNow.AddSeconds(2);
        while (notice.Visible && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
        Assert(!notice.Visible, "Confirmation didn't dismiss itself");
        owner.Close();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    private static void TestOverlayBaseline()
    {
        Rectangle bounds = SystemInformation.VirtualScreen;
        using var background = new Bitmap(bounds.Width, bounds.Height);
        var timings = new List<double>();
        var samples = new List<object>();
        int deviceDpi = 0;
        uint warmGdi = 0, warmUser = 0, finalGdi = 0, finalUser = 0;
        for (int i = 0; i < 100; i++)
        {
            using (var graphics = Graphics.FromImage(background)) graphics.Clear(i % 2 == 0 ? Color.White : Color.FromArgb(20, 20, 20));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            using (var overlay = new SelectionOverlay(background, bounds, CaptureMode.Copy))
            using (var timer = new System.Windows.Forms.Timer { Interval = 20 })
            {
                deviceDpi = overlay.DeviceDpi;
                int cycle = i;
                Exception? cycleError = null;
                overlay.Shown += (_, _) => { timings.Add(clock.Elapsed.TotalMilliseconds); timer.Start(); };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    try
                    {
                        if (cycle is 0 or 99)
                        {
                            var picker = (ShapePicker)typeof(SelectionOverlay).GetField("shapePicker", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(overlay)!;
                            picker.OpenMenuAt(overlay, new Point(picker.Left, picker.Bottom + 6));
                            Application.DoEvents(); DwmFlush();
                            using var shot = CaptureService.FreezeDesktop(bounds);
                            shot.Save(Path.Combine(root, "actual-overlay-popup-" + cycle + ".png"), ImageFormat.Png);
                            picker.Popup.Close();
                        }
                    }
                    catch (Exception error) { cycleError = error; }
                    finally { OverlayEvent(overlay, "OnKeyDown", new KeyEventArgs(Keys.Escape)); }
                };
                Assert(overlay.ShowDialog() == DialogResult.Cancel, "Escape did not cancel overlay cycle " + i);
                if (cycleError != null) throw new Exception("Overlay evidence failed at cycle " + i, cycleError);
            }
            if (i is 9 or 49 or 99)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                using var process = System.Diagnostics.Process.GetCurrentProcess(); process.Refresh();
                uint gdi = GetGuiResources(process.Handle, 0), user = GetGuiResources(process.Handle, 1);
                // Windows Forms font/menu caches had not plateaued at cycle 10 in the initial run.
                if (i == 49) { warmGdi = gdi; warmUser = user; }
                if (i == 99) { finalGdi = gdi; finalUser = user; }
                samples.Add(new { Cycle = i + 1, GdiObjects = gdi, UserObjects = user, process.HandleCount, process.PrivateMemorySize64 });
            }
        }
        timings.Sort();
        File.WriteAllText(Path.Combine(root, "overlay-baseline.json"), JsonSerializer.Serialize(new {
            Cycles = 100, DeviceDpi = deviceDpi,
            ShownMedianMs = timings[50], ShownP95Ms = timings[94], Samples = samples,
            GdiGrowthAfterWarmup = (long)finalGdi - warmGdi, UserGrowthAfterWarmup = (long)finalUser - warmUser,
            Scope = "Production overlay shown modally over a synthetic full desktop; Escape dispatched through its key handler. Does not test WM_HOTKEY latency, clipboard contention, mixed DPI, or manual pointer smoothness."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Assert(finalGdi <= warmGdi + 2 && finalUser <= warmUser + 2, "Overlay GUI resources grew after warmup; inspect evidence");
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);
}

