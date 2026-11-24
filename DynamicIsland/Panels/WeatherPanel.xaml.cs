using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using DynamicIsland.Services;

namespace DynamicIsland.Panels;

public partial class WeatherPanel : UserControl
{
    static readonly CultureInfo Culture = new("pt-BR");

    readonly WeatherService _weather;

    /// Empty asks wttr.in to resolve the location from the request IP; set
    /// WeatherLocation in settings.json to pin a city instead.
    public string Location { get; set; } = "";

    /// Shares the window's service so the panel and the idle pill hit one
    /// cache rather than making two requests for the same reading.
    public WeatherPanel(WeatherService weather)
    {
        _weather = weather;
        InitializeComponent();
    }

    public async Task RefreshAsync(bool force = false)
    {
        var reading = await _weather.GetAsync(Location, force);

        if (reading is null)
        {
            LocationLabel.Text = "Sem conexão";
            Description.Text = _weather.LastError ?? "Não foi possível consultar o wttr.in";
            return;
        }

        // wttr.in answers with the nearest weather station, which for a pinned
        // city is often a neighbouring town: Criciuma comes back as Icara. The
        // city the user typed is the one to show, as a phone weather app does.
        LocationLabel.Text = Location.Trim().Length > 0
            ? Location.Trim()
            : string.IsNullOrWhiteSpace(reading.Location)
            ? "Tempo agora"
            : reading.Location;

        BuildForecast(reading.Forecast);

        ConditionGlyph.Text = reading.Glyph;
        Temperature.Text = $"{reading.TempC}°";
        Description.Text = reading.Description;
        FeelsLike.Text = $"{reading.FeelsLikeC}°";
        Humidity.Text = $"{reading.Humidity}%";
        Wind.Text = $"{reading.WindKmph} km/h {reading.WindDir}".Trim();
    }

    /// Raised once the forecast strip exists, because it arrives after the
    /// window has already sized itself to the panel without it.
    public event Action? LayoutChanged;

    void BuildForecast(IReadOnlyList<ForecastDay> days)
    {
        bool had = ForecastSection.Visibility == Visibility.Visible;

        Days.Children.Clear();
        ForecastSection.Visibility = days.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var day in days) Days.Children.Add(Column(day));

        if (!had && days.Count > 0) LayoutChanged?.Invoke();
    }

    UIElement Column(ForecastDay day)
    {
        var column = new StackPanel();

        column.Children.Add(Line(WeatherService.DayLabel(day.Date), 10.5, "TextMuted"));
        column.Children.Add(new TextBlock
        {
            Text = day.Glyph,
            FontFamily = new FontFamily("Segoe UI Emoji"),
            FontSize = 16,
            Foreground = (Brush)FindResource("TextPrimary"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 5)
        });
        column.Children.Add(Line($"{day.MaxC}°", 11.5, "TextPrimary", bold: true));
        column.Children.Add(Line($"{day.MinC}°", 10.5, "TextFaint"));

        return column;
    }

    TextBlock Line(string text, double size, string brush, bool bold = false) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = (Brush)FindResource(brush),
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        HorizontalAlignment = HorizontalAlignment.Center
    };

    void Refresh_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _ = RefreshAsync(force: true);
    }
}
