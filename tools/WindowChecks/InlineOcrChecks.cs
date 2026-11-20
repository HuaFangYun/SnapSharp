using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NimCap.Screenshot.Localization;
using NimCap.Screenshot.Models;
using NimCap.Screenshot.Services;
using NimCap.Screenshot.ViewModels;
using NimCap.Screenshot.Views;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Brushes = System.Windows.Media.Brushes;
using FlowDirection = System.Windows.FlowDirection;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;

namespace NimCap.Screenshot.WindowChecks;

internal static partial class Program
{
    private static void InlineOcrChecks()
    {
        Exception? failure = null;
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () => await Run()));
        Dispatcher.PushFrame(frame);
        if (failure is not null) throw new InvalidOperationException("Inline OCR checks failed.", failure);

        async Task Run()
        {
            var originalCulture = UiText.Culture;
            try
            {
                var document = new RecognitionDocument([
                    new("Alpha beta", new Rect(10, 20, 190, 28))
                    {
                        TextSpans = [new(0, 5, new Rect(10, 20, 70, 28)), new(6, 4, new Rect(125, 20, 75, 28))]
                    },
                    new("中文🙂e\u0301", new Rect(10, 85, 140, 28))
                    {
                        TextSpans = [new(0, 1, new Rect(10, 85, 28, 28)), new(1, 1, new Rect(38, 85, 28, 28)),
                            new(2, 2, new Rect(66, 85, 40, 28)), new(4, 2, new Rect(106, 85, 44, 28))]
                    }
                ], "fixture");
                var model = new OcrTextSelection(document, new Size(600, 300));
                var edgeDocument = new RecognitionDocument([new("Inside", new Rect(10, 10, 40, 20)),
                    new("Rounded edge text", new Rect(601, 35, 40, 20)), new("Missing bounds text", Rect.Empty)], "edge fixture");
                var edgeSelection = new OcrTextSelection(edgeDocument, new Size(600, 300));
                Require(edgeDocument.Blocks.All(b => edgeSelection.Text.Contains(b.Text)), "Copy-all dropped text because selection bounds were invalid.");
                var spaced = "真 正 要 注 意 的只剩两件事 PaddleOCR LaMa Qwen GGUF THIRD-PARTY-NOTICES.md 社 区 版 永 远 免 费";
                var spacedDocument = new RecognitionDocument([new(spaced, new Rect(0, 0, 1000, 20))
                {
                    TextSpans = [new(2, 1, new Rect(20, 0, 10, 20)), new(spaced.IndexOf("PaddleOCR", StringComparison.Ordinal), 9, new Rect(300, 0, 90, 20))]
                }], "spacing fixture");
                var cleaned = OcrTextCleanup.RemoveCjkSpacing(spacedDocument);
                Require(cleaned.Text == "真正要注意的只剩两件事PaddleOCR LaMa Qwen GGUF THIRD-PARTY-NOTICES.md社区版永远免费",
                    "OCR spacing cleanup damaged Chinese text or English word separators.");
                Require(cleaned.Blocks[0].TextSpans!.All(s => cleaned.Text.Substring(s.Start, s.Length) is "正" or "PaddleOCR"),
                    "Spacing cleanup broke native selection offsets.");
                Require(string.Concat(cleaned.Text.Where(c => !char.IsWhiteSpace(c))) == string.Concat(spaced.Where(c => !char.IsWhiteSpace(c))),
                    "Spacing cleanup dropped non-whitespace characters.");
                var chineseLanguage = ScreenWordService.WindowsLanguage("zh-Hans");
                if (chineseLanguage?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var smallVisual = new DrawingVisual();
                    using (var dc = smallVisual.RenderOpen())
                    {
                        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1200, 1800));
                        void Line(string text, double y) => dc.DrawText(new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                            new Typeface("Microsoft YaHei"), 14, Brushes.Black, 1), new Point(20, y));
                        Line("真正要注意的只剩两件事", 20);
                        Line("你自己引入的第三方模型和库许可：PaddleOCR、LaMa、Qwen GGUF，这些不是你写的。", 65);
                        Line("免费用户的预期管理：README 里写清楚社区版永远免费，功能范围明确。", 105);
                        Line("分段边界检查：重要名称和数字 1234567890 不能丢失", 1130);
                        Line("末尾检查：最后一行文字必须完整保留", 1740);
                    }
                    var smallImage = new RenderTargetBitmap(1200, 1800, 96, 96, PixelFormats.Pbgra32);
                    smallImage.Render(smallVisual); smallImage.Freeze();
                    var actualSmall = await ScreenWordService.RecognizeImage(smallImage,
                        new AppSettings { OcrBackend = "Windows", WindowsOcrLanguage = chineseLanguage }, CancellationToken.None);
                    var baselinePixels = Pixels(smallImage);
                    using var baselineBitmap = Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromBuffer(
                        Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(baselinePixels),
                        Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, 1200, 1800, Windows.Graphics.Imaging.BitmapAlphaMode.Straight);
                    var baselineEngine = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(chineseLanguage));
                    var baseline = await baselineEngine!.RecognizeAsync(baselineBitmap);
                    File.WriteAllText(Path.Combine(Root, "small-font-ocr.txt"), "Original size:\n" + baseline.Text + "\nEnhanced:\n" + actualSmall.Text);
                    Require(actualSmall.Text.Contains("真正要注意的只剩两件事") && actualSmall.Text.Contains("PaddleOCR") &&
                        actualSmall.Text.Contains("README") && actualSmall.Text.Contains("社区版永远") &&
                        actualSmall.Text.Contains("重要名称和数字") && actualSmall.Text.Contains("最后一行文字必须完整保留"),
                        "Small-font Chinese/English screenshot OCR lost key text or the final strip.");
                    Require(actualSmall.Text.Split("重要名称和数字").Length == 2 && actualSmall.Blocks.All(b => b.Bounds.Bottom <= 1800 && b.Bounds.Left >= 0),
                        "Strip OCR duplicated boundary text or lost source coordinates.");
                    Console.WriteLine("PASS: real small-font mixed Chinese/English OCR; tall screenshot boundary/end text and native coordinates.");
                }
                Require(model.Begin(new Point(10, 30)), "Cannot begin selecting on the screenshot text.");
                model.Extend(new Point(200, 30));
                Require(model.SelectedText == "Alpha beta", "Word gaps lost their original whitespace.");
                Require(model.Begin(new Point(106, 95)), "Cannot anchor a Unicode selection.");
                model.Extend(new Point(66, 95));
                Require(model.SelectedText == "🙂", "Reverse selection split a surrogate pair.");
                model.Begin(new Point(106, 95)); model.Extend(new Point(150, 95));
                Require(model.SelectedText == "e\u0301", "Selection split a combining sequence.");
                model.Begin(new Point(10, 30)); model.Extend(new Point(150, 95));
                Require(model.SelectedText == document.Text, "Multiline selection changed reading order or line breaks.");
                Require(!model.Begin(new Point(450, 200)) && model.SelectedText.Length == 0, "Blank click selected unrelated text.");

                // The screenshot is a generated fixture. Never capture the real desktop
                // or write the user's clipboard for these checks.
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 960, 720));
                    foreach (var block in document.Blocks)
                        dc.DrawText(new FormattedText(block.Text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                            new Typeface("Segoe UI"), 28, Brushes.Black, 1), new Point(block.Bounds.Left + 100, block.Bounds.Top + 180));
                }
                var image = new RenderTargetBitmap(960, 720, 96, 96, PixelFormats.Pbgra32);
                image.Render(visual); image.Freeze();
                var windowsLanguage = ScreenWordService.WindowsLanguage("en-US");
                if (windowsLanguage is not null)
                {
                    var crop = new CroppedBitmap(image, new Int32Rect(100, 180, 600, 300)); crop.Freeze();
                    var actual = await ScreenWordService.RecognizeImage(crop,
                        new AppSettings { OcrBackend = "Windows", WindowsOcrLanguage = windowsLanguage }, CancellationToken.None);
                    Require(actual.Text.Contains("Alpha", StringComparison.OrdinalIgnoreCase), "Installed Windows OCR did not recognize the generated screenshot.");
                    Require(actual.Blocks.Any(b => b.TextSpans is { Count: > 0 }), "Windows OCR did not preserve native word coordinates.");
                    Console.WriteLine("PASS: real Windows OCR on generated screenshot; native word coordinates preserved.");
                }
                else Console.WriteLine("SKIP: Windows OCR language pack unavailable; synthetic OCR integration checks still run.");
                foreach (var scale in new[] { 1d, 1.25, 1.5, 1.75, 2d })
                {
                    LocalizationService.Apply(scale == 1 ? "zh-CN" : "en");
                    var window = new SelectionWindow(new CapturedScreen(new System.Drawing.Rectangle(0, 0, 960, 720), image), [], new PrecisionCapture());
                    try
                    {
                        window.Show(); window.UpdateLayout();
                        window.Width = 960 / scale; window.Height = 720 / scale; window.UpdateLayout();
                        typeof(SelectionWindow).GetField("_selection", PrivateInstance)!.SetValue(window, new Rect(
                            100 * window.ActualWidth / 960, 180 * window.ActualHeight / 720,
                            600 * window.ActualWidth / 960, 300 * window.ActualHeight / 720));
                        Invoke(window, "BeginInlineEditing");
                        var layer = (OcrTextLayer)window.FindName("TextSelectionLayer");
                        var annotation = (AnnotationSurface)window.FindName("AnnotationView");
                        var editor = (EditorViewModel)annotation.DataContext;
                        var initialImage = editor.Image;
                        var initialCount = editor.Annotations.Count;
                        var initialOutput = Pixels((BitmapSource)Invoke(window, "RenderCaptureImage")!);
                        var captures = 0; var cancellations = 0; var copies = new List<string>();
                        window.CaptureAccepted += (_, _) => captures++;
                        window.CaptureCanceled += (_, _) => cancellations++;
                        window.CopyRecognizedText = copies.Add;
                        window.RecognizeSelectionText = (_, _, _) => Task.FromResult(document);
                        await (Task)Invoke(window, "RecognizeTextInPlace")!;
                        window.UpdateLayout();
                        Require(copies.Count == 1 && copies[0] == document.Text && layer.Selection?.SelectedText == "",
                            "OCR must immediately copy all text without highlighting the whole screenshot.");
                        Require(layer.Visibility == Visibility.Visible && !annotation.IsHitTestVisible, "OCR did not replace annotation input in place.");
                        Require(((FrameworkElement)window.FindName("ToolPanel")).Visibility == Visibility.Collapsed &&
                            ((FrameworkElement)window.FindName("Handles")).Visibility == Visibility.Collapsed, "Editing chrome remained interactive during text selection.");
                        var mapped = layer.RenderTransform.Transform(new Point(66, 85));
                        Require((mapped - new Point(166 * window.ActualWidth / 960, 265 * window.ActualHeight / 720)).Length < .01 &&
                            mapped == annotation.RenderTransform.Transform(new Point(66, 85)), "Text coordinates drift at scale " + scale);

                        // Run the drag/release path used by mouse handlers with source
                        // coordinates; remote/headless sessions cannot move the OS cursor.
                        Require(layer.BeginSelection(new Point(10, 30)), "Drag could not start.");
                        layer.ExtendSelection(new Point(200, 30));
                        Require(copies.Count == 1, "Text was copied before mouse release.");
                        layer.EndSelection(new Point(200, 30));
                        Require(copies.Count == 2 && copies[1] == "Alpha beta", "Mouse release did not copy selected screenshot text at scale " + scale);

                        Require(window.ProcessTextRecognitionKey(Key.A, ModifierKeys.Control) && copies.Count == 2, "Ctrl+A must select without copying.");
                        Require(window.ProcessTextRecognitionKey(Key.C, ModifierKeys.Control), "Ctrl+C was not handled in text mode.");
                        Invoke(window, "CopyAllRecognizedText_Click", window, new RoutedEventArgs());
                        Require(copies.Count == 4 && copies.Skip(2).All(t => t == document.Text), "Select-all or copy-all lost text.");
                        Require(ReferenceEquals(editor.Image, initialImage) && editor.Annotations.Count == initialCount && captures == 0 && cancellations == 0,
                            "OCR changed the screenshot, annotations, or capture session.");
                        var cleanOutput = (BitmapSource)Invoke(window, "RenderCaptureImage")!;
                        Require(cleanOutput.PixelWidth == 600 && cleanOutput.PixelHeight == 300, "Text highlights changed screenshot output geometry.");
                        Require(Pixels(cleanOutput).SequenceEqual(initialOutput), "Selection highlights leaked into saved image pixels.");
                        if (scale is 1 or 1.5)
                        {
                            window.UpdateLayout();
                            var root = (FrameworkElement)window.Content;
                            var preview = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                            preview.Render(root);
                            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(preview));
                            using var output = File.Create(Path.Combine(Root, $"inline-ocr-{scale}.png")); encoder.Save(output);
                        }
                        var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Escape)
                            { RoutedEvent = Keyboard.KeyDownEvent };
                        Invoke(window, "HandleTextRecognitionKey", escape);
                        Require(escape.Handled && layer.Visibility == Visibility.Collapsed && annotation.IsHitTestVisible && captures == 0 && cancellations == 0,
                            "Esc closed the capture instead of restoring editing.");

                        // Late completions must not overwrite a newer session or reopen the layer.
                        var late = new TaskCompletionSource<RecognitionDocument>();
                        CancellationToken pendingToken = default;
                        window.RecognizeSelectionText = (_, _, token) => { pendingToken = token; return late.Task; };
                        var pending = (Task)Invoke(window, "RecognizeTextInPlace")!;
                        Invoke(window, "ExitTextRecognition");
                        Require(pendingToken.IsCancellationRequested, "Leaving OCR did not cancel pending recognition.");
                        window.RecognizeSelectionText = (_, _, _) => Task.FromResult(new RecognitionDocument([], "empty"));
                        await (Task)Invoke(window, "RecognizeTextInPlace")!;
                        late.SetResult(document); await pending;
                        Require(layer.Selection?.Text == "" && !((Button)window.FindName("CopyAllRecognizedTextButton")).IsEnabled,
                            "A stale OCR result replaced the current empty session.");
                        Invoke(window, "ExitTextRecognition");
                        window.RecognizeSelectionText = (_, _, _) => Task.FromException<RecognitionDocument>(new InvalidOperationException("fixture failure"));
                        await (Task)Invoke(window, "RecognizeTextInPlace")!;
                        Require(((TextBlock)window.FindName("TextRecognitionStatus")).Text.Contains("fixture failure"), "Recognition failure lost its actionable message.");
                        Invoke(window, "ExitTextRecognition");
                        Require(layer.Selection is null && ((FrameworkElement)window.FindName("ToolPanel")).Visibility == Visibility.Visible,
                            "Failure recovery did not restore editing.");

                        var summaryPanel = (FrameworkElement)window.FindName("SummaryResultPanel");
                        var summaryText = (TextBox)window.FindName("SummaryResultText");
                        var copySummary = (Button)window.FindName("CopySummaryButton");
                        var retrySummary = (Button)window.FindName("RetrySummaryButton");
                        var configureSummary = (Button)window.FindName("ConfigureSummaryButton");
                        var ocrCalls = 0;
                        var beforeSummaryCopies = copies.Count;
                        window.RecognizeSelectionText = (_, _, _) => { ocrCalls++; return Task.FromResult(document); };
                        var summaryPending = new TaskCompletionSource<string>();
                        CancellationToken summaryToken = default;
                        window.SummarizeSelectionText = (text, _, token) =>
                        {
                            Require(text == document.Text, "Summary did not receive all screenshot text.");
                            summaryToken = token;
                            return summaryPending.Task;
                        };
                        var summaryTask = (Task)Invoke(window, "SummarizeInPlace")!;
                        Require(summaryPanel.Visibility == Visibility.Visible && !copySummary.IsEnabled && !retrySummary.IsEnabled,
                            "Summary did not show progress and prevent duplicate work.");
                        await (Task)Invoke(window, "SummarizeInPlace")!;
                        Require(ocrCalls == 1 && copies.Count == beforeSummaryCopies, "Direct summary copied source text or repeated OCR.");
                        summaryPending.SetResult("First point\nSecond point"); await summaryTask;
                        window.UpdateLayout();
                        Require(summaryText.Text == "First point\nSecond point" && copySummary.IsEnabled && retrySummary.IsEnabled,
                            "Inline summary lost formatting or left its controls disabled.");
                        var panelOrigin = summaryPanel.TranslatePoint(new Point(), (UIElement)window.Content);
                        Require(panelOrigin.X >= 0 && panelOrigin.Y >= 0 && panelOrigin.X + summaryPanel.ActualWidth <= window.ActualWidth + 1 &&
                            panelOrigin.Y + summaryPanel.ActualHeight <= window.ActualHeight + 1, "Summary card escaped the viewport at scale " + scale);
                        Invoke(window, "CopySummary_Click", window, new RoutedEventArgs());
                        Require(copies.Last() == summaryText.Text && copies.Count == beforeSummaryCopies + 1, "Copy summary did not copy the complete result.");
                        Require(ReferenceEquals(editor.Image, initialImage) && Pixels((BitmapSource)Invoke(window, "RenderCaptureImage")!).SequenceEqual(initialOutput) &&
                            captures == 0 && cancellations == 0, "Summary changed the screenshot or ended capture.");
                        if (scale is 1 or 1.5)
                        {
                            var root = (FrameworkElement)window.Content;
                            var preview = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                            preview.Render(root);
                            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(preview));
                            using var output = File.Create(Path.Combine(Root, $"inline-summary-{scale}.png")); encoder.Save(output);
                        }
                        Invoke(window, "CloseSummary");
                        window.SummarizeSelectionText = (_, _, _) => Task.FromException<string>(new InvalidOperationException("summary fixture failure"));
                        await (Task)Invoke(window, "SummarizeInPlace")!;
                        Require(ocrCalls == 1 && configureSummary.Visibility == Visibility.Visible, "Summary retry did not reuse OCR or expose model setup.");
                        string? settingsCategory = null;
                        window.SettingsRequested += category => settingsCategory = category;
                        Invoke(window, "ConfigureSummary_Click", window, new RoutedEventArgs());
                        Require(settingsCategory == "Shield", "Summary model setup opened the wrong settings category.");
                        summaryPending = new TaskCompletionSource<string>();
                        window.SummarizeSelectionText = (_, _, token) => { summaryToken = token; return summaryPending.Task; };
                        summaryTask = (Task)Invoke(window, "SummarizeInPlace")!;
                        window.ProcessTextRecognitionKey(Key.Escape, ModifierKeys.None);
                        Require(summaryToken.IsCancellationRequested && summaryPanel.Visibility == Visibility.Collapsed && annotation.IsHitTestVisible,
                            "Esc did not cancel the summary and restore editing.");
                        summaryPending.SetResult("stale result"); await summaryTask;
                        Require(summaryText.Text == "" && summaryPanel.Visibility == Visibility.Collapsed, "A late summary reopened the card.");

                        // OCR failures can be retried from the summary card after configuration.
                        window.RecognizeSelectionText = (_, _, _) => Task.FromException<RecognitionDocument>(new InvalidOperationException("OCR fixture failure"));
                        await (Task)Invoke(window, "SummarizeInPlace")!;
                        Invoke(window, "ConfigureSummary_Click", window, new RoutedEventArgs());
                        Require(settingsCategory == "ScanText", "OCR failure did not offer OCR settings.");
                        window.RecognizeSelectionText = (_, _, _) => { ocrCalls++; return Task.FromResult(document); };
                        window.SummarizeSelectionText = (_, _, _) => Task.FromResult("Recovered summary");
                        await (Task)Invoke(window, "SummarizeInPlace")!;
                        Require(summaryText.Text == "Recovered summary", "Retry did not recover after OCR configuration.");
                        Invoke(window, "ExitTextRecognition");
                        await (Task)Invoke(window, "RecognizeTextInPlace")!;
                        var beforeReuse = ocrCalls;
                        await (Task)Invoke(window, "SummarizeInPlace")!;
                        Require(ocrCalls == beforeReuse, "O followed by J unnecessarily repeated OCR.");
                        Invoke(window, "ExitTextRecognition");

                        // Nonrectangular regions must retain the image's exact clip.
                        ((ComboBox)window.FindName("ShapePicker")).SelectedIndex = 1;
                        Invoke(window, "UpdateEditorSurface");
                        window.RecognizeSelectionText = (_, _, _) => Task.FromResult(document);
                        await (Task)Invoke(window, "RecognizeTextInPlace")!;
                        Require(layer.Clip is not null && ReferenceEquals(layer.Clip, annotation.Clip), "OCR lost the nonrectangular selection mask.");
                        Invoke(window, "ExitTextRecognition");

                    }
                    finally { window.Close(); }
                }
                Console.WriteLine("PASS: in-place OCR; drag/release-to-copy logic; Unicode/reverse/multiline selection; copy-all; unchanged capture; Esc/cancellation/stale results/failure recovery; five DPI scales.");
                Console.WriteLine("PASS: automatic copy-all; inline summary progress/copy/retry/settings; OCR reuse and recovery; unchanged capture; cancellation/stale summary; card bounds at five DPI scales (summary model uses fixtures).");
                Console.WriteLine("Previews: " + Root);
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                LocalizationService.Apply(originalCulture.Name);
                frame.Continue = false;
            }
        }

        static byte[] Pixels(BitmapSource bitmap)
        {
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var bytes = new byte[converted.PixelWidth * converted.PixelHeight * 4];
            converted.CopyPixels(bytes, converted.PixelWidth * 4, 0);
            return bytes;
        }
    }
}
