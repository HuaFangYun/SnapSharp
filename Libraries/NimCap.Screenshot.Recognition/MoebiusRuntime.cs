using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NimCap.Screenshot.Models;
using SkiaSharp;
using System.Windows.Media.Imaging;

namespace NimCap.Screenshot.Services;

public sealed partial class OnnxImageRuntime
{
    private const int LatentPlane = 64 * 64;
    private const float MoebiusScale = .13025f;

    private static (InferenceSession Encoder, InferenceSession Unet, InferenceSession Decoder) OpenMoebius(ModelAccess models, string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var encoder = models.Open(Path.Combine(directory, "vae_encoder.onnx"), "Moebius VAE encoder", false);
        var unet = models.Open(path, "Moebius UNet", false);
        var decoder = models.Open(Path.Combine(directory, "vae_decoder.onnx"), "Moebius VAE decoder", false);
        static bool Matches(IReadOnlyDictionary<string, NodeMetadata> nodes, string name, Type type, params int[] shape) =>
            nodes.TryGetValue(name, out var node) && node.IsTensor && node.ElementType == type && node.Dimensions.Length == shape.Length
            && node.Dimensions.Select((n, i) => n == shape[i] || n == -1).All(x => x);
        if (encoder.InputMetadata.Count != 1 || decoder.InputMetadata.Count != 1 || unet.InputMetadata.Count != 3
            || !Matches(encoder.InputMetadata, "image", typeof(float), 1, 3, 512, 512)
            || !Matches(encoder.OutputMetadata, "moments", typeof(float), 1, 8, 64, 64)
            || !Matches(decoder.InputMetadata, "latent", typeof(float), 1, 4, 64, 64)
            || !Matches(decoder.OutputMetadata, "image", typeof(float), 1, 3, 512, 512)
            || !Matches(unet.InputMetadata, "latent", typeof(float), 2, 9, 64, 64)
            || !Matches(unet.InputMetadata, "timesteps", typeof(long), 2)
            || !Matches(unet.InputMetadata, "input_ids", typeof(long), 2, 10)
            || !Matches(unet.OutputMetadata, "noise", typeof(float), 2, 4, 64, 64))
            throw new InvalidDataException("Incompatible Moebius ONNX model set.");
        return (encoder, unet, decoder);
    }

    // DDIM eta=0, scaled-linear training betas, 20 steps at strength .99 (19 actual steps).
    internal static double[] MoebiusAlphas()
    {
        var values = new double[1000];
        var product = 1d;
        for (var i = 0; i < values.Length; i++)
        {
            var betaRoot = Math.Sqrt(.00085) + (Math.Sqrt(.012) - Math.Sqrt(.00085)) * i / 999;
            product *= 1 - betaRoot * betaRoot;
            values[i] = product;
        }
        return values;
    }

    internal static void MoebiusDdimStep(float[] latent, float[] noise, int timestep, int previous, double[] alphas)
    {
        var signal = Math.Sqrt(alphas[timestep]);
        var sigma = Math.Sqrt(1 - alphas[timestep]);
        var previousAlpha = previous < 0 ? 1 : alphas[previous];
        for (var i = 0; i < latent.Length; i++)
            latent[i] = (float)(Math.Sqrt(previousAlpha) * (latent[i] - sigma * noise[i]) / signal + Math.Sqrt(1 - previousAlpha) * noise[i]);
    }

