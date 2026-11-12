using System.Windows.Media;
using Windows.UI.ViewManagement;

namespace DynamicIsland.Interop;

/// Reads the colour the user picked in Windows personalisation.
public static class AccentColor
{
    static readonly Color Fallback = Color.FromRgb(0x4C, 0x8D, 0xFF);

    /// AccentLight2 rather than the raw accent: on a near-black island the raw
    /// colour is often too dark to read against the background.
    public static Color Current()
    {
        try
        {
            var value = new UISettings().GetColorValue(UIColorType.AccentLight2);
            return Color.FromRgb(value.R, value.G, value.B);
        }
        catch { return Fallback; }
    }

    public static Brush Brush()
    {
        var brush = new SolidColorBrush(Current());
        brush.Freeze();
        return brush;
    }
}
