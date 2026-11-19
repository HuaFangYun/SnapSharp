using SnapSharp.Models;
using L = SnapSharp.Localization.UiText;

namespace SnapSharp.Services;

internal static class WatermarkModelDownloadService
{
    internal static async Task<string> Download(WatermarkModelDefinition model, string root, IProgress<string> progress,
        CancellationToken token, string sourceKind = ModelDownloadSource.Official, System.Net.Http.HttpClient? httpClient = null)
    {
        if (model.Id == WatermarkModels.DetectorId) return await SlbrModelDownloadService.Download(root, progress, token, sourceKind, httpClient);
        if (model.Id == "moebius") return await DownloadMoebius(root, progress, token, sourceKind, httpClient);
        using var lease = await ModelInstallCache.Lock(root, "watermark-" + model.Id + "-", token);
        var assets = new[] { new ModelInstallCache.Asset(model.Id + ".onnx", model.Sha256, model.Size, model.Size) };
        var existing = await ModelInstallCache.Find(root, "watermark-" + model.Id + "-", assets, progress, token);
        if (existing is not null) return Path.Combine(existing, model.Id + ".onnx");
        // Stable staging paths allow the existing download service to resume interrupted transfers.
        var staging = Path.Combine(root, "watermark-downloads", model.Id + "-" + model.Sha256[..12]);
        Directory.CreateDirectory(staging);
        var target = Path.Combine(staging, model.Id + ".onnx");
        await ResumableModelDownload.Download(model.Source, target, model.Sha256, model.Size, progress, token,
            httpClient: httpClient, logPath: Path.Combine(AppPaths.DiagnosticsRoot, "watermark-download.log"), sourceKind: sourceKind);
        token.ThrowIfCancellationRequested();
        ThirdPartyLicenseService.CopyTo(staging, model.Id == "lama" ? new[] { "Apache-2.0.txt", model.LicenseFile } : new[] { model.LicenseFile });
        File.WriteAllText(Path.Combine(staging, "source.txt"), $"Source: {model.Source}\nSHA256: {model.Sha256}\nSize: {model.Size}\nContract: {model.Profile}\nLicense files: {model.LicenseFile}\n");
        var installed = Path.Combine(root, "watermark-" + model.Id + "-" + Guid.NewGuid().ToString("N"));
        progress.Report(L.Get("VerifyingFilesAndONNXContractLargeModels"));
        var path = Path.Combine(staging, model.Id + ".onnx");
        var settings = new AppSettings { WatermarkDefaultModelId = model.Id, RecognitionTimeoutSeconds = 120, WatermarkModels = new() { [model.Id] = path } };
        var dummy = System.Windows.Media.Imaging.BitmapSource.Create(16, 16, 96, 96, System.Windows.Media.PixelFormats.Gray8, null, new byte[256], 16); dummy.Freeze();
        await OnnxWorker.Execute("watermark-check", dummy, settings, token);
        token.ThrowIfCancellationRequested();
        Directory.Move(staging, installed);
        return Path.Combine(installed, model.Id + ".onnx");
    }

    internal static bool IsInstalled(string id, string? path) => !string.IsNullOrEmpty(path) && File.Exists(path)
        && (id != "moebius" || WatermarkModels.MoebiusAssets.All(x => File.Exists(Path.Combine(Path.GetDirectoryName(path)!, x.Name))));

    private static async Task<string> DownloadMoebius(string root, IProgress<string> progress,
        CancellationToken token, string sourceKind, System.Net.Http.HttpClient? httpClient)
    {
        const string prefix = "watermark-moebius-";
        using var lease = await ModelInstallCache.Lock(root, prefix, token);
        var assets = WatermarkModels.MoebiusAssets.Select(x => new ModelInstallCache.Asset(x.Name, x.Sha256, x.Size, x.Size)).ToArray();
        var existing = await ModelInstallCache.Find(root, prefix, assets, progress, token);
        if (existing is not null) return Path.Combine(existing, "unet.onnx");
        var staging = Path.Combine(root, "watermark-downloads", "moebius-" + WatermarkModels.MoebiusRevision[..12]);
        Directory.CreateDirectory(staging);
        foreach (var asset in WatermarkModels.MoebiusAssets)
        {
            token.ThrowIfCancellationRequested();
            progress.Report("Moebius · " + asset.Name);
            await ResumableModelDownload.Download(WatermarkModels.MoebiusSource(asset.Name), Path.Combine(staging, asset.Name),
                asset.Sha256, asset.Size, progress, token, httpClient: httpClient,
                logPath: Path.Combine(AppPaths.DiagnosticsRoot, "watermark-download.log"), sourceKind: sourceKind);
        }
        ThirdPartyLicenseService.CopyTo(staging, "Apache-2.0.txt", "Moebius-ONNX-MODEL-CARD.md");
        var candidate = new AppSettings { WatermarkDefaultModelId = "moebius", RecognitionTimeoutSeconds = 120,
            WatermarkModels = new() { ["moebius"] = Path.Combine(staging, "unet.onnx") } };
        var dummy = System.Windows.Media.Imaging.BitmapSource.Create(16, 16, 96, 96, System.Windows.Media.PixelFormats.Gray8, null, new byte[256], 16); dummy.Freeze();
        progress.Report(L.Get("VerifyingFilesAndONNXContractLargeModels"));
        await OnnxWorker.Execute("watermark-check", dummy, candidate, token);
        token.ThrowIfCancellationRequested();
        File.WriteAllText(Path.Combine(staging, "source.txt"), string.Join("\n", WatermarkModels.MoebiusAssets.Select(x =>
            $"{x.Name}\nSource: {WatermarkModels.MoebiusSource(x.Name)}\nSHA256: {x.Sha256}\nSize: {x.Size}")));
        var installed = Path.Combine(root, prefix + Guid.NewGuid().ToString("N"));
        Directory.Move(staging, installed);
        return Path.Combine(installed, "unet.onnx");
    }
}
