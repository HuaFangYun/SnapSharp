using SnapSharp.Models;
using System.Drawing;
using System.Windows;

namespace SnapSharp.Services;

// Architecture references: Aquashot's physical-pixel selection model and
// WpfMonitorAware's per-monitor WPF coordinate handling. Independently implemented.
internal static class ScreenCoordinateMapper
{
    internal static Point DesktopPixelsToSurface(Point desktopPixel, Rectangle bounds, System.Windows.Size surfaceDip)
    {
        ValidateSurface(bounds, surfaceDip);
        return new((desktopPixel.X - bounds.Left) * surfaceDip.Width / bounds.Width,
            (desktopPixel.Y - bounds.Top) * surfaceDip.Height / bounds.Height);
    }

    internal static Point SurfaceToDesktopPixels(Point localDip, Rectangle bounds, System.Windows.Size surfaceDip)
    {
        ValidateSurface(bounds, surfaceDip);
        return new(bounds.Left + localDip.X * bounds.Width / surfaceDip.Width,
            bounds.Top + localDip.Y * bounds.Height / surfaceDip.Height);
    }

    private static void ValidateSurface(Rectangle bounds, System.Windows.Size surface)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || !double.IsFinite(surface.Width) ||
            !double.IsFinite(surface.Height) || surface.Width <= 0 || surface.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(surface));
    }

    internal static PixelRect DipRectToImagePixels(
        Rect selectionDip,
        System.Windows.Size surfaceDip,
        int imagePixelWidth,
        int imagePixelHeight)
    {
        if (surfaceDip.Width <= 0 || surfaceDip.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(surfaceDip));
        if (imagePixelWidth <= 0 || imagePixelHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(imagePixelWidth));

        var scaleX = imagePixelWidth / surfaceDip.Width;
        var scaleY = imagePixelHeight / surfaceDip.Height;
        var x = Math.Clamp((int)Math.Round(selectionDip.X * scaleX), 0, imagePixelWidth - 1);
        var y = Math.Clamp((int)Math.Round(selectionDip.Y * scaleY), 0, imagePixelHeight - 1);
        // Round both edges independently so adjacent regions share the same physical boundary.
        var right = Math.Clamp((int)Math.Round(selectionDip.Right * scaleX), x + 1, imagePixelWidth);
        var bottom = Math.Clamp((int)Math.Round(selectionDip.Bottom * scaleY), y + 1, imagePixelHeight);
        var width = right - x;
        var height = bottom - y;
        return new PixelRect(x, y, width, height);
    }

    internal static PixelRect ImagePixelsToScreenPixels(Rectangle screenBounds, PixelRect imageRegion) =>
        new(screenBounds.Left + imageRegion.X, screenBounds.Top + imageRegion.Y,
            imageRegion.Width, imageRegion.Height);

    internal static Point ScreenPixelsToImagePoint(
        int cursorX,
        int cursorY,
        PixelRect screenPixelBounds,
        int imagePixelWidth,
        int imagePixelHeight)
    {
        if (screenPixelBounds.IsEmpty || imagePixelWidth <= 0 || imagePixelHeight <= 0)
            return default;

        var x = (cursorX - screenPixelBounds.Left) * imagePixelWidth /
                (double)screenPixelBounds.Width;
        var y = (cursorY - screenPixelBounds.Top) * imagePixelHeight /
                (double)screenPixelBounds.Height;
        return new Point(
            Math.Clamp(x, 0, imagePixelWidth),
            Math.Clamp(y, 0, imagePixelHeight));
    }
}
