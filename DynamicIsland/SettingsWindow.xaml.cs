using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Navigation;
using DynamicIsland.Services;

namespace DynamicIsland;

public partial class SettingsWindow : Window
{
    readonly Settings _settings;
    readonly GoogleCalendar _google;
    readonly Action _changed;
    CancellationTokenSource? _signIn;
    Updater.Release? _update;

    public SettingsWindow(Settings settings, GoogleCalendar google, Action changed)
    {
        _settings = settings;
        _google = google;
        _changed = changed;

        InitializeComponent();

        // Added before the CLIMA heading, which is already in the XAML.
        int at = 0;
        Section(ref at, "PÍLULA",
            ("Clima", () => _settings.ShowWeather, v => _settings.ShowWeather = v),
            ("Bateria", () => _settings.ShowBattery, v => _settings.ShowBattery = v),
            ("Pontos de câmera e microfone", () => _settings.ShowPrivacyDots, v => _settings.ShowPrivacyDots = v),
            ("Próximo evento da agenda", () => _settings.NextEventInPill, v => _settings.NextEventInPill = v));

        Section(ref at, "EXPANSÕES AUTOMÁTICAS",
            ("Música tocando (também o ponto verde)", () => _settings.MediaExpansion, v => _settings.MediaExpansion = v),
            ("Volume", () => _settings.VolumeOsd, v => _settings.VolumeOsd = v),
            ("Brilho", () => _settings.BrightnessOsd, v => _settings.BrightnessOsd = v),
            ("Bateria (carregador e aviso de carga baixa)", () => _settings.BatteryOsd, v => _settings.BatteryOsd = v));

        Section(ref at, "ABAS DO PAINEL",
            ("Calendário", () => _settings.CalendarTab, v => _settings.CalendarTab = v),
            ("Tempo", () => _settings.WeatherTab, v => _settings.WeatherTab = v),
            ("Hardware", () => _settings.HardwareTab, v => _settings.HardwareTab = v),
            ("Controle de volume", () => _settings.VolumeTab, v => _settings.VolumeTab = v),
            ("Mensagens", () => _settings.MessagesTab, v => _settings.MessagesTab = v));

        City.Text = _settings.WeatherLocation;
        MessageApp.Text = _settings.MessageApp;
        UpdateStatus.Text = $"Versão {Updater.Current}";
        RefreshGoogleUi();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Dark title bar to match the content (Windows 10 20H1+ / 11).
        int on = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, sizeof(int));
    }

    protected override void OnClosed(EventArgs e)
    {
        _signIn?.Cancel();

        // Closing a window does not raise LostFocus on the control that has it,
        // so text typed and then left focused would be thrown away.
        SaveCity();
        SaveMessageApp();

        base.OnClosed(e);
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // ---- switches -------------------------------------------------------

    void Section(ref int at, string title,
        params (string Label, Func<bool> Get, Action<bool> Set)[] items)
    {
        Body.Children.Insert(at++, new TextBlock
        {
            Text = title,
            Style = (Style)FindResource("Heading"),
            Margin = new Thickness(0, at == 1 ? 16 : 20, 0, 4)
        });

        foreach (var (label, get, set) in items)
        {
            var box = new CheckBox
            {
                Content = label,
                Style = (Style)FindResource("Switch"),
                IsChecked = get()
            };
            RoutedEventHandler apply = (_, _) => { set(box.IsChecked == true); Commit(); };
            box.Checked += apply;
            box.Unchecked += apply;
            Body.Children.Insert(at++, box);
        }
    }

    void Commit()
    {
        SettingsStore.Save(_settings);
        _changed();
    }

    void City_LostFocus(object sender, RoutedEventArgs e) => SaveCity();

    void SaveCity()
    {
        string city = City.Text.Trim();
        if (city == _settings.WeatherLocation) return;

        _settings.WeatherLocation = city;
        Commit();
    }

    void MessageApp_LostFocus(object sender, RoutedEventArgs e) => SaveMessageApp();

    void SaveMessageApp()
    {
        string app = MessageApp.Text.Trim();
        if (app == _settings.MessageApp) return;

        _settings.MessageApp = app;
        Commit();
    }

    // ---- Google ---------------------------------------------------------

    void RefreshGoogleUi()
    {
        bool signedIn = _google.SignedIn;

        GoogleButton.Content = _signIn is not null ? "Cancelar"
            : signedIn ? "Sair da conta Google"
            : "Entrar com o Google";

        if (_signIn is null)
        {
            GoogleStatus.Text = signedIn
                ? "Conectado" + (_settings.GoogleEmail.Length > 0 ? $" como {_settings.GoogleEmail}" : "")
                : "Não conectado — os eventos vêm só do Calendário do Windows.";
        }
    }

    async void Google_Click(object sender, RoutedEventArgs e)
    {
        if (_signIn is not null) { _signIn.Cancel(); return; }

        if (_google.SignedIn)
        {
            await _google.SignOutAsync();
            Commit();
            RefreshGoogleUi();
            return;
        }

        _signIn = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        GoogleStatus.Text = "Aguardando o navegador…";
        RefreshGoogleUi();

        try { await _google.SignInAsync(_signIn.Token); }
        catch (OperationCanceledException) { GoogleStatus.Text = "Login cancelado."; }
        catch (Exception ex) { GoogleStatus.Text = "Não deu certo: " + ex.Message; }
        finally
        {
            _signIn.Dispose();
            _signIn = null;
        }

        string failure = GoogleStatus.Text;
        Commit();
        RefreshGoogleUi();
        if (!_google.SignedIn) GoogleStatus.Text = failure;
    }

    // ---- update ---------------------------------------------------------

    /// First click looks for a release, the second installs it.
    async void Update_Click(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        try
        {
            if (_update is null)
            {
                UpdateStatus.Text = "Procurando…";
                _update = await Updater.FindNewerAsync();

                if (_update is null)
                {
                    UpdateStatus.Text = $"Versão {Updater.Current} — já é a mais nova.";
                    return;
                }

                UpdateStatus.Text = $"Versão {_update.Version} disponível (você está na {Updater.Current}).";
                UpdateButton.Content = $"Atualizar para {_update.Version}";
                return;
            }

            UpdateStatus.Text = "Baixando… a ilha fecha e volta sozinha.";
            await Updater.InstallAsync(_update);
            Application.Current.Shutdown();
        }
        catch (Exception ex) { UpdateStatus.Text = "Não deu certo: " + ex.Message; }
        finally { UpdateButton.IsEnabled = true; }
    }
}
