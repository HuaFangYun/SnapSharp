using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace NimCap.Screenshot.Services;

/// <summary>Constrain dialogs in DIPs to their current monitor, without bitmap scaling.</summary>
internal static class DialogLayoutService
{
    internal static bool IsPerMonitorV2 => AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), new nint(-4));
    internal static (double Width, double Height) AvailableSize(int pixelWidth, int pixelHeight, double dpiX, double dpiY) =>
        (Math.Max(240, pixelWidth / dpiX - 24), Math.Max(200, pixelHeight / dpiY - 24));
    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) => Fit(window);
        window.Loaded += (_, _) => Fit(window);
        window.DpiChanged += (_, _) => window.Dispatcher.BeginInvoke(() => Fit(window), DispatcherPriority.Loaded);
    }
    private static void Fit(Window window)
    {
        if (!window.IsInitialized) return;
        var handle = new WindowInteropHelper(window).Handle; if (handle == 0) return;
        var area = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(window); var size = AvailableSize(area.Width, area.Height, dpi.DpiScaleX, dpi.DpiScaleY);
        // Lower the minimum first: WPF must not keep a 200%-scaled window larger than the screen.
        window.MinWidth = Math.Min(640, size.Width); window.MinHeight = Math.Min(420, size.Height);
        window.MaxWidth = size.Width; window.MaxHeight = size.Height;
        if (window.WindowState != WindowState.Normal) return;
        window.Width = Math.Min(window.Width, size.Width); window.Height = Math.Min(window.Height, size.Height);
        if (!window.IsLoaded || !GetWindowRect(handle, out var rect)) return;
        var x = Math.Clamp(rect.Left, area.Left, Math.Max(area.Left, area.Right - (rect.Right - rect.Left)));
        var y = Math.Clamp(rect.Top, area.Top, Math.Max(area.Top, area.Bottom - (rect.Bottom - rect.Top)));
        if (x != rect.Left || y != rect.Top) SetWindowPos(handle, 0, x, y, 0, 0, 0x0015); // NOACTIVATE | NOZORDER | NOSIZE
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern nint GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AreDpiAwarenessContextsEqual(nint first, nint second);
}
