using System.Windows;
using System.Windows.Media;

namespace SnapSharp.Views;

/// <summary>Targets the sampled pixel, including when a desktop edge shifts it away from the lens center.</summary>
internal sealed class RulerMagnifierReticle : FrameworkElement
{
    private int _column = 8, _row = 8;
    internal Rect PixelBounds => new(_column * ActualWidth / 17, _row * ActualHeight / 17, ActualWidth / 17, ActualHeight / 17);

    internal void SetPixel(int column, int row)
    {
        _column = Math.Clamp(column, 0, 16); _row = Math.Clamp(row, 0, 16);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var pixel = PixelBounds;
        var x = pixel.Left + pixel.Width / 2; var y = pixel.Top + pixel.Height / 2;
        const double gap = 6;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        // Both outlines are always present: black remains visible on white and white on black.
        foreach (var pen in new[] { new Pen(Brushes.Black, 3), new Pen(Brushes.White, 1) })
        {
            if (x > gap) dc.DrawLine(pen, new Point(0, y), new Point(x - gap, y));
            if (x + gap < ActualWidth) dc.DrawLine(pen, new Point(x + gap, y), new Point(ActualWidth, y));
            if (y > gap) dc.DrawLine(pen, new Point(x, 0), new Point(x, y - gap));
            if (y + gap < ActualHeight) dc.DrawLine(pen, new Point(x, y + gap), new Point(x, ActualHeight));
            dc.DrawRectangle(null, pen, new Rect(pixel.Left + .5, pixel.Top + .5,
                Math.Max(0, pixel.Width - 1), Math.Max(0, pixel.Height - 1)));
        }
        dc.Pop();
    }
}
