using System.Globalization;
using System.Windows;
using System.Windows.Media;
using NimCap.Screenshot.Models;

namespace NimCap.Screenshot.Views;

/// <summary>Screen pixel rulers and an unscaled pixel crosshair, rendered in monitor DIPs.</summary>
public sealed class PixelGuides : FrameworkElement
{
    public PixelGuides() => PixelReticle.TrackDeviceTransform(this);

    private DrawingGroup? _rulerDrawing;
    private (double Width, double Height, int PixelWidth, int PixelHeight, int OriginX, int OriginY, bool InsetOrigin, double Dpi) _rulerState;
    public bool ShowRulers { get; set; }
    public bool ShowCrosshair { get; set; } = true;
    public Point Pointer { get; set; }
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
    public int OriginX { get; set; }
    public int OriginY { get; set; }
    public bool InsetOrigin { get; set; }

    internal Rect MeasurementBounds
    {
        get
        {
            var inset = InsetOrigin ? 24 : 0;
            return new Rect(inset, inset, Math.Max(0, ActualWidth - inset), Math.Max(0, ActualHeight - inset));
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0 || PixelWidth <= 0 || PixelHeight <= 0) return;
        var bounds = MeasurementBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var px = bounds.Width / PixelWidth;
        var py = bounds.Height / PixelHeight;
        if (ShowRulers)
        {
            var state = (ActualWidth, ActualHeight, PixelWidth, PixelHeight, OriginX, OriginY, InsetOrigin, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            if (_rulerDrawing is null || state != _rulerState)
            {
                _rulerState = state;
                _rulerDrawing = new DrawingGroup();
                using (var drawing = _rulerDrawing.Open())
                {
                    var background = new SolidColorBrush(Color.FromArgb(235, 24, 27, 32));
                    drawing.DrawRectangle(background, null, new Rect(0, 0, ActualWidth, 24));
                    drawing.DrawRectangle(background, null, new Rect(0, 24, 24, Math.Max(0, ActualHeight - 24)));
                    DrawAxis(drawing, PixelWidth, px, OriginX, false, bounds.Left);
                    DrawAxis(drawing, PixelHeight, py, OriginY, true, bounds.Top);
                    if (InsetOrigin)
                    {
                        var baseline = new Pen(Brushes.DodgerBlue, 1);
                        drawing.DrawLine(baseline, bounds.TopLeft, bounds.TopRight);
                        drawing.DrawLine(baseline, bounds.TopLeft, bounds.BottomLeft);
                    }
                }
                _rulerDrawing.Freeze();
            }
            dc.DrawDrawing(_rulerDrawing);
        }
        if (ShowCrosshair) DrawCrosshair(dc, bounds, px, py);
        if (ShowRulers && InsetOrigin)
            dc.DrawEllipse(Brushes.DodgerBlue, new Pen(Brushes.White, 1), bounds.TopLeft, 3, 3);
    }

    private void DrawCrosshair(DrawingContext dc, Rect bounds, double px, double py)
    {
        var x = Math.Clamp(bounds.Left + Math.Floor((Pointer.X - bounds.Left) / px) * px + px / 2, bounds.Left, bounds.Right);
        var y = Math.Clamp(bounds.Top + Math.Floor((Pointer.Y - bounds.Top) / py) * py + py / 2, bounds.Top, bounds.Bottom);
        var transform = PixelReticle.DeviceTransform(this);
        PixelReticle.DrawCrosshair(dc, bounds, new Point(x, y), 7, transform);
        PixelReticle.DrawPixelFrame(dc, new Rect(x - px / 2, y - py / 2, px, py), transform);
    }

    private void DrawAxis(DrawingContext dc, int count, double scale, int origin, bool vertical, double offset)
    {
        var pen = new Pen(Brushes.LightGray, .75);
        var step = scale >= .75 ? 1 : 5;
        var major = scale >= .6 ? 50 : 100;
        for (var pixel = 0; pixel <= count; pixel += step)
        {
            var position = offset + pixel * scale;
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
