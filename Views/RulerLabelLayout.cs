using System.Windows;
using System.Windows.Media;
using Size = System.Windows.Size;

namespace NimCap.Screenshot.Views;

internal static class RulerLabelLayout
{
    internal static Rect Place(Rect bounds, Size size, Rect measurement, IReadOnlyList<Geometry> strokes,
        IReadOnlyList<Rect> labels, Rect? excluded = null, IReadOnlyList<Geometry>? guides = null)
    {
        const double gap = 10;
        var cx = measurement.Left + measurement.Width / 2;
        var cy = measurement.Top + measurement.Height / 2;
        var candidates = new List<Point> {
            new(cx - size.Width / 2, measurement.Top - size.Height - gap),
            new(cx - size.Width / 2, measurement.Bottom + gap),
            new(measurement.Right + gap, cy - size.Height / 2),
            new(measurement.Left - size.Width - gap, cy - size.Height / 2) };
        foreach (var x in new[] { measurement.Left, measurement.Right - size.Width })
        {
            candidates.Add(new Point(x, measurement.Top - size.Height - gap));
            candidates.Add(new Point(x, measurement.Bottom + gap));
        }
        foreach (var y in new[] { measurement.Top, measurement.Bottom - size.Height })
        {
            candidates.Add(new Point(measurement.Left - size.Width - gap, y));
            candidates.Add(new Point(measurement.Right + gap, y));
        }
        Rect Fit(Point p) => new(new Point(Math.Clamp(p.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - size.Width)),
            Math.Clamp(p.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - size.Height))), size);
        int Collisions(Rect r)
        {
            var padded = r; padded.Inflate(2, 2);
            var geometry = new RectangleGeometry(padded);
            return strokes.Count(s => s.FillContainsWithDetail(geometry) != IntersectionDetail.Empty) * 1000
                + labels.Count(l => l.IntersectsWith(padded)) * 1000
                + (excluded is { } e && e.IntersectsWith(padded) ? 1000000 : 0)
                + (guides?.Count(g => g.FillContainsWithDetail(geometry) != IntersectionDetail.Empty) ?? 0);
        }
        var best = Fit(candidates[0]); var bestScore = int.MaxValue;
        foreach (var point in candidates)
        {
            var candidate = Fit(point); var score = Collisions(candidate);
            if (score == 0) return candidate;
            if (score < bestScore) { best = candidate; bestScore = score; }
        }
        // Keep an otherwise clear label outside its measurement. A reference guide
        // behind the label is preferable to moving a box readout into its interior.
        if (bestScore < 1000) return best;
        // A diagonal or a measurement spanning the canvas can leave no room outside
        // its bounds. Search nearby free space rather than clamping a label onto a line.
        var free = new List<Point>();
        for (var y = bounds.Top; y <= bounds.Bottom - size.Height; y += 16)
            for (var x = bounds.Left; x <= bounds.Right - size.Width; x += 16) free.Add(new Point(x, y));
        foreach (var point in free.OrderBy(p => (p - new Point(cx - size.Width / 2, cy - size.Height / 2)).LengthSquared))
        {
            var candidate = new Rect(point, size); var score = Collisions(candidate);
            if (score == 0) return candidate;
            if (score < bestScore) { best = candidate; bestScore = score; }
        }
        return best;
    }
}
