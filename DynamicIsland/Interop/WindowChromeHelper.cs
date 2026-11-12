using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DynamicIsland.Interop;

/// Turns a WPF window into a non-activating always-on-top overlay: clicking it
/// never steals focus from whatever the user is typing in, and it stays out of
/// Alt+Tab and the taskbar.
public static class WindowChromeHelper
{
    const int GwlExStyle = -20;
    const int WsExNoActivate = 0x08000000;
    const int WsExToolWindow = 0x00000080;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    [StructLayout(LayoutKind.Sequential)]
    struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetCursorPos(out NativePoint point);

    /// Pointer position in device pixels. Read from Win32 rather than from a
    /// WPF mouse event, because during a drag the window is moving underneath
    /// the pointer and window-relative coordinates chase their own tail.
    public static System.Windows.Point CursorPosition()
    {
        return GetCursorPos(out var point)
            ? new System.Windows.Point(point.X, point.Y)
            : new System.Windows.Point();
    }

    public static void MakeOverlay(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        int ex = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, ex | WsExNoActivate | WsExToolWindow);
    }
}
