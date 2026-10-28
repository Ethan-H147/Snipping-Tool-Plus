using System.Diagnostics;
using QuietSnip;

namespace QuietSnip.Diagnostics;

internal static class ContentionTest
{
    internal static int Run(string output)
    {
        using var lease = new ClipboardLease();
        string eventName = "Local\\QuietSnip.Diagnostics." + Guid.NewGuid().ToString("N");
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--hold-clipboard"); start.ArgumentList.Add(eventName);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start clipboard contention helper.");
        if (!ready.WaitOne(3000))
        {
            if (!child.HasExited) child.Kill();
            throw new InvalidOperationException("Helper could not lock the clipboard; no production delivery attempted.");
        }
        using var image = FixtureForm.MakePattern();
        var timer = Stopwatch.StartNew();
        var result = Program.Wait(CaptureService.DeliverAsync(image, CaptureMode.Save, Path.Combine(output, "save-before-copy")));
        if (result.Copied) lease.MarkWritten();
        double elapsed = timer.Elapsed.TotalMilliseconds;
        if (!child.WaitForExit(20000)) { child.Kill(); throw new TimeoutException("Clipboard helper did not exit."); }
        bool passed = !result.Copied && result.SavedPath != null && File.Exists(result.SavedPath);
        Program.WriteJson(Path.Combine(output, "contention.json"), new {
            Passed = passed, Result = result, ElapsedMs = elapsed, ChildExitCode = child.ExitCode,
            SavedFile = result.SavedPath == null ? null : PixelEvidence.Inspect(result.SavedPath), Restore = lease.Restore(),
            Scope = "Real clipboard lock across retry window. No forced clipboard writes while locked."
        });
        if (!passed) throw new InvalidOperationException("Busy clipboard did not produce the expected saved-but-not-copied result.");
        Console.WriteLine("PASS save survives busy clipboard and bounded retry exhaustion");
        return 0;
    }
}
