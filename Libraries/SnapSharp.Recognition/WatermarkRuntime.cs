using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SnapSharp.Models;
using SkiaSharp;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using L = SnapSharp.Localization.UiText;

namespace SnapSharp.Services;

public sealed partial class OnnxImageRuntime
{
    public Task<string> CheckWatermarkModel(AppSettings settings, CancellationToken token = default) => Execute(settings, "CPU", 0, (models, _) =>
    {
        var id = settings.WatermarkDefaultModelId;
        if (id == "moebius")
        {
            OpenMoebius(models, ModelPath(settings, id));
            return id;
        }
        var session = models.Open(ModelPath(settings, id), id, imageInput: false);
        ValidateWatermarkContract(session, id == WatermarkModels.DetectorId ? "Slbr" : WatermarkModels.Find(id).Profile);
        return id;
    }, token, settings.WatermarkDefaultModelId == "moebius" ? 600 : null);

    public async Task<BitmapSource> Inpaint(BitmapSource image, byte[] mask, AppSettings settings, CancellationToken token = default)
    {
        ValidateImage(image);
        var selection = new WatermarkMask(image.PixelWidth, image.PixelHeight, mask);
        if (selection.Bounds().IsEmpty) throw new ArgumentException(L.Get("WatermarkEmptyMask"));
        if (!OnnxWorker.IsWorker)
        {
            var response = await OnnxWorker.Execute("inpaint", image, settings, token, mask).ConfigureAwait(false);
            return OnnxWorker.Decode(response.Image ?? throw new InvalidDataException("Missing inpainting image."));
        }
        var model = WatermarkModels.Find(settings.WatermarkDefaultModelId);
        return await Execute(settings, settings.WatermarkDevice, settings.OnnxDeviceId, (models, cancellation) =>
        {
            if (model.Id == "moebius") return InpaintMoebius(models, image, selection, ModelPath(settings, model.Id), cancellation);
            var session = models.Open(ModelPath(settings, model.Id), model.Label, imageInput: false);
            ValidateWatermarkContract(session, model.Profile);
            var crop = ContextBounds(selection.Bounds(), selection.Width, selection.Height);
            using var source = _images.ToSkBitmap(image);
            using var region = new SKBitmap(crop.Width, crop.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            if (!source.ExtractSubset(region, new SKRectI(crop.X, crop.Y, crop.X + crop.Width, crop.Y + crop.Height)))
                throw new InvalidDataException("Cannot crop repair region.");
            // Bound tensor memory even for masks spanning a whole large screenshot.
            var scale = Math.Min(1, 1024d / Math.Max(region.Width, region.Height));
            var width = model.Profile == "Lama" ? 512 : Math.Max(16, (int)Math.Round(region.Width * scale));
            var height = model.Profile == "Lama" ? 512 : Math.Max(16, (int)Math.Round(region.Height * scale));
            using var resized = region.Resize(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul), new SKSamplingOptions(SKFilterMode.Linear))
                ?? throw new InvalidDataException("Cannot resize repair region.");
            using var run = new RunOptions();
            using var registration = cancellation.Register(() => run.Terminate = true);
            using var repaired = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            if (model.Profile == "MiganPipeline")
            {
                var input = new DenseTensor<byte>(new[] { 1, 3, height, width });
                var known = new DenseTensor<byte>(new[] { 1, 1, height, width });
                for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                {
                    var color = resized.GetPixel(x, y);
                    input[0, 0, y, x] = color.Red; input[0, 1, y, x] = color.Green; input[0, 2, y, x] = color.Blue;
                    known[0, 0, y, x] = IsHole(selection, crop, x, y, width, height) ? (byte)0 : (byte)255;
                }
                using var result = session.Run([NamedOnnxValue.CreateFromTensor("image", input), NamedOnnxValue.CreateFromTensor("mask", known)], session.OutputMetadata.Keys.ToArray(), run);
                var output = result.First().AsTensor<byte>();
                RequireShape(output.Dimensions.ToArray(), 3, height, width);
                for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                    repaired.SetPixel(x, y, new SKColor(output[0, 0, y, x], output[0, 1, y, x], output[0, 2, y, x]));
            }
            else
            {
                var input = RgbTensor(resized); var holes = new DenseTensor<float>(new[] { 1, 1, height, width });
                for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) holes[0, 0, y, x] = IsHole(selection, crop, x, y, width, height) ? 1 : 0;
                using var result = session.Run([NamedOnnxValue.CreateFromTensor("image", input), NamedOnnxValue.CreateFromTensor("mask", holes)], session.OutputMetadata.Keys.ToArray(), run);
                var output = result.First().AsTensor<float>();
                RequireShape(output.Dimensions.ToArray(), 3, height, width);
                for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                    // Carve's fixed FP32 export returns RGB values in [0,255], not [0,1].
                    repaired.SetPixel(x, y, new SKColor(Channel(output[0, 0, y, x]), Channel(output[0, 1, y, x]), Channel(output[0, 2, y, x])));
            }
            cancellation.ThrowIfCancellationRequested();
            using var full = repaired.Resize(new SKImageInfo(crop.Width, crop.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul), new SKSamplingOptions(SKFilterMode.Linear))
                ?? throw new InvalidDataException("Cannot restore repair dimensions.");
            return CompositeRepair(image, _images.ToBitmapSource(full), selection, crop, cancellation);
        }, token, model.Id == "moebius" ? 600 : null).ConfigureAwait(false);
    }

    public async Task<byte[]> DetectWatermark(BitmapSource image, byte[]? scope, AppSettings settings, CancellationToken token = default)
    {
        ValidateImage(image);
        if (!OnnxWorker.IsWorker)
        {
            var response = await OnnxWorker.Execute("watermark-detect", image, settings, token, scope).ConfigureAwait(false);
            return response.Mask ?? throw new InvalidDataException("Missing detected mask.");
        }
        return await Execute(settings, settings.WatermarkDevice, settings.OnnxDeviceId, (models, cancellation) =>
        {
            var restriction = scope is null ? null : new WatermarkMask(image.PixelWidth, image.PixelHeight, scope);
            var crop = restriction?.Bounds() ?? new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight);
            if (crop.IsEmpty) throw new ArgumentException(L.Get("WatermarkSelectRegion"));
            var session = models.Open(ModelPath(settings, WatermarkModels.DetectorId), "SLBR", imageInput: false);
            ValidateWatermarkContract(session, "Slbr");
            using var source = _images.ToSkBitmap(image);
            using var region = new SKBitmap(crop.Width, crop.Height);
            if (!source.ExtractSubset(region, new SKRectI(crop.X, crop.Y, crop.X + crop.Width, crop.Y + crop.Height))) throw new InvalidDataException("Cannot crop detection region.");
            using var inputImage = region.Resize(new SKImageInfo(256, 256), new SKSamplingOptions(SKFilterMode.Linear)) ?? throw new InvalidDataException("Cannot resize detection image.");
            var results = ModelAccess.RunAll(session, [NamedOnnxValue.CreateFromTensor("image", RgbTensor(inputImage))], cancellation);
            var output = results["mask"]; RequireShape(output.Shape, 1, 256, 256);
            var threshold = double.IsFinite(settings.WatermarkDetectionThreshold) ? Math.Clamp(settings.WatermarkDetectionThreshold, .05, .95) : .5;
            var mask = new byte[image.PixelWidth * image.PixelHeight];
            for (var y = 0; y < crop.Height; y++)
            {
                cancellation.ThrowIfCancellationRequested();
                for (var x = 0; x < crop.Width; x++)
                {
                    var index = (y + crop.Y) * image.PixelWidth + x + crop.X;
                    var probability = output.Data[Math.Min(255, y * 256 / crop.Height) * 256 + Math.Min(255, x * 256 / crop.Width)];
                    if (!float.IsFinite(probability) || probability < -.001 || probability > 1.001) throw new InvalidDataException(L.Get("WatermarkInvalidModel"));
                    if (probability >= threshold && (restriction is null || restriction.Pixels[index] != 0)) mask[index] = 255;
                }
            }
            return mask;
        }, token).ConfigureAwait(false);
    }

    public static void ValidateWatermarkContract(InferenceSession session, string profile)
    {
        bool Match(NodeMetadata node, Type type, int channels, int size = 0) => node.IsTensor && node.ElementType == type && node.Dimensions.Length == 4 &&
            node.Dimensions[0] is 1 or -1 && node.Dimensions[1] == channels && node.Dimensions.Skip(2).All(x => size == 0 ? x == -1 || x > 0 : x == size);
        var valid = profile switch
        {
            "MiganPipeline" => session.InputMetadata.Count == 2 && session.InputMetadata.TryGetValue("image", out var image) && Match(image, typeof(byte), 3) &&
                session.InputMetadata.TryGetValue("mask", out var mask) && Match(mask, typeof(byte), 1) && session.OutputMetadata.Count == 1 && Match(session.OutputMetadata.First().Value, typeof(byte), 3),
            "Lama" => session.InputMetadata.Count == 2 && session.InputMetadata.TryGetValue("image", out var image) && Match(image, typeof(float), 3, 512) &&
                session.InputMetadata.TryGetValue("mask", out var mask) && Match(mask, typeof(float), 1, 512) && session.OutputMetadata.Count == 1 && Match(session.OutputMetadata.First().Value, typeof(float), 3),
            "Slbr" => session.InputMetadata.Count == 1 && session.InputMetadata.TryGetValue("image", out var image) && Match(image, typeof(float), 3, 256) &&
                session.OutputMetadata.TryGetValue("mask", out var mask) && Match(mask, typeof(float), 1, 256),
            _ => false
        };
        if (!valid) throw new NotSupportedException(L.Get("WatermarkInvalidModel"));
    }

    public static BitmapSource CompositeRepair(BitmapSource original, BitmapSource repaired, WatermarkMask mask, Int32Rect crop, CancellationToken token = default)
    {
        if (original.PixelWidth != mask.Width || original.PixelHeight != mask.Height || crop.X < 0 || crop.Y < 0 || crop.Width < 1 || crop.Height < 1 ||
            (long)crop.X + crop.Width > mask.Width || (long)crop.Y + crop.Height > mask.Height || repaired.PixelWidth != crop.Width || repaired.PixelHeight != crop.Height)
            throw new ArgumentException("Invalid repair dimensions.");
        var source = new FormatConvertedBitmap(original, PixelFormats.Bgra32, null, 0);
        var patch = new FormatConvertedBitmap(repaired, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[mask.Width * mask.Height * 4]; source.CopyPixels(pixels, mask.Width * 4, 0);
        var replacement = new byte[crop.Width * crop.Height * 4]; patch.CopyPixels(replacement, crop.Width * 4, 0);
        for (var y = 0; y < crop.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < crop.Width; x++)
            {
                var index = (y + crop.Y) * mask.Width + x + crop.X;
                if (mask.Pixels[index] == 0) continue;
                var target = index * 4; var input = (y * crop.Width + x) * 4;
                pixels[target] = replacement[input]; pixels[target + 1] = replacement[input + 1]; pixels[target + 2] = replacement[input + 2];
                // Preserve source alpha and all pixels outside the mask exactly.
            }
        }
        var result = BitmapSource.Create(mask.Width, mask.Height, original.DpiX, original.DpiY, PixelFormats.Bgra32, null, pixels, mask.Width * 4); result.Freeze(); return result;
    }
    private static string ModelPath(AppSettings settings, string id) => settings.WatermarkModels.TryGetValue(id, out var path) && !string.IsNullOrWhiteSpace(path)
        ? path : throw new FileNotFoundException(L.Get(id == "slbr" ? "WatermarkSlbrMissing" : "WatermarkModelMissing"));
    private static void RequireShape(int[] shape, int channels, int height, int width)
    { if (!shape.SequenceEqual(new[] { 1, channels, height, width })) throw new InvalidDataException(L.Get("WatermarkInvalidModel")); }
    private static byte Channel(float value) => float.IsFinite(value) ? (byte)Math.Clamp((int)Math.Round(Math.Clamp(value, 0, 255)), 0, 255) : throw new InvalidDataException("Invalid repair pixels.");
    private static DenseTensor<float> RgbTensor(SKBitmap bitmap)
    {
        var tensor = new DenseTensor<float>(new[] { 1, 3, bitmap.Height, bitmap.Width });
        for (var y = 0; y < bitmap.Height; y++) for (var x = 0; x < bitmap.Width; x++)
        { var color = bitmap.GetPixel(x, y); tensor[0, 0, y, x] = color.Red / 255f; tensor[0, 1, y, x] = color.Green / 255f; tensor[0, 2, y, x] = color.Blue / 255f; }
        return tensor;
    }
    private static bool IsHole(WatermarkMask mask, Int32Rect crop, int x, int y, int width, int height)
    {
        // Max-pool when reducing the mask, so a thin stroke or one-pixel hole cannot disappear.
        var left = x * crop.Width / width; var top = y * crop.Height / height;
        var right = Math.Min(crop.Width, Math.Max(left + 1, (int)Math.Ceiling((x + 1d) * crop.Width / width)));
        var bottom = Math.Min(crop.Height, Math.Max(top + 1, (int)Math.Ceiling((y + 1d) * crop.Height / height)));
        for (var sy = top; sy < bottom; sy++) for (var sx = left; sx < right; sx++)
            if (mask.Pixels[(crop.Y + sy) * mask.Width + crop.X + sx] != 0) return true;
        return false;
    }
    private static Int32Rect ContextBounds(Int32Rect bounds, int width, int height)
    {
        var size = Math.Max(512, Math.Max(bounds.Width, bounds.Height) + 256);
        var w = Math.Min(width, size); var h = Math.Min(height, size);
        return new(Math.Clamp(bounds.X + bounds.Width / 2 - w / 2, 0, width - w), Math.Clamp(bounds.Y + bounds.Height / 2 - h / 2, 0, height - h), w, h);
    }
}
