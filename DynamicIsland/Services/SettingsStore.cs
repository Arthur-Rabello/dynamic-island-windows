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

    // What the island shows. Everything defaults to on, so an old
    // settings.json without these keys behaves exactly as before.
    public bool ShowWeather { get; set; } = true;
    public bool ShowBattery { get; set; } = true;
    public bool ShowPrivacyDots { get; set; } = true;

    /// The pill announces the next calendar event this many minutes ahead.
    public bool NextEventInPill { get; set; } = true;
    public int ReminderMinutes { get; set; } = 30;

    public bool MediaExpansion { get; set; } = true;
    public bool VolumeOsd { get; set; } = true;
    public bool BrightnessOsd { get; set; } = true;
    public bool BatteryOsd { get; set; } = true;

    public bool CalendarTab { get; set; } = true;
    public bool WeatherTab { get; set; } = true;
    public bool HardwareTab { get; set; } = true;
    public bool VolumeTab { get; set; } = true;

    // Google Calendar. The client id/secret come from the user's own Google
    // Cloud project (desktop-app credentials); the refresh token is not here,
    // it lives DPAPI-encrypted in google.token.
    /// Display name or AppUserModelId of the app whose notifications pop in
    /// the island. Empty keeps the feature off.
    public string MessageApp { get; set; } = "";

    public bool MessagesTab { get; set; } = true;

    public string GoogleClientId { get; set; } = "";
    public string GoogleClientSecret { get; set; } = "";
    public string GoogleEmail { get; set; } = "";
}

public static class SettingsStore
{
    internal static readonly string Folder = Path.Combine(
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
