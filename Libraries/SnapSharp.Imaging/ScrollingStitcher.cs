using L = SnapSharp.Localization.UiText;
using SnapSharp.Models;
using SkiaSharp;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapSharp.Services;

public sealed class ScrollingStitcher
{
    public const int MaximumEdge = 16384;
    public const long MaximumPixels = 64_000_000;
    public const long MaximumFramePixels = 48_000_000;
    private readonly SkiaImageService _images = new();

    private sealed class GrayImage
    {
        public int Width { get; }
        public int Height { get; }
        public byte[] Pixels { get; }
        public byte Background { get; }
        public GrayImage(BitmapSource source)
        {
            Width = source.PixelWidth; Height = source.PixelHeight;
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var data = new byte[checked(Width * Height * 4)]; converted.CopyPixels(data, Width * 4, 0);
            Pixels = new byte[Width * Height];
            for (var i = 0; i < Pixels.Length; i++) Pixels[i] = (byte)((data[i * 4] * 29 + data[i * 4 + 1] * 150 + data[i * 4 + 2] * 77) >> 8);
            var histogram = new int[256];
            foreach (var pixel in Pixels) histogram[pixel]++;
            Background = (byte)Enumerable.Range(0, 256).MaxBy(i => histogram[i]);
        }
    }

    public static double Difference(BitmapSource first, BitmapSource second)
    {
        var a = new GrayImage(first); var b = new GrayImage(second);
        if (a.Width != b.Width || a.Height != b.Height) return double.MaxValue;
        double total = 0; var count = 0;
        for (var y = 0; y < a.Height; y += Math.Max(1, a.Height / 60))
        for (var x = 0; x < a.Width; x += Math.Max(1, a.Width / 60))
        { total += Math.Abs(a.Pixels[y * a.Width + x] - b.Pixels[y * b.Width + x]); count++; }
        return total / Math.Max(1, count);
    }

    public ScrollMatch Match(BitmapSource first, BitmapSource second, ScrollDirection direction = ScrollDirection.Auto)
    {
        if (!Enum.IsDefined(direction)) throw new ArgumentException(L.Get("InvalidMatchingDirection"));
        var a = new GrayImage(first); var b = new GrayImage(second);
        if (a.Width != b.Width || a.Height != b.Height) throw new ArgumentException(L.Get("CapturedFrameDimensionsAreInconsistent"));
        var choices = direction == ScrollDirection.Auto ? new[] { ScrollDirection.Vertical, ScrollDirection.Horizontal } : [direction];
        return choices.Select(axis => MatchAxis(a, b, axis)).OrderByDescending(x => x.Reliable).ThenBy(x => x.Error).First();
    }

    private static ScrollMatch MatchAxis(GrayImage a, GrayImage b, ScrollDirection axis)
    {
        var length = axis == ScrollDirection.Vertical ? a.Height : a.Width;
        var breadth = axis == ScrollDirection.Vertical ? a.Width : a.Height;
        var rowMatch = MatchContinuousRows(a, b, axis);
        if (rowMatch is not null) return rowMatch;
        var minimumOverlap = Math.Max(24, length / 6);
        var maximumShift = length - minimumOverlap;
        var step = 1; // Full pixel search: coarse-only search misses sharp text/noise matches between sampled shifts.
        var scores = new Dictionary<int, (double Error, double Texture)>();
        (double Error, double Texture) Score(int shift, bool verify = false)
        {
            if (!verify && scores.TryGetValue(shift, out var cached)) return cached;
            // Avoid fixed bars and scrollbars near the frame edges. Reject low-texture
            // or ambiguous matches instead of silently joining unrelated/repeated content.
            var overlap = length - Math.Abs(shift);
            var margin = Math.Max(2, Math.Min(length / 12, overlap / 8));
            var start = margin; var end = overlap - margin;
            double totalError = 0, sum = 0, squares = 0; var count = 0; var foregroundCount = 0;
            for (var along = start; along < end; along += verify ? 1 : Math.Max(1, (end - start) / 28))
            for (var across = breadth / 12; across < breadth * 11 / 12; across += verify ? 3 : Math.Max(1, breadth / 28))
            {
                var aAlong = along + Math.Max(0, shift);
                var bAlong = along + Math.Max(0, -shift);
                var av = axis == ScrollDirection.Vertical ? a.Pixels[aAlong * a.Width + across] : a.Pixels[across * a.Width + aAlong];
                var bv = axis == ScrollDirection.Vertical ? b.Pixels[bAlong * b.Width + across] : b.Pixels[across * b.Width + bAlong];
                // Text occupies a small fraction of a viewport. Averaging its
                // errors with the background makes distinct paragraphs ambiguous.
                if (Math.Abs(av - a.Background) > 12 || Math.Abs(bv - b.Background) > 12)
                { totalError += Math.Abs(av - bv); foregroundCount++; }
                sum += av; squares += av * av; count++;
            }
            if (count == 0) return (255, 0);
            // Do not discard the largest errors: on sparse text pages those are
            // precisely the text pixels, and trimming them makes wrong shifts look identical.
            var error = foregroundCount == 0 ? 255 : totalError / foregroundCount;
            var texture = Math.Sqrt(Math.Max(0, squares / count - Math.Pow(sum / count, 2)));
            var result = (error, texture);
            if (!verify) scores[shift] = result;
            return result;
        }
        for (var shift = 1; shift <= maximumShift; shift += step) { Score(shift); Score(-shift); }
        // Sparse probes can align only with the background between text lines.
        // Verify the leading candidates across every overlap row before accepting
        // a document coordinate. Otherwise a false offset silently removes paragraphs.
        var verified = scores.OrderBy(x => x.Value.Error).Take(16)
            .ToDictionary(x => x.Key, x => Score(x.Key, verify: true));
        var best = verified.MinBy(x => x.Value.Error);
        var alternative = verified.Where(x => Math.Abs(x.Key - best.Key) > 4).Select(x => x.Value.Error).DefaultIfEmpty(255).Min();
        var reliable = best.Value.Error < 4 && best.Value.Texture > 5 && alternative - best.Value.Error > Math.Max(1, best.Value.Error * .15);
        return new ScrollMatch(axis, best.Key, best.Value.Error, reliable, best.Value.Texture);
    }

