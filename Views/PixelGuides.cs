using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SnapSharp.Views;

/// <summary>Screen pixel rulers and an unscaled pixel crosshair, rendered in monitor DIPs.</summary>
public sealed class PixelGuides : FrameworkElement
{
    private DrawingGroup? _rulerDrawing;
    private (double Width, double Height, int PixelWidth, int PixelHeight, int OriginX, int OriginY, double Dpi) _rulerState;
    public bool ShowRulers { get; set; }
    public bool ShowCrosshair { get; set; } = true;
    public Point Pointer { get; set; }
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
    public int OriginX { get; set; }
    public int OriginY { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0 || PixelWidth <= 0 || PixelHeight <= 0) return;
        var px = ActualWidth / PixelWidth;
        var py = ActualHeight / PixelHeight;
        if (ShowRulers)
        {
            var state = (ActualWidth, ActualHeight, PixelWidth, PixelHeight, OriginX, OriginY, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            if (_rulerDrawing is null || state != _rulerState)
            {
                _rulerState = state;
                _rulerDrawing = new DrawingGroup();
                using (var drawing = _rulerDrawing.Open())
                {
                    var background = new SolidColorBrush(Color.FromArgb(235, 24, 27, 32));
                    drawing.DrawRectangle(background, null, new Rect(0, 0, ActualWidth, 24));
                    drawing.DrawRectangle(background, null, new Rect(0, 24, 24, Math.Max(0, ActualHeight - 24)));
                    DrawAxis(drawing, PixelWidth, px, OriginX, false);
                    DrawAxis(drawing, PixelHeight, py, OriginY, true);
                }
                _rulerDrawing.Freeze();
            }
            dc.DrawDrawing(_rulerDrawing);
        }
        if (!ShowCrosshair) return;
        var x = Math.Clamp(Math.Floor(Pointer.X / px) * px + px / 2, 0, ActualWidth);
        var y = Math.Clamp(Math.Floor(Pointer.Y / py) * py + py / 2, 0, ActualHeight);
        foreach (var pen in new[] { new Pen(Brushes.Black, 2.5), new Pen(Brushes.White, .75) })
        {
            dc.DrawLine(pen, new Point(0, y), new Point(Math.Max(0, x - 7), y));
            dc.DrawLine(pen, new Point(Math.Min(ActualWidth, x + 7), y), new Point(ActualWidth, y));
            dc.DrawLine(pen, new Point(x, 0), new Point(x, Math.Max(0, y - 7)));
            dc.DrawLine(pen, new Point(x, Math.Min(ActualHeight, y + 7)), new Point(x, ActualHeight));
        }
        dc.DrawRectangle(null, new Pen(Brushes.White, .75), new Rect(x - px / 2, y - py / 2, px, py));
    }

    private void DrawAxis(DrawingContext dc, int count, double scale, int origin, bool vertical)
    {
        var pen = new Pen(Brushes.LightGray, .75);
        var step = scale >= .75 ? 1 : 5;
        var major = scale >= .6 ? 50 : 100;
        for (var pixel = 0; pixel <= count; pixel += step)
        {
            var position = pixel * scale;
            var length = pixel % major == 0 ? 12 : pixel % 10 == 0 ? 8 : pixel % 5 == 0 ? 5 : 2;
            dc.DrawLine(pen, vertical ? new Point(24 - length, position) : new Point(position, 24 - length),
                vertical ? new Point(24, position) : new Point(position, 24));
            if (pixel % major != 0) continue;
            var text = new FormattedText((origin + pixel).ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Consolas"), 10, Brushes.White,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            if (vertical)
            {
                dc.PushTransform(new RotateTransform(-90, 2, position - 2));
                dc.DrawText(text, new Point(2, position - 2));
                dc.Pop();
            }
            else dc.DrawText(text, new Point(position + 3, 0));
        }
    }
}
