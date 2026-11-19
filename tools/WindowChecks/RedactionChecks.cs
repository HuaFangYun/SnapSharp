using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapSharp.Models;
using SnapSharp.Services;
using SnapSharp.ViewModels;
using SnapSharp.Views;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;

namespace SnapSharp.WindowChecks;

internal static partial class Program
{
    private static void RedactionChecks()
    {
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
        RecognizedBlock B(string text, double x, double y, double width = 260) => new(text, new Rect(x, y, width, 20));
        foreach (var value in new[] { "13800138000", "+86 138 0013 8000", "138-0013-8000", "11010519491231002X", "6225 1688 8888 8888", "user@example.com", "姓名：张三", "住址：示例市幸福路1号", "Name: Alice", "Address: Main Street" })
            Require(SensitiveTextDetection.FindRegions(new([B(value, 10, 10)], "fixture"), 600, 500).Count > 0,
                "Sensitive text was not found: " + value);
        var ordinary = new RecognitionDocument([B("会议时间：2026年11月20日", 10, 10), B("总金额：1234.56", 10, 50), B("今天讨论项目进度", 10, 90), B("版本1.1.1", 10, 130)], "fixture");
        Require(SensitiveTextDetection.FindRegions(ordinary, 600, 500).Count == 0, "Ordinary text should remain unchanged.");
        var multiline = new RecognitionDocument([
            B("姓名", 10, 10, 40), B("张三", 160, 10, 50), B("性别：男", 10, 42),
            B("住址：示例市", 10, 90), B("幸福镇", 45, 118), B("幸福路1号", 45, 146), B("有效期限：长期", 10, 174),
            B("138", 10, 230, 30), B("0013", 45, 230, 40), B("8000", 90, 230, 40)
        ], "fixture");
        var regions = SensitiveTextDetection.FindRegions(multiline, 600, 500);
        foreach (var index in new[] { 0, 1, 3, 4, 5, 7, 8, 9 })
            Require(regions.Any(r => r.Contains(multiline.Blocks[index].Bounds)), "Separate/multiline sensitive value was missed.");
        foreach (var index in new[] { 2, 6 })
            Require(!regions.Any(r => r.Contains(multiline.Blocks[index].Bounds)), "Sensitive field consumed the next ordinary field.");
        var clipped = SensitiveTextDetection.FindRegions(new([B("13800138000", -10, -5)], "fixture"), 100, 50);
        Require(clipped.Count == 1 && new Rect(0, 0, 100, 50).Contains(clipped[0]), "Redaction bounds must stay inside the image.");

        // A varied pixel fixture makes actual mosaic pixels distinguishable from the input.
        var pixels = new byte[400 * 240 * 4];
        for (var y = 0; y < 240; y++)
        for (var x = 0; x < 400; x++)
        {
            var i = (y * 400 + x) * 4;
            pixels[i] = (byte)((x * 31 + y * 13) % 256);
            pixels[i + 1] = (byte)((x * 7 + y * 29) % 256);
            pixels[i + 2] = (byte)((x * 17 + y * 3) % 256);
            pixels[i + 3] = 255;
        }
        var source = BitmapSource.Create(400, 240, 96, 96, PixelFormats.Bgra32, null, pixels, 400 * 4); source.Freeze();
        Require(SelectionWindow.RunPrimaryToolbarSelfTest(source), "Screenshot toolbar layout and existing tools regressed.");
        byte[] At(BitmapSource image, int x, int y)
        {
            var result = new byte[4]; image.CopyPixels(new Int32Rect(x, y, 1, 1), result, 4, 0); return result;
        }
        var document = new RecognitionDocument([B("13800138000", 30, 50, 200)], "fixture");
        var settings = new SettingsService(Path.Combine(Root, "redaction-settings.json"));
        var pin = new PinWindow(source, new SkiaImageService(), settings, new ClipboardService());
        var overlay = new SelectionWindow(new CapturedScreen(new System.Drawing.Rectangle(0, 0, 400, 240), source), [], new PrecisionCapture());
        try
        {
            Require(pin.FindName("RedactEditButton") is Button && overlay.FindName("RedactCaptureButton") is Button,
                "Both screenshot and pin edit toolbars must offer redaction.");
            Invoke(pin, "BeginEditing");
            var editor = (EditorViewModel)typeof(PinWindow).GetField("_editViewModel", PrivateInstance)!.GetValue(pin)!;
            var annotation = new ShapeAnnotation(AnnotationTool.Rectangle, new Point(280, 150), new Point(310, 180), Colors.Red, 3);
            editor.Add(annotation);
            var action = new SensitiveRedactionAction((image, preferences, token) => Task.FromResult(document));
            var button = (Button)pin.FindName("RedactEditButton");
            WaitRedaction(action.Run(pin, button, editor, settings.Current, () => true));
            Require(button.IsEnabled && editor.Annotations.Count == 0 && editor.CanUndo && !ReferenceEquals(editor.Image, source),
                "Redaction should flatten into one undoable image edit and restore the action button.");
            Require(!At(editor.Image, 31, 51).SequenceEqual(At(source, 31, 51)) && At(editor.Image, 5, 5).SequenceEqual(At(source, 5, 5)),
                "Mosaic must alter sensitive pixels while preserving surrounding pixels.");
            Require(ReferenceEquals(((System.Windows.Controls.Image)pin.FindName("ImageView")).Source, editor.Image), "Pin preview must display the redacted image.");
            Require(At((BitmapSource)Invoke(pin, "OutputImage")!, 31, 51).SequenceEqual(At(editor.Image, 31, 51)), "Pin copy/save output must include redaction.");
            editor.UndoCommand.Execute(null);
            Require(ReferenceEquals(editor.Image, source) && editor.Annotations.SequenceEqual([annotation]), "One undo must restore the image and existing annotations.");
            editor.RedoCommand.Execute(null);
            Require(editor.Annotations.Count == 0 && !ReferenceEquals(editor.Image, source), "Redo must restore redaction.");
            Invoke(pin, "CancelEdit_Click", pin, new RoutedEventArgs());
            Require(ReferenceEquals(((System.Windows.Controls.Image)pin.FindName("ImageView")).Source, source), "Cancel pin editing must restore the original image.");

            // Cancellation and a discarded editing session must never apply late OCR results.
            foreach (var cancel in new[] { false, true })
            {
                var gate = new TaskCompletionSource<RecognitionDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
                var pending = new SensitiveRedactionAction((image, preferences, token) => gate.Task);
                var untouched = new EditorViewModel(source);
                var task = pending.Run(pin, button, untouched, settings.Current, () => false);
                if (cancel) pending.Cancel();
                gate.SetResult(document); WaitRedaction(task);
                Require(ReferenceEquals(untouched.Image, source) && !untouched.CanUndo && button.IsEnabled,
                    "Cancelled or discarded OCR results must leave the editor untouched.");
            }
            Console.WriteLine("PASS: sensitive text rules, split/multiline fields, clipping, mosaic pixels, both toolbar entries, pin output, undo/redo, cancel and stale session cleanup.");
        }
        finally { overlay.Close(); pin.Close(); }
    }

    private static void WaitRedaction(Task task)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }
}
