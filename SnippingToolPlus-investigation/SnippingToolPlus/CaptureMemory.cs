using System.Collections.Specialized;
using System.Text.Json;

namespace SnippingToolPlus;

internal static class ActionNames
{
    public static string ButtonLabel(AppAction action) => action switch
    {
        AppAction.CopySnip or AppAction.SaveSnip or AppAction.SaveProject => "Capture",
        AppAction.SaveLast => "Save", AppAction.RepeatRegion => "Repeat",
        AppAction.CopyAsFile => "Copy file", AppAction.RevealLast => "Show",
        AppAction.CopyPath => "Copy path", AppAction.ExtractText => "Capture", AppAction.TextActions => "Open", _ => "Run"
    };
    public static string Label(AppAction action) => action switch
    {
        AppAction.CopySnip => "Copy to clipboard", AppAction.SaveSnip => "Save + copy",
        AppAction.SaveLast => "Save last capture", AppAction.RepeatRegion => "Repeat last region",
        AppAction.CopyAsFile => "Copy last as file", AppAction.RevealLast => "Reveal last saved file",
        AppAction.CopyPath => "Copy last file path", AppAction.SaveProject => "Save to project folder",
        AppAction.ExtractText => "Extract text from screen", AppAction.TextActions => "Text actions for last snip",
        _ => action.ToString()
    };
}

internal sealed class CaptureMemory : IDisposable
{
    public Bitmap? Image { get; private set; }
    public string? SavedPath { get; private set; }
    public string? LastSavedPath { get; private set; }

    public void Replace(Bitmap image, string? savedPath)
    {
        var previous = Image;
        Image = (Bitmap)image.Clone();
        SavedPath = savedPath;
        if (savedPath != null) LastSavedPath = savedPath;
        previous?.Dispose();
    }

    public string EnsureSaved(string directory)
    {
        if (Image == null) throw new InvalidOperationException("Take a screenshot first.");
        if (SavedPath == null || !File.Exists(SavedPath)) SavedPath = CaptureService.SavePng(Image, directory);
        LastSavedPath = SavedPath;
        return SavedPath;
    }

    public void CopyAsFile(string directory)
    {
        string path = EnsureSaved(directory);
        Clipboard.SetFileDropList(new StringCollection { path });
    }
    public void MarkSaved(string? path)
    {
        SavedPath = path;
        if (path != null) LastSavedPath = path;
    }

    public void Dispose() { Image?.Dispose(); Image = null; }
}

internal sealed record RegionRecipe
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int MonitorWidth { get; init; }
    public int MonitorHeight { get; init; }
    public CaptureShape Shape { get; init; }
    public Point[] Polygon { get; init; } = [];
    public Rectangle OnMonitor(Rectangle monitor) => new(monitor.X + X, monitor.Y + Y, Width, Height);
}

internal sealed class RegionMemory
{
    private readonly string? path;
    private Dictionary<string, RegionRecipe> regions = new();
    public RegionMemory(string? directory = null)
    {
        path = directory == null ? null : Path.Combine(directory, "regions.json");
        try { if (path != null && File.Exists(path)) regions = JsonSerializer.Deserialize<Dictionary<string, RegionRecipe>>(File.ReadAllText(path)) ?? new(); }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { }
    }

    public void Remember(string device, Rectangle monitor, Rectangle screenSelection, CaptureShape shape, Point[] polygon)
    {
        regions[device] = new RegionRecipe { X = screenSelection.X - monitor.X, Y = screenSelection.Y - monitor.Y,
            Width = screenSelection.Width, Height = screenSelection.Height, MonitorWidth = monitor.Width,
            MonitorHeight = monitor.Height, Shape = shape, Polygon = polygon.ToArray() };
        if (path != null)
        {
            string temporary = path + ".tmp";
            try { File.WriteAllText(temporary, JsonSerializer.Serialize(regions)); File.Move(temporary, path, true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } }
        }
    }

    public RegionRecipe? Recall(string device, Rectangle monitor, Rectangle desktop)
    {
        if (!regions.TryGetValue(device, out var recipe) || recipe.MonitorWidth != monitor.Width || recipe.MonitorHeight != monitor.Height)
            return null;
        Rectangle selected = recipe.OnMonitor(monitor);
        if (selected.Width < 2 || selected.Height < 2 || !desktop.Contains(selected)) return null;
        return recipe;
    }
}

internal static class SelectionGeometry
{
    public static Rectangle Move(Rectangle rectangle, int dx, int dy, Size desktop) => new(
        Math.Clamp(rectangle.X + dx, 0, Math.Max(0, desktop.Width - rectangle.Width)),
        Math.Clamp(rectangle.Y + dy, 0, Math.Max(0, desktop.Height - rectangle.Height)),
        rectangle.Width, rectangle.Height);
}

