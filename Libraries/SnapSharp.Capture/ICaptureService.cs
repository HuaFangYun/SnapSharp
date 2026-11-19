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

    CapturedScreen CaptureVirtualScreen(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return CaptureVirtualScreen();
    }

    CapturedScreen? CaptureWindow(nint windowHandle, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return CaptureWindow(windowHandle);
    }
}
