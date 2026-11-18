using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapSharp.Views;
using Color = System.Windows.Media.Color;
using Size = System.Windows.Size;

namespace SnapSharp.WindowChecks;

internal static partial class Program
{
    private static void RulerMagnifierChecks()
    {
        foreach (var (name, color) in new[] { ("white", Colors.White), ("black", Colors.Black), ("color", Color.FromRgb(35, 86, 129)) })
        {
            var reticle = new MagnifierReticle();
            var lens = new Grid { Width = 119, Height = 119, Background = new SolidColorBrush(color) };
            // Match the 96-DPI render target rather than inheriting desktop DPI.
            lens.Children.Add(reticle);
            VisualTreeHelper.SetRootDpi(lens, new DpiScale(1, 1));
            lens.Measure(new Size(119, 119)); lens.Arrange(new Rect(0, 0, 119, 119)); lens.UpdateLayout();
            reticle.SetPixel(8, 8);
            var bitmap = new RenderTargetBitmap(119, 119, 96, 96, PixelFormats.Pbgra32); bitmap.Render(lens);
            var pixels = new byte[119 * 119 * 4]; bitmap.CopyPixels(pixels, 119 * 4, 0);
            bool IsColor(int x, int y, Color expected)
            {
                var i = (y * 119 + x) * 4;
                return pixels[i] == expected.B && pixels[i + 1] == expected.G && pixels[i + 2] == expected.R;
            }
            Require(reticle.PixelBounds == new Rect(56, 56, 7, 7), "Magnifier frame is not centered on the sampled pixel.");
            Require(IsColor(59, 59, color), "The crosshair paints over the sampled pixel's center color.");
            // Check an arm away from the central pixel: both high-contrast strokes must exist.
            Require(Enumerable.Range(56, 8).Any(y => IsColor(12, y, Colors.Black)) &&
                Enumerable.Range(56, 8).Any(y => IsColor(12, y, Colors.White)),
                "The magnifier reticle disappears on a uniform white or black background.");
            Require(Enumerable.Range(56, 8).Any(x => IsColor(x, 12, Colors.Black)) &&
                Enumerable.Range(56, 8).Any(x => IsColor(x, 12, Colors.White)),
                "The magnifier lacks a visible vertical crosshair arm.");
            SavePreview(lens, Path.Combine(Root, "magnifier-reticle-" + name + ".png"));
            reticle.SetPixel(2, 13);
            Require(reticle.PixelBounds == new Rect(14, 91, 7, 7), "Crosshair does not track an off-center sampled pixel.");
            reticle.SetPixel(-4, 30);
            Require(reticle.PixelBounds == new Rect(0, 112, 7, 7), "Desktop-edge sampling positions are not clamped to the lens.");
            SavePreview(lens, Path.Combine(Root, "magnifier-reticle-edge-" + name + ".png"));
        }
        Console.WriteLine("PASS: magnifier crosshair and pixel frame visible on white/black/color; center color preserved; off-center tracking and desktop-edge positions.");
    }
}
