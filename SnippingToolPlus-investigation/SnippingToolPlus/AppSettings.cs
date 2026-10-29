using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnippingToolPlus;

[Flags]
internal enum HotkeyModifiers : uint
{
    None = 0, Alt = 1, Control = 2, Shift = 4
}

internal readonly record struct Hotkey(Keys Key, HotkeyModifiers Modifiers)
{
    public bool IsValid => Enum.IsDefined(Key) && Key is >= Keys.A and <= Keys.Z or >= Keys.D0 and <= Keys.D9
        or >= Keys.F1 and <= Keys.F24 or Keys.PrintScreen or Keys.Insert or Keys.Home or Keys.End
        or Keys.PageUp or Keys.PageDown
        && (Modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt)) != 0
        && (Modifiers & ~(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift)) == 0;

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        parts.Add(Key is >= Keys.D0 and <= Keys.D9 ? ((int)Key - (int)Keys.D0).ToString() : Key.ToString());
        return string.Join(" + ", parts);
    }
}

internal enum CaptureShape { Rectangle, Freeform, FullScreen }
internal enum AppAction { CopySnip, SaveSnip, SaveLast, RepeatRegion, CopyAsFile, RevealLast, CopyPath, SaveProject, ExtractText, TextActions }

internal sealed record AppSettings
{
    public CaptureShape Shape { get; init; } = CaptureShape.Rectangle;
    public Dictionary<AppAction, Hotkey> ExtraHotkeys { get; init; } = new();
    public string ProjectFolder { get; init; } = "";
    public bool RememberRegions { get; init; } = true;
    public bool ShowConfirmation { get; init; } = true;
    public bool ExactSize { get; init; }
    public int CaptureWidth { get; init; } = 800;
    public int CaptureHeight { get; init; } = 600;
    public Hotkey CopyHotkey { get; init; } = new(Keys.S, HotkeyModifiers.Control | HotkeyModifiers.Alt);
    public Hotkey SaveHotkey { get; init; } = new(Keys.D, HotkeyModifiers.Control | HotkeyModifiers.Alt);
    public string SaveFolder { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Snipping Tool+");

    public void Validate()
    {
        if (!Enum.IsDefined(Shape)) throw new ArgumentException("Choose a valid capture mode.");
        if (CaptureWidth < 2 || CaptureHeight < 2 || CaptureWidth > 20000 || CaptureHeight > 20000)
            throw new ArgumentException("Capture dimensions must be between 2 and 20,000 pixels.");
        var used = new HashSet<Hotkey> { CopyHotkey, SaveHotkey };
        foreach (var pair in ExtraHotkeys)
        {
            if (!Enum.IsDefined(pair.Key) || pair.Key is AppAction.CopySnip or AppAction.SaveSnip || !pair.Value.IsValid)
                throw new ArgumentException("Choose a valid shortcut for each enabled action.");
            if (!used.Add(pair.Value)) throw new ArgumentException("Each action needs a different shortcut.");
        }
        if (!string.IsNullOrWhiteSpace(ProjectFolder) && (!Path.IsPathFullyQualified(ProjectFolder) || File.Exists(ProjectFolder)))
            throw new ArgumentException("Choose a full folder path for the project folder.");
        if (!CopyHotkey.IsValid || !SaveHotkey.IsValid)
            throw new ArgumentException("Choose shortcuts with Ctrl or Alt, plus a letter, number, function key, or navigation key.");
        if (CopyHotkey == SaveHotkey)
            throw new ArgumentException("Use a different shortcut for each capture mode.");
        if (string.IsNullOrWhiteSpace(SaveFolder) || !Path.IsPathFullyQualified(SaveFolder))
            throw new ArgumentException("Choose a full folder path for saved screenshots.");
        _ = Path.GetFullPath(SaveFolder);
        if (File.Exists(SaveFolder))
            throw new ArgumentException("The save location is a file. Choose a folder instead.");
    }

    public IEnumerable<KeyValuePair<AppAction, Hotkey>> Shortcuts()
    {
        yield return new(AppAction.CopySnip, CopyHotkey);
        yield return new(AppAction.SaveSnip, SaveHotkey);
        foreach (var pair in ExtraHotkeys) yield return pair;
    }
}

internal sealed class SettingsStore(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public string FilePath { get; } = Path.Combine(directory, "settings.json");
    public bool Exists => File.Exists(FilePath);

    public AppSettings Load()
    {
        if (!Exists) return new AppSettings();
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions)
            ?? throw new InvalidDataException("The settings file is empty.");
        settings.Validate();
        return settings;
    }

    public void Save(AppSettings settings)
    {
        settings.Validate();
        string temporaryPath = FilePath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, FilePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}

