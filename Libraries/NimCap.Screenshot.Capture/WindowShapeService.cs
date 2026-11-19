using System.Drawing;
using System.Runtime.InteropServices;
using NimCap.Screenshot.Models;
using NimCap.Screenshot.Native;

namespace NimCap.Screenshot.Services;

internal static class WindowShapeService
{
    internal static WindowCaptureShape? Snapshot(nint hwnd, Rectangle bounds)
    {
        if (hwnd == 0 || bounds.Width <= 0 || bounds.Height <= 0) return null;
        // Application-defined regions take precedence over DWM's corner preference.
        var region = CreateRectRgn(0, 0, 0, 0);
        if (region != 0)
        {
            try
            {
                var kind = GetWindowRgn(hwnd, region);
                if (kind is 2 or 3 && NativeMethods.GetWindowRect(hwnd, out var frame))
                {
                    var size = GetRegionData(region, 0, null);
                    if (size is >= 32 and <= 1048576)
                    {
                        var bytes = new byte[size];
                        if (GetRegionData(region, size, bytes) == size)
                        {
                            var count = BitConverter.ToUInt32(bytes, 8);
                            if (count <= (size - 32) / 16)
                            {
                                var rectangles = new List<Rectangle>();
                                for (var i = 0; i < count; i++)
                                {
                                    var offset = 32 + i * 16;
                                    rectangles.Add(Rectangle.FromLTRB(frame.Left + BitConverter.ToInt32(bytes, offset),
                                        frame.Top + BitConverter.ToInt32(bytes, offset + 4),
                                        frame.Left + BitConverter.ToInt32(bytes, offset + 8),
                                        frame.Top + BitConverter.ToInt32(bytes, offset + 12)));
                                }
                                return new WindowCaptureShape(bounds, RegionRectangles: rectangles);
                            }
                        }
                    }
                }
            }
            finally { NativeMethods.DeleteObject(region); }
        }
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || IsZoomed(hwnd) || IsIconic(hwnd)) return null;
        // DWMWA_WINDOW_CORNER_PREFERENCE is meaningful only on Windows 11+.
        if (NativeMethods.DwmGetWindowAttribute(hwnd, 33, out var preference, sizeof(int)) != 0 || preference == 1) return null;
        var style = NativeMethods.GetWindowLongPtr(hwnd, -16).ToInt64();
        var extended = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        // Default rounding is not guaranteed for borderless or per-pixel layered windows.
        if ((extended & 0x00080000L) != 0) return null; // WS_EX_LAYERED
        if (preference == 0 && (style & 0x00C00000L) != 0x00C00000L && (style & 0x00040000L) == 0) return null;
        if (preference is not (0 or 2 or 3)) return null;
        var dpi = GetDpiForWindow(hwnd);
        var radius = (preference == 3 ? 4d : 8d) * (dpi == 0 ? 96 : dpi) / 96;
        return new WindowCaptureShape(bounds, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2d));
    }

    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(nint hwnd, nint region);
    [DllImport("gdi32.dll")] private static extern uint GetRegionData(nint region, uint size, [Out] byte[]? data);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint hwnd);
}
