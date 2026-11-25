using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Interop;
using DynamicIsland.Panels;
using DynamicIsland.Services;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace DynamicIsland;

public partial class MainWindow : Window
{
    enum IslandState { Idle, Media, Osd, Panel, Overview, Message }

    enum PanelTab { Calendar, Weather, Hardware, Volume, Messages }

    const double CollapsedWidth = 184, CollapsedHeight = 34;

    /// Height of the calendar-event row under the clock (MainWindow.xaml, EventRow).
    const double EventRowHeight = 28;
    const double MediaWidth = 410, MediaHeight = 104;
    const double OsdWidth = 290, OsdHeight = 54;
    const double PanelWidth = 372;
    const double OverviewWidth = 440;
    const double MessageWidth = 420, MessageHeight = 72;

    /// Height of the tab strip above the panel content.
    const double TabStripHeight = 38;

    const double DefaultTop = 6;
    const double DragThreshold = 5;

    static readonly TimeSpan OsdDwell = TimeSpan.FromMilliseconds(1700);
    static readonly TimeSpan MediaDwell = TimeSpan.FromMilliseconds(3200);
    static readonly TimeSpan HoverExitDelay = TimeSpan.FromMilliseconds(220);

    const string GlyphPlay = "";
    const string GlyphPause = "";
    const string GlyphVolume = "";
    const string GlyphMute = "";
    const string GlyphBrightness = "";
    const string GlyphCharging = "";
    const string GlyphBatteryLow = "";

    static readonly CultureInfo Culture = new("pt-BR");

    readonly MediaService _media = new();
    readonly VolumeMonitor _volume = new();
    readonly BrightnessMonitor _brightness = new();
    readonly PowerMonitor _power = new();
    readonly PrivacyMonitor _privacy = new();
    readonly NotificationService _notifications = new();
    readonly WeatherService _weather = new();

    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _collapse = new();
    readonly DispatcherTimer _weatherTick = new() { Interval = TimeSpan.FromMinutes(5) };

    string _idleSignature = "";

    readonly Settings _settings = SettingsStore.Load();
    readonly GoogleCalendar _google;
    readonly CalendarService _calendar;
    string _weatherLocation;

    // Events of today and tomorrow, refreshed every few minutes. The pill picks
    // the next one from this list every second without touching the network.
    readonly DispatcherTimer _agendaTick = new() { Interval = TimeSpan.FromMinutes(5) };
    IReadOnlyList<CalendarEvent> _upcoming = [];
    bool _eventPreview;

    /// Shared with the settings window, which edits these in place and then
    /// calls ApplySettings.
    public Settings Settings => _settings;
    public GoogleCalendar Google => _google;

    CalendarPanel? _calendarPanel;
    WeatherPanel? _weatherPanel;
    HardwarePanel? _hardwarePanel;
    OverviewPanel? _overviewPanel;
    MessagesPanel? _messagesPanel;

    /// One set of performance counters for both the hardware panel and the
    /// overview card.
    readonly HardwareService _hardware = new();

    readonly DispatcherTimer _overviewTick = new() { Interval = TimeSpan.FromSeconds(1) };
    double _overviewHeight = 420;
    VolumePanel? _volumePanel;

    IslandState _state = IslandState.Idle;
    PanelTab _panelTab = PanelTab.Calendar;
    bool _panelOpen;
    double _panelHeight = 300;

    bool _hovering;
    string _lastMediaKey = "";
    bool _mediaPrimed;

    // Anchor is the island's centre, so panels of different widths grow
    // symmetrically around wherever the user parked it.
    double _centerX;
    double _top;

    Point _grabOffset;
    bool _dragArmed;
    bool _dragged;

