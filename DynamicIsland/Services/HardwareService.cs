using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace DynamicIsland.Services;

public sealed record HardwareReading(
    double CpuPercent,
    double GpuPercent,
    double DiskPercent,
    double RamPercent,
    long RamUsedBytes,
    long RamTotalBytes,
    double DownBytesPerSecond,
    double UpBytesPerSecond);

/// Samples CPU, GPU, disk, memory and network throughput.
///
/// Everything here is lazy and only runs while the hardware panel is on
/// screen: the counters below are the expensive part of the whole app, and
/// enumerating GPU engine instances in particular is slow enough that it is
/// refreshed on its own slower cadence.
public sealed class HardwareService : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    const int GpuInstanceRefreshTicks = 8;

    long _lastIdle, _lastKernel, _lastUser;
    bool _cpuPrimed;

    PerformanceCounter? _disk;
    bool _diskTried;

    List<PerformanceCounter> _gpuCounters = [];
    bool _gpuTried;
    int _gpuTicks;

    long _lastDown, _lastUp;
    DateTime _lastNetSample = DateTime.MinValue;

    /// Last reading taken, so a panel can paint something the instant it opens
    /// instead of waiting on the counters.
    public HardwareReading Latest { get; private set; } =
        new(0, 0, 0, 0, 0, 0, 0, 0);

    /// Samples off the UI thread. Enumerating the GPU engine instances blocks
    /// for long enough to be seen as a freeze when the panel is opening, which
    /// is the whole reason this is not just Sample().
    ///
    /// The mutable delta fields below are not thread-safe, so an overlapping
    /// call returns the previous reading rather than corrupting them.
    public async Task<HardwareReading> SampleAsync()
    {
        if (!await _gate.WaitAsync(0)) return Latest;

        try { Latest = await Task.Run(Sample); }
        catch { /* keep the last good reading */ }
        finally { _gate.Release(); }

        return Latest;
    }

    readonly SemaphoreSlim _gate = new(1, 1);

    public HardwareReading Sample()
    {
        var (down, up) = SampleNetwork();
        var (ramPercent, ramUsed, ramTotal) = SampleMemory();

        return new HardwareReading(
            CpuPercent: SampleCpu(),
            GpuPercent: SampleGpu(),
            DiskPercent: SampleDisk(),
            RamPercent: ramPercent,
            RamUsedBytes: ramUsed,
            RamTotalBytes: ramTotal,
            DownBytesPerSecond: down,
            UpBytesPerSecond: up);
    }

    PerformanceCounter? _cpu;
    bool _cpuTried;

    /// Task Manager reports "% Processor Utility", which normalises against the
    /// base clock, not "% Processor Time", which is pure elapsed time. With
    /// speed shift the two diverge badly: this machine read 100% by time while
    /// Task Manager showed 77%. Preferring Utility makes the island agree with
    /// the number the user can check, and GetSystemTimes stays as the fallback
    /// for a machine that lacks the counter.
    double SampleCpuUtility()
    {
        if (!_cpuTried)
        {
            _cpuTried = true;
            try
            {
                _cpu = new PerformanceCounter(
                    "Processor Information", "% Processor Utility", "_Total", true);
                _cpu.NextValue();
            }
            catch { _cpu = null; }
        }

        if (_cpu is null) return -1;

        // Turbo can push Utility past 100.
        try { return Math.Clamp(_cpu.NextValue(), 0, 100); }
        catch { _cpu = null; return -1; }
    }

    // GetSystemTimes rather than a counter: no category lookup, no first-read
    // warm-up, and the kernel bucket already includes idle so the maths is a
    // straight ratio of deltas.
    double SampleCpu()
    {
        double utility = SampleCpuUtility();
        if (utility >= 0) return utility;

        if (!GetSystemTimes(out long idle, out long kernel, out long user)) return 0;

        if (!_cpuPrimed)
        {
            _cpuPrimed = true;
            (_lastIdle, _lastKernel, _lastUser) = (idle, kernel, user);
            return 0;
        }

        long idleDelta = idle - _lastIdle;
        long totalDelta = (kernel - _lastKernel) + (user - _lastUser);

        (_lastIdle, _lastKernel, _lastUser) = (idle, kernel, user);

        if (totalDelta <= 0) return 0;
        return Math.Clamp((1.0 - (double)idleDelta / totalDelta) * 100, 0, 100);
    }

    static (double Percent, long Used, long Total) SampleMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status)) return (0, 0, 0);

        long total = (long)status.TotalPhys;
        long used = total - (long)status.AvailPhys;
        return (status.MemoryLoad, used, total);
    }

    double SampleDisk()
    {
        if (!_diskTried)
        {
            _diskTried = true;
            try { _disk = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total", true); }
            catch { _disk = null; }
        }

        // "% Disk Time" counts time with any request outstanding and can run
        // well past 100 on a queued device, so it is clamped for display.
        try { return _disk is null ? 0 : Math.Clamp(_disk.NextValue(), 0, 100); }
        catch { return 0; }
    }

    /// Sums the 3D engine instances, which is what Task Manager shows as the
    /// headline GPU figure. Needs no vendor SDK.
    double SampleGpu()
    {
        if (!_gpuTried || ++_gpuTicks >= GpuInstanceRefreshTicks)
        {
            _gpuTried = true;
            _gpuTicks = 0;
            RebuildGpuCounters();
        }

        double total = 0;
        foreach (var counter in _gpuCounters)
        {
            try { total += counter.NextValue(); } catch { }
        }

        return Math.Clamp(total, 0, 100);
    }

    void RebuildGpuCounters()
    {
        foreach (var counter in _gpuCounters) counter.Dispose();
        _gpuCounters = [];

        try
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            foreach (string instance in category.GetInstanceNames())
            {
                if (!instance.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    var counter = new PerformanceCounter(
                        "GPU Engine", "Utilization Percentage", instance, true);
                    counter.NextValue();
                    _gpuCounters.Add(counter);
                }
                catch { }
            }
        }
        catch { }
    }

    (double Down, double Up) SampleNetwork()
    {
        long down = 0, up = 0;

        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback
                    or NetworkInterfaceType.Tunnel) continue;

                var stats = adapter.GetIPStatistics();
                down += stats.BytesReceived;
                up += stats.BytesSent;
            }
        }
        catch { return (0, 0); }

        var now = DateTime.UtcNow;

        if (_lastNetSample == DateTime.MinValue)
        {
            (_lastDown, _lastUp, _lastNetSample) = (down, up, now);
            return (0, 0);
        }

        double seconds = (now - _lastNetSample).TotalSeconds;
        if (seconds <= 0) return (0, 0);

        double downRate = Math.Max(0, down - _lastDown) / seconds;
        double upRate = Math.Max(0, up - _lastUp) / seconds;

        (_lastDown, _lastUp, _lastNetSample) = (down, up, now);
        return (downRate, upRate);
    }

    public void Dispose()
    {
        _disk?.Dispose();
        foreach (var counter in _gpuCounters) counter.Dispose();
        _gpuCounters = [];
    }
}
