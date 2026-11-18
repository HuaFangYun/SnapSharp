using SnapSharp.Models;
using SnapSharp.Native;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace SnapSharp.Services;

public sealed class ScreenCaptureService(bool includeCursor = false) : ICaptureService
{
    public bool IncludeCursor { get; } = includeCursor;

    public IReadOnlyList<CapturedScreen> CaptureAllScreens() => DisplayMonitorService.EnumerateBounds()
        .Select(bounds => new CapturedScreen(bounds, Capture(bounds))).ToArray();

    public BitmapSource CaptureRegion(Rectangle bounds) => Capture(bounds);

    public CapturedScreen CaptureVirtualScreen()
    {
        var bounds = DisplayMonitorService.GetVirtualBounds();
        return new CapturedScreen(bounds, Capture(bounds));
    }

    public CapturedScreen? CaptureWindow(nint windowHandle) =>
        TryGetWindowBounds(windowHandle, out var bounds) ? new CapturedScreen(bounds, Capture(bounds)) : null;

    public IReadOnlyList<WindowCandidate> EnumerateTopLevelWindows()
    {
        var result = new List<WindowCandidate>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (TryGetWindowBounds(hwnd, out var bounds)) result.Add(CreateCandidate(hwnd, bounds));
            return true;
        }, 0);
        return result;
    }

    public WindowCandidate? FindWindowAt(System.Drawing.Point screenPoint)
    {
        var hwnd = NativeMethods.WindowFromPoint(new NativeMethods.PointL { X = screenPoint.X, Y = screenPoint.Y });
        hwnd = NativeMethods.GetAncestor(hwnd, 2); // GA_ROOT
        return TryGetWindowBounds(hwnd, out var bounds) ? CreateCandidate(hwnd, bounds) : null;
    }

    private static WindowCandidate CreateCandidate(nint hwnd, Rectangle bounds)
    {
        Rectangle? client = null;
        if (NativeMethods.GetClientRect(hwnd, out var rect))
        {
            var origin = new NativeMethods.PointL { X = rect.Left, Y = rect.Top };
            var end = new NativeMethods.PointL { X = rect.Right, Y = rect.Bottom };
            if (NativeMethods.ClientToScreen(hwnd, ref origin) && NativeMethods.ClientToScreen(hwnd, ref end))
            {
                var clipped = Rectangle.Intersect(bounds, Rectangle.FromLTRB(origin.X, origin.Y, end.X, end.Y));
                if (clipped.Width > 0 && clipped.Height > 0) client = clipped;
            }
        }
        return new WindowCandidate(bounds, hwnd, client) { CaptureShape = WindowShapeService.Snapshot(hwnd, bounds) };
    }

    internal static bool TryGetWindowBounds(nint hwnd, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (hwnd == nint.Zero || !NativeMethods.IsWindowVisible(hwnd)) return false;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        if (processId == (uint)Environment.ProcessId || (exStyle & NativeMethods.WsExToolWindow) != 0) return false;
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
            return false;

        var hasDwmBounds = NativeMethods.DwmGetWindowRectAttribute(hwnd,
            NativeMethods.DwmwaExtendedFrameBounds, out var rect, Marshal.SizeOf<NativeMethods.Rect>()) == 0;
        if (!hasDwmBounds && !NativeMethods.GetWindowRect(hwnd, out rect)) return false;
        bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return bounds.Width >= 24 && bounds.Height >= 24;
    }

    private BitmapSource Capture(Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) throw new ArgumentOutOfRangeException(nameof(bounds));
        var screenDc = NativeMethods.GetDC(nint.Zero);
        if (screenDc == nint.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to acquire the desktop DC.");
        var memoryDc = nint.Zero;
        var bitmap = nint.Zero;
        var previous = nint.Zero;
        try
        {
            memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, bounds.Width, bounds.Height);
            if (memoryDc == nint.Zero || bitmap == nint.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to allocate the capture bitmap.");
            previous = NativeMethods.SelectObject(memoryDc, bitmap);
            if (!NativeMethods.BitBlt(memoryDc, 0, 0, bounds.Width, bounds.Height, screenDc,
                    bounds.Left, bounds.Top, NativeMethods.Srccopy | NativeMethods.CaptureBlt))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "BitBlt failed while capturing the screen.");
            if (IncludeCursor) DrawCursor(memoryDc, bounds);

            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, nint.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            if (previous != nint.Zero && memoryDc != nint.Zero) NativeMethods.SelectObject(memoryDc, previous);
            if (bitmap != nint.Zero) NativeMethods.DeleteObject(bitmap);
            if (memoryDc != nint.Zero) NativeMethods.DeleteDC(memoryDc);
            NativeMethods.ReleaseDC(nint.Zero, screenDc);
        }
    }

    private static void DrawCursor(nint destinationDc, Rectangle captureBounds)
    {
        var info = new NativeMethods.CursorInfo { Size = Marshal.SizeOf<NativeMethods.CursorInfo>() };
        if (!NativeMethods.GetCursorInfo(ref info) || info.Flags != NativeMethods.CursorShowing || info.Cursor == nint.Zero) return;
        if (!NativeMethods.GetIconInfo(info.Cursor, out var iconInfo)) return;
        try
        {
            NativeMethods.DrawIconEx(destinationDc,
                info.ScreenPosition.X - captureBounds.Left - iconInfo.HotspotX,
                info.ScreenPosition.Y - captureBounds.Top - iconInfo.HotspotY,
                info.Cursor, 0, 0, 0, nint.Zero, NativeMethods.DiNormal);
        }
        finally
        {
            if (iconInfo.ColorBitmap != nint.Zero) NativeMethods.DeleteObject(iconInfo.ColorBitmap);
            if (iconInfo.MaskBitmap != nint.Zero) NativeMethods.DeleteObject(iconInfo.MaskBitmap);
        }
    }
}

