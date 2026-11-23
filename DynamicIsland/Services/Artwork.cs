using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland.Services;

/// Turns the raw thumbnail bytes from MediaService into something WPF can
/// show. Shared by the media view and the overview card so the decode settings
/// stay in one place.
public static class Artwork
{
    public static ImageSource? Decode(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 160;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }
}
