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
        public GrayImage(BitmapSource source)
        {
            Width = source.PixelWidth; Height = source.PixelHeight;
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var data = new byte[checked(Width * Height * 4)]; converted.CopyPixels(data, Width * 4, 0);
            Pixels = new byte[Width * Height];
            for (var i = 0; i < Pixels.Length; i++) Pixels[i] = (byte)((data[i * 4] * 29 + data[i * 4 + 1] * 150 + data[i * 4 + 2] * 77) >> 8);
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
        if (!Enum.IsDefined(direction)) throw new ArgumentException("匹配方向无效。");
        var a = new GrayImage(first); var b = new GrayImage(second);
        if (a.Width != b.Width || a.Height != b.Height) throw new ArgumentException("捕获帧尺寸不一致。");
        var choices = direction == ScrollDirection.Auto ? new[] { ScrollDirection.Vertical, ScrollDirection.Horizontal } : [direction];
        return choices.Select(axis => MatchAxis(a, b, axis)).OrderByDescending(x => x.Reliable).ThenBy(x => x.Error).First();
    }

    private static ScrollMatch MatchAxis(GrayImage a, GrayImage b, ScrollDirection axis)
    {
        var length = axis == ScrollDirection.Vertical ? a.Height : a.Width;
        var breadth = axis == ScrollDirection.Vertical ? a.Width : a.Height;
        var minimumOverlap = Math.Max(24, length / 6);
        var maximumShift = length - minimumOverlap;
        var step = 1; // Full pixel search: coarse-only search misses sharp text/noise matches between sampled shifts.
        var scores = new Dictionary<int, (double Error, double Texture)>();
        (double Error, double Texture) Score(int shift)
        {
            if (scores.TryGetValue(shift, out var cached)) return cached;
            // Avoid fixed bars and scrollbars near the frame edges. Reject low-texture
            // or ambiguous matches instead of silently joining unrelated/repeated content.
            var margin = Math.Max(2, length / 12);
            var start = margin; var end = length - shift - margin;
            double totalError = 0, sum = 0, squares = 0; var count = 0;
            for (var along = start; along < end; along += Math.Max(1, (end - start) / 28))
            for (var across = breadth / 12; across < breadth * 11 / 12; across += Math.Max(1, breadth / 28))
            {
                var av = axis == ScrollDirection.Vertical ? a.Pixels[(along + shift) * a.Width + across] : a.Pixels[across * a.Width + along + shift];
                var bv = axis == ScrollDirection.Vertical ? b.Pixels[along * b.Width + across] : b.Pixels[across * b.Width + along];
                totalError += Math.Abs(av - bv); sum += av; squares += av * av; count++;
            }
            if (count == 0) return (255, 0);
            // Do not discard the largest errors: on sparse text pages those are
            // precisely the text pixels, and trimming them makes wrong shifts look identical.
            var error = totalError / count;
            var texture = Math.Sqrt(Math.Max(0, squares / count - Math.Pow(sum / count, 2)));
            return scores[shift] = (error, texture);
        }
        for (var shift = 1; shift <= maximumShift; shift += step) Score(shift);
        var best = scores.MinBy(x => x.Value.Error);
        for (var shift = Math.Max(1, best.Key - step); shift <= Math.Min(maximumShift, best.Key + step); shift++) Score(shift);
        best = scores.MinBy(x => x.Value.Error);
        var alternative = scores.Where(x => Math.Abs(x.Key - best.Key) > Math.Max(4, step * 2)).Select(x => x.Value.Error).DefaultIfEmpty(255).Min();
        var reliable = best.Value.Error < 14 && best.Value.Texture > 5 && alternative - best.Value.Error > Math.Max(1, best.Value.Error * .15);
        return new ScrollMatch(axis, best.Key, best.Value.Error, reliable, best.Value.Texture);
    }

    public ScrollStitchResult Stitch(ScrollCaptureSession session, ScrollTrim? trim = null)
    {
        trim ??= new();
        if (session.Frames.Count is < 1 or > 80 || session.Offsets.Count != session.Frames.Count - 1 || session.Direction is not (ScrollDirection.Vertical or ScrollDirection.Horizontal))
            throw new ArgumentException("拼接工程的帧数、偏移或方向无效。");
        var first = session.Frames[0];
        if (trim.Left < 0 || trim.Top < 0 || trim.Right < 0 || trim.Bottom < 0 ||
            (long)trim.Left + trim.Right > first.PixelWidth - 16 || (long)trim.Top + trim.Bottom > first.PixelHeight - 16)
            throw new ArgumentException("每帧裁边后至少需保留 16 × 16 像素。");
        var width = first.PixelWidth - trim.Left - trim.Right; var height = first.PixelHeight - trim.Top - trim.Bottom;
        if (trim.Left < 0 || trim.Top < 0 || trim.Right < 0 || trim.Bottom < 0 || width < 16 || height < 16) throw new ArgumentException("每帧裁边后至少需保留 16 × 16 像素。");
        var vertical = session.Direction == ScrollDirection.Vertical;
        var length = vertical ? height : width;
        if (session.Offsets.Any(x => x < 1 || x > length)) throw new ArgumentException($"帧间位移需为 1–{length} 像素；过大的裁边可能让相邻帧失去重叠。");
        var total = (long)length + session.Offsets.Sum(x => (long)x);
        var resultWidth = vertical ? width : total; var resultHeight = vertical ? total : height;
        if (resultWidth > MaximumEdge || resultHeight > MaximumEdge || resultWidth * resultHeight > MaximumPixels) throw new ArgumentException("拼接结果超过 16384 像素边长或 6400 万像素上限，请分段采集。");
        using var surface = SKSurface.Create(new SKImageInfo((int)resultWidth, (int)resultHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        var cursor = length; var start = 0; var seams = new List<int>(); var starts = new List<int> { 0 };
        for (var index = 0; index < session.Frames.Count; index++)
        {
            var frame = session.Frames[index];
            if (frame.PixelWidth != first.PixelWidth || frame.PixelHeight != first.PixelHeight) throw new ArgumentException("原始帧尺寸不一致。");
            using var bitmap = _images.ToSkBitmap(frame);
            if (index == 0)
            {
                surface.Canvas.DrawBitmap(bitmap, new SKRect(trim.Left, trim.Top, trim.Left + width, trim.Top + height), new SKRect(0, 0, width, height));
                continue;
            }
            var offset = session.Offsets[index - 1];
            seams.Add(cursor); start += offset; starts.Add(start);
            var source = vertical ? SKRect.Create(trim.Left, trim.Top + height - offset, width, offset)
                : SKRect.Create(trim.Left + width - offset, trim.Top, offset, height);
            var destination = vertical ? SKRect.Create(0, cursor, width, offset) : SKRect.Create(cursor, 0, offset, height);
            surface.Canvas.DrawBitmap(bitmap, source, destination); cursor += offset;
        }
        surface.Canvas.Flush(); using var image = surface.Snapshot();
        return new ScrollStitchResult(_images.ToBitmapSource(image), seams, starts);
    }
}
