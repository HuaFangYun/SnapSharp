using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SnapSharp.Models;
using SkiaSharp;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SnapSharp.Services;

public sealed partial class LocalRecognitionService
{
    public Task<RecognitionDocument> RecognizeFormula(BitmapSource image, AppSettings settings, CancellationToken token = default) =>
        OnnxImageRuntime.Shared.Execute(settings, (models, cancellation) =>
        {
            OnnxImageRuntime.ValidateImage(image);
            var directory = settings.FormulaModelDirectory;
            if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
                throw new InvalidOperationException("请在设置 → OCR 与识别中一键下载并配置公式模型。");
            var encoder = models.Open(Path.Combine(directory, "encoder.onnx"), "公式编码器", false);
            var decoder = models.Open(Path.Combine(directory, "decoder.onnx"), "公式解码器", false);
            var resizer = models.Open(Path.Combine(directory, "image_resizer.onnx"), "公式尺寸预测模型", false);
            var vocabularyPath = Path.Combine(directory, "tokenizer.json");
            OnnxImageRuntime.RequireFile(vocabularyPath, "公式词表");
            if (new FileInfo(vocabularyPath).Length > 2_000_000) throw new InvalidDataException("公式词表过大。");
            using var vocabulary = JsonDocument.Parse(File.ReadAllText(vocabularyPath));
            var labels = vocabulary.RootElement.GetProperty("model").GetProperty("vocab").EnumerateObject().ToDictionary(x => x.Value.GetInt32(), x => x.Name);
            using var bitmap = _images.ToSkBitmap(image);
            using var input = FormulaCrop(bitmap);
            var w = input.Width; var h = input.Height; var ratio = 1d;
            DenseTensor<float>? tensor = null;
            for (var i = 0; i < 10; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                h = (int)Math.Clamp(h * ratio, 1, 192);
                using var resized = input.Resize(new SKImageInfo(w, h), new SKSamplingOptions(SKFilterMode.Linear))!;
                using var padded = FormulaCrop(resized);
                tensor = FormulaTensor(padded);
                var prediction = OnnxImageRuntime.ModelAccess.RunAll(resizer,
                    [NamedOnnxValue.CreateFromTensor(resizer.InputMetadata.First().Key, tensor)], cancellation).Values.First().Data;
                var best = ArgMax(prediction);
                w = Math.Clamp((best + 1) * 32, 32, 672);
                if (w == padded.Width) break;
                ratio = w / (double)padded.Width;
            }
            var encoded = OnnxImageRuntime.ModelAccess.RunAll(encoder,
                [NamedOnnxValue.CreateFromTensor(encoder.InputMetadata.First().Key, tensor!)], cancellation).Values.First();
            var context = new DenseTensor<float>(encoded.Data, encoded.Shape);
            var ids = new List<long> { 1 }; var decoded = new List<int>(); var ended = false;
            for (var step = 0; step < 512; step++)
            {
                cancellation.ThrowIfCancellationRequested();
                var tokens = new DenseTensor<long>(ids.ToArray(), new[] { 1, ids.Count });
                var mask = new DenseTensor<bool>(Enumerable.Repeat(true, ids.Count).ToArray(), new[] { 1, ids.Count });
                var inputs = decoder.InputMetadata.Select(x => x.Value.ElementType == typeof(long)
                    ? NamedOnnxValue.CreateFromTensor(x.Key, tokens)
                    : x.Value.ElementType == typeof(bool) ? NamedOnnxValue.CreateFromTensor(x.Key, mask)
                    : x.Value.ElementType == typeof(float) ? NamedOnnxValue.CreateFromTensor(x.Key, context)
                    : throw new NotSupportedException("公式解码器输入类型不受支持。")).ToArray();
                var output = OnnxImageRuntime.ModelAccess.RunAll(decoder, inputs, cancellation).Values.First();
                if (output.Shape.Length != 3 || output.Shape[0] != 1 || output.Shape[2] < 3 || output.Shape[1] < 1)
                    throw new InvalidDataException("公式解码器需返回 [1,T,V] logits。");
                var count = output.Shape[2];
                var next = ArgMax(output.Data.AsSpan(output.Data.Length - count, count));
                if (next == 2) { ended = true; break; }
                if (next is 0 or 1 || !labels.ContainsKey(next)) throw new InvalidDataException("公式模型生成无效词元，请裁剪到单个公式后重试。");
                ids.Add(next); decoded.Add(next);
            }
            if (!ended) throw new InvalidDataException("公式超过 512 个词元或未正常结束，请缩小识别区域。");
            var latex = DecodeFormulaTokens(decoded, labels);
            if (string.IsNullOrWhiteSpace(latex)) throw new InvalidDataException("未识别到公式，请框选单个清晰公式。");
            return new RecognitionDocument([new RecognizedBlock(latex, new Rect(0, 0, image.PixelWidth, image.PixelHeight), Kind: "latex")],
                "本地 LaTeX-OCR / ONNX " + models.DeviceLabel);
        }, token);

