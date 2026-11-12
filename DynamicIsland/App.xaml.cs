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

        var menu = new ContextMenuStrip();
        menu.Items.Add("Sair", null, (_, _) => Shutdown());

        _tray = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Dynamic Island",
            Visible = true,
            ContextMenuStrip = menu
        };
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
