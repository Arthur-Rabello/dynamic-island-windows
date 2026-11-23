using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Interop;
using DynamicIsland.Services;

namespace DynamicIsland.Panels;

/// The panel the island opens on hover: one card per live function, each one
/// a shortcut into its own full panel.
///
/// Everything here paints from data the services already hold. Nothing in
/// Render blocks, which is what keeps the panel from stalling as it opens;
/// the slow work lives in RefreshAsync.
public partial class OverviewPanel : UserControl
{
    const double WarnAt = 75;
    const double CriticalAt = 90;

    static readonly CultureInfo Culture = new("pt-BR");

    static readonly Brush Warn = Freeze(Color.FromRgb(0xF5, 0xA5, 0x24));
    static readonly Brush Critical = Freeze(Color.FromRgb(0xEF, 0x44, 0x44));

    readonly MediaService _media;
    readonly WeatherService _weather;
    readonly HardwareService _hardware;
    readonly VolumeMonitor _volume;
    readonly Settings _settings;
    readonly Brush _accent = AccentColor.Brush();

    /// Carries the PanelTab name of the card that was clicked.
    public event Action<string>? CardClicked;

    public OverviewPanel(MediaService media, WeatherService weather,
        HardwareService hardware, VolumeMonitor volume, Settings settings)
    {
        _media = media;
        _weather = weather;
        _hardware = hardware;
        _volume = volume;
        _settings = settings;

        InitializeComponent();
    }

    // ---- painting -------------------------------------------------------

    /// Copies the collapsed pill's text. A null argument hides that segment,
    /// matching whatever the pill itself is currently showing.
    public void SetPill(string clock, string date, string? glyph, string? temperature,
        string? batteryGlyph, string? battery)
    {
        Clock.Text = clock;
        Date.Text = date;

        bool weather = glyph is not null && temperature is not null;
        WeatherSeparator.Visibility = Visible(weather);
        PillGlyph.Visibility = Visible(weather);
        PillTemperature.Visibility = Visible(weather);
        PillGlyph.Text = glyph ?? "";
        PillTemperature.Text = temperature ?? "";

        bool power = battery is not null;
        BatterySeparator.Visibility = Visible(power);
        Battery.Visibility = Visible(power);
        BatteryGlyph.Visibility = Visible(power && batteryGlyph is not null);
        BatteryGlyph.Text = batteryGlyph ?? "";
        Battery.Text = battery ?? "";
    }

    /// Instant pass: media, agenda and the last hardware reading.
    public void Render(IReadOnlyList<CalendarEvent> upcoming)
    {
        RenderMedia();
        RenderAgenda(upcoming);
        RenderSystem(_hardware.Latest);
    }

    /// Everything that has to wait on a counter or the network.
    public async Task RefreshAsync()
    {
        RenderSystem(await _hardware.SampleAsync());

        var reading = await _weather.GetAsync(_settings.WeatherLocation);
        if (reading is not null) RenderWeather(reading);
    }

    void RenderMedia()
    {
        bool playing = _media.HasTrack;
        MediaCard.Visibility = Visible(playing);
        if (!playing) return;

        TrackTitle.Text = _media.Title.Length > 0 ? _media.Title : "Reproduzindo";
        TrackArtist.Text = _media.Artist;
        PlayPauseGlyph.Text = _media.IsPlaying ? "" : "";
        Artwork.Source = Services.Artwork.Decode(_media.ArtworkBytes);

        var (position, end) = _media.Timeline();
        SetBar(TrackDone, TrackLeft, end > TimeSpan.Zero ? position / end : 0);
        Elapsed.Text = Clipped(position);
        Total.Text = end > TimeSpan.Zero ? Clipped(end) : "";
    }

