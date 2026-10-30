using System.Drawing.Imaging;
using System.Text.Json;
using SnippingToolPlus;

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
            if (args.Contains("--ocr-quality"))
            {
                Run("Reported passage preserves version, arrow, durations, and copied punctuation", TestPassageRecognition);
                Run("DPI metadata does not resize OCR pixels; punctuation and non-Latin spacing survive selection", TestDpiAndFormatting);
                Run("Inline arrow recovery preserves letters and mathematical symbols", TestArrowSymbols);
                Run("Compact durations distinguish seconds from I/l and retain normal text", TestDurationRecognition);
                Run("Small let's text keeps its apostrophe and genuine O/C letters", TestSmallTextQuality);
                Run("Icon false positives are removed without damaging real text", TestIconFalsePositives);
                Run("Custom rounded toolbar glyphs at five display scales", TestRoundedToolbarGlyphs);
                return 0;
            }
            if (args.Contains("--ocr-overlay"))
            {
                Run("One highlight per line with continuous word selection", TestLineHighlights);
                Run("On-screen OCR stays aligned, selectable, and open until dismissed", TestScreenTextOverlay);
                Run("Resizing, stale recognition cancellation, back, and close", TestScreenTextResize);
                Run("Screen OCR is default; app editor is optional", TestTextCaptureThroughTray);
                return 0;
            }
            if (args.Contains("--ocr-ui"))
            {
                Run("OCR word selection, editable copy text, light/dark UI, and close during recognition", TestTextActionsUi);
                Run("Text toolbar mode and region selection", TestTextCaptureMode);
                Run("Shortcut actions and scrolling layout", TestActionButtons);
                Run("Light/dark settings and capture toolbar layout", TestLayout);
                Run("Text capture and last-snip actions leave files and clipboard untouched", TestTextCaptureThroughTray);
                return 0;
            }
            if (args.Contains("--ocr-tests"))
            {
                Run("Windows OCR recognizes text and maps original word positions", TestRecognition);
                Run("OCR scaling, selected text, and line break formatting", TestRecognitionFormatting);
                Run("OCR cancellation does not retain source images", TestRecognitionCancellation);
                return 0;
            }
            if (args.Contains("--investigation-core"))
            {
                Run("Shortcut validation and formatting", TestShortcuts);
                Run("Renamed startup command compatibility", TestStartupCommands);
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
            if (args.Contains("--rapid-capture"))
            {
                Run("Second capture survives the previous confirmation timeout", TestRapidCapture);
                Run("Rapid captures dismiss old notices and stay open until cancelled", TestRapidCaptureThroughTray);
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
            Run("Renamed startup command compatibility", TestStartupCommands);
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
            Run("Second capture survives the previous confirmation timeout", TestRapidCapture);
            Run("Rapid captures dismiss old notices and stay open until cancelled", TestRapidCaptureThroughTray);
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


    private static T Pump<T>(Task<T> task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(5); }
        Assert(task.IsCompleted, "The asynchronous operation did not finish");
        return task.GetAwaiter().GetResult();
    }

    private static void TestRecognition()
    {
        Task<TextRecognitionResult> pending;
        using (var image = new Bitmap(1024, 300))
        using (var graphics = Graphics.FromImage(image))
        using (var font = new Font("Segoe UI", 32))
        {
            graphics.Clear(Color.White);
            graphics.DrawString("SNIPPING TOOL PLUS", font, Brushes.Black, 32, 30);
            graphics.DrawString("Read text 12345", font, Brushes.Black, 32, 130);
            image.Save(Path.Combine(root, "ocr-fixture.png"), ImageFormat.Png);
            pending = TextRecognition.RecognizeAsync(image);
        }
        var result = Pump(pending);
        Assert(result.Text.Contains("SNIPPING TOOL PLUS", StringComparison.OrdinalIgnoreCase), "Headline not recognized: " + result.Text);
        Assert(result.Text.Contains("12345"), "Digits not recognized: " + result.Text);
        Assert(result.Words.Count >= 6, "Word boxes missing");
        Assert(result.Words.All(word => word.Bounds.Width > 0 && word.Bounds.Height > 0
            && word.Bounds.Left >= 0 && word.Bounds.Top >= 0 && word.Bounds.Right <= 1025 && word.Bounds.Bottom <= 301), "Word bounds were not mapped to original pixels");
        Console.WriteLine("  Windows OCR language: " + result.Language);
        using var blank = new Bitmap(200, 100);
        using (var graphics = Graphics.FromImage(blank)) graphics.Clear(Color.White);
        Assert(Pump(TextRecognition.RecognizeAsync(blank)).Words.Count == 0, "Blank image produced text");
    }

    private static void TestRecognitionFormatting()
    {
        Assert(TextRecognition.RecognitionSize(new Size(6000, 3000), 2600) == new Size(2600, 1300), "Large image scaling exceeded the engine limit");
        Assert(TextRecognition.RecognitionSize(new Size(100, 50), 2600) == new Size(400, 200), "Small text was not upscaled");
        Assert(TextRecognition.RecognitionSize(new Size(1, 6000), 2600).Width == 1, "Thin image collapsed");
        Assert(TextRecognitionResult.WithoutLineBreaks("First\r\n  second\nthird") == "First second third", "Line break removal failed");
        var result = new TextRecognitionResult("One two\nThree", [
            new("One", new RectangleF(1, 1, 10, 10), 0),
            new("two", new RectangleF(12, 1, 10, 10), 0),
            new("Three", new RectangleF(1, 14, 10, 10), 1)], "English");
        Assert(result.SelectedText([2, 0, 0, 1]) == "One two" + Environment.NewLine + "Three", "Selection order or line grouping was lost");
    }

    private static void TestRecognitionCancellation()
    {
        using var image = new Bitmap(50, 50);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Throws<OperationCanceledException>(() => Pump(TextRecognition.RecognizeAsync(image, cancellation.Token)));
    }


    private static void CanvasEvent(TextRecognitionCanvas canvas, string method, MouseEventArgs args) =>
        typeof(TextRecognitionCanvas).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(canvas, [args]);

    private static void TestTextActionsUi()
    {
        using var image = new Bitmap(1024, 300);
        using (var graphics = Graphics.FromImage(image))
        using (var font = new Font("Segoe UI", 32))
        {
            graphics.Clear(Color.White);
            graphics.DrawString("SNIPPING TOOL PLUS", font, Brushes.Black, 32, 30);
            graphics.DrawString("Read text 12345", font, Brushes.Black, 32, 130);
        }
        var recognized = Pump(TextRecognition.RecognizeAsync(image));
        foreach (bool dark in new[] { false, true })
        {
            Theme.TestDark = dark;
            using var form = new TextActionsForm(image, recognized);
            form.Show(); Application.DoEvents();
            Assert(form.Editor.Text.Contains("12345"), "Recognized text did not reach the editor");
            foreach (var control in Descendants(form).Where(c => c is ActionButton || c is Label))
                Assert(control.Parent!.ClientRectangle.Contains(control.Bounds), "Clipped OCR control: " + control.Text + " " + control.Bounds + " parent " + control.Parent.ClientSize);
            var first = form.Preview.WordBounds(0);
            var second = form.Preview.WordBounds(1);
            var start = Point.Round(new PointF(first.Left + first.Width / 2, first.Top + first.Height / 2));
            var end = Point.Round(new PointF(second.Left + second.Width / 2, second.Top + second.Height / 2));
            CanvasEvent(form.Preview, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0));
            CanvasEvent(form.Preview, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, end.X, end.Y, 0));
            CanvasEvent(form.Preview, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, end.X, end.Y, 0));
            Assert(form.Preview.SelectedIndices.Count == 2, "Dragging did not select the first two words");
            Assert(form.CopyText(true) == recognized.SelectedText([0, 1]), "Selected image words did not reach the copy action");
            form.Editor.Select(0, 3);
            Assert(form.CopyText(true) == form.Editor.SelectedText, "Editable text selection was ignored");
            Descendants(form).OfType<CheckBox>().Single(c => c.Text == "Remove line breaks").Checked = true;
            Assert(!form.CopyText(false).Contains('\n') && !form.CopyText(false).Contains('\r'), "Copy all did not flatten line breaks");
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(200);
            while (DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(5); }
            using var rendered = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(rendered, new Rectangle(Point.Empty, rendered.Size));
            rendered.Save(Path.Combine(root, dark ? "text-actions-dark.png" : "text-actions-light.png"), ImageFormat.Png);
            form.Close(); form.Dispose();
        }
        Theme.TestDark = null;
        using var empty = new TextActionsForm(image, new TextRecognitionResult("", [], "English"));
        empty.Show(); Application.DoEvents();
        Assert(!Descendants(empty).OfType<ActionButton>().Any(b => b.Text.StartsWith("Copy") && b.Enabled), "Empty OCR result enabled copying");
        empty.Close();
        using var cancelled = new TextActionsForm(image);
        cancelled.Show(); cancelled.Close();
        for (int i = 0; i < 30; i++) { Application.DoEvents(); Thread.Sleep(5); }
    }

    private static void TestTextCaptureMode()
    {
        Rectangle bounds = SystemInformation.VirtualScreen;
        using var desktop = new Bitmap(bounds.Width, bounds.Height);
        using var overlay = new SelectionOverlay(desktop, bounds, CaptureMode.Copy);
        var textButton = overlay.ModeButtonBounds(2);
        OverlayEvent(overlay, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, textButton.Left + 5, textButton.Top + 5, 0));
        Assert(overlay.Selection.IsEmpty && overlay.DestinationMode == CaptureMode.Text, "Text mode click began a capture or kept the old mode");
        OverlayEvent(overlay, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 100, 200, 0));
        OverlayEvent(overlay, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 400, 400, 0));
        OverlayEvent(overlay, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 400, 400, 0));
        Assert(overlay.DestinationMode == CaptureMode.Text && overlay.DialogResult == DialogResult.OK, "Region completion lost text mode");
        Assert(SelectionOverlay.ResolveMode(CaptureMode.Text, true) == CaptureMode.Text, "Holding Alt changed OCR into image copying");
        using var image = overlay.CreateImage();
        Assert(image.Size == new Size(300, 200), "Text capture has incorrect region dimensions");
        overlay.Show(); Application.DoEvents();
        using var rendered = new Bitmap(overlay.Width, overlay.Height);
        overlay.DrawToBitmap(rendered, new Rectangle(Point.Empty, rendered.Size));
        using var toolbar = rendered.Clone(Rectangle.Inflate(overlay.ModeToolbarBounds, 12, 12), PixelFormat.Format32bppArgb);
        toolbar.Save(Path.Combine(root, "capture-toolbar-text.png"), ImageFormat.Png);
        overlay.Close();
    }


    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();

    private static void TestTextCaptureThroughTray()
    {
        // A real tray app keeps its UI synchronization context through closing its last visible form.
        Exception? failure = null;
        using var loop = new ApplicationContext();
        EventHandler? start = null;
        start = (_, _) =>
        {
            Application.Idle -= start;
            try { TestTextCaptureThroughTrayCore(); }
            catch (Exception error) { failure = error; }
            finally { loop.ExitThread(); }
        };
        Application.Idle += start;
        Application.Run(loop);
        if (failure != null) throw new Exception("Text capture integration failed", failure);
    }

    private static void TestTextCaptureThroughTrayCore()
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var execute = typeof(TrayApplication).GetMethod("Execute", flags)!;
        var busy = typeof(TrayApplication).GetField("busy", flags)!;
        string destination = Path.Combine(root, "ocr-must-not-save-" + Guid.NewGuid().ToString("N"));
        uint clipboardBefore = GetClipboardSequenceNumber();
        using var app = new TrayApplication("SnippingToolPlus.Tests.Ocr." + Guid.NewGuid().ToString("N"));
        typeof(TrayApplication).GetField("settings", flags)!.SetValue(app, new AppSettings { SaveFolder = destination });
        bool selected = false, opened = false;
        using var driver = new System.Windows.Forms.Timer { Interval = 25 };
        driver.Tick += (_, _) =>
        {
            var overlay = Application.OpenForms.OfType<SelectionOverlay>().FirstOrDefault();
            if (!selected && overlay is { Visible: true })
            {
                selected = true;
                OverlayEvent(overlay, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 100, 300, 0));
                OverlayEvent(overlay, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 420, 500, 0));
                OverlayEvent(overlay, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 420, 500, 0));
            }
        };
        driver.Start();
        try
        {
            execute.Invoke(app, [AppAction.ExtractText]);
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                if (Application.OpenForms.OfType<ScreenTextOverlay>().Any() && !(bool)busy.GetValue(app)!) { opened = true; break; }
                Thread.Sleep(5);
            }
            Assert(selected && opened, "Text capture did not open the text actions UI");
            Assert(!(bool)busy.GetValue(app)!, "Text recognition left capture shortcuts blocked");
            Assert(!Directory.Exists(destination), "Text mode saved an unwanted screenshot");
            Assert(GetClipboardSequenceNumber() == clipboardBefore, "Text mode changed the clipboard before Copy was requested");
            Assert(!Application.OpenForms.OfType<TextActionsForm>().Any(), "Text capture opened the app editor by default");
            var window = Application.OpenForms.OfType<ScreenTextOverlay>().Single();
            selected = false;
            execute.Invoke(app, [AppAction.ExtractText]);
            deadline = DateTime.UtcNow.AddSeconds(5);
            while ((!Application.OpenForms.OfType<ScreenTextOverlay>().Any() || (bool)busy.GetValue(app)!) && DateTime.UtcNow < deadline)
            { Application.DoEvents(); Thread.Sleep(5); }
            Assert(window.IsDisposed && Application.OpenForms.OfType<ScreenTextOverlay>().Any(), "A repeated capture did not replace the existing screen OCR");
            Assert(!Application.OpenForms.OfType<TextActionsForm>().Any(), "Repeated capture opened the app editor");
            window = Application.OpenForms.OfType<ScreenTextOverlay>().Single();
            Assert(!(bool)busy.GetValue(app)!, "The repeated capture action did not finish");
            window.Options.Show(window, new Point(window.ToolbarBounds.X, window.ToolbarBounds.Bottom));
            Application.DoEvents();
            var openItem = (ToolStripMenuItem)window.Options.Items.Cast<ToolStripItem>().Single(i => i.Text == "Open in app");
            openItem.PerformClick();
            deadline = DateTime.UtcNow.AddSeconds(5);
            while ((!window.IsDisposed || !Application.OpenForms.OfType<TextActionsForm>().Any()) && DateTime.UtcNow < deadline)
            { Application.DoEvents(); Thread.Sleep(5); }
            Assert(window.IsDisposed && Application.OpenForms.OfType<TextActionsForm>().Any(), "Open in app did not hand off from the screen overlay");
            foreach (var editor in Application.OpenForms.OfType<TextActionsForm>().ToArray()) editor.Close();
            execute.Invoke(app, [AppAction.TextActions]);
            Assert(Application.OpenForms.OfType<TextActionsForm>().Any(), "Last-capture text actions did not reopen");
        }
        finally
        {
            driver.Stop();
            foreach (var window in Application.OpenForms.OfType<ScreenTextOverlay>().ToArray()) window.Close();
            foreach (var window in Application.OpenForms.OfType<TextActionsForm>().ToArray()) window.Close();
            foreach (var overlay in Application.OpenForms.OfType<SelectionOverlay>().ToArray()) overlay.Close();
        }
    }


    private static void ScreenEvent(ScreenTextOverlay overlay, string method, MouseEventArgs args) =>
        typeof(ScreenTextOverlay).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(overlay, [args]);

    private static void TestScreenTextOverlay()
    {
        Rectangle bounds = SystemInformation.VirtualScreen;
        using var desktop = new Bitmap(bounds.Width, bounds.Height);
        var region = new Rectangle(Math.Max(40, (bounds.Width - 1300) / 2), Math.Min(500, bounds.Height / 2), Math.Min(1300, bounds.Width - 80), 220);
        using (var graphics = Graphics.FromImage(desktop))
        using (var heading = new Font("Segoe UI", 23, FontStyle.Bold))
        using (var body = new Font("Segoe UI", 18))
        using (var card = new SolidBrush(Color.FromArgb(40, 48, 61)))
        {
            graphics.Clear(Color.FromArgb(24, 29, 38));
            graphics.FillRectangle(card, 32, 38, bounds.Width - 64, 90);
            graphics.DrawString("WORKSPACE     Library     Projects     Activity", heading, Brushes.White, 60, 60);
            graphics.FillRectangle(card, region);
            graphics.DrawString("PROJECT STATUS", heading, Brushes.White, region.X + 24, region.Y + 28);
            graphics.DrawString("Build ready     Version 0.4.1     Copy text directly from this region.", body, Brushes.White, region.X + 24, region.Y + 110);
            graphics.DrawString("Recent projects", heading, Brushes.White, 60, region.Bottom + 70);
        }
        using var crop = CaptureService.Crop(desktop, region);
        var recognized = Pump(TextRecognition.RecognizeAsync(crop));
        using var owner = new HotkeyWindow("SnippingToolPlus.Tests.ScreenText");
        foreach (bool dark in new[] { false, true })
        {
            Theme.TestDark = dark;
            var screenRegion = new Rectangle(region.X + bounds.X, region.Y + bounds.Y, region.Width, region.Height);
            using var overlay = new ScreenTextOverlay(desktop, bounds, screenRegion, recognized);
            overlay.Show(owner); Application.DoEvents();
            Assert(overlay.SelectedScreenBounds == screenRegion, "OCR moved the selection");
            Assert(overlay.Preview.RectangleToScreen(overlay.Preview.ClientRectangle) == screenRegion, "OCR pixels are not aligned with the original screen");
            Assert(overlay.Preview.WordBounds(0) == recognized.Words[0].Bounds, "Native word coordinates were scaled");
            Assert(!overlay.ShowInTaskbar && overlay.FormBorderStyle == FormBorderStyle.None, "Default OCR opened a normal app window");
            var first = overlay.Preview.WordBounds(0); var second = overlay.Preview.WordBounds(1);
            Point a = Point.Round(new PointF(first.Left + first.Width / 2, first.Top + first.Height / 2));
            Point b = Point.Round(new PointF(second.Left + second.Width / 2, second.Top + second.Height / 2));
            CanvasEvent(overlay.Preview, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, a.X, a.Y, 0));
            CanvasEvent(overlay.Preview, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, b.X, b.Y, 0));
            CanvasEvent(overlay.Preview, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, b.X, b.Y, 0));
            Assert(overlay.CopyText(true) == recognized.SelectedText([0, 1]), "On-screen text selection did not copy the selected words");
            ((ToolStripMenuItem)overlay.Options.Items.Cast<ToolStripItem>().Single(i => i.Text == "Remove line breaks")).Checked = true;
            Assert(!overlay.CopyText(false).Contains('\n'), "On-screen copy options ignored line breaks");
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(dark ? 250 : 1300);
            while (DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(5); }
            Assert(overlay.Visible, "OCR result dismissed itself");
            using var rendered = new Bitmap(overlay.Width, overlay.Height);
            overlay.DrawToBitmap(rendered, new Rectangle(Point.Empty, rendered.Size));
            rendered.Save(Path.Combine(root, dark ? "screen-text-dark.png" : "screen-text-light.png"), ImageFormat.Png);
            Assert(!overlay.ToolbarBounds.IntersectsWith(region), "Toolbar covers the selected text");
            overlay.Options.Show(overlay, new Point(overlay.ToolbarBounds.Right - 250, overlay.ToolbarBounds.Bottom + 8));
            Application.DoEvents();
            using (var menuImage = new Bitmap(overlay.Options.Width, overlay.Options.Height))
            {
                overlay.Options.DrawToBitmap(menuImage, new Rectangle(Point.Empty, menuImage.Size));
                menuImage.Save(Path.Combine(root, dark ? "text-options-dark.png" : "text-options-light.png"), ImageFormat.Png);
            }
            overlay.Options.Close();
            TextActionsForm? editor = null;
            overlay.OpenInAppRequested += (snapshot, result) => { editor = new TextActionsForm(snapshot, result); editor.Show(); };
            overlay.OpenEditor(); Application.DoEvents();
            Assert(overlay.IsDisposed && editor != null && editor.Editor.Text.Contains("PROJECT"), "Optional app handoff lost the recognized text");
            editor!.Close(); editor.Dispose();
        }
        Theme.TestDark = null;
    }

    private static void TestScreenTextResize()
    {
        Rectangle bounds = SystemInformation.VirtualScreen;
        using var desktop = new Bitmap(bounds.Width, bounds.Height);
        using (var graphics = Graphics.FromImage(desktop))
        using (var font = new Font("Segoe UI", 30))
        {
            graphics.Clear(Color.White);
            graphics.DrawString("ALPHA FIRST", font, Brushes.Black, 90, 300);
            graphics.DrawString("BRAVO SECOND", font, Brushes.Black, 760, 600);
        }
        var initial = new Rectangle(60, 270, 520, 130);
        using var overlay = new ScreenTextOverlay(desktop, bounds, new Rectangle(bounds.X + initial.X, bounds.Y + initial.Y, initial.Width, initial.Height));
        overlay.Show(); Application.DoEvents();
        int changes = 0;
        overlay.ImageChanged += (_, _) => changes++;
        ScreenEvent(overlay, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, initial.Right + 1, initial.Top + initial.Height / 2, 0));
        ScreenEvent(overlay, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, initial.Right + 81, initial.Top + initial.Height / 2, 0));
        ScreenEvent(overlay, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, initial.Right + 81, initial.Top + initial.Height / 2, 0));
        Assert(overlay.SelectionBounds.Width == initial.Width + 80 && changes == 1, "Dragging the region edge did not resize and re-recognize");
        overlay.ReplaceRegion(new Rectangle(60, 270, 520, 130));
        overlay.ReplaceRegion(new Rectangle(720, 560, 600, 150));
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (overlay.RecognitionResult == null && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(5); }
        Assert(overlay.RecognitionResult?.Text.Contains("BRAVO") == true && !overlay.RecognitionResult.Text.Contains("ALPHA"), "A stale recognition result replaced the resized region");
        Assert(overlay.Preview.Bounds == overlay.SelectionBounds, "Resizing detached the word highlights from the selection");
        var clipped = ScreenTextOverlay.ResizeRegion(initial, TextResizeEdges.Left | TextResizeEdges.Top, -5000, -5000, desktop.Size);
        Assert(clipped.Left == 0 && clipped.Top == 0, "Resizing escaped the captured desktop");
        int newRegions = 0;
        overlay.NewCaptureRequested += () => newRegions++;
        overlay.NewRegion();
        Assert(overlay.IsDisposed && newRegions == 1, "Back did not dismiss OCR and request another region");
        using var closing = new ScreenTextOverlay(desktop, bounds, new Rectangle(bounds.X + initial.X, bounds.Y + initial.Y, initial.Width, initial.Height));
        closing.Show(); closing.Close(); closing.Dispose();
        for (int i = 0; i < 25; i++) { Application.DoEvents(); Thread.Sleep(5); }
    }


    private static void TestPassageRecognition()
    {
        foreach (string family in new[] { "Segoe UI Variable Text", "Arial" })
        foreach (float points in new[] { 10f, 12f })
        foreach (bool dark in new[] { false, true })
        {
            using var image = new Bitmap(1000, 220);
            image.SetResolution(96, 96);
            using (var graphics = Graphics.FromImage(image))
            using (var font = new Font(family, points))
            using (var heading = new Font(family, points + 1, FontStyle.Bold))
            {
                graphics.Clear(dark ? Color.FromArgb(32, 32, 32) : Color.White);
                var ink = dark ? Brushes.White : Brushes.Black;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                graphics.DrawString("Installed v0.4.3.", heading, ink, 14, 15);
                graphics.DrawString("• Each line now has one subtle box, with continuous highlighting when selecting words.", font, ink, 14, 65);
                graphics.DrawString("• Added duration-aware correction for 24m Ils → 24m 11s.", font, ink, 14, 110);
                graphics.DrawString("• All 13 targeted checks passed; settings and shortcuts preserved.", font, ink, 14, 155);
            }
            if (family.StartsWith("Segoe") && points == 12 && dark) image.Save(Path.Combine(root, "reported-passage-source.png"));
            var corrected = Pump(TextRecognition.RecognizeAsync(image));
            Assert(corrected.Text.Contains("Installed v0.4.3."), "Version number was misread: " + corrected.Text);
            Assert(corrected.Text.Contains("24m Ils → 24m 11s."), "Arrow, duration, or punctuation was damaged: " + corrected.Text);
            Assert(corrected.Text.Contains("All 13 targeted checks passed; settings and shortcuts preserved."), "The bottom line was clipped or misread: " + corrected.Text);
            Assert(corrected.Text == corrected.SelectedText(Enumerable.Range(0, corrected.Words.Count)), "Copy all and selected text format differently");
            if (family.StartsWith("Segoe"))
            {
                string expected = string.Join(Environment.NewLine,
                    "Installed v0.4.3.",
                    "• Each line now has one subtle box, with continuous highlighting when selecting words.",
                    "• Added duration-aware correction for 24m Ils → 24m 11s.",
                    "• All 13 targeted checks passed; settings and shortcuts preserved.");
                Assert(corrected.Text == expected, "The reproduced passage differs from the source: " + corrected.Text);
            }
        }
    }

    private static void TestDpiAndFormatting()
    {
        using var source = new Bitmap(700, 230);
        source.SetResolution(96, 96);
        using (var graphics = Graphics.FromImage(source))
        using (var font = new Font("Segoe UI", 18))
        {
            graphics.Clear(Color.White);
            graphics.DrawString("TOP LINE 12345", font, Brushes.Black, 18, 18);
            graphics.DrawString("BOTTOM LINE 67890", font, Brushes.Black, 18, 180);
        }
        TextRecognitionResult? baseline = null;
        foreach (int dpi in new[] { 96, 144, 192 })
        {
            using var image = (Bitmap)source.Clone(); image.SetResolution(dpi, dpi);
            var result = Pump(TextRecognition.RecognizeAsync(image));
            Assert(result.Text.Contains("12345") && result.Text.Contains("67890"), "DPI metadata clipped the source image");
            if (baseline != null)
                Assert(result.Text == baseline.Text && result.Words.Select(w => w.Bounds).SequenceEqual(baseline.Words.Select(w => w.Bounds)), "DPI metadata changed OCR pixels or word geometry");
            baseline = result;
        }
        var punctuation = new TextRecognitionResult("Hello , world .", [
            new("Hello", new(0, 0, 35, 20), 0),
            new(",", new(35, 0, 3, 20), 0),
            new("world", new(45, 0, 35, 20), 0),
            new(".", new(80, 0, 3, 20), 0)], "English").Format();
        Assert(punctuation.Text == "Hello, world." && punctuation.SelectedText([0, 1, 2, 3]) == punctuation.Text, "Punctuation introduced spaces in copied selections");
        var noSpaces = new TextRecognitionResult("中文。", [
            new("中", new(0, 0, 20, 20), 0, ""),
            new("文", new(20, 0, 20, 20), 0, ""),
            new("。", new(40, 0, 5, 20), 0, "")], "Chinese").Format();
        Assert(noSpaces.Text == "中文。" && noSpaces.SelectedText([0, 1, 2]) == "中文。", "Selection inserted spaces into text without word separators");
        foreach (string text in new[] { "vo.4.3", "vO.12.0", "v1.O.2" })
        {
            var corrected = VersionTextCorrection.Apply(new(text, [new(text, new(0, 0, 50, 20), 0)], "English"));
            Assert(!corrected.Text.Contains('o') && !corrected.Text.Contains('O'), "A dotted version retained an O/0 error");
        }
        foreach (string text in new[] { "hello", "voodoo", "OOO", "O C 0", "cavo.4.3" })
            Assert(VersionTextCorrection.Apply(new(text, [new(text, new(0, 0, 50, 20), 0)], "English")).Text == text, "Version disambiguation changed ordinary text");
    }

    private static void TestArrowSymbols()
    {
        foreach (string symbol in new[] { "→", "←", "—Y", "—i", "-4", "m", "w", "⊣", "⊢" })
        {
            using var image = new Bitmap(700, 100); image.SetResolution(96, 96);
            using (var graphics = Graphics.FromImage(image))
            using (var font = new Font("Segoe UI", 18))
            {
                graphics.Clear(Color.White);
                graphics.DrawString("Alpha " + symbol + " Beta", font, Brushes.Black, 14, 28);
            }
            var result = Pump(TextRecognition.RecognizeAsync(image));
            if (symbol is "→" or "←")
                Assert(result.Text.Contains(symbol), "A real inline arrow was not recovered: " + result.Text);
            else
                Assert(!result.Text.Contains('→') && !result.Text.Contains('←'), "Non-arrow pixels were replaced by an arrow: " + symbol + " -> " + result.Text);
        }
    }

    private static void TestDurationRecognition()
    {
        TextRecognitionResult Recognized(string text)
        {
            var words = new List<RecognizedWord>();
            string[] lines = text.Split('\n');
            for (int line = 0; line < lines.Length; line++)
            {
                int x = 0;
                foreach (string word in lines[line].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                { words.Add(new(word, new RectangleF(x, line * 24, word.Length * 8, 20), line)); x += (word.Length + 1) * 8; }
            }
            return new(text, words, "English");
        }
        foreach (var (input, expected) in new[]
        {
            ("Worked for 24m Ils", "Worked for 24m 11s"),
            ("Worked for 24m IIs", "Worked for 24m 11s"),
            ("Worked for 24m lls.", "Worked for 24m 11s."),
            ("Worked for 24m 1ls", "Worked for 24m 11s"),
            ("Worked for 24m 1 Is .", "Worked for 24m 11s."),
            ("Worked for 24m 11 s.", "Worked for 24m 11s."),
            ("Worked for 24m I5s", "Worked for 24m 15s"),
            ("Worked for 1h 5ls", "Worked for 1h 51s"),
            ("Worked for 1h 24m Ils\nIls parlent", "Worked for 1h 24m 11s\nIls parlent")
        })
        {
            var raw = Recognized(input);
            var corrected = DurationTextCorrection.Apply(raw);
            Assert(corrected.Text == expected.Replace("\n", Environment.NewLine), "Duration correction failed: " + corrected.Text);
            Assert(corrected.Words.Select(w => w.Bounds).Aggregate(RectangleF.Union) == raw.Words.Select(w => w.Bounds).Aggregate(RectangleF.Union), "Duration correction moved text");
            Assert(corrected.SelectedText(Enumerable.Range(0, corrected.Words.Count)) == corrected.Text, "Selected duration and Copy all disagree");
        }
        foreach (string text in new[]
        {
            "Ils parlent", "IIS server", "Worked for 24m IIS", "Worked for 24m Ils parlent",
            "Worked for 24ms", "Worked for 24m 11s", "Worked for 24m 6Is", "IIs", "for 24m Files"
        })
            Assert(DurationTextCorrection.Apply(Recognized(text)).Text == text, "Ordinary text was changed: " + text);

        var merged = new TextRecognitionResult("Worked for 24m Ils", [
            new("Worked", new RectangleF(0, 0, 40, 20), 0),
            new("for", new RectangleF(48, 0, 20, 20), 0),
            new("24m Ils", new RectangleF(76, 0, 50, 20), 0)], "English");
        var mergedCorrection = DurationTextCorrection.Apply(merged);
        Assert(mergedCorrection.Text == "Worked for 24m 11s" && mergedCorrection.Words[2].Text == "24m 11s", "A merged OCR token corrupted the duration or copied text");

        foreach (bool dark in new[] { false, true })
        foreach (float points in new[] { 9f, 10f, 11f, 12f })
        {
            using var image = new Bitmap(420, 70);
            image.SetResolution(96, 96);
            using (var graphics = Graphics.FromImage(image))
            using (var font = new Font("Segoe UI Variable Text", points))
            {
                graphics.Clear(dark ? Color.FromArgb(32, 32, 32) : Color.White);
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                graphics.DrawString("Worked for 24m 11s", font, dark ? Brushes.White : Brushes.Black, 12, 20);
            }
            var actual = Pump(TextRecognition.RecognizeAsync(image));
            Assert(System.Text.RegularExpressions.Regex.IsMatch(actual.Text, @"24\s*m\s*11\s*s"), $"Duration misread at {points}pt, dark={dark}: {actual.Text}");
        }
    }

    private static void TestLineHighlights()
    {
        using var image = new Bitmap(1040, 260);
        image.SetResolution(96, 96);
        using (var graphics = Graphics.FromImage(image))
        using (var font = new Font("Segoe UI Variable Text", 14))
        {
            graphics.Clear(Color.FromArgb(32, 32, 32));
            graphics.DrawString("Improved small-text OCR and filtering for the supplied icons.", font, Brushes.White, 24, 30);
            graphics.DrawString("1. Replaced copy, X, and ... with custom rounded-stroke glyphs.", font, Brushes.White, 24, 100);
            graphics.DrawString("2. Passed 24 targeted checks; settings and shortcuts preserved.", font, Brushes.White, 24, 150);
        }
        var recognized = Pump(TextRecognition.RecognizeAsync(image));
        var rows = recognized.Words.Select((word, index) => (word, index)).GroupBy(item => item.word.Line).ToArray();
        Assert(rows.Length == 3 && recognized.Words.Count > 20, "The paragraph fixture lost its text lines: " + recognized.Text);
        foreach (bool native in new[] { false, true })
        {
            Theme.TestDark = native;
            using var host = new Form { ClientSize = native ? image.Size : new Size(780, 230), ShowInTaskbar = false, AutoScaleMode = AutoScaleMode.None };
            using var canvas = new TextRecognitionCanvas(image) { Dock = DockStyle.Fill, NativePixels = native };
            host.Controls.Add(canvas); canvas.SetResult(recognized, false);
            host.Show(); Application.DoEvents();
            Assert(canvas.LineBounds.Count == 3, "The paragraph did not have one box per line");
            using (var rendered = new Bitmap(canvas.Width, canvas.Height))
            {
                canvas.DrawToBitmap(rendered, new Rectangle(Point.Empty, rendered.Size));
                rendered.Save(Path.Combine(root, native ? "line-highlights-native.png" : "line-highlights-scaled.png"), ImageFormat.Png);
                var a = canvas.WordBounds(rows[0].First().index);
                var b = canvas.WordBounds(rows[0].Skip(1).First().index);
                int x = (int)((a.Right + b.Left) / 2), y = (int)(canvas.LineBounds[0].Top + 3);
                Assert(rendered.GetPixel(x, y).ToArgb() != image.GetPixel(0, 0).ToArgb(), "The line highlight left a gap between words");
            }
            Point Center(int index)
            {
                var bounds = canvas.WordBounds(index);
                return Point.Round(new PointF(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2));
            }
            void Drag(int start, int end)
            {
                var a = Center(start); var b = Center(end);
                CanvasEvent(canvas, "OnMouseDown", new(MouseButtons.Left, 1, a.X, a.Y, 0));
                CanvasEvent(canvas, "OnMouseMove", new(MouseButtons.Left, 0, b.X, b.Y, 0));
                CanvasEvent(canvas, "OnMouseUp", new(MouseButtons.Left, 1, b.X, b.Y, 0));
            }
            int first = rows[0].Skip(1).First().index, second = rows[0].Skip(2).First().index;
            Drag(first, second);
            Assert(canvas.SelectedIndices.Count == 2 && canvas.SelectionBounds.Count == 1, "Adjacent selected words were not one continuous span");
            Assert(recognized.SelectedText(canvas.SelectedIndices) == recognized.SelectedText([first, second]), "Word selection changed with line boxes");
            int nextLine = rows[1].Skip(1).First().index;
            Drag(nextLine, first);
            Assert(canvas.SelectionBounds.Count == 2, "A selection across lines was not split by line");
            Assert(canvas.SelectedIndices.Count == nextLine - first + 1, "Reverse multi-line drag lost words");
            using (var rendered = new Bitmap(canvas.Width, canvas.Height))
            {
                canvas.DrawToBitmap(rendered, new Rectangle(Point.Empty, rendered.Size));
                rendered.Save(Path.Combine(root, native ? "line-selection-native.png" : "line-selection-scaled.png"), ImageFormat.Png);
            }
            var beforeGap = canvas.WordBounds(rows[0].First().index);
            var afterGap = canvas.WordBounds(rows[0].Skip(1).First().index);
            int gapX = (int)((beforeGap.Right + afterGap.Left) / 2), gapY = (int)(canvas.LineBounds[0].Top + canvas.LineBounds[0].Height / 2);
            CanvasEvent(canvas, "OnMouseDown", new(MouseButtons.Left, 1, gapX, gapY, 0));
            CanvasEvent(canvas, "OnMouseUp", new(MouseButtons.Left, 1, gapX, gapY, 0));
            Assert(canvas.SelectedIndices.Count == 1, "Clicking a space inside the line could not start a text selection");

            canvas.SelectAllWords();
            Assert(canvas.SelectedIndices.Count == recognized.Words.Count && canvas.SelectionBounds.Count == 3, "Select all did not retain line grouping");
            canvas.SetResult(new("", [], "English"), false);
            Assert(canvas.LineBounds.Count == 0 && canvas.SelectionBounds.Count == 0, "Old line highlights survived an empty result");
            canvas.SetResult(new("i", [new("i", new RectangleF(1, 1, 1, 1), 0)], "English"), false);
            using (var tiny = new Bitmap(canvas.Width, canvas.Height)) canvas.DrawToBitmap(tiny, new Rectangle(Point.Empty, tiny.Size));
            host.Close();
        }
        Theme.TestDark = null;
    }

    private static void TestSmallTextQuality()
    {
        foreach (bool dark in new[] { false, true })
        foreach (float points in new[] { 8f, 9f, 10f, 11f })
        foreach (string contraction in new[] { "Let's", "let's" })
        {
            using var image = new Bitmap(720, 80);
            image.SetResolution(96, 96);
            using (var graphics = Graphics.FromImage(image))
            using (var font = new Font("Segoe UI Variable Text", points))
            using (var brush = new SolidBrush(dark ? Color.FromArgb(190, 190, 190) : Color.FromArgb(70, 70, 70)))
            {
                graphics.Clear(dark ? Color.FromArgb(32, 32, 32) : Color.White);
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                graphics.DrawString(contraction + " keep going. Don't change O, C or 0.", font, brush, 12, 20);
            }
            var result = Pump(TextRecognition.RecognizeAsync(image));
            string text = result.Text.Replace('’', '\'');
            Assert(text.Contains("let's", StringComparison.OrdinalIgnoreCase), $"The apostrophe in let's was lost at {points}pt, dark={dark}: {text}");
            Assert(text.Contains("O") && text.Contains("C"), "Real O/C text was removed: " + text);
        }
        var mapped = TextRecognition.MapBounds(new RectangleF(42, 32, 30, 15), 0, new Size(324, 174), new Size(100, 50), 12);
        Assert(Math.Abs(mapped.X - 10) < .001 && Math.Abs(mapped.Y - 20f / 3) < .001 && Math.Abs(mapped.Width - 10) < .001 && Math.Abs(mapped.Height - 5) < .001, "OCR margin or scale shifted word coordinates");
        var rotated = TextRecognition.MapBounds(new RectangleF(30, 40, 10, 20), 90, new Size(100, 100), new Size(100, 100), 0);
        Assert(Math.Abs(rotated.X - 40) < .01 && Math.Abs(rotated.Y - 30) < .01 && Math.Abs(rotated.Width - 20) < .01, "Deskewed word geometry did not map back to the screen");
    }

    private static void TestIconFalsePositives()
    {
        foreach (string name in new[] { "refresh-icon.png", "edit-icon.png" })
        {
            using var icon = new Bitmap(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Ocr", name));
            var fakeWord = new RecognizedWord(name.StartsWith("refresh") ? "O" : "c", new RectangleF(0, 0, icon.Width, icon.Height), 0);
            Assert(IconTextFilter.IsIconLike(icon, fakeWord), "Disconnected icon strokes were retained as a letter: " + name);
            var filtered = IconTextFilter.Apply(icon, new TextRecognitionResult(fakeWord.Text, [fakeWord], "English"));
            Assert(filtered.Words.Count == 0 && filtered.Text.Length == 0, "The rejected icon remained in copied text");
            Assert(Pump(TextRecognition.RecognizeAsync(icon)).Words.Count == 0, "The icon was returned as text: " + name);
        }
        using var realLetter = new Bitmap(70, 70);
        foreach (string letter in new[] { "O", "C", "o", "c" })
        {
            using (var graphics = Graphics.FromImage(realLetter))
            using (var font = new Font("Segoe UI", 26))
            { graphics.Clear(Color.White); graphics.DrawString(letter, font, Brushes.Black, 12, 12); }
            Assert(!IconTextFilter.IsIconLike(realLetter, new RecognizedWord(letter, new RectangleF(0, 0, 70, 70), 0)), "A genuine standalone letter was filtered: " + letter);
        }

        using var refresh = new Bitmap(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Ocr", "refresh-icon.png"));
        using var edit = new Bitmap(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Ocr", "edit-icon.png"));
        using var row = new Bitmap(700, 90);
        using (var graphics = Graphics.FromImage(row))
        using (var font = new Font("Segoe UI", 12))
        {
            graphics.Clear(Color.FromArgb(32, 32, 32));
            graphics.DrawImageUnscaled(refresh, 12, 10);
            graphics.DrawImageUnscaled(edit, 75, 10);
            graphics.DrawString("Let's copy text. O C and 0.", font, Brushes.White, 150, 22);
        }
        var actual = Pump(TextRecognition.RecognizeAsync(row));
        Assert(actual.Words.All(w => w.Bounds.Left > 130), "An inline icon was treated as text");
        Assert(actual.Text.Contains("Let's") && actual.Text.Contains("O C"), "Filtering damaged adjacent text: " + actual.Text);
        var raw = new TextRecognitionResult("O" + Environment.NewLine + "c" + Environment.NewLine + actual.Text,
            [new("O", new RectangleF(12, 10, refresh.Width, refresh.Height), 0),
             new("c", new RectangleF(75, 10, edit.Width, edit.Height), 1),
             ..actual.Words.Select(word => word with { Line = word.Line + 2 })], "English");
        var cleaned = IconTextFilter.Apply(row, raw);
        Assert(cleaned.Text == actual.Text && cleaned.Words.Count == actual.Words.Count, "Icon cleanup changed neighboring text or left ghost characters");
    }

    private static void TestRoundedToolbarGlyphs()
    {
        using var atlas = new Bitmap(850, 420);
        using (var graphics = Graphics.FromImage(atlas))
        using (var labelFont = new Font("Segoe UI", 11))
        {
            graphics.Clear(Color.FromArgb(29, 31, 36));
            for (int row = 0; row < 5; row++)
            {
                float scale = new[] { 1f, 1.25f, 1.5f, 2f, 3f }[row];
                graphics.DrawString((scale * 100) + "%", labelFont, Brushes.White, 20, 25 + row * 78);
                int column = 0;
                foreach (var glyph in new[] { ToolbarGlyph.Back, ToolbarGlyph.Copy, ToolbarGlyph.More, ToolbarGlyph.Close })
                {
                    ToolbarGlyphs.Draw(graphics, glyph, new RectangleF(140 + column++ * 170, 20 + row * 78, 20 * scale, 20 * scale), Color.White);
                }
            }
        }
        atlas.Save(Path.Combine(root, "rounded-toolbar-glyphs.png"), ImageFormat.Png);
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

    private static void TestStartupCommands()
    {
        const string executable = @"C:\Apps\Snipping Tool+\Snipping Tool+.exe";
        Assert(StartupManager.MatchesInstall("\"" + executable + "\" --background", executable), "New startup command rejected");
        Assert(StartupManager.MatchesInstall(@"""C:\Apps\Snipping Tool+\QuietSnip.exe"" --background", executable), "Legacy startup command rejected");
        Assert(StartupManager.MatchesInstall(@"""c:\apps\snipping tool+\QUIETSNIP.EXE"" --background", executable), "Windows path case rejected");
        Assert(!StartupManager.MatchesInstall(@"""C:\Other\QuietSnip.exe"" --background", executable), "Another installation matched");
        Assert(!StartupManager.MatchesInstall(@"""C:\Apps\Snipping Tool+\QuietSnip.exe"" --quit", executable), "Wrong arguments matched");
        Assert(!StartupManager.MatchesInstall(null, executable), "Missing startup entry matched");
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
        using var original = new HotkeyWindow("SnippingToolPlus.Tests.Original");
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
        using var other = new HotkeyWindow("SnippingToolPlus.Tests.Other");
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
        using var window = new HotkeyWindow("SnippingToolPlus.OptionalTests");
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
        var lastAction = form.ActionButtons[AppAction.TextActions];
        var scroll = (Panel)lastAction.Parent!;
        scroll.ScrollControlIntoView(lastAction); Application.DoEvents();
        Assert(scroll.ClientRectangle.Contains(lastAction.Bounds), "The last text action is clipped after scrolling");
        Assert(!scroll.HorizontalScroll.Visible, "Shortcut actions require horizontal scrolling");
        using (var rendered = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(rendered, new Rectangle(Point.Empty, rendered.Size));
            rendered.Save(Path.Combine(root, "settings-text-shortcuts.png"), ImageFormat.Png);
        }
        foreach (var input in Descendants(form).OfType<HotkeyBox>().Where(input => input.AccessibleName != "Copy to clipboard shortcut" && input.AccessibleName != "Save + copy shortcut"))
            Assert(!input.Enabled, "Running an action enabled its global hotkey");
        form.Close();
    }

    private static void TestIcons()
    {
        using var data = typeof(CaptureMemory).Assembly.GetManifestResourceStream("SnippingToolPlus.app.ico")!;
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
        using var owner = new Form { Text = "Snipping Tool+ confirmation check", Size = new Size(360, 180), ShowInTaskbar = false };
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

    private static void TestRapidCapture()
    {
        Rectangle bounds = SystemInformation.VirtualScreen;
        using var desktop = new Bitmap(bounds.Width, bounds.Height);
        using var owner = new HotkeyWindow("SnippingToolPlus.Tests.RapidCapture");
        using var notice = new CaptureNotice("Copied", new Rectangle(bounds.Left + 100, bounds.Top + 100, 300, 200));
        notice.Show();
        Application.DoEvents();
        using var overlay = new SelectionOverlay(desktop, bounds, CaptureMode.Copy);
        using var cancel = new System.Windows.Forms.Timer { Interval = 1300 };
        cancel.Tick += (_, _) => { cancel.Stop(); OverlayEvent(overlay, "OnKeyDown", new KeyEventArgs(Keys.Escape)); };
        overlay.Shown += (_, _) => cancel.Start();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        DialogResult result = overlay.ShowDialog(owner);
        Assert(elapsed.ElapsedMilliseconds >= 1100,
            $"Second overlay closed after {elapsed.ElapsedMilliseconds}ms when the previous confirmation expired");
        Assert(result == DialogResult.Cancel, "Explicit cancellation did not close the second overlay");
    }

    private static void TestRapidCaptureThroughTray()
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var confirm = typeof(TrayApplication).GetMethod("Confirm", flags)!;
        var execute = typeof(TrayApplication).GetMethod("Execute", flags)!;
        var busy = typeof(TrayApplication).GetField("busy", flags)!;
        using var app = new TrayApplication("SnippingToolPlus.Tests.RapidCapture." + Guid.NewGuid().ToString("N"));
        for (int cycle = 0; cycle < 3; cycle++)
        {
            confirm.Invoke(app, [cycle % 2 == 0 ? "Copied" : "Saved + copied"]);
            var notice = Application.OpenForms.OfType<CaptureNotice>().Single();
            bool noticeDismissed = false;
            SelectionOverlay? active = null;
            long shownAt = 0, closedAt = 0;
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            using var watchdog = new System.Windows.Forms.Timer { Interval = 25 };
            watchdog.Tick += (_, _) =>
            {
                var overlay = Application.OpenForms.OfType<SelectionOverlay>().FirstOrDefault();
                if (active == null && overlay != null)
                {
                    active = overlay; shownAt = elapsed.ElapsedMilliseconds;
                    noticeDismissed = notice.IsDisposed || !notice.Visible;
                    overlay.FormClosed += (_, _) => closedAt = elapsed.ElapsedMilliseconds;
                }
                if (active is { IsDisposed: false, Visible: true } && elapsed.ElapsedMilliseconds - shownAt >= 1300)
                    OverlayEvent(active, "OnKeyDown", new KeyEventArgs(Keys.Escape));
            };
            watchdog.Start();
            execute.Invoke(app, [AppAction.CopySnip]);
            try
            {
                while ((bool)busy.GetValue(app)! && elapsed.ElapsedMilliseconds < 5000)
                { Application.DoEvents(); Thread.Sleep(5); }
                Assert(active != null, "The next capture did not open");
                Assert(closedAt - shownAt >= 1100, $"Capture cycle {cycle} closed after {closedAt - shownAt}ms");
                Assert(noticeDismissed, "The previous confirmation was still visible when the next capture opened");
                Assert(!(bool)busy.GetValue(app)!, "Cancelling left the app busy");
            }
            finally
            {
                watchdog.Stop();
                if (active is { IsDisposed: false }) OverlayEvent(active, "OnKeyDown", new KeyEventArgs(Keys.Escape));
            }
        }
    }

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

