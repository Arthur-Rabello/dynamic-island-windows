using System.Net.Http;
using System.Text.Json;

namespace DynamicIsland.Services;

public sealed record WeatherReading(
    string Location,
    int TempC,
    int FeelsLikeC,
    int Humidity,
    int WindKmph,
    string WindDir,
    string Description,
    string Glyph);

/// Current conditions from wttr.in.
///
/// The request carries no identifying information beyond what any HTTP call
/// does: a generic user agent and, when no location is configured, the IP the
/// request comes from, which is how wttr.in geolocates. Setting
/// WeatherLocation in settings.json pins a city and avoids even that.
public sealed class WeatherService
{
    static readonly HttpClient Http = CreateClient();

    static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(15);

    WeatherReading? _cached;
    DateTime _fetchedAt = DateTime.MinValue;

    public string? LastError { get; private set; }

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIsland/1.0");
        return client;
    }

    public async Task<WeatherReading?> GetAsync(string location, bool force = false)
    {
        if (!force && _cached is not null && DateTime.UtcNow - _fetchedAt < CacheFor)
            return _cached;

        try
        {
            string place = Uri.EscapeDataString(location.Trim());
            string url = $"https://wttr.in/{place}?format=j1";

            string json = await Http.GetStringAsync(url);
            var reading = Parse(json);

            if (reading is not null)
            {
                _cached = reading;
                _fetchedAt = DateTime.UtcNow;
                LastError = null;
            }

            return reading ?? _cached;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return _cached;
        }
    }

    static WeatherReading? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("current_condition", out var conditions) ||
            conditions.GetArrayLength() == 0)
            return null;

        var now = conditions[0];

        string location = "";
        if (root.TryGetProperty("nearest_area", out var areas) && areas.GetArrayLength() > 0)
        {
            var area = areas[0];
            string city = FirstValue(area, "areaName");
            string region = FirstValue(area, "region");
            location = string.IsNullOrEmpty(region) || region == city ? city : $"{city}, {region}";
        }

        int code = Int(now, "weatherCode");

        return new WeatherReading(
            Location: location,
            TempC: Int(now, "temp_C"),
            FeelsLikeC: Int(now, "FeelsLikeC"),
            Humidity: Int(now, "humidity"),
            WindKmph: Int(now, "windspeedKmph"),
            WindDir: Str(now, "winddir16Point"),
            Description: FirstValue(now, "weatherDesc"),
            Glyph: GlyphFor(code));
    }

    static string Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";

    static int Int(JsonElement element, string name) =>
        int.TryParse(Str(element, name), out int parsed) ? parsed : 0;

    static string FirstValue(JsonElement element, string arrayName)
    {
        if (!element.TryGetProperty(arrayName, out var array) || array.GetArrayLength() == 0)
            return "";

        return array[0].TryGetProperty("value", out var value) ? value.GetString() ?? "" : "";
    }

    /// World Weather Online condition codes, which is what wttr.in reports.
    ///
    /// Only glyphs Segoe UI Emoji actually carries are used: U+1F32B (fog),
    /// for one, is absent and renders as an empty box, so mist falls back to
    /// the plain cloud.
    static string GlyphFor(int code) => code switch
    {
        113 => "☀",                                    // clear
        116 => "⛅",                                    // partly cloudy
        119 or 122 => "☁",                             // cloudy / overcast
        143 or 248 or 260 => "☁",                      // mist / fog
        176 or 263 or 266 or 293 or 296 or 299 or 302
            or 305 or 308 or 311 or 314 or 353 or 356
            or 359 => "\U0001F327",                         // rain
        179 or 182 or 185 or 227 or 230 or 317 or 320
            or 323 or 326 or 329 or 332 or 335 or 338
            or 350 or 362 or 365 or 368 or 371 or 374
            or 377 => "\U0001F328",                         // snow / sleet
        200 or 386 or 389 or 392 or 395 => "⛈",        // thunder
        _ => "☁"
    };
}
