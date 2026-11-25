using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace DynamicIsland.Services;

/// Updates from the repository's GitHub releases: finds a newer installer,
/// downloads it and runs it silently. The installer closes the island,
/// replaces the executable and starts it again.
public static class Updater
{
    const string LatestRelease =
        "https://api.github.com/repos/Arthur-Rabello/dynamic-island-windows/releases/latest";

    // Only installers from this repository's releases are ever executed.
    const string TrustedDownloads =
        "https://github.com/Arthur-Rabello/dynamic-island-windows/releases/download/";

    static readonly HttpClient Http = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "DynamicIsland" } }
    };

    public sealed record Release(Version Version, string SetupUrl);

    /// Three parts, like the release tags, so 1.0.2 and 1.0.2.0 compare equal.
    public static Version Current
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    /// Null when the newest release is not newer than this build.
    public static async Task<Release?> FindNewerAsync()
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(LatestRelease));
        var root = doc.RootElement;

        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v'), out var latest) || latest <= Current) return null;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            string url = asset.GetProperty("browser_download_url").GetString() ?? "";

            if (name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase)
                && url.StartsWith(TrustedDownloads, StringComparison.Ordinal))
                return new Release(latest, url);
        }

        return null;
    }

    /// Starts the installer; the caller must exit right after so the
    /// executable is free to be replaced.
    public static async Task InstallAsync(Release release)
    {
        string file = Path.Combine(Path.GetTempPath(), $"DynamicIsland-{release.Version}-setup.exe");
        await File.WriteAllBytesAsync(file, await Http.GetByteArrayAsync(release.SetupUrl));

        Process.Start(new ProcessStartInfo(file, "/SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS")
        {
            UseShellExecute = true
        });
    }
}
