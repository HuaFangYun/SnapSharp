using System.IO.Compression;
using System.Text.Json;
using SnapSharp.Models;
using L = SnapSharp.Localization.UiText;

namespace SnapSharp.Services;

internal static class SlbrModelDownloadService
{
    private const string Prefix = "watermark-slbr-";
    internal const string MirrorSource = "https://huggingface.co/CuiMuxuan/moonshine-models/resolve/43d683e99c44c77ada2e653009629d23069b5ce3/slbr.pth.tar";
    internal static ModelInstallCache.Asset Checkpoint(string kind) => kind == ModelDownloadSource.Domestic
        ? new("checkpoint.pth", "f3984bd73e8eff5bfd69ad4786788c049a934fc8619821e4d9b9605c31a5d9b0", 85782395, 85782395)
        : new("checkpoint.pth", "be5c0a89513803c8ce56b1768e0c6296900f6ef76df8cbda12900324f9b1caf9", 85707015, 85707015);
    internal static string Source(string kind) => kind == ModelDownloadSource.Domestic ? MirrorSource : WatermarkModels.Detector.Source;
    internal static long MaximumDownloadBytes => Checkpoint(ModelDownloadSource.Domestic).MaximumBytes;

    internal static async Task<string> Download(string root, IProgress<string> progress, CancellationToken token,
        string sourceKind = ModelDownloadSource.Official, System.Net.Http.HttpClient? httpClient = null)
    {
        if (sourceKind is not (ModelDownloadSource.Official or ModelDownloadSource.Domestic)) throw new ArgumentException("Unknown model source.");
        using var lease = await ModelInstallCache.Lock(root, Prefix, token);
        var model = WatermarkModels.Detector;
        var installedAsset = new ModelInstallCache.Asset("slbr.onnx", model.Sha256, model.Size, model.Size);
        var existing = await ModelInstallCache.Find(root, Prefix, new[] { installedAsset }, progress, token);
        if (existing is not null) return Path.Combine(existing, "slbr.onnx");
        var checkpoint = Checkpoint(sourceKind);
        // The two hosts package the original tensors differently. Never resume one container into the other.
        var staging = Path.Combine(root, "watermark-downloads", "slbr-" + checkpoint.Hash[..12]);
        Directory.CreateDirectory(staging);
        var checkpointPath = Path.Combine(staging, checkpoint.Name);
        await ResumableModelDownload.Download(Source(sourceKind), checkpointPath, checkpoint.Hash, checkpoint.MaximumBytes,
            progress, token, sourceKind: sourceKind, httpClient: httpClient,
            logPath: Path.Combine(AppPaths.DiagnosticsRoot, "watermark-download.log"));
        progress.Report(L.Get("WatermarkPreparingDetector"));
        var target = Path.Combine(staging, "slbr.onnx");
        await Reconstruct(checkpointPath, target, sourceKind, token);
        ThirdPartyLicenseService.CopyTo(staging, model.LicenseFile);
        var candidate = new AppSettings { WatermarkDefaultModelId = model.Id, RecognitionTimeoutSeconds = 120,
            WatermarkModels = new() { [model.Id] = target } };
        var dummy = System.Windows.Media.Imaging.BitmapSource.Create(16, 16, 96, 96, System.Windows.Media.PixelFormats.Gray8, null, new byte[256], 16); dummy.Freeze();
        progress.Report(L.Get("VerifyingFilesAndONNXContractLargeModels"));
        await OnnxWorker.Execute("watermark-check", dummy, candidate, token);
        token.ThrowIfCancellationRequested();
        File.WriteAllText(Path.Combine(staging, "source.txt"), $"Source: {Source(sourceKind)}\nCheckpoint SHA256: {checkpoint.Hash}\nONNX SHA256: {model.Sha256}\nONNX size: {model.Size}\nContract: Slbr\nNative installer restores official BN statistics; no Python or pickle execution.\n");
        // Only the usable ONNX and provenance are retained in the installed model folder.
        File.Delete(checkpointPath);
        var installed = Path.Combine(root, Prefix + Guid.NewGuid().ToString("N"));
        Directory.Move(staging, installed);
        return Path.Combine(installed, "slbr.onnx");
    }

    internal static async Task Reconstruct(string checkpointPath, string outputPath, string sourceKind, CancellationToken token)
    {
        var checkpoint = Checkpoint(sourceKind);
        if (!await ModelInstallCache.Matches(checkpointPath, checkpoint, token)) throw new InvalidDataException("Unrecognized SLBR checkpoint.");
        using var resource = typeof(SlbrModelDownloadService).Assembly.GetManifestResourceStream("SnapSharp.SlbrInstallRecipe")
            ?? throw new InvalidDataException("SLBR installation recipe is missing.");
        using var compressed = new GZipStream(resource, CompressionMode.Decompress);
        var recipe = await JsonSerializer.DeserializeAsync<Recipe>(compressed, cancellationToken: token)
            ?? throw new InvalidDataException("Invalid SLBR installation recipe.");
        var model = WatermarkModels.Detector;
        if (recipe.OutputSize != model.Size || recipe.OutputSha256 != model.Sha256
            || !recipe.Sources.TryGetValue(sourceKind, out var source) || source.Sha256 != checkpoint.Hash || source.Size != checkpoint.Size)
            throw new InvalidDataException("SLBR recipe does not match the pinned model.");
        var temporary = outputPath + ".building";
        try
        {
            await using (var input = new FileStream(checkpointPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                foreach (var segment in source.Segments)
                {
                    token.ThrowIfCancellationRequested();
                    if (segment.Data is { } literal)
                    {
                        var bytes = Convert.FromBase64String(literal);
                        if (bytes.Length > model.Size - output.Position) throw new InvalidDataException("SLBR recipe overflow.");
                        await output.WriteAsync(bytes, token);
                    }
                    else
                    {
                        if (segment.Offset < 0 || segment.Length <= 0 || segment.Offset > input.Length - segment.Length
                            || segment.Length > model.Size - output.Position) throw new InvalidDataException("Invalid SLBR tensor range.");
                        input.Position = segment.Offset;
                        for (var remaining = segment.Length; remaining > 0;)
                        {
                            var count = await input.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), token);
                            if (count == 0) throw new EndOfStreamException();
                            await output.WriteAsync(buffer.AsMemory(0, count), token);
                            remaining -= count;
                        }
                    }
                }
            }
            if (!await ModelInstallCache.Matches(temporary, new("slbr.onnx", model.Sha256, model.Size, model.Size), token))
                throw new InvalidDataException("SLBR ONNX verification failed.");
            token.ThrowIfCancellationRequested();
            File.Move(temporary, outputPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed class Recipe
    {
        public string OutputSha256 { get; set; } = "";
        public long OutputSize { get; set; }
        public Dictionary<string, SourceRecipe> Sources { get; set; } = new();
    }
    private sealed class SourceRecipe
    {
        public string Sha256 { get; set; } = "";
        public long Size { get; set; }
        public List<Segment> Segments { get; set; } = new();
    }
    private sealed class Segment
    {
        public string? Data { get; set; }
        public long Offset { get; set; }
        public int Length { get; set; }
    }
}