    public MainWindow()
    {
        InitializeComponent();

        _google = new GoogleCalendar(_settings);
        _calendar = new CalendarService(_google);
        _weatherLocation = _settings.WeatherLocation;

        Width = CollapsedWidth;
        Height = CollapsedHeight;

        _centerX = _settings.CenterX ?? SystemParameters.PrimaryScreenWidth / 2;
        _top = _settings.Top ?? DefaultTop;

        if (Enum.TryParse<PanelTab>(_settings.LastPanel, out var tab)) _panelTab = tab;

        _tick.Tick += (_, _) => OnTick();
        _collapse.Tick += (_, _) => { _collapse.Stop(); Relax(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        WindowChromeHelper.MakeOverlay(this);

        ClampAnchor(CollapsedWidth, CollapsedHeight);
        Left = _centerX - CollapsedWidth / 2;
        Top = _top;

        OnTick();
        _tick.Start();

        _volume.Changed += (level, muted) =>
        {
            _volumePanel?.Refresh(level, muted);

            if (_settings.VolumeOsd)
                ShowOsd(muted ? GlyphMute : GlyphVolume, muted ? 0 : level, muted ? "Mudo" : null);
        };
        _volume.Start();

        _brightness.Changed += level =>
            Dispatcher.Invoke(() => { if (_settings.BrightnessOsd) ShowOsd(GlyphBrightness, level); });
        _brightness.Start();

        _power.Changed += (percent, onAc) =>
        {
            if (!_settings.BatteryOsd) return;

            double fraction = percent < 0 ? 1 : percent / 100.0;
            string label = percent < 0 ? (onAc ? "AC" : "Bateria") : $"{percent}%";
            ShowOsd(onAc ? GlyphCharging : GlyphBatteryLow, fraction, label);
        };
        _power.Start();

        _media.Changed += () => Dispatcher.Invoke(OnMediaChanged);
        _ = _media.InitAsync();

        _privacy.Changed += () => Dispatcher.Invoke(RefreshPrivacy);
        _privacy.Start();

        _notifications.AppFilter = _settings.MessageApp;
        _notifications.Arrived += message => Dispatcher.Invoke(() => ShowMessage(message));
        _ = _notifications.StartAsync();

        _overviewTick.Tick += (_, _) => TickOverview();

        // Enumerating the GPU engine instances is the slow part, so it happens
        // now, in the background, instead of on the first hover.
        _ = _hardware.SampleAsync();

        _weatherTick.Tick += (_, _) => _ = RefreshIdleWeatherAsync();
        _weatherTick.Start();
        _ = RefreshIdleWeatherAsync();

        _agendaTick.Tick += (_, _) => _ = RefreshUpcomingAsync();
        _agendaTick.Start();
        _ = RefreshUpcomingAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _brightness.Stop();
        _privacy.Stop();
        _notifications.Stop();
        _hardwarePanel?.Deactivate();
        base.OnClosed(e);
    }

    // ---- idle pill ------------------------------------------------------

    /// Re-reads everything the settings window can change. Called after each edit.
    public void ApplySettings()
    {
        bool locationChanged = _settings.WeatherLocation != _weatherLocation;
        _weatherLocation = _settings.WeatherLocation;
        if (_weatherPanel is not null) _weatherPanel.Location = _weatherLocation;

        // Also covers a Google sign-in/out, which is committed through here.
        _ = RefreshUpcomingAsync();

        if (_settings.ShowWeather) _ =RefreshIdleWeatherAsync(force: locationChanged);
        else
        {
            WeatherSeparator.Visibility = IdleWeatherGlyph.Visibility = IdleTemperature.Visibility =
                Visibility.Collapsed;
        }

        _notifications.AppFilter = _settings.MessageApp;

        RefreshBattery();
        RefreshPrivacy();
        PlayingDot.Visibility = Visible(_settings.MediaExpansion && _media.IsPlaying);

        if (_panelOpen)
        {
            if (EnabledTabs().Any()) OpenPanel(_panelTab);
            else ClosePanel();
        }
        else if (_state == IslandState.Media && !MediaOn) Relax();

        RefreshIdleSize();
    }

    async Task RefreshUpcomingAsync()
    {
        if (_settings.NextEventInPill)
        {
            try
            {
                var loaded = await _calendar.LoadRangeAsync(DateTime.Today, 2);
                // The preview may have been set while this load was in flight.
                if (!_eventPreview) _upcoming = loaded;
            }
            catch { /* keep the last list; the calendar panel reports errors */ }
        }

        RefreshNextEvent();
    }

    /// Shows "em 28 min · Reunião" in the pill when an event starts soon.
    void RefreshNextEvent()
    {
        var now = DateTime.Now;
        var limit = now.AddMinutes(_settings.ReminderMinutes);

        // _upcoming is ordered by start. One minute of grace keeps "agora" on
        // screen while the event is just beginning.
        var next = _settings.NextEventInPill
            ? _upcoming.FirstOrDefault(e => !e.AllDay && e.Start > now.AddMinutes(-1) && e.Start <= limit)
            : null;

        if (next is not null)
        {
            int minutes = (int)Math.Ceiling((next.Start - now).TotalMinutes);
            IdleEvent.Text = (minutes <= 0 ? "agora" : $"em {minutes} min") + " · " + next.Subject;
        }
        else IdleEvent.Text = "";

        if (next is not null) EventDay.Text = next.Start.Day.ToString();
        EventRow.Visibility = Visible(next is not null);
    }

    /// Pins a made-up event 12 minutes out so the pill layout can be inspected
    /// without waiting for a real one (--event).
    public void ShowEventPreview()
    {
        _eventPreview = true;
        _upcoming = [new CalendarEvent(DateTime.Now.AddMinutes(12), false, "Reunião de alinhamento do projeto")];
        RefreshNextEvent();
        RefreshIdleSize();
    }

    async Task RefreshIdleWeatherAsync(bool force = false)
    {
        if (!_settings.ShowWeather) return;

        var reading = await _weather.GetAsync(_settings.WeatherLocation, force);
        if (reading is null || !_settings.ShowWeather) return;

        IdleWeatherGlyph.Text = reading.Glyph;
        IdleTemperature.Text = $"{reading.TempC}°";

        WeatherSeparator.Visibility = Visibility.Visible;
        IdleWeatherGlyph.Visibility = Visibility.Visible;
        IdleTemperature.Visibility = Visibility.Visible;

        RefreshIdleSize();
    }

    void RefreshBattery()
    {
        var (hasBattery, percent, onAc) = PowerMonitor.Current();
        bool show = _settings.ShowBattery && hasBattery && percent >= 0;

        BatterySeparator.Visibility = Visible(show);
        IdleBattery.Visibility = Visible(show);

        // Only the charging bolt is drawn; a glyph per battery level would be
        // guesswork across icon-font versions, and the figure says it anyway.
        IdleBatteryGlyph.Visibility = Visible(show && onAc);

        if (!show) return;

        IdleBatteryGlyph.Text = GlyphCharging;
        IdleBattery.Text = $"{percent}%";
    }

    void RefreshPrivacy()
    {
        CameraDot.Visibility = Visible(_settings.ShowPrivacyDots && _privacy.CameraInUse);
        MicrophoneDot.Visibility = Visible(_settings.ShowPrivacyDots && _privacy.MicrophoneInUse);
        RefreshIdleSize();
    }

    /// The pill is only as wide as what it currently carries, so it stays a
    /// small lozenge when there is nothing to say and grows when there is.
    /// The pill grows downward by one row while a calendar event is announced.
    double IdleHeight() =>
        EventRow.Visibility == Visibility.Visible ? CollapsedHeight + EventRowHeight : CollapsedHeight;

    double IdleWidth()
    {
        IdleView.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Max(CollapsedWidth, Math.Ceiling(IdleView.DesiredSize.Width) + 34);
    }

    void RefreshIdleSize()
    {
        if (_state != IslandState.Idle) return;

        string signature = string.Join('|',
            IdleClock.Text, IdleDate.Text, IdleEvent.Text, IdleTemperature.Text, IdleBattery.Text,
            IdleWeatherGlyph.Visibility, IdleBatteryGlyph.Visibility,
            CameraDot.Visibility, MicrophoneDot.Visibility, PlayingDot.Visibility);

        if (signature == _idleSignature) return;

        _idleSignature = signature;
        AnimateTo(IdleWidth(), IdleHeight());
    }

    // ---- state ----------------------------------------------------------

    void GoTo(IslandState state)
    {
        bool changed = _state != state;
        _state = state;

        IdleView.Visibility = Visible(state == IslandState.Idle);
        MediaView.Visibility = Visible(state == IslandState.Media);
        OsdView.Visibility = Visible(state == IslandState.Osd);
        PanelView.Visibility = Visible(state == IslandState.Panel);
        OverviewHost.Visibility = Visible(state == IslandState.Overview);
        MessageView.Visibility = Visible(state == IslandState.Message);

        if (state != IslandState.Overview) _overviewTick.Stop();

        var (width, height) = state switch
        {
            IslandState.Media => (MediaWidth, MediaHeight),
            IslandState.Osd => (OsdWidth, OsdHeight),
            IslandState.Panel => (PanelWidth, _panelHeight),
            IslandState.Overview => (OverviewWidth, _overviewHeight),
            IslandState.Message => (MessageWidth, MessageHeight),
            _ => (IdleWidth(), IdleHeight())
        };

        AnimateTo(width, height);

        if (changed) FadeInContent();
    }

    void Relax()
    {
        if (_panelOpen) { GoTo(IslandState.Panel); return; }
        if (_hovering) { ShowOverview(); return; }
        GoTo(IslandState.Idle);
    }

    /// A toast from the watched app. Like the media pop, it waits its turn
    /// rather than snatching a pinned panel or the hover overview away.
    void ShowMessage(IncomingMessage message)
    {
        // The list is live whether or not the pop is welcome right now.
        if (_panelOpen && _panelTab == PanelTab.Messages) _messagesPanel?.Refresh();

        if (_panelOpen || _state == IslandState.Overview) return;

        MessageInitial.Text = message.Sender.Length > 0
            ? message.Sender[..1].ToUpper(Culture)
            : "?";

        MessageSender.Text = message.Sender;
        MessagePreview.Text = message.Preview.Length > 0 ? message.Preview : message.App;

        GoTo(IslandState.Message);
        Hold(MediaDwell);
    }

    // ---- overview -------------------------------------------------------

    OverviewPanel Overview()
    {
        if (_overviewPanel is null)
        {
            _overviewPanel = new OverviewPanel(_media, _weather, _hardware, _volume, _settings);
            _overviewPanel.CardClicked += tab =>
            {
                if (tab == "Media") { GoTo(IslandState.Media); Hold(MediaDwell); return; }
                if (Enum.TryParse<PanelTab>(tab, out var panel)) OpenPanel(panel);
            };
        }

        return _overviewPanel;
    }

    void ShowOverview()
    {
        var panel = Overview();

        OverviewHost.Content = panel;
        // Measure needs the host visible; GoTo keeps it that way afterwards.
        OverviewHost.Visibility = Visibility.Visible;

        SyncOverviewPill();
        panel.Render(_upcoming);
        _overviewHeight = MeasureOverview(panel);

        GoTo(IslandState.Overview);

        _ = panel.RefreshAsync();
        _overviewTick.Start();
    }

    /// Opens the overview with one running and one upcoming event, so the
    /// agenda timeline can be inspected without waiting for a real meeting
    /// (--overview).
    public void ShowOverviewPreview()
    {
        _eventPreview = true;
        var now = DateTime.Now;
        _upcoming =
        [
            new CalendarEvent(now.AddMinutes(-15), false, "Valdicleide e Equipamentos Next Fit", TimeSpan.FromMinutes(30)),
            new CalendarEvent(now.AddMinutes(75), false, "Reunião NR — análise", TimeSpan.FromHours(1))
        ];

        _hovering = true;
        ShowOverview();
    }

    void TickOverview()
    {
        if (_state != IslandState.Overview || _overviewPanel is null) return;

        SyncOverviewPill();
        _overviewPanel.Render(_upcoming);
        _ = _overviewPanel.RefreshAsync();

        double height = MeasureOverview(_overviewPanel);
        if (Math.Abs(height - _overviewHeight) > 1)
        {
            _overviewHeight = height;
            AnimateTo(OverviewWidth, height);
        }
    }

    /// The overview draws its own copy of the pill, because a control cannot be
    /// in two places at once; this keeps the copy showing the same thing.
    void SyncOverviewPill() => _overviewPanel?.SetPill(
        IdleClock.Text,
        IdleDate.Text,
        Shown(IdleWeatherGlyph) ? IdleWeatherGlyph.Text : null,
        Shown(IdleTemperature) ? IdleTemperature.Text : null,
        Shown(IdleBatteryGlyph) ? IdleBatteryGlyph.Text : null,
        Shown(IdleBattery) ? IdleBattery.Text : null);

    static bool Shown(UIElement element) => element.Visibility == Visibility.Visible;

    /// Returns 0 until the host has had a layout pass, which on the very first
    /// open happens after this runs. The caller keeps its previous height in
    /// that case and the next tick corrects it.
    double MeasureOverview(UIElement content)
    {
        OverviewHost.UpdateLayout();
        content.Measure(new Size(OverviewWidth, double.PositiveInfinity));

        double height = Math.Ceiling(content.DesiredSize.Height);
        return height < 100 ? _overviewHeight : height;
    }

    /// Media expansion is a user option, so "there is a track" is not enough.
    bool MediaOn => _settings.MediaExpansion && _media.HasTrack;

    void ShowOsd(string glyph, double fraction, string? label = null)
    {
        // A pinned panel is already showing information; do not yank it away
        // because the volume moved.
        if (_panelOpen) return;

        OsdGlyph.Text = glyph;
        OsdLabel.Text = label ?? $"{(int)Math.Round(Math.Clamp(fraction, 0, 1) * 100)}%";
        SetBar(OsdDone, OsdLeft, fraction);

        GoTo(IslandState.Osd);
        Hold(OsdDwell);
    }

    void Hold(TimeSpan duration)
    {
        _collapse.Stop();
        _collapse.Interval = duration;
        _collapse.Start();
    }

    // ---- panels ---------------------------------------------------------

    bool TabOn(PanelTab tab) => tab switch
    {
        PanelTab.Calendar => _settings.CalendarTab,
        PanelTab.Weather => _settings.WeatherTab,
        PanelTab.Hardware => _settings.HardwareTab,
        PanelTab.Volume => _settings.VolumeTab,
        // Without an app to watch there is nothing the tab could ever show.
        _ => _settings.MessagesTab && _settings.MessageApp.Length > 0
    };

    IEnumerable<PanelTab> EnabledTabs() => Enum.GetValues<PanelTab>().Where(TabOn);

    void OpenPanel(PanelTab tab)
    {
        // A tab the user turned off falls back to the first one still on; with
        // none left there is nothing to open.
        if (!TabOn(tab))
        {
            if (!EnabledTabs().Any()) return;
            tab = EnabledTabs().First();
        }

        _panelOpen = true;
        _panelTab = tab;
        _collapse.Stop();

        if (tab != PanelTab.Hardware) _hardwarePanel?.Deactivate();

        UIElement content = tab switch
        {
            PanelTab.Calendar => Calendar(),
            PanelTab.Weather => Weather(),
            PanelTab.Hardware => Hardware(),
            PanelTab.Volume => Volume(),
            _ => Messages()
        };

        PanelHost.Content = content;
        _panelHeight = MeasurePanel(content);

        HighlightTabs();
        GoTo(IslandState.Panel);

        switch (tab)
        {
            case PanelTab.Calendar: _ = Calendar().RefreshAsync(); break;
            case PanelTab.Weather: _ = Weather().RefreshAsync(); break;
            case PanelTab.Hardware: Hardware().Activate(); break;
            case PanelTab.Messages: Messages().Refresh(); break;
        }

        _settings.LastPanel = tab.ToString();
        SettingsStore.Save(_settings);
    }

    void ClosePanel()
    {
        _panelOpen = false;
        _hardwarePanel?.Deactivate();
        PanelHost.Content = null;
        Relax();
    }

    CalendarPanel Calendar()
    {
        if (_calendarPanel is null)
        {
            _calendarPanel = new CalendarPanel(_calendar);
            _calendarPanel.LayoutChanged += () =>
            {
                if (_state != IslandState.Panel || _panelTab != PanelTab.Calendar) return;
                _panelHeight = MeasurePanel(_calendarPanel);
                AnimateTo(PanelWidth, _panelHeight);
            };
        }

        return _calendarPanel;
    }

    WeatherPanel Weather()
    {
        if (_weatherPanel is null)
        {
            _weatherPanel = new WeatherPanel(_weather) { Location = _settings.WeatherLocation };
            _weatherPanel.LayoutChanged += () =>
            {
                if (_state != IslandState.Panel || _panelTab != PanelTab.Weather) return;
                _panelHeight = MeasurePanel(_weatherPanel);
                AnimateTo(PanelWidth, _panelHeight);
            };
        }

        return _weatherPanel;
    }

    HardwarePanel Hardware() => _hardwarePanel ??= new HardwarePanel(_hardware);

    VolumePanel Volume() => _volumePanel ??= new VolumePanel(_volume);

    MessagesPanel Messages()
    {
        if (_messagesPanel is null)
        {
            _messagesPanel = new MessagesPanel(_notifications);
            _messagesPanel.LayoutChanged += () =>
            {
                if (_state != IslandState.Panel || _panelTab != PanelTab.Messages) return;
                _panelHeight = MeasurePanel(_messagesPanel);
                AnimateTo(PanelWidth, _panelHeight);
            };
        }

        return _messagesPanel;
    }

    static double MeasurePanel(UIElement content)
    {
        // WPF marks only the element that changed as dirty, not its parents, so
        // a Measure on the root alone returns the stale cached size after a
        // child grew. Flushing the pending layout first makes it current.
        content.UpdateLayout();
        content.Measure(new Size(PanelWidth, double.PositiveInfinity));
        return TabStripHeight + Math.Ceiling(content.DesiredSize.Height);
    }

    void HighlightTabs()
    {
        var accent = AccentColor.Brush();
        var idle = (Brush)FindResource("TextFaint");

        TabCalendar.Foreground = _panelTab == PanelTab.Calendar ? accent : idle;
        TabWeather.Foreground = _panelTab == PanelTab.Weather ? accent : idle;
        TabHardware.Foreground = _panelTab == PanelTab.Hardware ? accent : idle;
        TabVolume.Foreground = _panelTab == PanelTab.Volume ? accent : idle;
        TabMessages.Foreground = _panelTab == PanelTab.Messages ? accent : idle;

        TabCalendar.Visibility = Visible(_settings.CalendarTab);
        TabWeather.Visibility = Visible(_settings.WeatherTab);
        TabHardware.Visibility = Visible(_settings.HardwareTab);
        TabVolume.Visibility = Visible(_settings.VolumeTab);
        TabMessages.Visibility = Visible(TabOn(PanelTab.Messages));

        TabDots.Children.Clear();
        var tabs = EnabledTabs().ToList();
        if (tabs.Count < 2) return;

        foreach (var tab in tabs)
        {
            TabDots.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 5,
                Height = 5,
                Margin = new Thickness(2.5, 0, 2.5, 0),
                Fill = tab == _panelTab ? accent : idle
            });
        }
    }

