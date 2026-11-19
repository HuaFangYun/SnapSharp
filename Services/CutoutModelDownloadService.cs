using L = SnapSharp.Localization.UiText;
using Microsoft.ML.OnnxRuntime;
using SnapSharp.Models;
using System.Net.Http;
using System.Security.Cryptography;

namespace SnapSharp.Services;

internal sealed class CutoutModelDownloadService
{
    internal static string Source => CutoutModels.All[0].Source;
    internal static string Sha256 => CutoutModels.All[0].Hash;
    public Task<string> Download(string root, IProgress<string> progress, CancellationToken token, string sourceKind = ModelDownloadSource.Official, HttpClient? httpClient = null) => Download(CutoutModels.All[0], root, progress, token, sourceKind, httpClient);

    public async Task<string> Download(CutoutModelDefinition model, string root, IProgress<string> progress, CancellationToken token, string sourceKind = ModelDownloadSource.Official, HttpClient? httpClient = null)
    {
        using var lease = await ModelInstallCache.Lock(root, model.Id + "-", token);
        var existing = await ModelInstallCache.Find(root, model.Id + "-",
            [new(model.Id + ".onnx", model.Hash, model.MaximumBytes)], progress, token);
        if (existing is not null) return Path.Combine(existing, model.Id + ".onnx");
        Directory.CreateDirectory(root);
        var temporary = Path.Combine(root, "download-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
            using var ownedClient = httpClient is null ? new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) } : null;
            var client = httpClient ?? ownedClient!;
            progress.Report(L.Format("ModelDownloadConnectingSource", new Uri(ModelDownloadSource.Resolve(model.Source, sourceKind)).Host));
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await ModelDownloadTransfer.CopyTo(client, model.Source, sourceKind, output, model.MaximumBytes,
                    total => progress.Report(L.Format("Downloading01F1MB", model.Id, total / 1048576d)), token);
            progress.Report(L.Get("VerifyingFilesAndONNXContractLargeModels"));
            return await Task.Run(() => InstallFile(model, temporary, root, token), token);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static string Install(byte[] data, string root, CancellationToken token) =>
        InstallLocal(data, root, token).GetAwaiter().GetResult();

    private static async Task<string> InstallLocal(byte[] data, string root, CancellationToken token)
    {
        if (!Convert.ToHexString(SHA256.HashData(data)).Equals(Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(L.Get("BackgroundRemovalModelSHA256VerificationFailedExisting"));
        using var lease = await ModelInstallCache.Lock(root, "u2netp-", token).ConfigureAwait(false);
        var existing = await ModelInstallCache.Find(root, "u2netp-",
            [new("u2netp.onnx", Sha256, CutoutModels.All[0].MaximumBytes)], null, token).ConfigureAwait(false);
        if (existing is not null) return Path.Combine(existing, "u2netp.onnx");
        Directory.CreateDirectory(root);
        var temporary = Path.Combine(root, Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllBytes(temporary, data); return InstallFile(CutoutModels.All[0], temporary, root, token); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string InstallFile(CutoutModelDefinition model, string temporary, string root, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using (var stream = File.OpenRead(temporary))
        {
            if (stream.Length > model.MaximumBytes) throw new InvalidDataException(L.Get("ModelExceedsItsExpectedSize"));
            using HashAlgorithm hash = model.Hash.Length == 64 ? SHA256.Create() : MD5.Create();
            if (!Convert.ToHexString(hash.ComputeHash(stream)).Equals(model.Hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(L.Get("ModelVerificationFailedExistingConfigurationWasPreserved"));
        }
        token.ThrowIfCancellationRequested();
        using (var options = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1, GraphOptimizationLevel = GraphOptimizationLevel.ORT_DISABLE_ALL })
        using (var session = new InferenceSession(temporary, options))
        {
            var input = session.InputMetadata.Values.First(); var output = session.OutputMetadata.Values.First();
            bool Matches(int[] dimensions, int channels) => dimensions.Length == 4 &&
                (dimensions[0] == 1 || dimensions[0] == -1) && dimensions[1] == channels &&
                dimensions.Skip(2).All(x => x == -1 || x == model.InputSize);
            if (session.InputMetadata.Count != 1 || input.ElementType != typeof(float) || !Matches(input.Dimensions, 3) ||
                output.ElementType != typeof(float) || !Matches(output.Dimensions, 1))
                throw new InvalidDataException(L.Get("BackgroundRemovalModelInputsOutputsDoNot"));
        }
        token.ThrowIfCancellationRequested();
        var directory = Path.Combine(root, model.Id + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, model.Id + ".onnx");
        var provenance = Path.Combine(directory, "source.txt");
        IReadOnlyList<string> licenses = Array.Empty<string>();
        try
        {
            var licenseNames = model.Id switch
            {
                "u2netp" => new[] { "U-2-Net-LICENSE.txt", "rembg-LICENSE.txt" },
                "birefnet-matting" => new[] { "BiRefNet-LICENSE.txt", "BiRefNet-Matting-Export-MODEL-CARD.md" },
                "birefnet-hr-matting" => new[] { "BiRefNet-LICENSE.txt", "BiRefNet-HR-Export-LICENSE.txt", "BiRefNet-HR-Export-MODEL-CARD.md" },
                _ => new[] { "BiRefNet-LICENSE.txt", "rembg-LICENSE.txt" }
            };
            File.WriteAllText(provenance, $"Source: {model.Source}\nChecksum: {model.Hash}\nProfile: {model.Profile}; input: {model.InputSize}\nLicense: {(model.Profile == "U2Net" ? "Apache-2.0" : "MIT")}\nLicense files: {string.Join(", ", licenseNames)}\n");
            licenses = ThirdPartyLicenseService.CopyTo(directory, licenseNames);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path);
            return path;
        }
        catch
        {
            if (File.Exists(provenance)) File.Delete(provenance);
            foreach (var license in licenses) if (File.Exists(license)) File.Delete(license);
            Directory.Delete(directory, false);
            throw;
        }
    }
}
