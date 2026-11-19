using System.Windows;

namespace NimCap.Screenshot.Models;

/// <summary>Original-pixel binary repair mask: 255 is a hole, 0 preserves the source.</summary>
public sealed class WatermarkMask
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }
    public WatermarkMask(int width, int height, byte[]? pixels = null)
    {
        if (width < 1 || height < 1 || (long)width * height > 16_000_000 ||
            pixels is not null && pixels.Length != width * height) throw new ArgumentException("Invalid mask dimensions.");
        Width = width; Height = height; Pixels = pixels?.ToArray() ?? new byte[width * height];
    }
    public Int32Rect Bounds()
    {
        int left = Width, top = Height, right = -1, bottom = -1;
        for (var y = 0; y < Height; y++) for (var x = 0; x < Width; x++)
            if (Pixels[y * Width + x] != 0) { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = y; }
        return right < left ? Int32Rect.Empty : new(left, top, right - left + 1, bottom - top + 1);
    }
    public void Rectangle(Point a, Point b, bool erase = false)
    {
        if (!Valid(a) || !Valid(b)) return;
        var left = (int)Math.Clamp(Math.Floor(Math.Min(a.X, b.X)), 0, Width);
        var top = (int)Math.Clamp(Math.Floor(Math.Min(a.Y, b.Y)), 0, Height);
        var right = (int)Math.Clamp(Math.Ceiling(Math.Max(a.X, b.X)), 0, Width);
        var bottom = (int)Math.Clamp(Math.Ceiling(Math.Max(a.Y, b.Y)), 0, Height);
        for (var y = top; y < bottom; y++) Array.Fill(Pixels, erase ? (byte)0 : (byte)255, y * Width + left, right - left);
    }
    public void Stroke(Point a, Point b, double radius, bool erase = false)
    {
        if (!Valid(a) || !Valid(b) || !double.IsFinite(radius)) return;
        radius = Math.Clamp(radius, .5, 256);
        // Bound work to the image even when a captured pointer moves far outside it.
        var left = (int)Math.Clamp(Math.Floor(Math.Min(a.X, b.X) - radius), 0, Width);
        var top = (int)Math.Clamp(Math.Floor(Math.Min(a.Y, b.Y) - radius), 0, Height);
        var right = (int)Math.Clamp(Math.Ceiling(Math.Max(a.X, b.X) + radius), 0, Width);
        var bottom = (int)Math.Clamp(Math.Ceiling(Math.Max(a.Y, b.Y) + radius), 0, Height);
        var dx = b.X - a.X; var dy = b.Y - a.Y; var length = dx * dx + dy * dy;
        for (var y = top; y < bottom; y++) for (var x = left; x < right; x++)
        {
            var t = length < .0001 ? 0 : Math.Clamp(((x + .5 - a.X) * dx + (y + .5 - a.Y) * dy) / length, 0, 1);
            var px = x + .5 - a.X - t * dx; var py = y + .5 - a.Y - t * dy;
            if (px * px + py * py <= radius * radius) Pixels[y * Width + x] = erase ? (byte)0 : (byte)255;
        }
    }
    private static bool Valid(Point p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && Math.Abs(p.X) < 1e7 && Math.Abs(p.Y) < 1e7;
}
