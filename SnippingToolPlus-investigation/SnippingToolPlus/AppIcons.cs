using System.Runtime.InteropServices;

namespace SnippingToolPlus;

internal static class AppIcons
{
    public static int TrayPixels
    {
        get
        {
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            uint dpi = taskbar == IntPtr.Zero ? GetDpiForSystem() : GetDpiForWindow(taskbar);
            return Math.Max(16, GetSystemMetricsForDpi(49, dpi == 0 ? 96u : dpi));
        }
    }
    public static Icon Load(int pixels)
    {
        using var stream = OpenResource();
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt16() != 0 || reader.ReadUInt16() != 1) throw new InvalidDataException("Invalid app icon.");
        int count = reader.ReadUInt16();
        var frames = new List<(int Size, int Length, int Offset)>();
        for (int index = 0; index < count; index++)
        {
            int size = reader.ReadByte(); if (size == 0) size = 256;
            stream.Position += 7;
            int length = reader.ReadInt32(), offset = reader.ReadInt32();
            frames.Add((size, length, offset));
        }
        var selected = frames.OrderBy(frame => Math.Abs(frame.Size - pixels)).ThenByDescending(frame => frame.Size).First();
        stream.Position = selected.Offset;
        byte[] png = reader.ReadBytes(selected.Length);
        IntPtr handle = CreateIconFromResourceEx(png, (uint)png.Length, true, 0x00030000, pixels, pixels, 0);
        if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try { using var borrowed = Icon.FromHandle(handle); return (Icon)borrowed.Clone(); }
        finally { DestroyIcon(handle); }
    }

    // Keep the full ICO data for WinForms to choose the small and large window
    // icons independently. Tray icons use an exact-size native frame above.
    public static Icon LoadWindow()
    {
        using var stream = OpenResource();
        using var icon = new Icon(stream, 32, 32);
        return (Icon)icon.Clone();
    }
    private static Stream OpenResource() => typeof(AppIcons).Assembly.GetManifestResourceStream("SnippingToolPlus.app.ico")
        ?? throw new InvalidOperationException("The application icon is missing.");
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconFromResourceEx(byte[] data, uint length, [MarshalAs(UnmanagedType.Bool)] bool isIcon,
        uint version, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? windowName);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int metric, uint dpi);
}
