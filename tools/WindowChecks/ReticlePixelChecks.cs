using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapSharp.Models;
using SnapSharp.Views;
using SnapSharp.ViewModels;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace SnapSharp.WindowChecks;

internal static partial class Program
{
    private static void ReticlePixelChecks()
    {
        var background = Color.FromRgb(35, 86, 129);
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2, 3 })
        foreach (var zoom in new[] { .75, 1d, 1.5 })
        foreach (var kind in new[] { "ruler", "capture", "guides", "annotation" })
        {
            FrameworkElement visual = kind switch
            {
                "ruler" => new MagnifierReticle { Width = 119, Height = 119 },
                "capture" => new MagnifierReticle { Width = 266, Height = 154, Columns = 19, Rows = 11 },
                "guides" => new PixelGuides { Width = 119, Height = 119, PixelWidth = 119, PixelHeight = 119, Pointer = new Point(70, 80) },
                _ => AnnotationReticle(background)
            };
            visual.RenderTransform = new ScaleTransform(zoom, zoom);
            var root = new Canvas { Width = visual.Width * zoom + 24, Height = visual.Height * zoom + 24,
                Background = new SolidColorBrush(background) };
            Canvas.SetLeft(visual, 7.25); Canvas.SetTop(visual, 9.5);
            root.Children.Add(visual);
            VisualTreeHelper.SetRootDpi(root, new DpiScale(scale, scale));
            root.Measure(new Size(root.Width, root.Height)); root.Arrange(new Rect(0, 0, root.Width, root.Height)); root.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.Width * scale), (int)Math.Ceiling(root.Height * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            Color At(int x, int y)
            {
                var index = (y * bitmap.PixelWidth + x) * 4;
                return Color.FromRgb(pixels[index + 2], pixels[index + 1], pixels[index]);
            }
            var transform = PixelReticle.DeviceTransform(visual);
            var probe = transform.Transform(new Point(kind == "annotation" ? 58 : 12, kind == "annotation" ? 58 : 12));
            var probeX = (int)Math.Floor(probe.X); var probeY = (int)Math.Floor(probe.Y);
            // Sample around the arm, excluding unrelated image-edge interpolation in the annotation preview.
            var center = transform.Transform(new Point(60, 60));
            var yStart = kind == "annotation" ? (int)center.Y - 4 : 1;
            var xStart = kind == "annotation" ? (int)center.X - 4 : 1;
            var column = Enumerable.Range(yStart, kind == "annotation" ? 10 : bitmap.PixelHeight - 2)
                .Select(y => At(probeX, y)).Where(c => c != background).ToArray();
            var row = Enumerable.Range(xStart, kind == "annotation" ? 10 : bitmap.PixelWidth - 2)
                .Select(x => At(x, probeY)).Where(c => c != background).ToArray();
            var description = $"{kind}, DPI {scale}, zoom {zoom}";
            Require(column.SequenceEqual(new[] { Colors.Black, Colors.White }), "Horizontal arm is not exactly one black and one white pixel: " + description + "; " + string.Join(",", column));
            Require(row.SequenceEqual(new[] { Colors.Black, Colors.White }), "Vertical arm is not exactly one black and one white pixel: " + description + "; " + string.Join(",", row));
            if (visual is MagnifierReticle reticle)
            {
                var pixel = new MatrixTransform(transform).TransformBounds(reticle.PixelBounds);
                var x = (int)Math.Floor(pixel.Left); var y = (int)Math.Floor(pixel.Top);
                Require(At(x, y - 3) == background && At(x, y - 2) == Colors.Black &&
                    At(x, y - 1) == Colors.White && At(x, y) == background,
                    "Pixel frame has an extra outline or covers the sampled pixel: " + description);
                Require(At((int)(pixel.Left + pixel.Width / 2), (int)(pixel.Top + pixel.Height / 2)) == background,
                    "Reticle obscures the sampled center: " + description);
            }
        }

        var handle = (System.Runtime.InteropServices.SafeHandle)typeof(System.Windows.Input.Cursor)
            .GetProperty("Handle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(PrecisionCursor.Cross)!;
        using var cursor = new System.Windows.Forms.Cursor(handle.DangerousGetHandle());
        Require(cursor.HotSpot == new System.Drawing.Point(15, 15), "Precision cursor hotspot moved away from the target.");
        using var cursorBitmap = new System.Drawing.Bitmap(32, 32);
        using (var graphics = System.Drawing.Graphics.FromImage(cursorBitmap))
        {
            graphics.Clear(System.Drawing.Color.FromArgb(background.R, background.G, background.B));
            cursor.Draw(graphics, new System.Drawing.Rectangle(0, 0, 32, 32));
        }
        var cursorBackground = System.Drawing.Color.FromArgb(background.R, background.G, background.B).ToArgb();
        var horizontal = Enumerable.Range(0, 32).Select(y => cursorBitmap.GetPixel(6, y).ToArgb()).Where(c => c != cursorBackground);
        var vertical = Enumerable.Range(0, 32).Select(x => cursorBitmap.GetPixel(x, 6).ToArgb()).Where(c => c != cursorBackground);
        var pair = new[] { System.Drawing.Color.Black.ToArgb(), System.Drawing.Color.White.ToArgb() };
        Require(horizontal.SequenceEqual(pair) && vertical.SequenceEqual(pair), "Precision cursor arms are not one black and one white pixel.");
        cursorBitmap.Save(System.IO.Path.Combine(Root, "precision-cursor.png"));
        Console.WriteLine("PASS: crosshairs and frames use exactly one black and one white device pixel at 100/125/150/200/300% DPI, zoom and fractional offsets; sample centers and cursor hotspot preserved.");
    }

    private static AnnotationSurface AnnotationReticle(Color color)
    {
        var pixels = new byte[119 * 119 * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = color.B; pixels[index + 1] = color.G; pixels[index + 2] = color.R; pixels[index + 3] = 255;
        }
        var image = BitmapSource.Create(119, 119, 96, 96, PixelFormats.Bgra32, null, pixels, 119 * 4);
        var model = new EditorViewModel(image);
        model.Add(new MagnifierAnnotation(new Point(60, 60), new Rect(90, 90, 10, 10)));
        model.SelectedAnnotation = null;
        return new AnnotationSurface { Width = 119, Height = 119, DataContext = model };
    }
}
