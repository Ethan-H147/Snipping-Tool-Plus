using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace QuietSnip.Diagnostics;

internal sealed record HdrRecovery(uint AdapterLow, int AdapterHigh, uint TargetId, uint SdrWhiteRaw);

internal static partial class DisplayEvidence
{
    internal static int RunHdrCycle(Options options, string output)
    {
        HdrRecovery initial = ReadHdrRecovery();
        string recovery = Path.Combine(output, "hdr-recovery.json");
        Program.WriteJson(recovery, initial);
        string stopName = "Local\\QuietSnip.HdrRestore." + Guid.NewGuid().ToString("N");
        using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, stopName);
        var launch = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        launch.ArgumentList.Add("--hdr-watchdog"); launch.ArgumentList.Add(recovery); launch.ArgumentList.Add(stopName);
        using var guard = Process.Start(launch) ?? throw new InvalidOperationException("Cannot start HDR restoration watchdog; display was not changed.");
        bool restored = false;
        bool changed = false;
        Exception? failure = null;
        try
        {
            SetHdr(initial, false);
            changed = true;
            WaitHdr(initial, false);
            if (options.Command == "--hdr-window")
            {
                Program.WriteJson(Path.Combine(output, "off-ready.json"), Read());
                Console.WriteLine("HDR_OFF_READY — automatic restore after 25 seconds");
                Thread.Sleep(25000);
            }
            else RunFixture(options, Path.Combine(output, "hdr-off"), "HDR off; known SDR fixture");
        }
        catch (Exception error) { failure = error; }
        finally
        {
            try
            {
                if (changed) SetHdr(initial, true);
                WaitHdr(initial, true);
                uint white = WhiteFor(initial);
                restored = white == initial.SdrWhiteRaw;
                Program.WriteJson(Path.Combine(output, "restoration.json"), new {
                    HdrActive = true, ExpectedWhiteRaw = initial.SdrWhiteRaw, ActualWhiteRaw = white,
                    WhiteLevelRestored = restored, Environment = Read()
                });
                if (restored) { stop.Set(); guard.WaitForExit(5000); }
                else failure = new InvalidOperationException("HDR is on but SDR white level changed; restore the recorded white level through Windows Settings.", failure);
            }
            catch (Exception error) { failure = new InvalidOperationException("HDR restoration failed; the watchdog will retry.", error); }
        }
        if (failure != null)
        {
            Program.WriteJson(Path.Combine(output, "failure.json"), new { Error = failure.ToString(), HdrAndWhiteRestored = restored });
            throw failure;
        }
        if (options.Command != "--hdr-window") RunFixture(options, Path.Combine(output, "hdr-on-restored"), "HDR restored; known SDR fixture");
        Program.WriteJson(Path.Combine(output, "completed.json"), new {
            HdrAndWhiteRestored = restored, InitialWhiteNits = initial.SdrWhiteRaw / 1000.0 * 80,
            Final = Read(), Scope = "Single-monitor HDR off/on comparison of SDR content. No actual HDR content or production capture changes."
        });
        return 0;
    }

    private static void RunFixture(Options options, string folder, string label)
    {
        Directory.CreateDirectory(folder);
        Program.WriteJson(Path.Combine(folder, "environment.json"), Read());
        using var fixture = new FixtureForm(options with { Command = "--fixture", Output = folder, Label = label,
            Cycles = 100, ClipboardRoundtrip = false, Monitor = 0, Hold = false }, folder);
        Application.Run(fixture);
        if (fixture.Failure != null) throw new InvalidOperationException("Controlled fixture failed: " + label, fixture.Failure);
    }

    private static HdrRecovery ReadHdrRecovery()
    {
        ValidateNativeLayouts();
        if (Marshal.SizeOf<HdrState>() != 24) throw new InvalidDataException("HDR set packet ABI mismatch.");
        int error = GetDisplayConfigBufferSizes(2, out uint paths, out uint modes);
        if (error != 0) throw new Win32Exception(error);
        if (paths != 1 || modes > 1024) throw new InvalidOperationException("This experiment requires exactly one active display path.");
        var path = new PathInfo[paths];
        IntPtr modeArray = Marshal.AllocHGlobal(checked((int)Math.Max(1, modes) * 64));
        try
        {
            error = QueryDisplayConfig(2, ref paths, path, ref modes, modeArray, IntPtr.Zero);
            if (error != 0 || paths != 1) throw new Win32Exception(error, "Display topology changed before experiment.");
            var target = path[0].Target;
            var info = new ColorInfo2 { Header = Header.For<ColorInfo2>(15, target.Adapter, target.Id) };
            error = GetColorInfo2(ref info);
            if (error != 0) throw new Win32Exception(error);
            if (info.Mode != 2 || (info.Flags & 32) == 0) throw new InvalidOperationException("Expected HDR already active and user enabled. No display change performed.");
            var result = new HdrRecovery(target.Adapter.Low, target.Adapter.High, target.Id, 0);
            return result with { SdrWhiteRaw = WhiteFor(result) };
        }
        finally { Marshal.FreeHGlobal(modeArray); }
    }

    private static Luid Adapter(HdrRecovery state) => new() { Low = state.AdapterLow, High = state.AdapterHigh };
    private static uint WhiteFor(HdrRecovery state)
    {
        var white = new WhiteLevel { Header = Header.For<WhiteLevel>(11, Adapter(state), state.TargetId) };
        int error = GetWhite(ref white);
        if (error != 0) throw new Win32Exception(error);
        return white.Level;
    }
    private static void SetHdr(HdrRecovery state, bool enabled)
    {
        var packet = new HdrState { Header = Header.For<HdrState>(16, Adapter(state), state.TargetId), Enabled = enabled ? 1u : 0u };
        int error = SetHdrInfo(ref packet);
        if (error != 0) throw new Win32Exception(error, "DisplayConfigSetDeviceInfo HDR state failed.");
    }
    private static void WaitHdr(HdrRecovery state, bool enabled)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(8))
        {
            var info = new ColorInfo2 { Header = Header.For<ColorInfo2>(15, Adapter(state), state.TargetId) };
            int error = GetColorInfo2(ref info);
            if (error == 0 && (info.Mode == 2) == enabled && ((info.Flags & 32) != 0) == enabled)
            { Thread.Sleep(1500); return; }
            Thread.Sleep(100);
        }
        throw new TimeoutException("HDR state did not settle to " + enabled);
    }

    internal static int HdrWatchdog(string recoveryFile, string stopName)
    {
        var initial = JsonSerializer.Deserialize<HdrRecovery>(File.ReadAllText(recoveryFile)) ?? throw new InvalidDataException("Missing HDR recovery state.");
        using var stop = EventWaitHandle.OpenExisting(stopName);
        if (stop.WaitOne(45000)) return 0;
        SetHdr(initial, true); WaitHdr(initial, true);
        Program.WriteJson(Path.Combine(Path.GetDirectoryName(recoveryFile)!, "watchdog-restoration.json"),
            new { HdrRestored = true, SdrWhiteRaw = WhiteFor(initial), ExpectedWhiteRaw = initial.SdrWhiteRaw });
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)] private struct HdrState { public Header Header; public uint Enabled; }
    [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo")] private static extern int SetHdrInfo(ref HdrState state);
}