internal static class DisplayMonitorService
{
    internal sealed record Monitor(nint Handle, Rectangle Bounds, uint DpiX, uint DpiY, bool DpiKnown)
    {
        internal MonitorGeometry Geometry => new(Bounds, DpiX, DpiY);
    }

    internal static IReadOnlyList<Monitor> Enumerate()
    {
        var monitors = new List<Monitor>();
        if (!NativeMethods.EnumDisplayMonitors(nint.Zero, nint.Zero,
            (nint monitor, nint _, ref NativeMethods.Rect rect, nint _) =>
            {
                var dpi = ReadDpi(monitor);
                monitors.Add(new Monitor(monitor, Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom),
                    dpi.X, dpi.Y, dpi.Known));
                return true;
            }, nint.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "EnumDisplayMonitors failed.");
        if (monitors.Count == 0) throw new InvalidOperationException("Windows reported no active displays.");
        return monitors;
    }

    private static (uint X, uint Y, bool Known) ReadDpi(nint monitor)
    {
        // Query in a temporary per-monitor context; restore it before any WPF work.
        // Desktop rectangles themselves remain physical pixels.
        var previous = SetThreadDpiAwarenessContext(new nint(-3));
        try
        {
            if (GetDpiForMonitor(monitor, 0, out var x, out var y) == 0 && x > 0 && y > 0)
                return (x, y, true);
            if (GetScaleFactorForMonitor(monitor, out var percent) == 0 && percent > 0)
            {
                var dpi = (uint)Math.Round(96d * percent / 100);
                return (dpi, dpi, true);
            }
            return (96, 96, false);
        }
        finally { if (previous != 0) SetThreadDpiAwarenessContext(previous); }
    }

    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int kind, out uint x, out uint y);
    [DllImport("shcore.dll")] private static extern int GetScaleFactorForMonitor(nint monitor, out int scale);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);

    internal static IReadOnlyList<Rectangle> EnumerateBounds()
        => Enumerate().Select(x => x.Bounds).ToArray();

    internal static Rectangle GetVirtualBounds() => new(
        NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen),
        NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen),
        NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen),
        NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen));
}
