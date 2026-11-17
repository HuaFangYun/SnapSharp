using L = SnapSharp.Localization.UiText;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace SnapSharp.Services;

internal static class LocalTranslationSetupService
{
    internal const string Version = "b12396";
    internal const string ModelRevision = "91cad51170dc346986eccefdc2dd33a9da36ead9";
    internal const string ModelName = "qwen2.5-1.5b-instruct-q4_k_m.gguf";
    internal const string ModelHash = "6a1a2eb6d15622bf3c96857206351ba97e1af16c30d7a74ee38970e434e9407e";
    internal const long ModelSize = 1117320736;
    internal sealed record Installation(string Runtime, string Model);
    internal sealed class InlineProgress(Action<string> report) : IProgress<string>
    { public void Report(string value) => report(value); }
    private static readonly SemaphoreSlim Gate = new(1);

    public static async Task<Installation> Install(string root, string device, IProgress<string> progress, CancellationToken token, IProgress<double?>? percentage = null, string sourceKind = ModelDownloadSource.Official)
    {
        progress.Report(L.Get("LocalDownloadPreparing"));
        percentage?.Report(null);
        if (device is not ("CPU" or "Vulkan")) throw new ArgumentException(L.Get("UnknownLocalTranslationDevice2"));
        await Gate.WaitAsync(token);
        try
        {
            Directory.CreateDirectory(root);
            var gpu = device == "Vulkan";
            var package = $"llama-{Version}-bin-win-{(gpu ? "vulkan" : "cpu")}-x64.zip";
            var hash = gpu ? "6aa5037ba1ffb124952a028d24a541610a20c21bcb253391c1dbaa2d2b19ea15" : "3b0b61e7a51e988d9a2c5db5f370012b728d936b9262528b4c11f3769f6560a8";
            var zipPath = Path.Combine(root, package);
            await ResumableModelDownload.Download($"https://github.com/ggml-org/llama.cpp/releases/download/{Version}/{package}", zipPath,
                hash, gpu ? 38127547 : 25127274, progress, token, percentage, sourceKind: sourceKind);
            percentage?.Report(null);
            progress.Report(L.Get("LocalDownloadExtracting"));
            var directory = Path.Combine(root, $"llama-{Version}-{device}");
            var server = Path.Combine(directory, "llama-server.exe");
            if (!await VerifyRuntime(server, token))
            {
                // A unique install never overwrites an existing user/runtime directory.
                if (Directory.Exists(directory)) directory += "-" + Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(directory);
                using var archive = ZipFile.OpenRead(zipPath);
                var serverEntry = archive.Entries.Single(e => e.Name == "llama-server.exe");
                var prefix = serverEntry.FullName[..^serverEntry.Name.Length];
                var manifest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                long total = 0;
                foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal) && !string.IsNullOrEmpty(e.Name)))
                {
                    token.ThrowIfCancellationRequested();
                    total += entry.Length;
                    if (total > 800_000_000) throw new InvalidDataException(L.Get("ExtractedRuntimePackageExceedsTheSizeLimit"));
                    var relative = entry.FullName[prefix.Length..];
                    var target = Path.GetFullPath(Path.Combine(directory, relative));
                    if (!target.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(L.Get("RuntimePackageContainsAnInvalidPath"));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await using (var input = entry.Open())
                    await using (var output = new FileStream(target, FileMode.CreateNew))
                        await input.CopyToAsync(output, token);
                    manifest[relative] = await FileHash(target, token);
                }
                server = Path.Combine(directory, "llama-server.exe");
                await File.WriteAllTextAsync(Path.Combine(directory, "verified-files.json"), JsonSerializer.Serialize(manifest), token);
                if (!await VerifyRuntime(server, token)) throw new InvalidDataException(L.Get("LocalTranslationRuntimeIntegrityCheckFailed"));
            }
            ThirdPartyLicenseService.CopyTo(directory, "llama.cpp-LICENSE.txt");
            var model = Path.Combine(root, ModelName);
            await ResumableModelDownload.Download($"https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/{ModelRevision}/{ModelName}",
                model, ModelHash, ModelSize, progress, token, percentage, sourceKind: sourceKind);
            ThirdPartyLicenseService.CopyTo(root, "Apache-2.0.txt");
            progress.Report(L.Format("LocalTranslationConfigured", device));
            return new(server, model);
        }
        finally { Gate.Release(); }
    }

    internal static async Task<bool> VerifyRuntime(string server, CancellationToken token)
    {
        if (!Path.IsPathFullyQualified(server) || Path.GetFileName(server) != "llama-server.exe" || !File.Exists(server)) return false;
        var root = Path.GetDirectoryName(server)!;
        var manifest = Path.Combine(root, "verified-files.json");
        if (!File.Exists(manifest) || new FileInfo(manifest).Length > 100_000) return false;
        var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(manifest, token));
        if (entries is null || !entries.ContainsKey("llama-server.exe")) return false;
        foreach (var item in entries)
        {
            var file = Path.GetFullPath(Path.Combine(root, item.Key));
            if (!file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(file) || !string.Equals(await FileHash(file, token), item.Value, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    internal static async Task<string> FileHash(string path, CancellationToken token)
    {
        await using var file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, token));
    }

}
