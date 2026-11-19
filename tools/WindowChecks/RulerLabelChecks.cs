using System.Windows;
using System.Windows.Media;
using NimCap.Screenshot.Views;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Pen = System.Windows.Media.Pen;
using Brushes = System.Windows.Media.Brushes;

namespace NimCap.Screenshot.WindowChecks;

internal static partial class Program
{
    private static void RulerLabelChecks()
    {
        var bounds = new Rect(24, 24, 496, 280);
        Geometry Stroke(Geometry path) => path.GetWidenedPathGeometry(new Pen(Brushes.Black, 8));
        void Check(Point[] points, Size size, bool distance = false, bool angle = false)
        {
            var strokes = new List<Geometry> { Stroke(new LineGeometry(points[0], points[1])) };
            if (distance)
            {
                var projection = new Point(points[1].X, points[0].Y);
                strokes.Add(Stroke(new LineGeometry(points[0], projection)));
                strokes.Add(Stroke(new LineGeometry(projection, points[1])));
            }
            if (angle)
            {
                strokes.Add(Stroke(new LineGeometry(points[0], points[2])));
                // Excluding a full disk is stricter than excluding just the angle arc.
                strokes.Add(new EllipseGeometry(points[0], 32, 32));
            }
            strokes.AddRange(points.Select(p => (Geometry)new EllipseGeometry(p, 7, 7)));
            var measurement = new Rect(new Point(points.Min(p => p.X), points.Min(p => p.Y)),
                new Point(points.Max(p => p.X), points.Max(p => p.Y)));
            var label = RulerLabelLayout.Place(bounds, size, measurement, strokes, []);
            Require(bounds.Contains(label) && strokes.All(g => g.FillContainsWithDetail(new RectangleGeometry(label)) == IntersectionDetail.Empty),
                "Ruler readout covers a measurement line, projection, endpoint or angle arc.");
        }
        foreach (var (a, b) in new[] {
            (new Point(100, 100), new Point(400, 100)),
            (new Point(300, 270), new Point(300, 70)),
            (new Point(50, 50), new Point(480, 270)),
            (new Point(480, 50), new Point(50, 270)),
            (new Point(24, 24), new Point(520, 304)),
            (new Point(24, 304), new Point(520, 24)),
            (new Point(30, 28), new Point(510, 28)),
            (new Point(30, 300), new Point(510, 300)) })
        {
            Check([a, b], new Size(100, 22));
            Check([a, b], new Size(250, 52), distance: true);
        }
        Check([new Point(240, 140), new Point(400, 140), new Point(290, 250)], new Size(70, 22), angle: true);
        Check([new Point(30, 28), new Point(400, 28), new Point(30, 280)], new Size(70, 22), angle: true);
        Check([new Point(510, 295), new Point(200, 295), new Point(510, 40)], new Size(70, 22), angle: true);
        var prior = new List<Rect>();
        var excluded = new Rect(370, 120, 133, 165);
        var diagonal = Stroke(new LineGeometry(new Point(40, 40), new Point(500, 280)));
        for (var i = 0; i < 4; i++)
        {
            var label = RulerLabelLayout.Place(bounds, new Size(130, 30), new Rect(40, 40, 460, 240), [diagonal], prior, excluded);
            Require(!label.IntersectsWith(excluded) && prior.All(p => !p.IntersectsWith(label)) &&
                diagonal.FillContainsWithDetail(new RectangleGeometry(label)) == IntersectionDetail.Empty,
                "Multiple readouts overlap each other, a diagonal or the magnifier.");
            prior.Add(label);
        }
        Console.WriteLine("PASS: ruler readouts avoid lines, two-point projections, endpoints, angle arcs, other labels and magnifier; edge/canvas-spanning measurements stay readable.");
    }
}
