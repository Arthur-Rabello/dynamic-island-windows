using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace DynamicIsland.Interop;

/// Watches the master volume and mute state of the default playback device.
/// Polls instead of registering an IAudioEndpointVolumeCallback: two COM
/// property reads every 150 ms cost nothing measurable, and re-acquiring the
/// endpoint periodically picks up a changed default device for free.
public sealed class VolumeMonitor
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    // Every method ahead of the ones actually called must be declared so the
    // vtable offsets line up; the unused slots are placeholders on purpose.
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out uint count);
        int SetMasterVolumeLevel(float level, Guid context);
        int SetMasterVolumeLevelScalar(float level, Guid context);
        int GetMasterVolumeLevel(out float level);
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(uint channel, float level, Guid context);
        int SetChannelVolumeLevelScalar(uint channel, float level, Guid context);
        int GetChannelVolumeLevel(uint channel, out float level);
        int GetChannelVolumeLevelScalar(uint channel, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, Guid context);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    const int Render = 0;
    const int Multimedia = 1;
    const int ReacquireAfterTicks = 400;

    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    IAudioEndpointVolume? _endpoint;
    double _lastVolume = -1;
    bool _lastMute;
    bool _primed;
    int _ticksSinceAcquire;

    /// Fires with (volume 0..1, muted), only when something actually changed.
    public event Action<double, bool>? Changed;

    public void Start()
    {
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
    }

    void Acquire()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        enumerator.GetDefaultAudioEndpoint(Render, Multimedia, out var device);

        var iid = typeof(IAudioEndpointVolume).GUID;
        device.Activate(ref iid, 0, IntPtr.Zero, out var instance);

        _endpoint = (IAudioEndpointVolume)instance;
        _ticksSinceAcquire = 0;
    }

    void Poll()
    {
        try
        {
            if (_endpoint is null || ++_ticksSinceAcquire > ReacquireAfterTicks) Acquire();

            _endpoint!.GetMasterVolumeLevelScalar(out float level);
            _endpoint!.GetMute(out bool muted);

            double volume = Math.Round(level, 3);

            if (!_primed)
            {
                _primed = true;
                _lastVolume = volume;
                _lastMute = muted;
                return;
            }

            if (Math.Abs(volume - _lastVolume) > 0.0005 || muted != _lastMute)
            {
                _lastVolume = volume;
                _lastMute = muted;
                Changed?.Invoke(volume, muted);
            }
        }
        catch
        {
            _endpoint = null;
        }
    }
}
