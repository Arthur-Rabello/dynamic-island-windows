using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace DynamicIsland.Services;

public sealed record ForecastDay(DateTime Date, int MaxC, int MinC, string Glyph);

public sealed record WeatherReading(
    string Location,
    int TempC,
    int FeelsLikeC,
    int Humidity,
    int WindKmph,
    string WindDir,
    string Description,
    string Glyph,
    IReadOnlyList<ForecastDay> Forecast);

/// Current conditions and a seven-day forecast from Open-Meteo.
///
/// Open-Meteo is keyless but works in coordinates, so a configured city goes
/// through its geocoder first. With no city set, wttr.in supplies the
/// coordinates of whatever its IP lookup finds, which keeps the old
/// "empty means detect me" behaviour without adding a second geolocation
/// provider.
///
/// Nothing identifying is sent: a city name or a pair of coordinates, under a
/// generic user agent.
public sealed class WeatherService
{
    static readonly HttpClient Http = CreateClient();

    static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(15);

    static readonly CultureInfo Culture = new("pt-BR");

    WeatherReading? _cached;
    string _cachedFor = "";
    DateTime _fetchedAt = DateTime.MinValue;

    readonly Dictionary<string, (double Lat, double Lon, string Name)> _places = new();

    public string? LastError { get; private set; }

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIsland/1.0");
        return client;
    }

    public async Task<WeatherReading?> GetAsync(string location, bool force = false)
    {
        string key = location.Trim();

        // Keyed by location: changing the city used to keep serving the old
        // city's reading until the cache aged out.
        if (!force && _cached is not null && _cachedFor == key
            && DateTime.UtcNow - _fetchedAt < CacheFor)
            return _cached;

        try
        {
            var place = await ResolveAsync(key);
            if (place is null) return _cached;

            var reading = await LoadAsync(place.Value);
            if (reading is not null)
            {
                _cached = reading;
                _cachedFor = key;
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

    // ---- where ----------------------------------------------------------

    async Task<(double Lat, double Lon, string Name)?> ResolveAsync(string location)
    {
        if (_places.TryGetValue(location, out var known)) return known;

        var place = location.Length == 0
            ? await LocateByAddressAsync()
            : await GeocodeAsync(location);

        if (place is not null) _places[location] = place.Value;
        return place;
    }

    async Task<(double Lat, double Lon, string Name)?> GeocodeAsync(string city)
    {
        string url = "https://geocoding-api.open-meteo.com/v1/search?name="
                     + Uri.EscapeDataString(city) + "&count=1&language=pt&format=json";

        using var document = JsonDocument.Parse(await Http.GetStringAsync(url));

        if (!document.RootElement.TryGetProperty("results", out var results)
            || results.GetArrayLength() == 0)
        {
            LastError = $"Não encontrei a cidade {city}.";
            return null;
        }

        var first = results[0];
        return (first.GetProperty("latitude").GetDouble(),
                first.GetProperty("longitude").GetDouble(),
                first.GetProperty("name").GetString() ?? city);
    }

    /// Open-Meteo has no IP lookup, so wttr.in is kept for this one job.
    static async Task<(double Lat, double Lon, string Name)?> LocateByAddressAsync()
    {
        using var document = JsonDocument.Parse(await Http.GetStringAsync("https://wttr.in/?format=j1"));

        if (!document.RootElement.TryGetProperty("nearest_area", out var areas)
            || areas.GetArrayLength() == 0)
            return null;

        var area = areas[0];
        return (double.Parse(area.GetProperty("latitude").GetString()!, CultureInfo.InvariantCulture),
                double.Parse(area.GetProperty("longitude").GetString()!, CultureInfo.InvariantCulture),
                area.GetProperty("areaName")[0].GetProperty("value").GetString() ?? "");
    }

    // ---- what -----------------------------------------------------------

    static async Task<WeatherReading?> LoadAsync((double Lat, double Lon, string Name) place)
    {
        string url = string.Format(CultureInfo.InvariantCulture,
            "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}"
            + "&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,"
            + "wind_speed_10m,wind_direction_10m"
            + "&daily=weather_code,temperature_2m_max,temperature_2m_min"
            + "&timezone=auto&forecast_days=7",
            place.Lat, place.Lon);

        using var document = JsonDocument.Parse(await Http.GetStringAsync(url));
        var root = document.RootElement;

        if (!root.TryGetProperty("current", out var now)) return null;

        int code = now.GetProperty("weather_code").GetInt32();

        return new WeatherReading(
            Location: place.Name,
            TempC: Round(now, "temperature_2m"),
            FeelsLikeC: Round(now, "apparent_temperature"),
            Humidity: Round(now, "relative_humidity_2m"),
            WindKmph: Round(now, "wind_speed_10m"),
            WindDir: Compass(now.GetProperty("wind_direction_10m").GetDouble()),
            Description: DescriptionFor(code),
            Glyph: GlyphFor(code),
            Forecast: ReadForecast(root));
    }

    static IReadOnlyList<ForecastDay> ReadForecast(JsonElement root)
    {
        if (!root.TryGetProperty("daily", out var daily)) return [];

        var dates = daily.GetProperty("time");
        var codes = daily.GetProperty("weather_code");
        var highs = daily.GetProperty("temperature_2m_max");
        var lows = daily.GetProperty("temperature_2m_min");

        var days = new List<ForecastDay>();
        for (int i = 0; i < dates.GetArrayLength(); i++)
        {
            days.Add(new ForecastDay(
                DateTime.Parse(dates[i].GetString()!, CultureInfo.InvariantCulture),
                (int)Math.Round(highs[i].GetDouble()),
                (int)Math.Round(lows[i].GetDouble()),
                GlyphFor(codes[i].GetInt32())));
        }

        return days;
    }

    static int Round(JsonElement element, string name) =>
        (int)Math.Round(element.GetProperty(name).GetDouble());

    static string Compass(double degrees)
    {
        string[] points =
            ["N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE",
             "S", "SSO", "SO", "OSO", "O", "ONO", "NO", "NNO"];

        return points[(int)Math.Round(degrees / 22.5) % 16];
    }

    // ---- WMO codes ------------------------------------------------------

    /// Only glyphs Segoe UI Emoji actually carries: U+1F32B (fog) is absent and
    /// renders as an empty box, so fog falls back to the plain cloud.
    static string GlyphFor(int code) => code switch
    {
        0 => "☀",
        1 or 2 => "⛅",
        3 or 45 or 48 => "☁",
        56 or 57 or 66 or 67 or 71 or 73 or 75 or 77 or 85 or 86 => "\U0001F328",
        95 or 96 or 99 => "⛈",
        _ => "\U0001F327"
    };

    static string DescriptionFor(int code) => code switch
    {
        0 => "Céu limpo",
        1 => "Predominantemente limpo",
        2 => "Parcialmente nublado",
        3 => "Encoberto",
        45 or 48 => "Nevoeiro",
        51 or 53 or 55 => "Garoa",
        56 or 57 => "Garoa congelante",
        61 => "Chuva fraca",
        63 => "Chuva",
        65 => "Chuva forte",
        66 or 67 => "Chuva congelante",
        71 => "Neve fraca",
        73 => "Neve",
        75 => "Neve forte",
        77 => "Grãos de neve",
        80 => "Pancadas isoladas",
        81 => "Pancadas de chuva",
        82 => "Pancadas fortes",
        85 or 86 => "Pancadas de neve",
        95 => "Tempestade",
        96 or 99 => "Tempestade com granizo",
        _ => "Sem leitura"
    };

    /// Short weekday for the forecast strip; today reads as "hoje".
    public static string DayLabel(DateTime date) =>
        date.Date == DateTime.Today
            ? "hoje"
            : Culture.DateTimeFormat.AbbreviatedDayNames[(int)date.DayOfWeek].TrimEnd('.');
}
