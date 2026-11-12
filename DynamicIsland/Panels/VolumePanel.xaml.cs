using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DynamicIsland.Interop;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace DynamicIsland.Panels;

/// Master volume: click or drag the bar, click the speaker to mute.
public partial class VolumePanel : UserControl
{
    readonly VolumeMonitor _volume;
    bool _dragging;

    public VolumePanel(VolumeMonitor volume)
    {
        _volume = volume;
        InitializeComponent();
        Refresh(volume.Level, volume.Muted);
    }

    /// Called whenever the system volume changes. Ignored mid-drag, so the bar
    /// follows the cursor instead of the slightly older polled value.
    public void Refresh(double level, bool muted)
    {
        if (_dragging) return;
        Show(level, muted);
    }

    void Show(double level, bool muted)
    {
        double shown = muted ? 0 : Math.Clamp(level, 0, 1);

        Done.Width = new GridLength(shown, GridUnitType.Star);
        Left.Width = new GridLength(1 - shown, GridUnitType.Star);
        Value.Text = muted ? "Mudo" : $"{(int)Math.Round(shown * 100)}%";
        Glyph.Text = muted ? "" : "";
    }

    void Apply(MouseEventArgs e)
    {
        double fraction = Math.Clamp(e.GetPosition(Bar).X / Math.Max(Bar.ActualWidth, 1), 0, 1);
        _volume.Set(fraction);
        Show(fraction, false);
    }

    // The island starts a window drag from any unhandled mouse-down, so these
    // handle the events: dragging the bar must move the volume, not the island.
    void Bar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _dragging = true;
        Bar.CaptureMouse();
        Apply(e);
    }

    void Bar_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging) Apply(e);
    }

    void Bar_MouseUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _dragging = false;
        Bar.ReleaseMouseCapture();
    }

    void Mute_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _volume.ToggleMute();
    }
}