    void TabCalendar_Click(object sender, MouseButtonEventArgs e) => SwitchTab(e, PanelTab.Calendar);

    void TabWeather_Click(object sender, MouseButtonEventArgs e) => SwitchTab(e, PanelTab.Weather);

    void TabHardware_Click(object sender, MouseButtonEventArgs e) => SwitchTab(e, PanelTab.Hardware);

    void TabVolume_Click(object sender, MouseButtonEventArgs e) => SwitchTab(e, PanelTab.Volume);

    void TabMessages_Click(object sender, MouseButtonEventArgs e) => SwitchTab(e, PanelTab.Messages);

    void SwitchTab(MouseButtonEventArgs e, PanelTab tab)
    {
        e.Handled = true;
        if (_dragged) return;
        OpenPanel(tab);
    }

    void ClosePanel_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_dragged) return;
        ClosePanel();
    }

    /// Pins the media panel open with placeholder content so the layout can be
    /// inspected without waiting for something to actually play (--demo).
    public void ShowLayoutPreview()
    {
        _tick.Stop();
        _collapse.Stop();

        TrackTitle.Text = "Linha do Equador";
        TrackArtist.Text = "Pedro Mafama";
        PlayPauseGlyph.Text = GlyphPause;
        SetBar(ProgressDone, ProgressLeft, 0.42);

        GoTo(IslandState.Media);
    }

    /// Opens a panel straight away, for --panel=calendar|weather|hardware.
    public void ShowPanelPreview(string name)
    {
        _tick.Stop();
        OpenPanel(Enum.TryParse<PanelTab>(name, true, out var tab) ? tab : PanelTab.Calendar);
    }

    // ---- media ----------------------------------------------------------

    void OnMediaChanged()
    {
        TrackTitle.Text = _media.Title.Length > 0 ? _media.Title : "Reproduzindo";
        TrackArtist.Text = _media.Artist;
        PlayPauseGlyph.Text = _media.IsPlaying ? GlyphPause : GlyphPlay;
        Artwork.Source = Services.Artwork.Decode(_media.ArtworkBytes);
        PlayingDot.Visibility = Visible(_media.IsPlaying && _settings.MediaExpansion);

        string key = _media.StateKey;
        bool isNew = key != _lastMediaKey;
        _lastMediaKey = key;

        if (!_mediaPrimed)
        {
            _mediaPrimed = true;
            return;
        }

        if (isNew && MediaOn)
        {
            // The overview already shows the track, so a change there should
            // refresh the card rather than replace the panel with the pop-up.
            if (_state == IslandState.Overview) _overviewPanel?.Render(_upcoming);
            // Not over a message either: WhatsApp Web's chime registers as
            // media, and its start/stop would bury the message it announces.
            else if (_state is not (IslandState.Osd or IslandState.Panel or IslandState.Message))
            {
                GoTo(IslandState.Media);
                Hold(MediaDwell);
            }
        }
        else if (_state == IslandState.Media && !MediaOn)
        {
            Relax();
        }
    }

    void OnTick()
    {
        var now = DateTime.Now;
        IdleClock.Text = now.ToString("HH:mm", Culture);
        IdleDate.Text = now.ToString("ddd, dd MMM", Culture);

        RefreshNextEvent();
        RefreshBattery();
        RefreshIdleSize();

        if (_state == IslandState.Media)
        {
            var (position, end) = _media.Timeline();
            double fraction = end > TimeSpan.Zero ? position.TotalSeconds / end.TotalSeconds : 0;
            SetBar(ProgressDone, ProgressLeft, fraction);
        }
    }

    // ---- dragging -------------------------------------------------------

    Point CursorInDips()
    {
        var device = WindowChromeHelper.CursorPosition();
        var target = PresentationSource.FromVisual(this)?.CompositionTarget;
        return target is null ? device : target.TransformFromDevice.Transform(device);
    }

    void Island_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _grabOffset = e.GetPosition(this);
        _dragArmed = true;
        _dragged = false;
    }

    void Island_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragArmed) return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _dragArmed = false;
            return;
        }

        var cursor = CursorInDips();
        double targetLeft = cursor.X - _grabOffset.X;
        double targetTop = cursor.Y - _grabOffset.Y;

        if (!_dragged)
        {
            if (Math.Abs(targetLeft - Left) + Math.Abs(targetTop - Top) < DragThreshold) return;

            _dragged = true;
            FreezePosition();
            // Capture only once a drag is real, so ordinary clicks still reach
            // the buttons and calendar cells inside the island.
            Island.CaptureMouse();
        }

        Left = targetLeft;
        Top = targetTop;
    }

    void Island_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Island.IsMouseCaptured) Island.ReleaseMouseCapture();
        _dragArmed = false;

        if (!_dragged) return;

        PersistPosition();
        e.Handled = true;

        // Cleared after the click has been swallowed, so the handlers that run
        // next still see that this gesture was a drag.
        Dispatcher.BeginInvoke(() => _dragged = false, DispatcherPriority.Input);
    }

    void FreezePosition()
    {
        double left = Left, top = Top;
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        Left = left;
        Top = top;
    }

    void PersistPosition()
    {
        _centerX = Left + ActualWidth / 2;
        _top = Top;
        ClampAnchor(ActualWidth, ActualHeight);

        _settings.CenterX = _centerX;
        _settings.Top = _top;
        SettingsStore.Save(_settings);

        Left = _centerX - ActualWidth / 2;
        Top = _top;
    }

    /// Keeps the island inside the virtual desktop, which spans every monitor,
    /// so it can be parked on a secondary screen but never off the edge.
    void ClampAnchor(double width, double height)
    {
        double minX = SystemParameters.VirtualScreenLeft + width / 2;
        double maxX = SystemParameters.VirtualScreenLeft
            + SystemParameters.VirtualScreenWidth - width / 2;

        double minY = SystemParameters.VirtualScreenTop;
        double maxY = SystemParameters.VirtualScreenTop
            + SystemParameters.VirtualScreenHeight - height;

        if (maxX > minX) _centerX = Math.Clamp(_centerX, minX, maxX);
        if (maxY > minY) _top = Math.Clamp(_top, minY, maxY);
    }

    // ---- input ----------------------------------------------------------

    void Island_MouseEnter(object sender, MouseEventArgs e)
    {
        _hovering = true;

        if (_state is IslandState.Idle or IslandState.Media)
        {
            _collapse.Stop();
            ShowOverview();
        }
    }

    void Island_MouseLeave(object sender, MouseEventArgs e)
    {
        _hovering = false;

        if (_state is IslandState.Media or IslandState.Overview) Hold(HoverExitDelay);
    }

    void Island_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragged) return;

        if (_panelOpen) return;
        OpenPanel(_panelTab);
    }

    void PlayPause_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_dragged) return;

        _media.TogglePlayPause();
        Hold(MediaDwell);
    }

    void Previous_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_dragged) return;

        _media.Previous();
        Hold(MediaDwell);
    }

    void Next_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_dragged) return;

        _media.Next();
        Hold(MediaDwell);
    }

    // ---- presentation ---------------------------------------------------

    void AnimateTo(double width, double height)
    {
        var duration = TimeSpan.FromMilliseconds(260);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        ClampAnchor(width, height);

        BeginAnimation(WidthProperty, Animate(width, duration, ease));
        BeginAnimation(HeightProperty, Animate(height, duration, ease));
        BeginAnimation(LeftProperty, Animate(_centerX - width / 2, duration, ease));

        Top = _top;
        Island.CornerRadius = new CornerRadius(Math.Min(height / 2, 22));
    }

    static DoubleAnimation Animate(double to, TimeSpan duration, IEasingFunction ease) =>
        new(to, duration) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };

    void FadeInContent()
    {
        Root.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(200)));
    }

    /// Bars are built from two star-sized columns so they stay correct while
    /// the window itself is still animating to its new width.
    static void SetBar(ColumnDefinition done, ColumnDefinition left, double fraction)
    {
        double value = Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1);
        done.Width = new GridLength(value, GridUnitType.Star);
        left.Width = new GridLength(1 - value, GridUnitType.Star);
    }

    static Visibility Visible(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
