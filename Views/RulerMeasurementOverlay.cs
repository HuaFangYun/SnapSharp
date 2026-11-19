using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Size = System.Windows.Size;

namespace NimCap.Screenshot.Views;

internal sealed class RulerMeasurementOverlay(RulerMeasurements measurements) : FrameworkElement
{
    internal Rect Bounds { get; set; }
    internal Vector Scale { get; set; } = new(1, 1);
    internal int? HoveredGuide { get; set; }
    internal Rect? ExcludedBounds { get; set; }
    internal IReadOnlyList<Rect> LabelBounds { get; private set; } = [];
    internal Point ToLocal(Point p) => new(Bounds.Left + p.X * Scale.X, Bounds.Top + p.Y * Scale.Y);

    protected override void OnRender(DrawingContext dc)
    {
        if (Bounds.IsEmpty || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        dc.PushClip(new RectangleGeometry(Bounds));
        var strokes = new List<Geometry>();
        var guides = new List<Geometry>();
        var labels = new List<(string Text, Rect Measurement, Brush Color, bool Box)>();
        void Protect(Geometry geometry) => strokes.Add(geometry.GetWidenedPathGeometry(new Pen(Brushes.Black, 8)));
        if (measurements.ShowGuides)
        {
            for (var index = 0; index < measurements.Guides.Count; index++)
            {
                var guide = measurements.Guides[index];
                var guidePen = new Pen(HoveredGuide == index ? Brushes.White : Brushes.MediumTurquoise, HoveredGuide == index ? 2 : 1);
                var p = ToLocal(guide.Vertical ? new Point(guide.Position, 0) : new Point(0, guide.Position));
                dc.DrawLine(guidePen, p, guide.Vertical ? new Point(p.X, Bounds.Bottom) : new Point(Bounds.Right, p.Y));
                guides.Add(new LineGeometry(p, guide.Vertical ? new Point(p.X, Bounds.Bottom) : new Point(Bounds.Right, p.Y))
                    .GetWidenedPathGeometry(new Pen(Brushes.Black, 4)));
                if (HoveredGuide == index)
                    labels.Add((FormattableString.Invariant($"{(guide.Vertical ? "X" : "Y")}: {guide.Position:0} px"),
                        new Rect(p, new Size(0, 0)), Brushes.MediumTurquoise, false));
            }
        }
        foreach (var (tool, points) in measurements.Drawings())
        {
            if (points.Length < 2) continue;
            var color = tool switch { RulerTool.Rectangle => Brushes.DodgerBlue, RulerTool.Distance => Brushes.Orange,
                RulerTool.Angle => Brushes.Violet, _ => Brushes.LimeGreen };
            var pen = new Pen(color, 1.5);
            var a = ToLocal(points[0]); var b = ToLocal(points[1]);
            string label;
            if (tool == RulerTool.Rectangle)
            {
                var rect = new Rect(a, b);
                dc.DrawRectangle(null, pen, rect);
                Protect(new RectangleGeometry(rect));
                strokes.Add(new RectangleGeometry(new Rect(rect.Left, rect.Top, rect.Width, Math.Min(24, rect.Height))));
                strokes.Add(new RectangleGeometry(new Rect(rect.Left, rect.Top, Math.Min(40, rect.Width), rect.Height)));
                DrawBoxScales(dc, rect, new Rect(points[0], points[1]), color);
                label = FormattableString.Invariant($"{Math.Abs(points[1].X - points[0].X):0} × {Math.Abs(points[1].Y - points[0].Y):0} px");
            }
            else
            {
                dc.DrawLine(new Pen(Brushes.Black, 3), a, b); dc.DrawLine(pen, a, b);
                Protect(new LineGeometry(a, b));
                label = FormattableString.Invariant($"{RulerMeasurements.Distance(points[0], points[1]):0.##} px");
                if (tool == RulerTool.Distance)
                {
                    label = FormattableString.Invariant($"Δx: {points[1].X - points[0].X:0}  Δy: {points[1].Y - points[0].Y:0}\nD: {RulerMeasurements.Distance(points[0], points[1]):0.##} px\nA: ({points[0].X:0}, {points[0].Y:0})  B: ({points[1].X:0}, {points[1].Y:0})");
                    var projection = new Point(b.X, a.Y);
                    var dashed = new Pen(color, 1) { DashStyle = DashStyles.Dash };
                    dc.DrawLine(dashed, a, projection); dc.DrawLine(dashed, projection, b);
                    Protect(new LineGeometry(a, projection)); Protect(new LineGeometry(projection, b));
                }
                if (tool == RulerTool.Angle && points.Length == 3)
                {
                    var c = ToLocal(points[2]); dc.DrawLine(new Pen(Brushes.Black, 3), a, c); dc.DrawLine(pen, a, c);
                    Protect(new LineGeometry(a, c));
                    var first = b - a; var second = c - a;
                    if (first.Length > 0 && second.Length > 0)
                    {
                        var radius = Math.Min(28, Math.Min(first.Length, second.Length));
                        first.Normalize(); second.Normalize();
                        var start = a + first * radius; var end = a + second * radius;
                        var arc = new StreamGeometry();
                        using (var geometry = arc.Open())
                        {
                            geometry.BeginFigure(start, false, false);
                            geometry.ArcTo(end, new Size(radius, radius), 0, false,
                                Vector.CrossProduct(first, second) >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
                        }
                        dc.DrawGeometry(null, pen, arc);
                        Protect(arc);
                    }
                    label = FormattableString.Invariant($"{RulerMeasurements.Angle(points[0], points[1], points[2]):0.##}°");
                }
            }
            foreach (var point in points)
            {
                dc.DrawEllipse(color, new Pen(Brushes.White, 1), ToLocal(point), 3, 3);
                strokes.Add(new EllipseGeometry(ToLocal(point), 7, 7));
            }
            var localPoints = points.Select(ToLocal).ToArray();
            var extent = new Rect(new Point(localPoints.Min(p => p.X), localPoints.Min(p => p.Y)),
                new Point(localPoints.Max(p => p.X), localPoints.Max(p => p.Y)));
            labels.Add((label, extent, color, tool == RulerTool.Rectangle));
        }
        var placed = new List<Rect>();
        var formatted = labels.Select(label => (Label: label, Text: new FormattedText(label.Text, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface("Consolas"), 12, label.Box ? Brushes.White : label.Color,
            VisualTreeHelper.GetDpi(this).PixelsPerDip))).OrderByDescending(item => item.Text.Width * item.Text.Height);
        foreach (var (label, text) in formatted)
        {
            var position = RulerLabelLayout.Place(Bounds, new Size(text.Width + 10, text.Height + 6), label.Measurement, strokes, placed, ExcludedBounds, guides);
            DrawLabel(dc, text, position, label.Box); placed.Add(position);
        }
        LabelBounds = placed;
        dc.Pop();
    }

    private void DrawBoxScales(DrawingContext dc, Rect rect, Rect pixels, Brush color)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        dc.PushClip(new RectangleGeometry(rect));
        var pen = new Pen(color, .75);
        var major = Math.Min(Scale.X, Scale.Y) >= .6 ? 50 : 100;
        var step = Math.Min(Scale.X, Scale.Y) >= .75 ? 1 : 5;
        void Axis(double count, double scale, bool vertical)
        {
            for (var pixel = 0; pixel <= count; pixel += step)
            {
                var length = pixel % major == 0 ? 12 : pixel % 10 == 0 ? 8 : pixel % 5 == 0 ? 5 : 2;
                var p = vertical ? new Point(rect.Left, rect.Top + pixel * scale) : new Point(rect.Left + pixel * scale, rect.Top);
                dc.DrawLine(pen, p, p + (vertical ? new Vector(length, 0) : new Vector(0, length)));
                if (pixel % major != 0) continue;
                var text = new FormattedText(pixel.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Consolas"), 10, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                var position = p + (vertical ? new Vector(13, 4) : new Vector(4, 2));
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(230, 20, 24, 30)), null,
                    new Rect(position, new Size(text.Width + 2, text.Height)));
                dc.DrawText(text, position);
            }
        }
        Axis(pixels.Width, Scale.X, false); Axis(pixels.Height, Scale.Y, true);
        dc.Pop();
        dc.DrawEllipse(color, new Pen(Brushes.White, 1), rect.TopLeft, 3, 3);
    }

    private static void DrawLabel(DrawingContext dc, FormattedText text, Rect position, bool box)
    {
        dc.DrawRoundedRectangle(box ? new SolidColorBrush(Color.FromRgb(46, 96, 201)) : new SolidColorBrush(Color.FromArgb(240, 27, 36, 50)), null,
            position, 3, 3);
        dc.DrawText(text, position.TopLeft + new Vector(5, 3));
    }
}
