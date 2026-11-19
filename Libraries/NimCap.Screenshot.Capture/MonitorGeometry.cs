using System.Drawing;

namespace NimCap.Screenshot.Services;

/// <summary>DIPs are monitor-local. Only physical desktop pixels can be shared across monitors.</summary>
internal sealed record MonitorGeometry(Rectangle Bounds, double DpiX, double DpiY)
{
    internal double ScaleX => DpiX / 96;
    internal double ScaleY => DpiY / 96;
    internal System.Windows.Point ToDesktop(System.Windows.Point localDip) =>
        new(Bounds.Left + localDip.X * ScaleX, Bounds.Top + localDip.Y * ScaleY);
    internal System.Windows.Point FromDesktop(System.Windows.Point desktopPixel) =>
        new((desktopPixel.X - Bounds.Left) / ScaleX, (desktopPixel.Y - Bounds.Top) / ScaleY);
    internal System.Windows.Point ToMonitor(System.Windows.Point localDip, MonitorGeometry target) =>
        target.FromDesktop(ToDesktop(localDip));
}