    private static int ArgMax(ReadOnlySpan<float> values)
    {
        if (values.Length == 0) throw new InvalidDataException("模型输出为空。");
        var best = 0;
        for (var i = 0; i < values.Length; i++)
        {
            if (!float.IsFinite(values[i])) throw new InvalidDataException("模型输出包含无效值。");
            if (values[i] > values[best]) best = i;
        }
        return best;
    }

    public static string DecodeFormulaTokens(IEnumerable<int> ids, IReadOnlyDictionary<int, string> labels)
    {
        var text = new StringBuilder();
        foreach (var id in ids)
        {
            if (id is 0 or 1 or 2) continue;
            if (!labels.TryGetValue(id, out var value)) throw new InvalidDataException("公式词表与模型不匹配。");
            text.Append(value);
        }
        return text.ToString().Replace("Ġ", " ").Replace("Ċ", "\n").Trim();
    }

    // Match LaTeX-OCR's grayscale, auto polarity, content crop and 32-pixel padding.
    private static SKBitmap FormulaCrop(SKBitmap source)
    {
        var gray = new byte[source.Width * source.Height]; byte min = 255, max = 0; long total = 0;
        for (var y = 0; y < source.Height; y++) for (var x = 0; x < source.Width; x++)
        {
            var p = source.GetPixel(x, y); var alpha = p.Alpha / 255d;
            var value = (byte)Math.Clamp((int)Math.Round((.299 * p.Red + .587 * p.Green + .114 * p.Blue) * alpha + 255 * (1 - alpha)), 0, 255);
            gray[y * source.Width + x] = value; min = Math.Min(min, value); max = Math.Max(max, value);
        }
        if (max == min) throw new InvalidDataException("公式图像没有可识别的前景。");
        for (var i = 0; i < gray.Length; i++) { gray[i] = (byte)((gray[i] - min) * 255 / (max - min)); total += gray[i]; }
        var invert = total / (double)gray.Length < 128;
        var left = source.Width; var right = -1; var top = source.Height; var bottom = -1;
        for (var y = 0; y < source.Height; y++) for (var x = 0; x < source.Width; x++)
        {
            var value = gray[y * source.Width + x]; if (invert) value = (byte)(255 - value);
            gray[y * source.Width + x] = value;
            if (value >= 128) continue;
            left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
        }
        if (right < left) throw new InvalidDataException("公式图像没有可识别的前景。");
        using var cropped = new SKBitmap(right - left + 1, bottom - top + 1);
        for (var y = 0; y < cropped.Height; y++) for (var x = 0; x < cropped.Width; x++)
        { var v = gray[(top + y) * source.Width + left + x]; cropped.SetPixel(x, y, new SKColor(v, v, v)); }
        var scale = Math.Min(1, Math.Min(672d / cropped.Width, 192d / cropped.Height));
        var width = Math.Max(1, (int)(cropped.Width * scale)); var height = Math.Max(1, (int)(cropped.Height * scale));
        var result = new SKBitmap((width + 31) / 32 * 32, (height + 31) / 32 * 32);
        using var canvas = new SKCanvas(result); canvas.Clear(SKColors.White);
        using var small = cropped.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear))!;
        canvas.DrawBitmap(small, 0, 0); return result;
    }
    private static DenseTensor<float> FormulaTensor(SKBitmap input)
    {
        var result = new DenseTensor<float>(new[] { 1, 1, input.Height, input.Width });
        for (var y = 0; y < input.Height; y++) for (var x = 0; x < input.Width; x++)
            result[0, 0, y, x] = (input.GetPixel(x, y).Red / 255f - .7931f) / .1738f;
        return result;
    }
}
