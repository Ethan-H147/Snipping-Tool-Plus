using System.Runtime.InteropServices;
using System.Text;

namespace QuietSnip.Diagnostics;

internal static partial class DisplayEvidence
{
    internal static object Read()
    {
        object paths;
        try { paths = ReadPaths(); } catch (Exception error) { paths = new { Unavailable = error.Message }; }
        var drivers = new List<object>();
        for (uint i = 0; i < 32; i++)
        {
            var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, i, ref device, 0)) break;
            if ((device.Flags & 1) != 0) drivers.Add(new { device.Name, device.Description, device.Id, device.RegistryKey });
        }
        return new {
            RecordedUtc = DateTimeOffset.UtcNow, OS = Environment.OSVersion.VersionString,
            Runtime = RuntimeInformation.FrameworkDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            SessionId = System.Diagnostics.Process.GetCurrentProcess().SessionId, Environment.UserInteractive,
            Screens = Screen.AllScreens.Select((screen, index) => new {
                Index = index, screen.DeviceName, screen.Bounds, screen.WorkingArea, screen.Primary,
                IcmProfile = Profile(screen.DeviceName)
            }).ToArray(), Paths = paths, Adapters = drivers,
            Notes = "Read-only. AdvancedColorEnabled alone is not proof of HDR; prefer Info2.ActiveColorMode. ICM profile lookup is legacy GDI state, not proof of capture encoding. Driver version and viewer version must be recorded separately."
        };
    }

    private static object ReadPaths()
    {
        const uint activePaths = 2;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int result = GetDisplayConfigBufferSizes(activePaths, out uint pathCount, out uint modeCount);
            if (result != 0) return new { Error = result, Operation = "GetDisplayConfigBufferSizes" };
            if (pathCount > 128 || modeCount > 1024) throw new InvalidDataException("Unexpected display array size.");
            var paths = new PathInfo[pathCount];
            IntPtr modes = Marshal.AllocHGlobal(checked((int)Math.Max(1, modeCount) * 64));
            try
            {
                result = QueryDisplayConfig(activePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                if (result == 122) continue;
                if (result != 0) return new { Error = result, Operation = "QueryDisplayConfig" };
                return paths.Take((int)pathCount).Select(path => ReadPath(path)).ToArray();
            }
            finally { Marshal.FreeHGlobal(modes); }
        }
        return new { Error = "Display topology changed during three read attempts." };
    }

    private static object ReadPath(PathInfo path)
    {
        var source = new SourceName { Header = Header.For<SourceName>(1, path.Source.Adapter, path.Source.Id) };
        int sourceError = GetSourceName(ref source);
        var target = new TargetName { Header = Header.For<TargetName>(2, path.Target.Adapter, path.Target.Id) };
        int targetError = GetTargetName(ref target);
        var oldColor = new ColorInfo { Header = Header.For<ColorInfo>(9, path.Target.Adapter, path.Target.Id) };
        int oldError = GetColorInfo(ref oldColor);
        var color = new ColorInfo2 { Header = Header.For<ColorInfo2>(15, path.Target.Adapter, path.Target.Id) };
        int colorError = GetColorInfo2(ref color);
        var white = new WhiteLevel { Header = Header.For<WhiteLevel>(11, path.Target.Adapter, path.Target.Id) };
        int whiteError = GetWhite(ref white);
        return new {
            GdiDeviceName = sourceError == 0 ? source.Name : null, SourceNameError = sourceError,
            MonitorName = targetError == 0 ? target.FriendlyName : null, TargetNameError = targetError,
            TargetId = path.Target.Id, AdapterLuid = $"{path.Target.Adapter.High:X8}{path.Target.Adapter.Low:X8}",
            RefreshHz = path.Target.Refresh.Denominator == 0 ? (double?)null : path.Target.Refresh.Numerator / (double)path.Target.Refresh.Denominator,
            Legacy = new { Error = oldError, Flags = oldError == 0 ? oldColor.Flags : (uint?)null,
                AdvancedColorEnabled = oldError == 0 ? (bool?)((oldColor.Flags & 2) != 0) : null,
                BitsPerChannel = oldError == 0 ? oldColor.Bits : (uint?)null },
            Info2 = new { Error = colorError, Flags = colorError == 0 ? color.Flags : (uint?)null,
                ActiveColorMode = colorError == 0 ? color.Mode switch { 0 => "SDR", 1 => "WCG", 2 => "HDR", _ => "Unknown: " + color.Mode } : "Unavailable",
                HdrUserEnabled = colorError == 0 ? (bool?)((color.Flags & 32) != 0) : null,
                BitsPerChannel = colorError == 0 ? color.Bits : (uint?)null },
            SdrWhite = new { Error = whiteError, Raw = whiteError == 0 ? white.Level : (uint?)null,
                Nits = whiteError == 0 ? white.Level / 1000.0 * 80 : (double?)null }
        };
    }

    private static object Profile(string device)
    {
        IntPtr dc = CreateDC("DISPLAY", device, null, IntPtr.Zero);
        if (dc == IntPtr.Zero) return new { Error = Marshal.GetLastWin32Error() };
        try
        {
            uint size = 32768; var path = new StringBuilder((int)size);
            if (!GetICMProfile(dc, ref size, path)) return new { Error = Marshal.GetLastWin32Error() };
            string file = path.ToString();
            try { return new { Path = file, Sha256 = File.Exists(file) ? Program.Hash(file) : null }; }
            catch (Exception error) { return new { Path = file, Error = error.Message }; }
        }
        finally { DeleteDC(dc); }
    }

    internal static void ValidateNativeLayouts()
    {
        if (Marshal.SizeOf<Luid>() != 8 || Marshal.SizeOf<Header>() != 20 || Marshal.SizeOf<SourceInfo>() != 20 ||
            Marshal.SizeOf<TargetInfo>() != 48 || Marshal.SizeOf<PathInfo>() != 72 || Marshal.SizeOf<ColorInfo2>() != 36)
            throw new InvalidDataException("DisplayConfig ABI layout mismatch.");
    }

    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct Header {
        public uint Type, Size; public Luid Adapter; public uint Id;
        public static Header For<T>(uint type, Luid adapter, uint id) => new() { Type = type, Size = (uint)Marshal.SizeOf<T>(), Adapter = adapter, Id = id };
    }
    [StructLayout(LayoutKind.Sequential)] private struct SourceInfo { public Luid Adapter; public uint Id, Mode, Status; }
    [StructLayout(LayoutKind.Sequential)] private struct Rational { public uint Numerator, Denominator; }
    [StructLayout(LayoutKind.Sequential)] private struct TargetInfo {
        public Luid Adapter; public uint Id, Mode, Technology, Rotation, Scaling; public Rational Refresh;
        public uint ScanLine; public int Available; public uint Status;
    }
    [StructLayout(LayoutKind.Sequential)] private struct PathInfo { public SourceInfo Source; public TargetInfo Target; public uint Flags; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct SourceName {
        public Header Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct TargetName {
        public Header Header; public uint Flags, Technology; public ushort Manufacturer, Product; public uint Connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string FriendlyName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ColorInfo { public Header Header; public uint Flags, Encoding, Bits; }
    [StructLayout(LayoutKind.Sequential)] private struct ColorInfo2 { public Header Header; public uint Flags, Encoding, Bits, Mode; }
    [StructLayout(LayoutKind.Sequential)] private struct WhiteLevel { public Header Header; public uint Level; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DisplayDevice {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string RegistryKey;
    }
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] PathInfo[] pathArray, ref uint modes, IntPtr modeArray, IntPtr topology);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetSourceName(ref SourceName data);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetTargetName(ref TargetName data);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetColorInfo(ref ColorInfo data);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetColorInfo2(ref ColorInfo2 data);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetWhite(ref WhiteLevel data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice data, uint flags);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateDC(string driver, string device, string? output, IntPtr init);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetICMProfile(IntPtr dc, ref uint size, StringBuilder path);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
