using L = SnapSharp.Localization.UiText;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapSharp.Services;

public static class OutputActions
{
    public static void CopyFile(string path) => WithClipboardRetry(() =>
        System.Windows.Clipboard.SetFileDropList(new StringCollection { Path.GetFullPath(path) }));

    public static void CopyPath(string path) => CopyText(Path.GetFullPath(path));
    public static void CopyText(string text) => WithClipboardRetry(() => System.Windows.Clipboard.SetText(text));

    private static void WithClipboardRetry(Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { action(); return; }
            catch (System.Runtime.InteropServices.COMException) when (attempt < 5) { Thread.Sleep(attempt * 25); }
        }
    }

    public static void Reveal(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(L.Get("TheFileHasBeenMovedOrDeleted"), path);
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        start.ArgumentList.Add("/select,");
        start.ArgumentList.Add(Path.GetFullPath(path));
        Process.Start(start)?.Dispose();
    }

    public static Rect FitPrintRect(double width, double height, Rect printable)
    {
        var scale = Math.Min(printable.Width / width, printable.Height / height);
        var size = new System.Windows.Size(width * scale, height * scale);
        return new Rect(printable.X + (printable.Width - size.Width) / 2,
            printable.Y + (printable.Height - size.Height) / 2, size.Width, size.Height);
    }

    public static void Print(BitmapSource image, Window? owner = null)
    {
        var dialog = new System.Windows.Controls.PrintDialog();
        if (WindowDialogService.ShowDialog(dialog, owner) != true) return;
        // Query after dialog so printer, paper and orientation choices are reflected.
        var area = dialog.PrintQueue.GetPrintCapabilities(dialog.PrintTicket).PageImageableArea;
        var printable = area is null ? new Rect(0, 0, dialog.PrintableAreaWidth, dialog.PrintableAreaHeight)
            : new Rect(area.OriginWidth, area.OriginHeight, area.ExtentWidth, area.ExtentHeight);
        if (printable.Width <= 0 || printable.Height <= 0) throw new InvalidOperationException(L.Get("ThePrinterDidNotProvideAValid"));
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) context.DrawImage(image, FitPrintRect(image.PixelWidth, image.PixelHeight, printable));
        dialog.PrintVisual(visual, L.Get("SnapSharpScreenshot"));
    }
}
