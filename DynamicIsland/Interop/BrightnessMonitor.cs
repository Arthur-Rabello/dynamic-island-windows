using System.Management;

namespace DynamicIsland.Interop;

/// Event-driven brightness watcher over WMI. Only machines with an integrated
/// panel expose WmiMonitorBrightnessEvent; on a desktop the watcher never
/// starts and the feature stays dormant instead of failing loudly.
public sealed class BrightnessMonitor
{
    ManagementEventWatcher? _watcher;

    /// Fires with brightness as 0..1.
    public event Action<double>? Changed;

    public bool Available => _watcher is not null;

    public void Start()
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\WMI");
            var query = new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent");

            _watcher = new ManagementEventWatcher(scope, query);
            _watcher.EventArrived += (_, e) =>
            {
                try
                {
                    double value = Convert.ToDouble(e.NewEvent["Brightness"]);
                    Changed?.Invoke(Math.Clamp(value / 100.0, 0, 1));
                }
                catch { }
            };
            _watcher.Start();
        }
        catch
        {
            _watcher = null;
        }
    }

    public void Stop()
    {
        try { _watcher?.Stop(); } catch { }
        _watcher?.Dispose();
        _watcher = null;
    }
}
