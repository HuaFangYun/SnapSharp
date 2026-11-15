using SnapSharp.Models;
using System.Drawing;
using System.Windows.Media.Imaging;

namespace SnapSharp.Services;

// Architecture reference: Aquashot's replaceable capture-service boundary.
// Independently implemented for SnapSharp.
public interface ICaptureService
{
    IReadOnlyList<CapturedScreen> CaptureAllScreens();
    BitmapSource CaptureRegion(Rectangle bounds);
    CapturedScreen CaptureVirtualScreen();
    CapturedScreen? CaptureWindow(nint windowHandle);
    IReadOnlyList<WindowCandidate> EnumerateTopLevelWindows();
    WindowCandidate? FindWindowAt(System.Drawing.Point screenPoint);
}
