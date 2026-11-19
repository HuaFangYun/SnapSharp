using System.Drawing;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NimCap.Screenshot.Models;

namespace NimCap.Screenshot.Services;

internal static class DesktopCaptureComposer
{
    internal static CapturedScreen Compose(IReadOnlyList<CapturedScreen> screens)
    {
        if (screens.Count == 0) throw new ArgumentException("No display captures.", nameof(screens));
        var bounds = screens.Select(x => x.Bounds).Aggregate(Rectangle.Union);
        var stride = checked(bounds.Width * 4);
        var pixels = new byte[checked(stride * bounds.Height)];
        // Gaps between monitors are opaque black, not uninitialized memory.
        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        foreach (var screen in screens)
        {
            if (screen.Image.PixelWidth != screen.Bounds.Width || screen.Image.PixelHeight != screen.Bounds.Height)
                throw new InvalidOperationException("Capture dimensions must match physical monitor bounds after rotation.");
            BitmapSource image = screen.Image.Format == PixelFormats.Bgra32 ? screen.Image :
                new FormatConvertedBitmap(screen.Image, PixelFormats.Bgra32, null, 0);
            var sourceStride = checked(image.PixelWidth * 4);
            var source = new byte[checked(sourceStride * image.PixelHeight)];
            image.CopyPixels(source, sourceStride, 0);
            var x = screen.Bounds.Left - bounds.Left;
            var y = screen.Bounds.Top - bounds.Top;
            for (var row = 0; row < image.PixelHeight; row++)
                Buffer.BlockCopy(source, row * sourceStride, pixels, checked((y + row) * stride + x * 4), sourceStride);
        }
        var bitmap = BitmapSource.Create(bounds.Width, bounds.Height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return new CapturedScreen(bounds, bitmap);
    }
}