    // Independently implemented row fingerprints find candidate translations in
    // linear time per displacement. Full rows, rather than sparse image probes,
    // establish the overlap. Blank rows alone are never matching evidence.
    private static ScrollMatch? MatchContinuousRows(GrayImage a, GrayImage b, ScrollDirection axis)
    {
        var vertical = axis == ScrollDirection.Vertical;
        var length = vertical ? a.Height : a.Width;
        var breadth = vertical ? a.Width : a.Height;
        var margin = Math.Min(breadth / 3, Math.Max(2, breadth / 20));
        (ulong[] Hashes, bool[] Textured) Fingerprints(GrayImage image)
        {
            var hashes = new ulong[length]; var textured = new bool[length];
            for (var row = 0; row < length; row++)
            {
                ulong hash = 14695981039346656037UL; var low = 255; var high = 0;
                for (var across = margin; across < breadth - margin; across++)
                {
                    var value = image.Pixels[vertical ? row * image.Width + across : across * image.Width + row];
                    hash = unchecked((hash ^ value) * 1099511628211UL);
                    low = Math.Min(low, value); high = Math.Max(high, value);
                }
                hashes[row] = hash; textured[row] = high - low > 12;
            }
            return (hashes, textured);
        }
        var first = Fingerprints(a); var second = Fingerprints(b);
        var minimumRun = Math.Max(8, Math.Min(24, length / 8));
        var candidates = new List<(int Shift, int Run, int Evidence)>();
        var maximumShift = length - minimumRun;
        for (var shift = -maximumShift; shift <= maximumShift; shift++)
        {
            if (shift == 0) continue;
            var run = 0; var runEvidence = 0; var bestRun = 0; var evidence = 0;
            for (var row = 0; row < length - Math.Abs(shift); row++)
            {
                var ai = row + Math.Max(shift, 0); var bi = row + Math.Max(-shift, 0);
                if (first.Hashes[ai] == second.Hashes[bi])
                {
                    run++; if (first.Textured[ai] && second.Textured[bi]) runEvidence++;
                    if (runEvidence >= 3) { bestRun = Math.Max(bestRun, run); evidence = Math.Max(evidence, runEvidence); }
                }
                else { run = 0; runEvidence = 0; }
            }
            if (bestRun >= minimumRun) candidates.Add((shift, bestRun, evidence));
        }
        if (candidates.Count == 0) return null;
        var ordered = candidates.OrderByDescending(x => x.Evidence).ThenByDescending(x => x.Run).ToArray();
        var best = ordered[0];
        var ambiguous = ordered.Skip(1).Any(x => x.Evidence == best.Evidence && x.Run >= best.Run);
        // Confirm fingerprints against pixels, so a hash collision cannot certify a seam.
        var verifiedRun = 0; var verifiedEvidence = 0; var verified = false;
        for (var row = 0; row < length - Math.Abs(best.Shift); row++)
        {
            var ai = row + Math.Max(best.Shift, 0); var bi = row + Math.Max(-best.Shift, 0);
            var equal = true;
            for (var across = margin; across < breadth - margin && equal; across++)
                equal = a.Pixels[vertical ? ai * a.Width + across : across * a.Width + ai] ==
                    b.Pixels[vertical ? bi * b.Width + across : across * b.Width + bi];
            if (equal) { verifiedRun++; if (first.Textured[ai]) verifiedEvidence++; }
            else { verifiedRun = 0; verifiedEvidence = 0; }
            if (verifiedRun >= minimumRun && verifiedEvidence >= 3) verified = true;
        }
        return new ScrollMatch(axis, best.Shift, 0, verified && !ambiguous, best.Evidence);
    }

