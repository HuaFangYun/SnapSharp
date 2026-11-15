using System.Drawing;
using System.Windows.Media.Imaging;

namespace SnapSharp.Models;

public sealed record CapturedScreen(Rectangle Bounds, BitmapSource Image);

public sealed record WindowCandidate(Rectangle Bounds, nint Handle = default);

public enum CaptureMode
{
    Region,
    FullScreen,
    ActiveWindow
}

public enum CaptureAction
{
    Copy,
    Save,
    Pin,
    Scrolling,
    OCR,
    Cutout,
    Recording
}

public sealed record CaptureResult(BitmapSource Image, Rectangle ScreenBounds, Rectangle PixelRegion,
    CaptureAction Action = CaptureAction.Copy);
