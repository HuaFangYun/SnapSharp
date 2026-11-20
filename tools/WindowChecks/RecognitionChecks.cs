using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NimCap.Screenshot.Localization;
using NimCap.Screenshot.Models;
using NimCap.Screenshot.Services;
using NimCap.Screenshot.Views;
using ListBox = System.Windows.Controls.ListBox;
using Brushes = System.Windows.Media.Brushes;
using FlowDirection = System.Windows.FlowDirection;
using DataGrid = System.Windows.Controls.DataGrid;
using TabControl = System.Windows.Controls.TabControl;
using Image = System.Windows.Controls.Image;

namespace NimCap.Screenshot.WindowChecks;

internal static partial class Program
{
    private static RecognitionDocument Lines(params string[] lines) => new(lines.Select((t, i) => new RecognizedBlock(t, new Rect(20, 20 + 32 * i, 420, 24), .97)).ToArray(), "synthetic OCR fixture");
    private static void RecognitionChecks(string? modelDirectory = null)
    {
        var id = Lines("姓名 张三", "性别 男 民族 汉", "出生 1949年12月31日", "住址 北京市朝阳区", "幸福路12号", "公民身份号码 11010519491231002X");
        var result = IdentityDocumentRecognition.Recognize(id);
        Require(result.Kind == IdentityDocumentKind.IdCardFront, "ID front classification failed.");
        Require(result.Fields.Single(f => f.Name == "姓名").Value == "张三" && result.Fields.Single(f => f.Name == "民族").Value == "汉", "Multiple labels in one OCR block were mixed.");
        Require(result.Fields.Single(f => f.Name == "地址").Value.Contains("幸福路12号"), "Multiline address was truncated.");
        Require(!result.Fields.Single(f => f.Name == "身份证号").NeedsReview, "Valid ID checksum flagged.");
        Require(!IdentityDocumentRecognition.IsValidIdNumber("110105194912310021"), "Invalid checksum accepted.");
        Require(IdentityDocumentRecognition.Recognize(Lines("中华人民共和国", "居民身份证", "签发机关 北京市公安局", "有效期限 2020.01.01-长期")).Kind == IdentityDocumentKind.IdCardBack, "ID reverse classification failed.");
        var back = Lines("中华人民共和国", "居民身份证", "签发机关 示例市公安局", "有效期限 2020.01.01-长期");
        foreach (var frontFirst in new[] { true, false })
        {
            var top = frontFirst ? id : back; var bottom = frontFirst ? back : id;
            var combined = new RecognitionDocument([.. top.Blocks, .. bottom.Blocks.Select(b => b with { Bounds = new Rect(b.Bounds.X, b.Bounds.Y + 500, b.Bounds.Width, b.Bounds.Height) })], "combined fixture");
            var both = IdentityDocumentRecognition.Recognize(combined);
            Require(both.Kind == IdentityDocumentKind.IdCardBoth && both.Fields.Count == 8 && both.Fields.All(f => f.Value.Length > 0), "Both-side ID must yield eight fields regardless of vertical order.");
            Require(both.Fields.Single(f => f.Name == "姓名").Value == "张三" && both.Fields.Single(f => f.Name == "签发机关").Value == "示例市公安局", "ID front and back fields were mixed.");
            Require(IdentityDocumentRecognition.Recognize(combined, IdentityDocumentKind.IdCardFront).Fields.Count == 6 && IdentityDocumentRecognition.Recognize(combined, IdentityDocumentKind.IdCardBack).Fields.Count == 2,
                "All three manual ID modes must remain distinct.");
        }
        var driving = IdentityDocumentRecognition.Recognize(Lines("中华人民共和国机动车驾驶证", "姓名 李四", "准驾车型 Cl", "有效期限 2020.01.01至2030.01.01"));
        Require(driving.Kind == IdentityDocumentKind.DrivingLicense && driving.Fields.Single(f => f.Name == "准驾车型") is { Value: "C1", OriginalValue: "Cl", NeedsReview: true }, "Driving class correction must preserve and flag its source.");
        Require(IdentityDocumentRecognition.Recognize(Lines("中华人民共和国机动车行驶证", "号牌号码 京A12345", "车辆识别代号 ABCDEFGH123456789")).Kind == IdentityDocumentKind.VehicleLicense, "Vehicle license confused with driving license.");
        Require(IdentityDocumentRecognition.Recognize(Lines("营业执照", "统一社会信用代码 91310000TEST123456", "名称 示例商店", "经营范围 日用品销售")).Kind == IdentityDocumentKind.BusinessLicense, "Business license classification failed.");
        BusinessLicenseChecks();
        PublicSampleExtractionChecks();
        Require(IdentityDocumentRecognition.Recognize(Lines("居民户口簿", "户别 家庭户", "户主姓名 王五", "户号 123456")).Kind == IdentityDocumentKind.HouseholdRegister, "Household page classification failed.");
        Require(IdentityDocumentRecognition.Recognize(Lines("PASSPORT", "Nationality CHN", "Date of birth 1990-01-02")).Kind == IdentityDocumentKind.Passport, "Passport classification failed.");
        var bank = IdentityDocumentRecognition.Recognize(Lines("示例银行 借记卡", "卡号 4111 1111 1111 1111"));
        Require(bank.Kind == IdentityDocumentKind.BankCard && bank.Fields.Single(f => f.Name == "卡号").NeedsReview == false, "Local Luhn validation failed.");
        Require(IdentityDocumentRecognition.Recognize(Lines("购买保险需要提供身份证复印件", "姓名 张三")).Kind == IdentityDocumentKind.Unknown, "Text mentioning a document should not be classified as that document.");
        Require(IdentityDocumentRecognition.Recognize(Lines("会议纪要", "姓名 张三", "民族文化活动")).Kind == IdentityDocumentKind.Unknown, "Generic text falsely classified.");
        Require(IdentityDocumentRecognition.Recognize(Lines("机动车驾驶证", "机动车行驶证")).Kind == IdentityDocumentKind.Unknown, "Ambiguous types should remain unknown.");
        var split = new RecognitionDocument([
            new("机动车驾驶证", new Rect(20, 0, 200, 20), .99),
            new("姓名", new Rect(20, 40, 40, 20), .99), new("赵六", new Rect(75, 40, 60, 20), .6),
            new("准驾车型", new Rect(20, 80, 90, 20), .99), new("C2", new Rect(120, 80, 40, 20), .99)
        ], "split fixture");
        var splitResult = IdentityDocumentRecognition.Recognize(split);
        Require(splitResult.Fields.Single(f => f.Name == "姓名") is { Value: "赵六", NeedsReview: true }, "Separated label/value or confidence flag failed.");
        Require(splitResult.Fields.Single(f => f.Name == "准驾车型").Value == "C2", "Split values crossed rows.");
        var incomplete = IdentityDocumentRecognition.Recognize(Lines("姓名 张三"), IdentityDocumentKind.DrivingLicense);
        Require(incomplete.Fields.Count(f => f.Value.Length > 0) == 1 && incomplete.Fields.Where(f => f.Value.Length == 0).All(f => f.NeedsReview), "Missing fields must remain blank and flagged.");
        var fragmented = new RecognitionDocument([
            new("姓", new Rect(30, 40, 22, 28), .98), new("名", new Rect(51, 40, 22, 28), .98), new("测试甲", new Rect(88, 40, 90, 28), .98),
            new("性", new Rect(30, 85, 22, 28), .98), new("别", new Rect(51, 85, 22, 28), .98), new("女", new Rect(88, 85, 25, 28), .98), new("民族汉", new Rect(145, 85, 90, 28), .98),
            new("出生", new Rect(30, 130, 50, 28), .98), new("1949 年 12 月 31 日", new Rect(86, 130, 220, 28), .98),
            new("住", new Rect(30, 175, 22, 28), .98), new("址", new Rect(51, 175, 22, 28), .98), new("北京市朝阳区", new Rect(88, 175, 180, 28), .98),
            new("幸福路12号", new Rect(88, 210, 150, 28), .98),
            new("公民身份号码", new Rect(30, 285, 130, 28), .98), new("110105 19491231 002X", new Rect(155, 285, 310, 28), .98)
        ], "fragmented card fixture");
        var fragmentedResult = IdentityDocumentRecognition.Recognize(fragmented);
        Require(fragmentedResult.Fields.Count == 6 && fragmentedResult.Fields.All(f => f.Value.Length > 0), "ID front must show all six fields from fragmented/overlapping boxes.");
        Require(fragmentedResult.Fields.Single(f => f.Name == "姓名").Value == "测试甲" && fragmentedResult.Fields.Single(f => f.Name == "性别").Value == "女", "Fragmented labels lost name or gender.");
        Require(fragmentedResult.Fields.Single(f => f.Name == "身份证号") is { Value: "11010519491231002X", NeedsReview: false }, "Spaced 18-digit number must retain every digit.");
        Require(fragmentedResult.Fields.Single(f => f.Name == "地址").Value.Contains("幸福路12号"), "Fragmented address lost continuation.");
        Require(fragmentedResult.Fields.Single(f => f.Name == "地址").Value == "北京市朝阳区幸福路12号", "Multiline address must join without inserted spaces.");
        var whitespace = IdentityDocumentRecognition.Recognize(Lines("姓名：测试甲", "性别：男", "民族：汉", "出生日期：1949 年 12 月 31 日",
            "地址：示例省示例市示例区朱仙 \r\n庄镇\t\u3000\u00a0\u200b\ufeff", "身份证号：11010519491231002X"));
        Require(whitespace.Fields.Count == 6 && whitespace.Fields.All(f => f.Value.Length > 0 && !f.Value.Any(char.IsWhiteSpace)), "Document values must remove all whitespace across all fields.");
        Require(whitespace.Fields.Single(f => f.Name == "地址").Value == "示例省示例市示例区朱仙庄镇", "Address retained an invisible or fullwidth space.");
        Require(whitespace.Fields.Single(f => f.Name == "地址").OriginalValue.Contains("\r\n"), "Whitespace cleanup must preserve raw evidence.");
        var nameAlternates = new RecognitionDocument([.. whitespace.Fields.Select((f, i) => new RecognizedBlock(f.Name + "：" + f.OriginalValue, new Rect(20, 20 + i * 32, 420, 24), .8)),
            new("姓名：RR", new Rect(20, 20, 420, 24), .99, "document-retry")], "name alternatives");
        Require(IdentityDocumentRecognition.Recognize(nameAlternates).Fields.Single(f => f.Name == "姓名") is { Value: "测试甲", NeedsReview: true }, "A Latin retry must not replace an available Chinese name.");
        var editedField = new IdentityDocumentField { Name = "地址", Value = "  示例\t地址\n" };
        editedField.Value += "\u3000补充\r\n内容";
        Require(editedField.Value == "示例地址补充内容", "Edited fields and copied/exported values must use the same whitespace cleanup.");
        var badId = IdentityDocumentRecognition.Recognize(Lines("民族 汉", "出生 1949年12月31晶", "公民身份号码 11010519491231002X"), IdentityDocumentKind.IdCardFront);
        Require(badId.Fields.Count == 6 && badId.Fields.Single(f => f.Name == "出生日期") is { Value: "1949年12月31晶", NeedsReview: true }, "Malformed date must be retained and flagged, not silently guessed.");
        var shortId = IdentityDocumentRecognition.Recognize(Lines("姓名 测试乙", "公民身份号码 11010519491231002"), IdentityDocumentKind.IdCardFront);
        Require(shortId.Fields.Single(f => f.Name == "身份证号") is { Value: "11010519491231002", NeedsReview: true }, "Short numbers must not be padded or fabricated.");
        var retryNumber = new RecognitionDocument([.. shortId.Fields.Where(f => f.Value.Length > 0).Select(f => new RecognizedBlock(f.Name + " " + f.Value, f.Bounds, .95)),
            new("公民身份号码 11010519491231002X", new Rect(20, 52, 420, 24), .9, "document-number-retry")], "retry fixture");
        Require(IdentityDocumentRecognition.Recognize(retryNumber, IdentityDocumentKind.IdCardFront).Fields.Single(f => f.Name == "身份证号") is { Value: "11010519491231002X", NeedsReview: true },
            "A complete number retry must supersede a truncated number while retaining a conflict warning.");
        Console.WriteLine("PASS: document classification, ambiguous/unknown inputs, split OCR, multiline fields, source-preserving correction and local checksums.");

        LocalizationService.Apply("zh-CN");
        Require(UiText.Get("DocumentDrivingLicense") == "机动车驾驶证", "Document resources have damaged Chinese text.");
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 540, 300));
            dc.DrawText(new FormattedText("中华人民共和国机动车驾驶证\n\n姓名  李四\n\n准驾车型  C1\n\n有效期限  2020.01.01 至 2030.01.01", CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, new Typeface("Microsoft YaHei"), 18, Brushes.Black, 1), new System.Windows.Point(30, 20));
        }
        var source = new RenderTargetBitmap(540, 300, 96, 96, PixelFormats.Pbgra32); source.Render(drawing); source.Freeze();
        if (modelDirectory is not null) DocumentModelChecks(modelDirectory);
        RecognitionWindow.RunLayoutSelfTest(source);
        var window = new RecognitionWindow(source, new SettingsService(Path.Combine(Root, "recognition-settings.json")), autoRun: false);
        try
        {
            var nav = (ListBox)window.FindName("ModeBox");
            Require(nav.Items.OfType<ListBoxItem>().Select(item => item.Tag?.ToString()).ToHashSet()
                .SetEquals(["OCR", "Summary", "Document", "Table", "Barcode", "Formula", "Cutout", "Watermark"]),
                "Recognition tools should be directly visible in navigation.");
            var transformTools = (System.Windows.Controls.Panel)window.FindName("ImageTransformTools");
            foreach (var action in new[] { "Left", "Right", "Half", "Half", "Vertical", "Vertical" })
            {
                var before = (BitmapSource)typeof(RecognitionWindow).GetField("_image", PrivateInstance)!.GetValue(window)!;
                var button = transformTools.Children.OfType<System.Windows.Controls.Button>().Single(b => b.Tag?.ToString() == action);
                button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                var after = (BitmapSource)typeof(RecognitionWindow).GetField("_image", PrivateInstance)!.GetValue(window)!;
                VerifyImageTransform(before, after, action);
                Require(ReferenceEquals(((Image)window.FindName("Picture")).Source, after) && after.IsFrozen, "Transformed preview must be the frozen OCR input.");
            }
            foreach (var width in new[] { 1240d, 980d })
            {
                var content = (FrameworkElement)window.Content;
                content.Measure(new System.Windows.Size(width, 720)); content.Arrange(new Rect(0, 0, width, 720)); content.UpdateLayout();
                RecognitionPreviewChecks(window);
                foreach (var mode in new[] { "Document", "OCR", "Summary", "Table", "Barcode", "Formula", "Cutout" })
                {
                    nav.SelectedItem = nav.Items.OfType<ListBoxItem>().Single(i => i.Tag?.ToString() == mode);
                    content.UpdateLayout();
                    window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                    Require(((FrameworkElement)window.FindName("PictureScroll")).ActualWidth > 250, "Preview became too narrow.");
                    if (mode == "Document")
                    {
                        typeof(RecognitionWindow).GetField("_document", PrivateInstance)!.SetValue(window, Lines("机动车驾驶证", "姓名 李四", "准驾车型 Cl"));
                        typeof(RecognitionWindow).GetMethod("ShowDocument", PrivateInstance)!.Invoke(window, [null]);
                        var editor = (System.Windows.Controls.TextBox)window.FindName("DocumentEditor");
                        content.UpdateLayout();
                        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                        Require(editor.ActualWidth > 250 && editor.Text.Contains("姓名：李四") && editor.Text.Contains("准驾车型：C1"), "Document text layout failed.");
                        Require(editor.TextWrapping == TextWrapping.Wrap && ((TextBlock)window.FindName("Status")).Text.Contains("准驾车型"), "Document text must wrap visually and retain review notices outside field values.");
                        editor.CaretIndex = editor.Text.IndexOf("姓名：", StringComparison.Ordinal) + 1;
                        Require(((Grid)window.FindName("PictureCanvas")).Children.OfType<System.Windows.Shapes.Rectangle>().Any(), "Field selection failed to locate source.");
                        editor.AppendText(Environment.NewLine + "手动修正");
                        Require((bool)typeof(RecognitionWindow).GetProperty("HasEdits", PrivateInstance)!.GetValue(window)!, "Edited document fields were not tracked.");
                        typeof(RecognitionWindow).GetField("_identityBaseline", PrivateInstance)!.SetValue(window, typeof(RecognitionWindow).GetProperty("IdentitySnapshot", PrivateInstance)!.GetValue(window));
                    }
                    var selected = ((TabControl)window.FindName("ResultTabs")).SelectedItem as TabItem;
                    Require(selected?.Visibility == Visibility.Visible, "Tool selected a collapsed result panel.");
                    if (mode is "Document" or "OCR")
                    {
                        var bitmap = new RenderTargetBitmap((int)width, 720, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var output = File.Create(Path.Combine(Root, $"recognition-{mode}-{width}.png")); encoder.Save(output);
                    }
                    if (mode == "Document")
                    {
                        var selectedTool = nav.SelectedItem;
                        Invoke(window, "SetBusy", true);
                        try
                        {
                            content.UpdateLayout();
                            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                            Require(!nav.IsEnabled && !transformTools.IsEnabled && !((System.Windows.Controls.Button)window.FindName("RunButton")).IsEnabled && ((System.Windows.Controls.Button)window.FindName("CancelButton")).IsEnabled,
                                "Busy state must lock tool selection and retain cancellation.");
                            var busyImage = typeof(RecognitionWindow).GetField("_image", PrivateInstance)!.GetValue(window);
                            Invoke(window, "TransformImage_Click", transformTools.Children[0], new RoutedEventArgs());
                            Require(ReferenceEquals(busyImage, typeof(RecognitionWindow).GetField("_image", PrivateInstance)!.GetValue(window)), "Image changed while recognition was running.");
                            var bitmap = new RenderTargetBitmap((int)width, 720, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
                            var point = nav.TranslatePoint(new System.Windows.Point(4, nav.ActualHeight - 4), content);
                            // RenderTargetBitmap includes the root element's layout margin.
                            point.Offset(content.Margin.Left, content.Margin.Top);
                            var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect((int)point.X, (int)point.Y, 1, 1), pixel, 4, 0);
                            var expected = ((SolidColorBrush)window.FindResource("SurfaceAltBrush")).Color;
                            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            using var output = File.Create(Path.Combine(Root, $"recognition-busy-{width}.png")); encoder.Save(output);
                            Require(Math.Abs(pixel[0] - expected.B) < 4 && Math.Abs(pixel[1] - expected.G) < 4 && Math.Abs(pixel[2] - expected.R) < 4,
                                $"Disabled navigation background: BGRA={string.Join(',', pixel)}, expected={expected}, point={point}; preview={Root}");
                        }
                        finally { Invoke(window, "SetBusy", false); }
                        Require(nav.IsEnabled && ReferenceEquals(nav.SelectedItem, selectedTool), "Completing recognition must restore navigation without changing the tool.");
                    }
                }
            }
            var rotate = transformTools.Children.OfType<System.Windows.Controls.Button>().Single(b => b.Tag?.ToString() == "Right");
            rotate.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Require(typeof(RecognitionWindow).GetField("_document", PrivateInstance)!.GetValue(window) is null && ((System.Windows.Controls.TextBox)window.FindName("DocumentEditor")).Text.Length == 0
                && !((System.Windows.Controls.Button)window.FindName("CommonCopyButton")).IsEnabled, "Image orientation changes must invalidate stale result coordinates and output.");
            Console.WriteLine("PASS: recognition navigation including summary, minimum/default window sizes, field location/edit tracking; previews: " + Root);
        }
        finally { window.Close(); }
    }

    private static void RecognitionPreviewChecks(RecognitionWindow window)
    {
        var scroll = (ScrollViewer)window.FindName("PictureScroll");
        var scale = (ScaleTransform)window.FindName("PictureScale");
        var picture = (Image)window.FindName("Picture");
        var source = picture.Source;
        Require(window.FindName("Zoom") is null, "The old preview size slider should be removed.");
        void Flush()
        {
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
        Invoke(window, "SetPreviewScale", 4d); Flush();
        scroll.ScrollToHorizontalOffset(160); scroll.ScrollToVerticalOffset(120); Flush();
        var anchor = new System.Windows.Point(100, 90);
        var before = new System.Windows.Point((scroll.HorizontalOffset + anchor.X) / scale.ScaleX,
            (scroll.VerticalOffset + anchor.Y) / scale.ScaleY);
        Invoke(window, "ZoomPreview", 1.2d, anchor); Flush();
        Require(Math.Abs((scroll.HorizontalOffset + anchor.X) / scale.ScaleX - before.X) < .5
            && Math.Abs((scroll.VerticalOffset + anchor.Y) / scale.ScaleY - before.Y) < .5,
            $"Zoom should preserve the image point beneath the mouse: before={before}, offset={scroll.HorizontalOffset},{scroll.VerticalOffset}, scale={scale.ScaleX}, viewport={scroll.ViewportWidth},{scroll.ViewportHeight}.");
        var initialX = scroll.HorizontalOffset; var initialY = scroll.VerticalOffset;
        typeof(RecognitionWindow).GetField("_previewDragStart", PrivateInstance)!.SetValue(window, (System.Windows.Point?)anchor);
        typeof(RecognitionWindow).GetField("_previewDragOffset", PrivateInstance)!.SetValue(window, new System.Windows.Point(initialX, initialY));
        Invoke(window, "PanPreview", new System.Windows.Point(70, 65)); Flush();
        Require(Math.Abs(scroll.HorizontalOffset - initialX - 30) < 1 && Math.Abs(scroll.VerticalOffset - initialY - 25) < 1,
            "Dragging should pan by the pointer displacement.");
        Invoke(window, "EndPreviewDrag");
        Require(typeof(RecognitionWindow).GetField("_previewDragStart", PrivateInstance)!.GetValue(window) is null && !scroll.IsMouseCaptured,
            "Ending a drag should clear capture and drag state.");
        Invoke(window, "ZoomPreview", 1e6, anchor); Flush();
        Require(scale.ScaleX == 16, "Preview zoom should have an upper limit.");
        Invoke(window, "FitImage"); Flush();
        Require(scroll.ScrollableWidth == 0 && scroll.ScrollableHeight == 0 && ReferenceEquals(source, picture.Source),
            "Fit should reveal the whole image, and preview gestures must preserve source pixels.");
        Console.WriteLine("PASS: preview cursor-anchored zoom, drag offsets, capture cleanup, zoom limit and fit; source preserved.");
    }

    private static void BusinessLicenseChecks()
    {
        RecognizedBlock B(string text, double x, double y, double width = 470) => new(text, new Rect(x, y, width, 26), .97);
        var twoColumns = new RecognitionDocument([
            B("营业执照", 550, 20, 250), B("统一社会信用代码", 20, 40, 240), B("91310000ABCDEFG123", 20, 80, 300),
            B("证照编号 12345678901234567890", 20, 120),
            B("名    称 示例商贸有限公司", 20, 180), B("注册资本 人民币300.0000万元整", 800, 180),
            B("类    型 有限责任公司(自然人投资或控股)", 20, 220, 650), B("成立日期 2019年01月01日", 800, 220),
            B("法定代表人 测试甲", 20, 260), B("营业期限 2019年01月01日至长期", 800, 260),
            B("经营范围 日用品销售；", 20, 300, 600), B("住    所 示例市示例区", 800, 300),
            B("依法须经批准的项目，", 170, 337, 430), B("幸福路12号", 950, 337, 300), B("经相关部门批准后方可开展经营活动。", 170, 374, 500),
            B("登记机关 示例市市场监督管理局", 800, 500), B("发照日期 2020年10月29日", 800, 545)
        ], "two-column business fixture");
        var result = IdentityDocumentRecognition.Recognize(twoColumns);
        string V(string name) => result.Fields.Single(f => f.Name == name).Value;
        Require(result.Kind == IdentityDocumentKind.BusinessLicense && result.Fields.Count == 14, "Business schema is incomplete.");
        Require(V("名称") == "示例商贸有限公司" && V("注册资本") == "人民币300.0000万元整", "Name and capital crossed columns.");
        Require(V("统一社会信用代码") == "91310000ABCDEFG123", "Credit code below its label was lost or mixed with the title.");
        Require(V("类型") == "有限责任公司(自然人投资或控股)" && V("成立日期") == "2019年01月01日", "Type and founding date crossed columns.");
        Require(V("法定代表人") == "测试甲" && V("营业期限") == "2019年01月01日至长期", "Representative and term crossed columns.");
        Require(V("经营范围") == "日用品销售;依法须经批准的项目,经相关部门批准后方可开展经营活动。", "Scope was truncated or contaminated by the address column: " + V("经营范围"));
        Require(V("地址") == "示例市示例区幸福路12号", "Address was contaminated by the scope column.");
        Require(V("登记机关") == "示例市市场监督管理局" && V("发照日期") == "2020年10月29日", "Registration fields missing.");
        var branch = IdentityDocumentRecognition.Recognize(Lines("营业执照", "注册号 123456789012345", "名称 示例商贸有限公司分公司", "类型 有限责任公司分公司", "负责人 测试乙",
            "营业期限", "营业场所 示例市示例区", "经营范围 汽车维修；", "依法须经批准的项目。"));
        Require(branch.Fields.Single(f => f.Name == "负责人").Value == "测试乙" && branch.Fields.Single(f => f.Name == "法定代表人").Value == "", "Branch responsible person must not be invented as a legal representative.");
        Require(branch.Fields.Single(f => f.Name == "营业期限").Value == "" && branch.Fields.Single(f => f.Name == "地址").Value == "示例市示例区", "Blank term swallowed the next field or branch address alias failed.");
        Require(branch.Fields.Single(f => f.Name == "注册号").Value == "123456789012345" && branch.Fields.Single(f => f.Name == "统一社会信用代码").Value == "", "Legacy registration numbers must not become credit codes.");
        var merged = IdentityDocumentRecognition.Recognize(Lines("营业执照", "统一社会信用代码 91310000ABCDEFG123", "名称 示例企业 注册资本 壹佰万元", "类型 有限责任公司 成立日期 2019年01月01日", "法定代表人 测试甲 营业期限 长期", "经营范围 商务服务业 住所 示例市"));
        Require(merged.Fields.Single(f => f.Name == "名称").Value == "示例企业" && merged.Fields.Single(f => f.Name == "经营范围").Value == "商务服务业", "Merged OCR rows must split at all business labels.");
        var badCode = IdentityDocumentRecognition.Recognize(Lines("营业执照", "统一社会信用代码 营业执照二维码登录", "名称 示例企业"));
        Require(badCode.Fields.Single(f => f.Name == "统一社会信用代码") is { Value: "", NeedsReview: true }, "Unrelated title/QR text must not be displayed as a credit code.");
        var headerCode = IdentityDocumentRecognition.Recognize(new RecognitionDocument([B("统一社会信用代码", 20, 40, 240), B("营业执照", 550, 40, 240),
            B("91310000ABCDEFG123", 20, 80, 300), B("名称 示例公司", 20, 180)], "same-row title fixture"));
        Require(headerCode.Fields.Single(f => f.Name == "统一社会信用代码").Value == "91310000ABCDEFG123", "Centered title must not hide a below-label credit code.");
        var footer = IdentityDocumentRecognition.Recognize(new RecognitionDocument([B("营业执照", 300, 20, 240), B("名称 示例公司", 20, 180), B("登记机关 示例市市场监督管理局", 800, 500),
            B("2020 年 10 月 29 日", 850, 555, 300)], "footer date fixture"));
        Require(footer.Fields.Single(f => f.Name == "发照日期").Value == "2020年10月29日" && footer.Fields.Single(f => f.Name == "登记机关").Value == "示例市市场监督管理局", "Footer issue date was lost or appended to the issuing authority.");
        BusinessSampleLayoutChecks();
        Console.WriteLine("PASS: horizontal/vertical business layouts, below-label credit code, full schema, branch aliases, empty fields, multiline scope/address and column isolation.");
    }

    // Transcribed OCR blocks using the five supplied images' layouts. These test
    // extraction from correct OCR, separately from model accuracy on the images.
    private static void BusinessSampleLayoutChecks()
    {
        RecognizedBlock B(string text, double x, double y, double w, double h = 18, string kind = "text") => new(text, new Rect(x, y, w, h), .97, kind);
        var sampleIndex = 0;
        IdentityDocumentResult Read(params RecognizedBlock[] blocks)
        {
            var result = IdentityDocumentRecognition.Recognize(new RecognitionDocument(blocks, "transcribed sample layout"));
            File.WriteAllText(Path.Combine(Root, $"business-transcribed-sample-{++sampleIndex}.txt"), string.Join(Environment.NewLine, result.Fields.Select(f => f.Name + "：" + f.Value)));
            return result;
        }
        string V(IdentityDocumentResult result, string name) => result.Fields.Single(f => f.Name == name).Value;
        var old = Read(B("营业执照", 655, 260, 380, 70), B("编号 32070000201502120000", 850, 387, 165, 12),
            B("注册号 320700000000008 (1/4)", 820, 405, 280),
            B("名", 596, 450, 18), B("称", 680, 450, 18), B("连云港市建设工程有限公司", 714, 450, 220),
            B("类", 596, 480, 18), B("型", 680, 480, 18), B("有限责任公司", 714, 480, 120),
            B("住", 596, 510, 18), B("所", 680, 510, 18), B("法定代表人 万海", 596, 538, 160),
            B("注册资本 10000万元整", 596, 566, 220), B("成立日期 2011年01月11日", 596, 595, 250),
            B("营业期限 2011年01月11日至2021年01月11日", 596, 624, 390),
            B("经营范围", 596, 652, 100), B("房屋建筑工程施工总承包；工业与民用建筑项目施工；", 714, 652, 425, 13),
            B("建筑工程、建筑结构工程、水电安装工程；", 714, 667, 425, 13), B("经相关部门批准后方可开展经营活动。", 714, 682, 320, 13),
            B("公省", 980, 800, 45, 26), B("登记机关", 826, 871, 110), B("2015年 02月 12日", 945, 931, 155));
        Require(V(old, "名称") == "连云港市建设工程有限公司" && V(old, "类型") == "有限责任公司", "Sample 1: widely separated label characters lost name/type.");
        Require(V(old, "地址") == "" && V(old, "法定代表人") == "万海", "Sample 1: a blank address borrowed the next field.");
        Require(V(old, "注册号") == "320700000000008" && V(old, "证照编号") == "32070000201502120000", "Sample 1: page suffix was retained or certificate number lost.");
        Require(V(old, "经营范围") == "房屋建筑工程施工总承包;工业与民用建筑项目施工;建筑工程、建筑结构工程、水电安装工程;经相关部门批准后方可开展经营活动。", "Sample 1: scope lost wrapped text or consumed a seal/footer: " + V(old, "经营范围"));
        Require(V(old, "登记机关") == "" && V(old, "发照日期") == "2015年02月12日", "Sample 1: invented a seal authority or lost footer date.");

        var horizontal = Read(B("营业执照", 635, 245, 400, 70), B("统一社会信用代码", 242, 262, 230), B("10440119MA06M85", 242, 302, 165),
            B("名", 243, 434, 23), B("称", 345, 434, 23), B("有限公司", 383, 434, 80), B("注册资本 贰佰万元（人民币）", 906, 434, 300),
            B("类", 243, 488, 23), B("型", 345, 488, 23), B("有限责任公司(自然人投资或控股)", 383, 488, 330), B("成立日期 2019年01月01日", 906, 488, 300),
            B("法定代表人 方平", 243, 542, 220), B("营业期限 2019年01月01日至长期", 906, 542, 340),
            B("经营范围 商务服务业", 243, 594, 240), B("住", 906, 594, 23), B("所", 1008, 594, 23), B("广州市", 1045, 594, 70),
            B("登记机关", 1030, 780, 150), B("年 月 日", 1240, 865, 180), B("2019", 1190, 891, 45), B("01", 1282, 891, 25), B("01", 1370, 891, 25));
        Require(V(horizontal, "名称") == "有限公司" && V(horizontal, "地址") == "广州市" && V(horizontal, "经营范围") == "商务服务业", "Sample 2: horizontal column values mixed.");
        Require(V(horizontal, "统一社会信用代码") == "10440119MA06M85" && horizontal.Fields.Single(f => f.Name == "统一社会信用代码").NeedsReview,
            "Sample 2: short printed code must be preserved and flagged, never padded or replaced by 编号.");
        Require(V(horizontal, "发照日期") == "2019年01月01日", "Sample 2: date digits printed below year/month/day labels were lost.");

        var branch = Read(B("营业执照", 635, 270, 380, 70), B("名称 上海汽车销售服务有限公司", 294, 457, 380),
            B("类型 有限责任公司分公司(自然人投资或控股)", 294, 510, 440), B("成立日期 2012年06月08日", 942, 457, 300),
            B("负责人 车车", 294, 562, 180), B("营业期限", 942, 510, 120), B("营业场所 上海市金山区", 942, 562, 300),
            B("经营范围 二类机动车维修（小型车辆维修，大、中型货车维修）。", 294, 615, 580),
            B("【依法须经批准的项目，经相关部门批准后方可开展经营活动】", 430, 637, 470),
            B("东新管局", 1200, 681, 95, 25), B("登记机关", 1020, 790, 140), B("2020 年 10月 29日", 1167, 870, 220));
        Require(V(branch, "营业期限") == "" && V(branch, "注册资本") == "" && V(branch, "法定代表人") == "" && V(branch, "负责人") == "车车", "Sample 3: blank branch fields borrowed adjacent values.");
        Require(V(branch, "地址") == "上海市金山区" && !V(branch, "经营范围").Contains("东新管局") && V(branch, "经营范围").Contains("经营活动"), "Sample 3: seal contamination or multiline truncation.");

        var blankRep = Read(B("营业执照", 650, 262, 380, 70), B("名称 上海娱乐有限公司", 285, 445, 300), B("注册资本 人民币300.0000万元整", 946, 445, 360),
            B("类型 有限责任公司(自然人投资或控股)", 285, 498, 430), B("成立日期 2016年10月10日", 946, 498, 320),
            B("法定代表人", 285, 555, 120), B("营业期限 2016年10月10日至2036年10月10日", 946, 555, 430),
            B("经营范围 【依法须经批准的项目，经相关部门批准后方可开展经营活动】", 285, 605, 570), B("住所 上海市奉贤区", 946, 605, 280),
            B("东新管局", 1210, 658, 80), B("登记机关", 1040, 785, 140), B("2019 年 03 月 01 日", 1175, 868, 240),
            B("住所 上海市奉贤区东新管局", 946, 605, 380, 18, "document-retry"));
        Require(V(blankRep, "法定代表人") == "" && V(blankRep, "地址") == "上海市奉贤区", "Sample 4: blank representative borrowed a value or longer retry beat clean original address.");
        Require(V(blankRep, "发照日期") == "2019年03月01日", "Sample 4: issue date confused with founding/term dates.");

        var modern = Read(B("营业执照", 642, 260, 380, 70), B("编号 S11120180049112(1-1)", 953, 355, 150, 12),
            B("统一社会信用代码 914401114152115211", 857, 398, 250, 14), B("名称 广州文化发展有限公司", 583, 442, 350),
            B("类型 有限责任公司(自然人投资或控股)", 583, 480, 420), B("住所 广州市白云区", 583, 513, 260), B("法定代表人 钟钟", 583, 550, 220),
            B("注册资本 壹仟万元（人民币）", 583, 579, 340), B("成立日期 2009年03月03日", 583, 610, 320), B("营业期限 2009年03月03日至长期", 583, 642, 350),
            B("经营范围 体育（依法须经批准的项目，经相关部门批准后方可开展经营", 583, 673, 520, 14), B("活动。）", 728, 690, 75, 14),
            B("广州市白云区工商行政", 943, 860, 175), B("登记机关", 814, 874, 112), B("管理局", 943, 880, 55), B("2020 年 10 月 29 日", 924, 929, 170));
        Require(V(modern, "证照编号") == "S11120180049112(1-1)" && V(modern, "统一社会信用代码") == "914401114152115211", "Sample 5: legitimate certificate suffix stripped or code misassigned.");
        Require(V(modern, "登记机关") == "广州市白云区工商行政管理局", "Sample 5: printed authority wrapping above/beside the label was lost: " + V(modern, "登记机关"));
        Require(V(modern, "经营范围").EndsWith("活动。)"), "Sample 5: final short scope line lost.");
        var detectorGeometry = Read(B("营业执照", 641, 245, 381, 71), B("统一社会信用代码", 244, 262, 225, 21), B("10440119MA06M85", 241, 300, 158, 19),
            B("类", 240, 483, 37, 33), B("型", 263, 487, 109, 23), B("有限责任公司（自然人投资或控股）", 367, 486, 275, 19));
        Require(V(detectorGeometry, "类型") == "有限责任公司(自然人投资或控股)", "Real detector geometry: overlapping label boxes with unequal heights truncated the type.");
        Require(V(detectorGeometry, "统一社会信用代码") == "10440119MA06M85", "Real detector geometry: title swallowed below-label code.");
        var borderline = Read(B("营业执照", 662, 265, 370, 66), B("经营范围", 595, 652, 105, 19),
            B("房屋建筑工程；", 714, 652, 429, 15), B("经相关部门", 716, 667, 427, 17),
            new RecognizedBlock("2", new Rect(1154, 667, 10, 11), .269, "text"), B("批准后方可开展经营活动。", 717, 684, 186, 13),
            B("成立日期 12011年01月11日", 596, 594, 250, 19), B("成立日期 2011年01月11日", 580, 594, 273, 19, "document-retry"));
        Require(V(borderline, "经营范围") == "房屋建筑工程;经相关部门批准后方可开展经营活动。", "Real detector geometry: a low-confidence border digit entered the scope.");
        Require(V(borderline, "成立日期") == "2011年01月11日" && borderline.Fields.Single(f => f.Name == "成立日期").NeedsReview,
            "A valid date retry must outrank a five-digit year without silently changing the original OCR evidence.");
        Console.WriteLine("PASS: extraction from manually transcribed OCR/coordinates for all five supplied layouts (not an OCR run on the original attachments).");
    }

    private static void VerifyImageTransform(BitmapSource before, BitmapSource after, string action)
    {
        var quarter = action is "Left" or "Right";
        Require(after.PixelWidth == (quarter ? before.PixelHeight : before.PixelWidth) && after.PixelHeight == (quarter ? before.PixelWidth : before.PixelHeight), "Image transform dimensions are wrong.");
        static byte[] Pixels(BitmapSource bitmap)
        {
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; converted.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
        }
        var original = Pixels(before); var transformed = Pixels(after);
        for (var y = 0; y < before.PixelHeight; y++) for (var x = 0; x < before.PixelWidth; x++)
        {
            var (tx, ty) = action switch { "Left" => (y, before.PixelWidth - 1 - x), "Right" => (before.PixelHeight - 1 - y, x), "Half" => (before.PixelWidth - 1 - x, before.PixelHeight - 1 - y), _ => (x, before.PixelHeight - 1 - y) };
            var a = (y * before.PixelWidth + x) * 4; var b = (ty * after.PixelWidth + tx) * 4;
            Require(original.AsSpan(a, 4).SequenceEqual(transformed.AsSpan(b, 4)), $"Image transform lost or misplaced pixels: {action} at {x},{y} -> {tx},{ty}: {string.Join(',', original.AsSpan(a,4).ToArray())} vs {string.Join(',', transformed.AsSpan(b,4).ToArray())}");
        }
    }

    private static void DocumentModelChecks(string modelDirectory)
    {
        modelDirectory = Path.GetFullPath(modelDirectory);
        var settings = new AppSettings
        {
            OcrDetectionModel = Path.Combine(modelDirectory, "detection.onnx"),
            OcrRecognitionModel = Path.Combine(modelDirectory, "recognition.onnx"),
            OcrDictionary = Path.Combine(modelDirectory, "dictionary.txt"), OnnxDevice = "CPU"
        };
        foreach (var fontSize in new[] { 24d, 18d })
        {
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 240, 246)), null, new Rect(0, 0, 700, 440));
                string[] lines = ["姓  名    测试甲", "性  别    女       民  族    汉", "出  生    1949 年 12 月 31 日", "住  址    北京市朝阳区", "           幸福路12号", "公民身份号码    11010519491231002X"];
                for (var i = 0; i < lines.Length; i++) dc.DrawText(new FormattedText(lines[i], CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                    new Typeface("Microsoft YaHei"), fontSize, Brushes.Black, 1), new System.Windows.Point(30, 35 + i * 60));
            }
            var bitmap = new RenderTargetBitmap(700, 440, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing); bitmap.Freeze();
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(Root, $"document-ocr-fixture-{fontSize}.png"))) encoder.Save(file);
            var document = new LocalRecognitionService().RecognizeIdentityDocument(bitmap, settings).GetAwaiter().GetResult();
            var result = IdentityDocumentRecognition.Recognize(document);
            File.WriteAllText(Path.Combine(Root, $"document-ocr-result-{fontSize}.txt"), document.Text + "\n\n" + string.Join("\n", result.Fields.Select(f => $"{f.Name}: {f.Value} (review={f.NeedsReview})")));
            Require(result.Kind == IdentityDocumentKind.IdCardFront, "Real model failed ID classification; see " + Root);
            Require(result.Fields.Single(f => f.Name == "姓名").Value == "测试甲" && result.Fields.Single(f => f.Name == "性别").Value == "女" && result.Fields.Single(f => f.Name == "民族").Value == "汉",
                "Real model lost name/gender/ethnicity; see " + Root);
            Require(result.Fields.Single(f => f.Name == "身份证号").Value == "11010519491231002X", "Real model lost ID digits; see " + Root);
            Require(result.Fields.Single(f => f.Name == "出生日期").Value == "1949年12月31日" && result.Fields.Single(f => f.Name == "地址").Value.Contains("幸福路12号"), "Real model lost date/address; see " + Root);
        }
        foreach (var frontFirst in new[] { true, false })
        {
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 700, 940));
                string[] frontLines = ["姓  名    测试甲", "性  别    女       民  族    汉", "出  生    1949 年 12 月 31 日", "住  址    北京市朝阳区", "           幸福路12号", "公民身份号码    11010519491231002X"];
                string[] backLines = ["中华人民共和国", "居民身份证", "签发机关 北京市公安局", "有效期限 2020.01.01-长期"];
                for (var side = 0; side < 2; side++)
                {
                    var lines = (side == 0) == frontFirst ? frontLines : backLines;
                    for (var i = 0; i < lines.Length; i++) dc.DrawText(new FormattedText(lines[i], CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                        new Typeface("Microsoft YaHei"), 24, Brushes.Black, 1), new System.Windows.Point(30, 35 + side * 480 + i * 60));
                }
            }
            var bitmap = new RenderTargetBitmap(700, 940, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing); bitmap.Freeze();
            var document = new LocalRecognitionService().RecognizeIdentityDocument(bitmap, settings).GetAwaiter().GetResult();
            var result = IdentityDocumentRecognition.Recognize(document);
            File.WriteAllText(Path.Combine(Root, $"document-ocr-both-{frontFirst}.txt"), document.Text + "\n\n" + string.Join("\n", result.Fields.Select(f => $"{f.Name}: {f.Value}")));
            Require(result.Kind == IdentityDocumentKind.IdCardBoth && result.Fields.Count == 8 && result.Fields.All(f => f.Value.Length > 0), "Real model failed combined ID in one vertical order; see " + Root);
            Require(result.Fields.Single(f => f.Name == "身份证号").Value == "11010519491231002X" && result.Fields.Single(f => f.Name == "签发机关").Value.Contains("北京市公安局"), "Combined ID fields crossed sides; see " + Root);
        }
        foreach (var columns in new[] { true, false })
        {
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1600, 1100));
                void Draw(string text, double x, double y, double size = 26) => dc.DrawText(new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, new Typeface("Microsoft YaHei"), size, Brushes.Black, 1), new System.Windows.Point(x, y));
                Draw("营 业 执 照", 650, 40, 42);
                Draw("统一社会信用代码", 50, 65); Draw("91310000ABCDEFG123", 50, 110);
                Draw("名    称 示例商贸有限公司", 50, 220);
                Draw("类    型 有限责任公司", 50, 280);
                Draw("法定代表人 测试甲", 50, 340);
                Draw("注册资本 壹佰万元人民币", columns ? 900 : 50, columns ? 220 : 400);
                Draw("成立日期 2019年01月01日", columns ? 900 : 50, columns ? 280 : 460);
                Draw("营业期限 2019年01月01日至长期", columns ? 900 : 50, columns ? 340 : 520);
                Draw("住    所 示例市幸福路12号", columns ? 900 : 50, columns ? 400 : 580);
                Draw("经营范围 日用品销售;", 50, columns ? 400 : 640);
                Draw("经相关部门批准后方可开展经营活动。", 200, columns ? 445 : 685);
                Draw("登记机关 示例市市场监督管理局", 850, 850);
                Draw("发照日期 2020年10月29日", 850, 920);
            }
            var bitmap = new RenderTargetBitmap(1600, 1100, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing); bitmap.Freeze();
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(Root, $"business-ocr-fixture-{columns}.png"))) encoder.Save(file);
            var document = new LocalRecognitionService().RecognizeIdentityDocument(bitmap, settings).GetAwaiter().GetResult();
            var result = IdentityDocumentRecognition.Recognize(document);
            File.WriteAllText(Path.Combine(Root, $"business-ocr-result-{columns}.txt"), document.Text + "\n\n" + string.Join("\n", result.Fields.Select(f => $"{f.Name}: {f.Value}")));
            string V(string name) => result.Fields.Single(f => f.Name == name).Value;
            Require(result.Kind == IdentityDocumentKind.BusinessLicense && V("名称") == "示例商贸有限公司" && V("法定代表人") == "测试甲", "Real business OCR lost name/representative; see " + Root);
            Require(V("统一社会信用代码") == "91310000ABCDEFG123" && V("类型") == "有限责任公司", "Real business OCR lost code/type; see " + Root);
            Require(V("注册资本").StartsWith("壹佰") && V("注册资本").EndsWith("人民币") && V("成立日期") == "2019年01月01日" && V("营业期限") == "2019年01月01日至长期", "Real business OCR mixed capital/date/term; see " + Root);
            Require(!V("注册资本").Contains("方元") || result.Fields.Single(f => f.Name == "注册资本").NeedsReview, "Malformed capital OCR must be flagged.");
            Require(V("地址") == "示例市幸福路12号" && V("经营范围").StartsWith("日用品销售") && V("经营范围").Contains("经营活动"), "Real business OCR mixed or truncated scope/address; see " + Root);
        }
        Console.WriteLine("PASS: real local PaddleOCR + row retry on ID front/both orders and horizontal/vertical business layouts (rendered fixtures, not a photograph accuracy benchmark).");
    }

    private static void DocumentImageDiagnostics(string imageDirectory, string modelDirectory)
    {
        var settings = new AppSettings
        {
            OcrDetectionModel = Path.GetFullPath(Path.Combine(modelDirectory, "detection.onnx")),
            OcrRecognitionModel = Path.GetFullPath(Path.Combine(modelDirectory, "recognition.onnx")),
            OcrDictionary = Path.GetFullPath(Path.Combine(modelDirectory, "dictionary.txt")), OnnxDevice = "CPU"
        };
        var files = Directory.EnumerateFiles(Path.GetFullPath(imageDirectory)).Where(f =>
            new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".tif", ".tiff" }.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
        Require(files.Length > 0, "No document images found in " + imageDirectory);
        for (var i = 0; i < files.Length; i++)
        {
            var image = new BitmapImage();
            using (var stream = File.OpenRead(files[i]))
            {
                image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
            }
            var document = new LocalRecognitionService().RecognizeIdentityDocument(image, settings).GetAwaiter().GetResult();
            var prefix = Path.Combine(Root, $"photo-{i + 1:D2}");
            var result = SaveDocumentDiagnostics(document, files[i], prefix);
            Console.WriteLine($"READ: {files[i]} => {result.Kind}; diagnostics: {prefix}-*.txt");
        }
        Console.WriteLine("Image OCR finished. Compare original photos, OCR blocks and extracted fields before attributing errors to the model. Output: " + Root);
    }

    private static IdentityDocumentResult SaveDocumentDiagnostics(RecognitionDocument document, string input, string prefix)
    {
            var result = IdentityDocumentRecognition.Recognize(document);
            File.WriteAllText(prefix + "-fields.txt", string.Join(Environment.NewLine, result.Fields.Select(f => f.Name + "：" + f.Value)));
            File.WriteAllText(prefix + "-ocr.txt", "Input: " + input + Environment.NewLine + string.Join(Environment.NewLine,
                document.Blocks.Select(b => $"[{b.Kind}] confidence={b.Confidence:F3}, bounds={b.Bounds.ToString(CultureInfo.InvariantCulture)}: {b.Text}")));
            File.WriteAllText(prefix + "-review.txt", string.Join(Environment.NewLine, result.Fields.Select(f =>
                $"{f.Name}: review={f.NeedsReview}, bounds={f.Bounds.ToString(CultureInfo.InvariantCulture)}, original={f.OriginalValue}")));
            File.WriteAllText(prefix + "-blocks.json", System.Text.Json.JsonSerializer.Serialize(new
            {
                input, backend = document.Backend, kind = result.Kind.ToString(),
                blocks = document.Blocks.Select(b => new { b.Text, b.Kind, b.Confidence, x = b.Bounds.X, y = b.Bounds.Y, width = b.Bounds.Width, height = b.Bounds.Height }),
                fields = result.Fields.Select(f => new { f.Name, f.Value, f.OriginalValue, f.NeedsReview })
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            return result;
    }

    private static void DocumentReplayDiagnostics(string directory)
    {
        var files = Directory.GetFiles(Path.GetFullPath(directory), "photo-*-blocks.json").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        Require(files.Length > 0, "No cached document OCR blocks in " + directory);
        for (var i = 0; i < files.Length; i++)
        {
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(files[i]));
            var root = json.RootElement;
            var blocks = root.GetProperty("blocks").EnumerateArray().Select(b => new RecognizedBlock(
                b.GetProperty("Text").GetString()!, new Rect(b.GetProperty("x").GetDouble(), b.GetProperty("y").GetDouble(), b.GetProperty("width").GetDouble(), b.GetProperty("height").GetDouble()),
                b.GetProperty("Confidence").ValueKind == System.Text.Json.JsonValueKind.Null ? null : b.GetProperty("Confidence").GetDouble(), b.GetProperty("Kind").GetString()!)).ToArray();
            var input = root.GetProperty("input").GetString()!;
            var document = new RecognitionDocument(blocks, root.GetProperty("backend").GetString()!);
            var result = SaveDocumentDiagnostics(document, input, Path.Combine(Root, $"photo-{i + 1:D2}"));
            Console.WriteLine($"REPLAY: {input} => {result.Kind}");
        }
        Console.WriteLine("Re-extracted existing OCR evidence without running a model. Output: " + Root);
    }
}
