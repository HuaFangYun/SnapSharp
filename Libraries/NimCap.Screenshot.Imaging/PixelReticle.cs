using System.Windows;
using System.Windows.Media;

namespace NimCap.Screenshot.Models;

/// <summary>Adjacent black and white device pixels, without a third outline or antialiasing fringe.</summary>
public static class PixelReticle
{
    public static void TrackDeviceTransform(FrameworkElement visual)
    {
        Matrix? previous = null;
        visual.LayoutUpdated += (_, _) =>
        {
            var current = DeviceTransform(visual);
            if (current == previous) return;
            previous = current;
            visual.InvalidateVisual();
        };
    }

    public static Matrix DeviceTransform(Visual visual)
    {
        var root = visual;
        while (VisualTreeHelper.GetParent(root) is Visual parent) root = parent;
        var transform = visual.TransformToAncestor(root);
        var origin = transform.Transform(new Point());
        var x = transform.Transform(new Point(1, 0)) - origin;
        var y = transform.Transform(new Point(0, 1)) - origin;
        var matrix = new Matrix(x.X, x.Y, y.X, y.Y, origin.X, origin.Y);
        var dpi = VisualTreeHelper.GetDpi(visual);
        matrix.Scale(dpi.DpiScaleX, dpi.DpiScaleY);
        return matrix;
    }

    public static void DrawCrosshair(DrawingContext dc, Rect bounds, Point center, double gap = 0,
        Matrix? deviceTransform = null)
    {
        var transform = deviceTransform ?? Matrix.Identity;
        if (!transform.HasInverse) return;
        var area = new MatrixTransform(transform).TransformBounds(bounds);
        var point = transform.Transform(center);
        var x = Math.Floor(point.X); var y = Math.Floor(point.Y);
        var gapX = gap * Math.Abs(transform.M11); var gapY = gap * Math.Abs(transform.M22);
        var inverse = transform; inverse.Invert();
        dc.PushTransform(new MatrixTransform(inverse));
        // Draw the two colors alongside each other, rather than a narrow line over a wide one.
        for (var offset = 0; offset < 2; offset++)
        {
            var brush = offset == 0 ? Brushes.Black : Brushes.White;
            if (gap <= 0)
            {
                Fill(dc, brush, Math.Ceiling(area.Left), y + offset, Math.Floor(area.Right), y + offset + 1);
                Fill(dc, brush, x + offset, Math.Ceiling(area.Top), x + offset + 1, Math.Floor(area.Bottom));
            }
            else
            {
                Fill(dc, brush, Math.Ceiling(area.Left), y + offset, Math.Floor(point.X - gapX), y + offset + 1);
                Fill(dc, brush, Math.Ceiling(point.X + gapX), y + offset, Math.Floor(area.Right), y + offset + 1);
                Fill(dc, brush, x + offset, Math.Ceiling(area.Top), x + offset + 1, Math.Floor(point.Y - gapY));
                Fill(dc, brush, x + offset, Math.Ceiling(point.Y + gapY), x + offset + 1, Math.Floor(area.Bottom));
            }
        }
        dc.Pop();
    }

    public static void DrawPixelFrame(DrawingContext dc, Rect pixel, Matrix? deviceTransform = null)
    {
        var transform = deviceTransform ?? Matrix.Identity;
        if (!transform.HasInverse) return;
        var area = new MatrixTransform(transform).TransformBounds(pixel);
        var inverse = transform; inverse.Invert();
        dc.PushTransform(new MatrixTransform(inverse));
        // Keep both one-pixel borders outside the sampled pixel so its color stays intact.
        var left = Math.Floor(area.Left); var top = Math.Floor(area.Top);
        var right = Math.Ceiling(area.Right); var bottom = Math.Ceiling(area.Bottom);
        Border(dc, Brushes.Black, left - 2, top - 2, right + 2, bottom + 2);
        Border(dc, Brushes.White, left - 1, top - 1, right + 1, bottom + 1);
        dc.Pop();
    }

    private static void Border(DrawingContext dc, Brush brush, double left, double top, double right, double bottom)
    {
        Fill(dc, brush, left, top, right, top + 1);
        Fill(dc, brush, left, bottom - 1, right, bottom);
        Fill(dc, brush, left, top + 1, left + 1, bottom - 1);
        Fill(dc, brush, right - 1, top + 1, right, bottom - 1);
    }

    private static void Fill(DrawingContext dc, Brush brush, double left, double top, double right, double bottom)
    {
        if (right > left && bottom > top) dc.DrawRectangle(brush, null, new Rect(left, top, right - left, bottom - top));
    }
}
