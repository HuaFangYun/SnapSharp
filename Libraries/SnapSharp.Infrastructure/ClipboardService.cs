using Clowd.Clipboard;
using System.Windows.Media.Imaging;

namespace SnapSharp.Services;

public interface IClipboardService
{
    BitmapSource? GetImage();
    void SetImage(BitmapSource image);
}

public sealed class ClipboardService : IClipboardService
{
    private const int MaxAttempts = 5;

    public BitmapSource? GetImage()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // Use the same format-aware library as image export (PNG / DIB / DIBV5),
                // rather than WPF's legacy bitmap-only clipboard conversion.
                var image = ClipboardWpf.GetImage();
                if (image is null) return null;
                // Own a pixel snapshot: clipboard ownership can change after this call.
                var snapshot = new WriteableBitmap(image);
                snapshot.Freeze();
                return snapshot;
            }
            catch (ClipboardBusyException) when (attempt < MaxAttempts)
            {
                Thread.Sleep(attempt * 25);
            }
            catch (System.Runtime.InteropServices.ExternalException) when (attempt < MaxAttempts)
            {
                Thread.Sleep(attempt * 25);
            }
        }
    }

    public void SetImage(BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                ClipboardWpf.SetImage(image);
                return;
            }
            catch (ClipboardBusyException) when (attempt < MaxAttempts)
            {
                Thread.Sleep(attempt * 25);
            }
        }
    }
}
