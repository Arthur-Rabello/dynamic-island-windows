using Microsoft.Win32;
using System.Windows.Threading;

namespace DynamicIsland.Interop;

/// Tells whether anything is currently holding the camera or the microphone.
///
/// Windows records this in the per-user ConsentStore: every app that has used
/// a device gets a subkey with LastUsedTimeStart and LastUsedTimeStop. A stop
/// time of zero means the session is still open, which is exactly what the
/// privacy indicator in the system tray keys off.
public sealed class PrivacyMonitor
{
    const string ConsentStore =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    const string NonPackaged = "NonPackaged";

    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };

    public bool CameraInUse { get; private set; }
    public bool MicrophoneInUse { get; private set; }

    public event Action? Changed;

    public void Start()
    {
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
        Poll();
    }

    public void Stop() => _timer.Stop();

    void Poll()
    {
        bool camera = AnyActive("webcam");
        bool microphone = AnyActive("microphone");

        if (camera == CameraInUse && microphone == MicrophoneInUse) return;

        CameraInUse = camera;
        MicrophoneInUse = microphone;
        Changed?.Invoke();
    }

    static bool AnyActive(string device)
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey($@"{ConsentStore}\{device}");
            if (root is null) return false;

            foreach (string name in root.GetSubKeyNames())
            {
                using var entry = root.OpenSubKey(name);
                if (entry is null) continue;

                // Desktop apps are nested one level deeper, under NonPackaged.
                if (name.Equals(NonPackaged, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string child in entry.GetSubKeyNames())
                    {
                        using var app = entry.OpenSubKey(child);
                        if (IsOpen(app)) return true;
                    }

                    continue;
                }

                if (IsOpen(entry)) return true;
            }
        }
        catch { }

        return false;
    }

    static bool IsOpen(RegistryKey? key)
    {
        if (key is null) return false;

        return key.GetValue("LastUsedTimeStart") is long start && start != 0
            && key.GetValue("LastUsedTimeStop") is long stop && stop == 0;
    }
}
