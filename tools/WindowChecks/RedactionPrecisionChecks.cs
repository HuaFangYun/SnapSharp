using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapSharp.Models;
using SnapSharp.Services;
using Brushes = System.Windows.Media.Brushes;

namespace SnapSharp.WindowChecks;

internal static partial class Program
{
    private static void MinimalRedactionChecks()
    {
        var drawing = new DrawingVisual();
        var font = new Typeface("Microsoft YaHei");
        var text = "值班人员发现张三驾驶京A12345号车辆，随后张三来到北京市朝阳区幸福路12号办理业务。检测结果已记录，其他情况正常。";
        var spans = new List<RecognizedTextSpan>();
        var x = 30d;
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1400, 220));
            for (var i = 0; i < text.Length; i++)
            {
                var glyph = new FormattedText(text[i].ToString(), CultureInfo.GetCultureInfo("zh-CN"), System.Windows.FlowDirection.LeftToRight,
                    font, 18, Brushes.Black, 1);
                dc.DrawText(glyph, new System.Windows.Point(x, 60));
                spans.Add(new(i, 1, new Rect(x, 60, glyph.WidthIncludingTrailingWhitespace, 26)));
                x += glyph.WidthIncludingTrailingWhitespace;
            }
        }
        var block = new RecognizedBlock(text, new Rect(30, 60, x - 30, 26)) { TextSpans = spans };
        var entities = new[] { "张三", "北京市朝阳区幸福路12号" };
        var regions = SensitiveTextDetection.FindRegions(new([block], "position fixture"), 1400, 220, entities);
        foreach (var value in new[] { "张三", "京A12345", "北京市朝阳区幸福路12号" })
        {
            for (var from = 0; ;)
            {
                var index = text.IndexOf(value, from, StringComparison.Ordinal);
                if (index < 0) break;
                var bounds = spans.Skip(index).Take(value.Length).Select(s => s.Bounds).Aggregate(Rect.Union);
                Require(regions.Any(r => r.Contains(bounds)), "Sensitive substring was not fully covered: " + value);
                from = index + value.Length;
            }
        }
        foreach (var value in new[] { "值班人员", "驾驶", "号车辆", "随后", "来到", "办理业务", "检测结果已记录", "其他情况正常" })
        {
            var index = text.IndexOf(value, StringComparison.Ordinal);
            foreach (var span in spans.Skip(index).Take(value.Length))
                Require(!regions.Any(r => r.Contains(new System.Windows.Point(span.Bounds.Left + span.Bounds.Width / 2, span.Bounds.Top + 13))),
                    "Redaction expanded into surrounding narrative: " + value);
        }
        Require(regions.All(r => r.Width < block.Bounds.Width / 3), "A sensitive match must not cover a whole prose line.");
        var estimated = SensitiveTextDetection.FindRegions(new([block with { TextSpans = null }], "legacy/plugin fixture"), 1400, 220, ["张三"]);
        Require(estimated.All(r => r.Width < block.Bounds.Width / 3), "Line-only OCR results must still return substring regions.");
        var paragraph = new RecognizedBlock("第一行保留全部文字\n第二行张三办理业务\n第三行保留全部文字", new Rect(20, 20, 200, 90));
        var paragraphRegions = SensitiveTextDetection.FindRegions(new([paragraph], "multiline plugin fixture"), 500, 200, ["张三"]);
        Require(paragraphRegions.Count == 1 && paragraphRegions[0].Top >= 48 && paragraphRegions[0].Bottom <= 82 && paragraphRegions[0].Width < 60,
            "A multiline OCR block must redact only the matched part of the matched line.");
        var field = new RecognizedBlock("姓名：张三 性别：男", new Rect(20, 110, 200, 20));
        var fieldRegions = SensitiveTextDetection.FindRegions(new([field], "field fixture"), 1400, 220);
        Require(fieldRegions.Count == 1 && fieldRegions[0].Width < 70 && fieldRegions[0].Left > field.Bounds.Left + 30
            && fieldRegions[0].Right < field.Bounds.Right - 30, "Field labels and adjacent ordinary fields must remain visible.");
        var split = new RecognitionDocument([
            new("联系电话：", new Rect(20, 150, 90, 20)), new("138", new Rect(112, 150, 30, 20)),
            new("0013", new Rect(144, 150, 40, 20)), new("8000", new Rect(186, 150, 40, 20)),
            new("，请及时联系", new Rect(228, 150, 100, 20))
        ], "split fixture");
        var splitRegions = SensitiveTextDetection.FindRegions(split, 1400, 220);
        Require(splitRegions.Count == 3 && splitRegions.All(r => r.Left > 110 && r.Right < 228),
            "Joining a phone number must not redact labels or neighboring blocks.");

        string[] dictionary = ["甲", "张", "三", "乙"];
        int[] sequence = [0, 1, 0, 2, 0, 3, 0, 4, 0, 0, 0, 0];
        var logits = new float[sequence.Length * 5];
        for (var i = 0; i < sequence.Length; i++) logits[i * 5 + sequence[i]] = 1;
        var positioned = LocalRecognitionService.DecodeCtcWithPositions(logits, [1, sequence.Length, 5], dictionary, new Rect(10, 10, 400, 20), .75);
        var original = LocalRecognitionService.DecodeCtc(logits, [1, sequence.Length, 5], dictionary);
        Require(positioned.Text == original.Text && positioned.Confidence == original.Confidence && positioned.Positions.Count == 4,
            "Adding CTC positions must preserve recognized text/confidence and repeated/blank token handling.");
        var native = new RecognizedBlock(positioned.Text, new Rect(10, 10, 400, 20)) { TextSpans = positioned.Positions };
        var nativeRegions = SensitiveTextDetection.FindRegions(new([native], "CTC fixture"), 500, 100, ["张三"]);
        Require(nativeRegions.Count == 1 && nativeRegions[0].Left > 50 && nativeRegions[0].Right < 350,
            "CTC source positions must isolate the name, accounting for recognition input padding.");
        var serialized = System.Text.Json.JsonSerializer.Serialize(new OnnxWorker.Response([
            new OnnxWorker.Block(native.Text, 10, 10, 400, 20, .99, "text", native.TextSpans.Select(s =>
                new OnnxWorker.TextSpan(s.Start, s.Length, s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height)).ToArray())]));
        var roundtrip = OnnxWorker.ToDocument(System.Text.Json.JsonSerializer.Deserialize<OnnxWorker.Response>(serialized)!);
        Require(roundtrip.Blocks[0].TextSpans!.SequenceEqual(native.TextSpans), "Character positions were lost across the isolated OCR worker.");

        var before = new RenderTargetBitmap(1400, 220, 96, 96, PixelFormats.Pbgra32); before.Render(drawing); before.Freeze();
        var after = SensitiveRedactionAction.Render(new SkiaImageService(), before, regions);
        foreach (var pair in new[] { (Name: "redaction-precision-before", Image: (BitmapSource)before), (Name: "redaction-precision-after", Image: after) })
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(pair.Image));
            using var output = File.Create(Path.Combine(Root, pair.Name + ".png")); encoder.Save(output);
        }
        Console.WriteLine("PASS: minimal in-sentence names/plates/addresses, repeated names, preserved prose/labels, split number boundaries, native positions and worker transport; previews: " + Root);
    }

    private static void RedactionModelPositions(string directory)
    {
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(Path.Combine(Root, "redaction-precision-before.png")); image.EndInit(); image.Freeze();
        directory = Path.GetFullPath(directory);
        var preferences = new AppSettings
        {
            OcrBackend = "PaddleOCR", OnnxDevice = "CPU", OcrDetectionModel = Path.Combine(directory, "detection.onnx"),
            OcrRecognitionModel = Path.Combine(directory, "recognition.onnx"), OcrDictionary = Path.Combine(directory, "dictionary.txt")
        };
        var document = new LocalRecognitionService().Recognize(image, preferences).GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(Root, "redaction-native-positions.json"), System.Text.Json.JsonSerializer.Serialize(document));
        Require(document.Blocks.Count > 0 && document.Blocks.All(b => b.TextSpans is { Count: > 0 }), "Real OCR must transport text positions through the inference worker.");
        Require(document.Text.Contains("张三"), "Fixture name was not present in the real OCR result; see " + Root);
        var regions = SensitiveTextDetection.FindRegions(document, image.PixelWidth, image.PixelHeight, ["张三", "北京市朝阳区幸福路12号"]);
        Require(regions.Count > 0 && regions.All(r => r.Width < image.PixelWidth / 3), "Real OCR redaction expanded a short entity into the whole line.");
        var result = SensitiveRedactionAction.Render(new SkiaImageService(), image, regions);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(result));
        using var output = File.Create(Path.Combine(Root, "redaction-native-after.png")); encoder.Save(output);
        Console.WriteLine("PASS: real local Paddle OCR character positions and narrow redaction; previews: " + Root);
    }
}