    private BitmapSource InpaintMoebius(ModelAccess models, BitmapSource image, WatermarkMask selection, string path, CancellationToken token)
    {
        var (encoder, unet, decoder) = OpenMoebius(models, path);
        var crop = ContextBounds(selection.Bounds(), selection.Width, selection.Height);
        using var source = _images.ToSkBitmap(image);
        using var region = new SKBitmap(crop.Width, crop.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        if (!source.ExtractSubset(region, new SKRectI(crop.X, crop.Y, crop.X + crop.Width, crop.Y + crop.Height)))
            throw new InvalidDataException("Cannot crop Moebius repair region.");
        using var resized = region.Resize(new SKImageInfo(512, 512), new SKSamplingOptions(SKFilterMode.Linear))
            ?? throw new InvalidDataException("Cannot resize Moebius input.");
        var masked = new DenseTensor<float>(new[] { 1, 3, 512, 512 });
        var holes = new float[512 * 512];
        for (var y = 0; y < 512; y++) for (var x = 0; x < 512; x++)
        {
            var hole = IsHole(selection, crop, x, y, 512, 512);
            holes[y * 512 + x] = hole ? 1 : 0;
            var color = resized.GetPixel(x, y);
            masked[0, 0, y, x] = hole ? 0 : color.Red / 127.5f - 1;
            masked[0, 1, y, x] = hole ? 0 : color.Green / 127.5f - 1;
            masked[0, 2, y, x] = hole ? 0 : color.Blue / 127.5f - 1;
        }
        var moments = ModelAccess.RunAll(encoder, [NamedOnnxValue.CreateFromTensor("image", masked)], token)["moments"];
        RequireShape(moments.Shape, 8, 64, 64);
        var nine = new float[2 * 9 * LatentPlane];
        for (var p = 0; p < LatentPlane; p++) nine[4 * LatentPlane + p] = holes[(p / 64 * 8) * 512 + p % 64 * 8];
        for (var i = 0; i < 4 * LatentPlane; i++) nine[5 * LatentPlane + i] = moments.Data[i] * MoebiusScale;
        var random = new Random(42);
        float Gaussian() => (float)(Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble()));
        var latent = new float[4 * LatentPlane];
        for (var i = 0; i < latent.Length; i++) latent[i] = Gaussian();
        for (var c = 0; c < 4; c++)
        {
            var offset = .0357f * Gaussian();
            for (var p = 0; p < LatentPlane; p++) latent[c * LatentPlane + p] += offset;
        }
        var ids = new DenseTensor<long>(Enumerable.Range(10, 10).Concat(Enumerable.Range(0, 10)).Select(x => (long)x).ToArray(), new[] { 2, 10 });
        var alphas = MoebiusAlphas();
        for (var t = 900; t >= 0; t -= 50)
        {
            token.ThrowIfCancellationRequested();
            latent.CopyTo(nine, 0);
            Array.Copy(nine, 0, nine, 9 * LatentPlane, 9 * LatentPlane);
            var prediction = ModelAccess.RunAll(unet, [
                NamedOnnxValue.CreateFromTensor("latent", new DenseTensor<float>(nine, new[] { 2, 9, 64, 64 })),
                NamedOnnxValue.CreateFromTensor("timesteps", new DenseTensor<long>(new long[] { t, t }, new[] { 2 })),
                NamedOnnxValue.CreateFromTensor("input_ids", ids)], token)["noise"];
            if (!prediction.Shape.SequenceEqual(new[] { 2, 4, 64, 64 })) throw new InvalidDataException("Invalid Moebius noise shape.");
            var guided = new float[latent.Length];
            for (var i = 0; i < guided.Length; i++) guided[i] = prediction.Data[i] + 7.5f * (prediction.Data[latent.Length + i] - prediction.Data[i]);
            MoebiusDdimStep(latent, guided, t, t - 50, alphas);
            if (latent.Any(x => !float.IsFinite(x))) throw new InvalidDataException("Non-finite Moebius latent.");
        }
        for (var i = 0; i < latent.Length; i++) latent[i] /= MoebiusScale;
        var decoded = ModelAccess.RunAll(decoder, [NamedOnnxValue.CreateFromTensor("latent", new DenseTensor<float>(latent, new[] { 1, 4, 64, 64 }))], token)["image"];
        RequireShape(decoded.Shape, 3, 512, 512);
        using var repaired = new SKBitmap(512, 512, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        byte Channel(int i)
        {
            if (!float.IsFinite(decoded.Data[i])) throw new InvalidDataException("Non-finite Moebius pixels.");
            return (byte)Math.Clamp((int)Math.Round((decoded.Data[i] + 1) * 127.5f), 0, 255);
        }
        for (var y = 0; y < 512; y++) for (var x = 0; x < 512; x++)
        {
            var p = y * 512 + x;
            repaired.SetPixel(x, y, new SKColor(Channel(p), Channel(512 * 512 + p), Channel(2 * 512 * 512 + p)));
        }
        using var full = repaired.Resize(new SKImageInfo(crop.Width, crop.Height), new SKSamplingOptions(SKFilterMode.Linear))
            ?? throw new InvalidDataException("Cannot restore Moebius output dimensions.");
        return CompositeRepair(image, _images.ToBitmapSource(full), selection, crop, token);
    }
}
