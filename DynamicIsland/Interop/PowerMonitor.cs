using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace DynamicIsland.Interop;

/// Reports charger plug/unplug and a one-shot low-battery warning.
public sealed class PowerMonitor
{
    [StructLayout(LayoutKind.Sequential)]
    struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    const byte NoBattery = 128;
    const byte UnknownPercent = 255;
    const int LowThreshold = 20;

    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(4) };
    bool _primed;
    bool _lastOnAc;
    bool _warnedLow;

    /// Fires with (percent 0..100, or -1 when unknown; onAcPower).
    public event Action<int, bool>? Changed;

    public void Start()
    {
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
        Poll();
    }

    /// Current state for the idle pill, independent of the change events.
    public static (bool HasBattery, int Percent, bool OnAc) Current()
    {
        if (!GetSystemPowerStatus(out var status)) return (false, -1, true);
        if (status.BatteryFlag == NoBattery) return (false, -1, status.AcLineStatus == 1);

        int percent = status.BatteryLifePercent == UnknownPercent ? -1 : status.BatteryLifePercent;
        return (true, percent, status.AcLineStatus == 1);
    }

    void Poll()
    {
        if (!GetSystemPowerStatus(out var status)) return;
        if (status.BatteryFlag == NoBattery) return;

        bool onAc = status.AcLineStatus == 1;
        int percent = status.BatteryLifePercent == UnknownPercent ? -1 : status.BatteryLifePercent;

        if (!_primed)
        {
            _primed = true;
            _lastOnAc = onAc;
            return;
        }

        if (onAc != _lastOnAc)
        {
            _lastOnAc = onAc;
            if (onAc) _warnedLow = false;
            Changed?.Invoke(percent, onAc);
            return;
        }

        if (!onAc && percent is >= 0 and <= LowThreshold && !_warnedLow)
        {
            _warnedLow = true;
            Changed?.Invoke(percent, false);
        }
        else if (percent > LowThreshold + 5)
        {
            _warnedLow = false;
        }
    }
}