    void RenderAgenda(IReadOnlyList<CalendarEvent> upcoming)
    {
        var now = DateTime.Now;

        var running = upcoming.FirstOrDefault(e => e.IsRunning(now));
        var next = upcoming.FirstOrDefault(e => !e.AllDay && e.Start > now);

        AccountRow.Visibility = Visible(_settings.GoogleEmail.Length > 0);
        Account.Text = _settings.GoogleEmail.Split('@')[0];
        IconDay.Text = now.Day.ToString(Culture);

        bool anything = running is not null || next is not null;
        NoEvents.Visibility = Visible(!anything);
        Timeline.Visibility = Visible(anything);
        if (!anything) return;

        NowBlock.Visibility = Visible(running is not null);
        NowDot.Visibility = Visible(running is not null);

        if (running is not null)
        {
            NowBadge.Text = "agora";
            NowRange.Text = $"{running.Start:HH:mm} – {running.End:HH:mm}";
            NowTitle.Text = running.Subject;
            SetBar(NowDone, NowLeft, (now - running.Start) / running.Duration);
        }

        NextBlock.Visibility = Visible(next is not null);
        NextDot.Visibility = Visible(next is not null);
        TimelineStem.Visibility = Visible(running is not null && next is not null);

        if (next is not null)
        {
            NextTime.Text = next.Start.ToString("HH:mm", Culture);
            NextIn.Text = Countdown(next.Start - now);
            NextTitle.Text = next.Subject;
        }
    }

    void RenderWeather(WeatherReading reading)
    {
        WeatherGlyph.Text = reading.Glyph;
        Temperature.Text = $"{reading.TempC}°";
        Condition.Text = reading.Description;
        FeelsLike.Text = $"{reading.FeelsLikeC}°";
        Humidity.Text = $"{reading.Humidity}%";
        Wind.Text = $"{reading.WindKmph} km/h";
    }

    void RenderSystem(HardwareReading reading)
    {
        Apply(CpuDone, CpuLeft, CpuBar, CpuValue, reading.CpuPercent);
        Apply(RamDone, RamLeft, RamBar, RamValue, reading.RamPercent);
        Apply(GpuDone, GpuLeft, GpuBar, GpuValue, reading.GpuPercent);
        Apply(VolumeDone, VolumeLeft, VolumeBar, VolumeValue,
            _volume.Muted ? 0 : _volume.Level * 100, load: false);
    }

    /// load: false keeps the accent at any level. The amber and red steps mean
    /// "this resource is running out", which says nothing about a volume at
    /// 100%.
    void Apply(ColumnDefinition done, ColumnDefinition left, Border bar,
        TextBlock value, double percent, bool load = true)
    {
        SetBar(done, left, percent / 100.0);

        bar.Background = !load ? _accent
            : percent >= CriticalAt ? Critical
            : percent >= WarnAt ? Warn
            : _accent;

        value.Text = $"{Math.Round(percent)}%";
    }

    // ---- input ----------------------------------------------------------

    void Media_Click(object sender, MouseButtonEventArgs e) => Open(e, "Media");

    void Agenda_Click(object sender, MouseButtonEventArgs e) => Open(e, "Calendar");

    void Weather_Click(object sender, MouseButtonEventArgs e) => Open(e, "Weather");

    void System_Click(object sender, MouseButtonEventArgs e) => Open(e, "Hardware");

    void Open(MouseButtonEventArgs e, string tab)
    {
        e.Handled = true;
        CardClicked?.Invoke(tab);
    }

    void PlayPause_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _media.TogglePlayPause();
        RenderMedia();
    }

    void Previous_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _media.Previous();
    }

    void Next_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _media.Next();
    }

    // ---- formatting -----------------------------------------------------

    static string Clipped(TimeSpan span) =>
        span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");

    static string Countdown(TimeSpan until)
    {
        if (until <= TimeSpan.Zero) return "começando";

        int minutes = (int)Math.Ceiling(until.TotalMinutes);
        if (minutes < 60) return $"em {minutes} min";

        int hours = minutes / 60;
        int rest = minutes % 60;
        return rest == 0 ? $"em {hours} h" : $"em {hours} h {rest}";
    }

    static void SetBar(ColumnDefinition done, ColumnDefinition left, double fraction)
    {
        double value = Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1);
        done.Width = new GridLength(value, GridUnitType.Star);
        left.Width = new GridLength(1 - value, GridUnitType.Star);
    }

    static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    static Visibility Visible(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