    public ScrollStitchResult Stitch(ScrollCaptureSession session, ScrollTrim? trim = null)
    {
        trim ??= new();
        if (session.Frames.Count is < 1 or > 80 || session.Offsets.Count != session.Frames.Count - 1 || session.Direction is not (ScrollDirection.Vertical or ScrollDirection.Horizontal))
            throw new ArgumentException(L.Get("InvalidStitchingProjectFrameCountOffsetsOr"));
        var first = session.Frames[0];
        if (trim.Left < 0 || trim.Top < 0 || trim.Right < 0 || trim.Bottom < 0 ||
            (long)trim.Left + trim.Right > first.PixelWidth - 16 || (long)trim.Top + trim.Bottom > first.PixelHeight - 16)
            throw new ArgumentException(L.Get("AtLeast1616PixelsPerFrame"));
        var width = first.PixelWidth - trim.Left - trim.Right; var height = first.PixelHeight - trim.Top - trim.Bottom;
        if (trim.Left < 0 || trim.Top < 0 || trim.Right < 0 || trim.Bottom < 0 || width < 16 || height < 16) throw new ArgumentException(L.Get("AtLeast1616PixelsPerFrame"));
        var vertical = session.Direction == ScrollDirection.Vertical;
        var length = vertical ? height : width;
        if (session.Offsets.Where((x, i) => x < -length || x > length ||
            (x == 0 && (i >= session.Matches.Count || session.Matches[i].Reliable || session.Matches[i].ManuallyAdjusted))).Any())
            throw new ArgumentException(L.Format("FrameOffsetMustBe10Pixels", length));
        var positions = new List<int> { 0 };
        foreach (var offset in session.Offsets) positions.Add(checked(positions[^1] + offset));
        var minimum = positions.Min();
        var total = (long)positions.Max() - minimum + length;
        var resultWidth = vertical ? width : total; var resultHeight = vertical ? total : height;
        if (resultWidth > MaximumEdge || resultHeight > MaximumEdge || resultWidth * resultHeight > MaximumPixels) throw new ArgumentException(L.Get("StitchedOutputExceeds16384PixelsPerEdge"));
        using var surface = SKSurface.Create(new SKImageInfo((int)resultWidth, (int)resultHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        var seams = new List<int>(); var starts = positions.Select(x => x - minimum).ToList();
        var coveredStart = starts[0]; var coveredEnd = coveredStart + length;
        for (var index = 0; index < session.Frames.Count; index++)
        {
            var frame = session.Frames[index];
            if (frame.PixelWidth != first.PixelWidth || frame.PixelHeight != first.PixelHeight) throw new ArgumentException(L.Get("OriginalFrameDimensionsAreInconsistent"));
            using var bitmap = _images.ToSkBitmap(frame);
            if (index == 0)
            {
                surface.Canvas.DrawBitmap(bitmap, new SKRect(trim.Left, trim.Top, trim.Left + width, trim.Top + height),
                    vertical ? SKRect.Create(0, starts[0], width, height) : SKRect.Create(starts[0], 0, width, height));
                continue;
            }
            var start = starts[index]; var end = start + length;
            // Returning over captured content adds no pixels. Only extend the union
            // at either end; never append a revisited viewport as new document content.
            var from = start < coveredStart ? start : coveredEnd;
            var to = start < coveredStart ? coveredStart : Math.Max(coveredEnd, end);
            seams.Add(start < coveredStart ? coveredStart : Math.Min(coveredEnd, end));
            if (to > from)
            {
                var source = vertical ? SKRect.Create(trim.Left, trim.Top + from - start, width, to - from)
                    : SKRect.Create(trim.Left + from - start, trim.Top, to - from, height);
                var destination = vertical ? SKRect.Create(0, from, width, to - from) : SKRect.Create(from, 0, to - from, height);
                surface.Canvas.DrawBitmap(bitmap, source, destination);
            }
            coveredStart = Math.Min(coveredStart, start); coveredEnd = Math.Max(coveredEnd, end);
        }
        surface.Canvas.Flush(); using var image = surface.Snapshot();
        return new ScrollStitchResult(_images.ToBitmapSource(image), seams, starts);
    }
}
