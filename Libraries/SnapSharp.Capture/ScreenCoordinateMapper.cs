using SnapSharp.Models;
using SnapSharp.Native;
using System.Drawing;
using System.Windows;
using System.Windows.Media;

namespace SnapSharp.Services;

// Architecture references: Aquashot's physical-pixel selection model and
// WpfMonitorAware's per-monitor WPF coordinate handling. Independently implemented.
internal static class ScreenCoordinateMapper
{
    internal static Point CursorToLocalDip(Visual relativeTo, NativeMethods.PointL cursor) =>
        relativeTo.PointFromScreen(new Point(cursor.X, cursor.Y));

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
        var width = Math.Clamp((int)Math.Round(selectionDip.Width * scaleX), 1, imagePixelWidth - x);
        var height = Math.Clamp((int)Math.Round(selectionDip.Height * scaleY), 1, imagePixelHeight - y);
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
