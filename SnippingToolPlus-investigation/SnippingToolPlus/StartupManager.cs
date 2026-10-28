using Microsoft.Win32;

namespace SnippingToolPlus;

internal static class StartupManager
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "Snipping Tool+";
    private const string LegacyEntryName = "QuietSnip";
    private static string Command => $"\"{Application.ExecutablePath}\" --background";

    internal static bool MatchesInstall(string? command, string executablePath)
    {
        string legacyPath = Path.Combine(Path.GetDirectoryName(executablePath)!, "QuietSnip.exe");
        return string.Equals(command, $"\"{executablePath}\" --background", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, $"\"{legacyPath}\" --background", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunPath);
        return MatchesInstall(key?.GetValue(EntryName) as string, Application.ExecutablePath)
            || MatchesInstall(key?.GetValue(LegacyEntryName) as string, Application.ExecutablePath);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunPath, true);
        if (enabled) key.SetValue(EntryName, Command);
        else if (MatchesInstall(key.GetValue(EntryName) as string, Application.ExecutablePath))
            key.DeleteValue(EntryName, false);
        // Only replace a legacy entry belonging to this installation folder.
        if (MatchesInstall(key.GetValue(LegacyEntryName) as string, Application.ExecutablePath))
            key.DeleteValue(LegacyEntryName, false);
    }
}
