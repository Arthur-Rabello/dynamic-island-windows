using System.Windows;
using Application = System.Windows.Application;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using NotifyIcon = System.Windows.Forms.NotifyIcon;

namespace DynamicIsland;

public partial class App : Application
{
    static Mutex? _mutex;
    NotifyIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "DynamicIslandForWindows.SingleInstance", out bool isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var window = new MainWindow();
        window.Show();

        if (e.Args.Contains("--demo", StringComparer.OrdinalIgnoreCase))
        {
            window.ShowLayoutPreview();
        }
        else
        {
            string? panel = e.Args
                .FirstOrDefault(a => a.StartsWith("--panel=", StringComparison.OrdinalIgnoreCase));

            if (panel is not null) window.ShowPanelPreview(panel["--panel=".Length..]);
        }

        if (e.Args.Contains("--event", StringComparer.OrdinalIgnoreCase)) window.ShowEventPreview();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Configurações", null, (_, _) => OpenSettings(window));
        menu.Items.Add("Sair", null, (_, _) => Shutdown());

        _tray = new NotifyIcon
        {
            // The icon baked into the exe (Assets\icon.ico).
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = "Dynamic Island",
            Visible = true,
            ContextMenuStrip = menu
        };

        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == System.Windows.Forms.MouseButtons.Left) OpenSettings(window);
        };

        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase)) OpenSettings(window);
    }

    SettingsWindow? _settingsWindow;

    void OpenSettings(MainWindow island)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(island.Settings, island.Google, island.ApplySettings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.OnExit(e);
    }
}
