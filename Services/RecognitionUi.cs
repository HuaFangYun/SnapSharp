using System.Windows;
using System.Windows.Media.Imaging;
namespace SnapSharp.Services;
internal static class RecognitionUi
{
    public static Action<BitmapSource, Window?, string>? OpenRequested { get; set; }
    public static void Open(BitmapSource image, Window? owner, string mode = "OCR") => OpenRequested?.Invoke(image, owner, mode);
}
