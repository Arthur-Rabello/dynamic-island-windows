using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Interop;
using DynamicIsland.Services;

namespace DynamicIsland.Panels;

public partial class HardwarePanel : UserControl
{
    const double WarnAt = 75;
    const double CriticalAt = 90;

    static readonly CultureInfo Culture = new("pt-BR");

    static readonly Brush Warn = Freeze(Color.FromRgb(0xF5, 0xA5, 0x24));
    static readonly Brush Critical = Freeze(Color.FromRgb(0xEF, 0x44, 0x44));

    readonly HardwareService _hardware = new();
    readonly Brush _accent = AccentColor.Brush();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public HardwarePanel()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Sample();
    }

    /// Sampling is the most expensive thing the app does, so it runs only
    /// while this panel is actually on screen.
    public void Activate()
    {
        Sample();
        _timer.Start();
    }

    public void Deactivate() => _timer.Stop();

    void Sample()
    {
        var reading = _hardware.Sample();

        Apply(CpuDone, CpuLeft, CpuBar, CpuValue, reading.CpuPercent);
        Apply(GpuDone, GpuLeft, GpuBar, GpuValue, reading.GpuPercent);
        Apply(RamDone, RamLeft, RamBar, RamValue, reading.RamPercent);
        Apply(DiskDone, DiskLeft, DiskBar, DiskValue, reading.DiskPercent);

        RamDetail.Text = reading.RamTotalBytes == 0
            ? ""
            : $"{Gigabytes(reading.RamUsedBytes)} de {Gigabytes(reading.RamTotalBytes)} GB";

        DownRate.Text = Rate(reading.DownBytesPerSecond);
        UpRate.Text = Rate(reading.UpBytesPerSecond);
    }

    void Apply(ColumnDefinition done, ColumnDefinition left, Border bar,
        TextBlock value, double percent)
    {
        double fraction = Math.Clamp(percent, 0, 100) / 100.0;

        done.Width = new GridLength(fraction, GridUnitType.Star);
        left.Width = new GridLength(1 - fraction, GridUnitType.Star);

        bar.Background = percent >= CriticalAt ? Critical
            : percent >= WarnAt ? Warn
            : _accent;

        value.Text = $"{Math.Round(percent)}%";
    }

    static string Gigabytes(long bytes) =>
        (bytes / 1024d / 1024 / 1024).ToString("0.0", Culture);

    static string Rate(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1024 * 1024)
            return (bytesPerSecond / 1024 / 1024).ToString("0.0", Culture) + " MB/s";

        if (bytesPerSecond >= 1024)
            return (bytesPerSecond / 1024).ToString("0", Culture) + " KB/s";

        return Math.Round(bytesPerSecond).ToString(Culture) + " B/s";
    }

    static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
