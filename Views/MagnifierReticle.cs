using System.Windows;
using System.Windows.Media;
using NimCap.Screenshot.Models;

namespace NimCap.Screenshot.Views;

/// <summary>Targets the sampled pixel, including when a desktop edge shifts it away from the lens center.</summary>
public sealed class MagnifierReticle : FrameworkElement
{
    public MagnifierReticle() => PixelReticle.TrackDeviceTransform(this);

    private int? _column, _row;
    public int Columns { get; set; } = 17;
    public int Rows { get; set; } = 17;
    internal Rect PixelBounds => new((_column ?? Columns / 2) * ActualWidth / Columns,
        (_row ?? Rows / 2) * ActualHeight / Rows, ActualWidth / Columns, ActualHeight / Rows);

    internal void SetPixel(int column, int row)
    {
        _column = Math.Clamp(column, 0, Columns - 1); _row = Math.Clamp(row, 0, Rows - 1);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0 || Columns <= 0 || Rows <= 0) return;
        var pixel = PixelBounds;
        var x = pixel.Left + pixel.Width / 2; var y = pixel.Top + pixel.Height / 2;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        var transform = PixelReticle.DeviceTransform(this);
        var scale = Math.Min(Math.Abs(transform.M11), Math.Abs(transform.M22));
        PixelReticle.DrawCrosshair(dc, new Rect(0, 0, ActualWidth, ActualHeight), new Point(x, y),
            Math.Max(6, Math.Max(pixel.Width, pixel.Height) / 2 + 2 / Math.Max(scale, .001)), transform);
        PixelReticle.DrawPixelFrame(dc, pixel, transform);
        dc.Pop();
    }
}
