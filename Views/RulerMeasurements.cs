using System.Windows;
using System.Windows.Media.Imaging;
using Size = System.Windows.Size;

namespace NimCap.Screenshot.Views;

internal enum RulerTool { None, Line, Rectangle, Distance, Angle }
internal sealed record RulerGuide(bool Vertical, double Position);

/// <summary>Measurements are relative to the inset origin, in physical pixels.</summary>
internal sealed class RulerMeasurements
{
    internal RulerTool ActiveTool { get; private set; } = RulerTool.Rectangle;
    internal HashSet<RulerTool> VisibleTools { get; } = [RulerTool.Line, RulerTool.Rectangle, RulerTool.Distance];
    internal Dictionary<RulerTool, Point[]> Results { get; } = [];
    internal List<RulerGuide> Guides { get; } = [];
    internal bool ShowGuides { get; set; }
    internal Size Extent { get; private set; }
    internal List<Point> Pending { get; } = [];
    internal Point Preview { get; private set; }
    internal bool HasPending => Pending.Count > 0;
    internal event Action<RulerTool, Point[]>? Committed;

    internal Rect? BoxBounds
    {
        get
        {
            if (!VisibleTools.Contains(RulerTool.Rectangle)) return null;
            if (HasPending && ActiveTool == RulerTool.Rectangle) return new Rect(Pending[0], Preview);
            return Results.TryGetValue(RulerTool.Rectangle, out var points) ? new Rect(points[0], points[1]) : null;
        }
    }

    internal Point ReadoutPoint(Point point)
    {
        point = Clamp(point);
        if (ActiveTool != RulerTool.Rectangle || BoxBounds is not { } box) return point;
        return new Point(Math.Clamp(point.X - box.Left, 0, box.Width), Math.Clamp(point.Y - box.Top, 0, box.Height));
    }

    internal void SelectTool(RulerTool tool)
    {
        Cancel();
        if (tool == ActiveTool && VisibleTools.Remove(tool)) ActiveTool = RulerTool.None;
        else { VisibleTools.Add(tool); ActiveTool = tool; }
    }

    internal void Resize(Size extent)
    {
        Extent = extent;
        foreach (var tool in Results.Keys.ToArray()) Results[tool] = Results[tool].Select(Clamp).ToArray();
        for (var i = 0; i < Guides.Count; i++)
            Guides[i] = Guides[i] with { Position = Math.Clamp(Guides[i].Position, 0, Guides[i].Vertical ? extent.Width : extent.Height) };
        for (var i = 0; i < Pending.Count; i++) Pending[i] = Clamp(Pending[i]);
        Preview = Clamp(Preview);
    }

    internal Point Clamp(Point p) => new(Math.Clamp(Math.Round(p.X), 0, Extent.Width), Math.Clamp(Math.Round(p.Y), 0, Extent.Height));

    internal Point Update(Point point, bool constrain = false)
    {
        point = Clamp(point);
        if (constrain && Pending.Count > 0 && ActiveTool is RulerTool.Line or RulerTool.Distance)
        {
            var start = Pending[0];
            if (Math.Abs(point.X - start.X) >= Math.Abs(point.Y - start.Y)) point.Y = start.Y;
            else point.X = start.X;
        }
        Preview = point;
        return point;
    }

    internal void Press(Point point)
    {
        if (ActiveTool == RulerTool.None) return;
        point = Update(point);
        if (Pending.Count == 0) Pending.Add(point);
        else if (ActiveTool == RulerTool.Angle)
        {
            if (point == Pending[0]) return;
            if (Pending.Count == 1) Pending.Add(point);
            else Commit([Pending[0], Pending[1], point]);
        }
        else if (point != Pending[0]) Commit([Pending[0], point]);
    }

    internal void Release(Point point, bool constrain = false)
    {
        point = Update(point, constrain);
        if (HasPending && ActiveTool != RulerTool.Angle && point != Pending[0]) Commit([Pending[0], point]);
    }

    private void Commit(Point[] points)
    {
        Results[ActiveTool] = points; Pending.Clear();
        Committed?.Invoke(ActiveTool, points);
    }
    internal void Cancel() => Pending.Clear();
    internal void Clear() { Cancel(); Results.Clear(); Guides.Clear(); }
    internal void DeleteActive() { Cancel(); Results.Remove(ActiveTool); }

    internal IEnumerable<(RulerTool Tool, Point[] Points)> Drawings()
    {
        foreach (var (tool, points) in Results)
            if (VisibleTools.Contains(tool) && (!HasPending || tool != ActiveTool)) yield return (tool, points);
        if (HasPending)
            yield return (ActiveTool, Pending.Count == 1 ? [Pending[0], Preview] : [Pending[0], Pending[1], Preview]);
    }

    internal Point SnapToGuides(Point point, double tolerance = 7)
    {
        var xs = new List<double> { 0, Extent.Width };
        var ys = new List<double> { 0, Extent.Height };
        if (ShowGuides)
            foreach (var guide in Guides) (guide.Vertical ? xs : ys).Add(guide.Position);
        var x = xs.MinBy(x => Math.Abs(point.X - x));
        var y = ys.MinBy(y => Math.Abs(point.Y - y));
        return Clamp(new Point(Math.Abs(point.X - x) <= tolerance ? x : point.X, Math.Abs(point.Y - y) <= tolerance ? y : point.Y));
    }

    internal static double Distance(Point a, Point b) => (b - a).Length;
    internal static double Angle(Point vertex, Point first, Point second)
    {
        var a = first - vertex; var b = second - vertex;
        if (a.Length == 0 || b.Length == 0) return 0;
        return Math.Acos(Math.Clamp(Vector.Multiply(a, b) / (a.Length * b.Length), -1, 1)) * 180 / Math.PI;
    }
}

internal static class RulerEdgeSnap
{
    // Find actual image boundaries near the sample center, without requiring OCR or window detection.
    internal static Point Find(BitmapSource bitmap, Point center, int radius = 7)
    {
        var image = bitmap.Format == System.Windows.Media.PixelFormats.Bgra32 ? bitmap :
            new FormatConvertedBitmap(bitmap, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var stride = image.PixelWidth * 4;
        var pixels = new byte[stride * image.PixelHeight]; image.CopyPixels(pixels, stride, 0);
        double Difference(int ax, int ay, int bx, int by)
        {
            var a = ay * stride + ax * 4; var b = by * stride + bx * 4;
            return Math.Abs(pixels[a] - pixels[b]) + Math.Abs(pixels[a + 1] - pixels[b + 1]) + Math.Abs(pixels[a + 2] - pixels[b + 2]);
        }
        var cx = (int)center.X; var cy = (int)center.Y;
        var bestX = center.X; var bestY = center.Y; double scoreX = 55, scoreY = 55;
        for (var offset = -radius; offset <= radius; offset++)
        {
            var x = cx + offset; var y = cy + offset;
            if (x > 0 && x < image.PixelWidth && cy > 0 && cy < image.PixelHeight - 1)
            {
                var score = Enumerable.Range(cy - 1, 3).Average(row => Difference(x - 1, row, x, row)) / (1 + Math.Abs(offset) * .2);
                if (score > scoreX) { scoreX = score; bestX = x; }
            }
            if (y > 0 && y < image.PixelHeight && cx > 0 && cx < image.PixelWidth - 1)
            {
                var score = Enumerable.Range(cx - 1, 3).Average(col => Difference(col, y - 1, col, y)) / (1 + Math.Abs(offset) * .2);
                if (score > scoreY) { scoreY = score; bestY = y; }
            }
        }
        return new Point(bestX, bestY);
    }
}
