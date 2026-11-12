using System.IO;
using System.Text.Json;

namespace DynamicIsland.Services;

public sealed class Settings
{
    /// Horizontal centre of the island, in DIPs. Stored as the centre rather
    /// than the left edge so the island keeps its place while panels of
    /// different widths grow and shrink around it.
    public double? CenterX { get; set; }

    public double? Top { get; set; }

    /// Empty means let wttr.in resolve the location from the request IP.
    public string WeatherLocation { get; set; } = "";

    public string LastPanel { get; set; } = "Calendar";
}

public static class SettingsStore
{
    static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland");

    static readonly string File = Path.Combine(Folder, "settings.json");

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (!System.IO.File.Exists(File)) return new Settings();
            return JsonSerializer.Deserialize<Settings>(System.IO.File.ReadAllText(File))
                ?? new Settings();
        }
        catch { return new Settings(); }
    }

    public static void Save(Settings settings)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            System.IO.File.WriteAllText(File, JsonSerializer.Serialize(settings, Options));
        }
        catch { }
    }
}
