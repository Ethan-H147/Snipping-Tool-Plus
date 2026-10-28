using Microsoft.Win32;

namespace QuietSnip;

internal static class StartupManager
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static string Command => $"\"{Application.ExecutablePath}\" --background";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunPath);
        return string.Equals(key?.GetValue("QuietSnip") as string, Command, StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        if (IsEnabled() == enabled) return;
        using var key = Registry.CurrentUser.CreateSubKey(RunPath, true);
        if (enabled) key.SetValue("QuietSnip", Command);
        else key.DeleteValue("QuietSnip", false);
    }
}

