using L = SnapSharp.Localization.UiText;
using Microsoft.ML.OnnxRuntime;
using System.Net.Http;
using System.Security.Cryptography;

namespace SnapSharp.Services;

/// <summary>Data-only downloads. Fixed sizes and SHA256; never installs or executes Python.</summary>
internal static class SpecialistModelDownloadService
{
    internal sealed record Asset(string Name, string Url, long Size, string Hash);
    internal static readonly Asset Table = new("table.onnx",
        "https://www.modelscope.cn/models/RapidAI/RapidTable/resolve/v2.0.0/ch_ppstructure_mobile_v2_SLANet.onnx", 7790807,
        "ddfc6c97ee4db2a5e9de4de8b6a14508a39d42d228503219fdfebfac364885e3");
    internal static readonly Asset[] Formula =
    [
        new("encoder.onnx", "https://api.github.com/repos/RapidAI/RapidLaTeXOCR/releases/assets/139457232", 89008136,
            "01bf5dc25539ca0cd5b1bd29296ea495977a6ba5f629dc4178277809d26e5e7d"),
        new("decoder.onnx", "https://api.github.com/repos/RapidAI/RapidLaTeXOCR/releases/assets/139457211", 50952726,
            "bd695497bf1b22279b7626f5916c79226e1e244c84355f8da7edfd2d921d0072"),
        new("image_resizer.onnx", "https://api.github.com/repos/RapidAI/RapidLaTeXOCR/releases/assets/139457248", 38967751,
            "e0b075c39700f64d50400f39c8fc186bbb3b5d84d31864008313f376603aca9d"),
        new("tokenizer.json", "https://api.github.com/repos/RapidAI/RapidLaTeXOCR/releases/assets/139457265", 24174,
            "1dc27b18d6a518d0d5ff3f4bb7bd98521fe80ad39e5b2a246d4109f1bb9d5019")
    ];

    internal static async Task Validate(string path, Asset asset, CancellationToken token)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != asset.Size) throw new InvalidDataException(asset.Name + L.Get("SizeVerificationFailed"));
        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
        if (!hash.Equals(asset.Hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(asset.Name + L.Get("SHA256VerificationFailedConfigurationWasPreserved"));
    }

    internal static async Task<string> Install(string root, bool table, IProgress<string> progress, CancellationToken token, string? localSource = null, string sourceKind = ModelDownloadSource.Official, HttpClient? httpClient = null)
    {
        var prefix = table ? "slanet-zh-" : "latex-ocr-";
        var assets = table ? new[] { Table } : Formula;
        using var lease = await ModelInstallCache.Lock(root, prefix, token);
        var existing = await ModelInstallCache.Find(root, prefix,
            assets.Select(asset => new ModelInstallCache.Asset(asset.Name, asset.Hash, asset.Size, asset.Size)).ToArray(), progress, token);
        if (existing is not null) return table ? Path.Combine(existing, Table.Name) : existing;
        Directory.CreateDirectory(root);
        var directory = Path.Combine(root, prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var ownedClient = httpClient is null ? new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(20) } : null;
        var client = httpClient ?? ownedClient!;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SnapSharp/1.0");
        var created = new List<string>();
        try
        {
            foreach (var asset in assets)
            {
                var target = Path.Combine(directory, asset.Name); created.Add(target);
                if (localSource is not null)
                {
                    var source = Path.Combine(localSource, asset.Name);
                    await Validate(source, asset, token);
                    File.Copy(source, target, false);
                }
                else
                {
                    progress.Report(L.Format("ModelDownloadConnectingSource", new Uri(ModelDownloadSource.Resolve(asset.Url, sourceKind)).Host));
                    using var response = await ModelDownloadSource.Get(client, asset.Url, sourceKind, token);
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength is long length && length != asset.Size) throw new InvalidDataException(asset.Name + L.Get("DownloadLengthMismatch"));
                    await using var input = await response.Content.ReadAsStreamAsync(token);
                    await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        var buffer = new byte[81920]; long total = 0; int read;
                        while ((read = await input.ReadAsync(buffer, token)) > 0)
                        {
                            total += read;
                            if (total > asset.Size) throw new InvalidDataException(L.Get("DownloadExceedsThePinnedSize"));
                            await output.WriteAsync(buffer.AsMemory(0, read), token);
                            progress.Report($"{asset.Name} · {total * 100 / asset.Size}% · {total / 1048576d:F1} / {asset.Size / 1048576d:F1} MB");
                        }
                    }
                }
                await Validate(target, asset, token);
                if (asset.Name.EndsWith(".onnx", StringComparison.Ordinal))
                    await Task.Run(() =>
                    {
                        using var options = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1 };
                        using var session = new InferenceSession(target, options);
                        if (session.InputMetadata.Count == 0 || session.OutputMetadata.Count == 0) throw new InvalidDataException(L.Get("TheModelHasNoInputsOrOutputs"));
                    }, token);
            }
            var manifest = Path.Combine(directory, "source.txt"); created.Add(manifest);
            await File.WriteAllTextAsync(manifest,
                (table ? "PP-Structure / SLANet, RapidTable v2.0.0. Apache-2.0. https://github.com/RapidAI/RapidTable\n" :
                    "LaTeX-OCR weights, RapidLaTeXOCR v0.0.0. https://github.com/RapidAI/RapidLaTeXOCR\nUpstream weights: https://github.com/lukas-blecher/LaTeX-OCR (MIT).\n") +
                string.Join("\n", assets.Select(x => $"{x.Name}\nSource: {x.Url}\nSize: {x.Size}\nSHA256: {x.Hash}")), token);
            created.AddRange(ThirdPartyLicenseService.CopyTo(directory, table
                ? new[] { "Apache-2.0.txt" }
                : new[] { "LaTeX-OCR-LICENSE.txt", "RapidLaTeXOCR-LICENSE.txt" }));
            token.ThrowIfCancellationRequested();
            return table ? Path.Combine(directory, Table.Name) : directory;
        }
        catch
        {
            // Only our uniquely created install and its explicit files; never user-selected models.
            foreach (var file in created) if (File.Exists(file)) File.Delete(file);
            Directory.Delete(directory, false);
            throw;
        }
    }
}
