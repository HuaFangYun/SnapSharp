using System.Drawing;
using System.Windows;

namespace SnapSharp.Models;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Left => X;
    public int Top => Y;
    public int Right => checked(X + Width);
    public int Bottom => checked(Y + Height);
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public Int32Rect ToInt32Rect() => new(X, Y, Width, Height);
    public Rectangle ToDrawingRectangle() => new(X, Y, Width, Height);
}
