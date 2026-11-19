using L = NimCap.Screenshot.Localization.UiText;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace NimCap.Screenshot.Services;

internal static class LocalTranslationSetupService
{
    internal const string Version = "b12470";
    internal const string ModelRevision = "91cad51170dc346986eccefdc2dd33a9da36ead9";
    internal const string ModelName = "qwen2.5-1.5b-instruct-q4_k_m.gguf";
    internal const string ModelHash = "6a1a2eb6d15622bf3c96857206351ba97e1af16c30d7a74ee38970e434e9407e";
    internal const long ModelSize = 1117320736;
    internal const ulong Q5MinimumVideoMemory = 24UL * 1024 * 1024 * 1024;
    internal sealed record Model(string Id, string DisplayName, string QualityKey, string Repository, string Revision, string FileName,
        long Size, string Hash, ulong MinimumCpuMemory, ulong MinimumGpuMemory)
    {
        public string SizeLabel => $"{Size / (1024d * 1024 * 1024):0.##} GiB";
        public string Label => $"{DisplayName} · {(Id.EndsWith("q5", StringComparison.Ordinal) ? "Q5_K_M" : "Q4_K_M")} · {SizeLabel} · {L.Get(QualityKey)}";
        public string Url => $"https://huggingface.co/{Repository}/resolve/{Revision}/{FileName}";
        public override string ToString() => Label;
    }
    internal static readonly IReadOnlyList<Model> Models =
    [
        new("qwen35-4b", "Qwen3.5 4B", "TranslationQualityBalanced", "bartowski/Qwen_Qwen3.5-4B-GGUF", "4168f45a16a1290d65a4ec0fa312ae917a4c15d6", "Qwen_Qwen3.5-4B-Q4_K_M.gguf", 3013027808, "13c16f426047e2de38cd075bdade4a7bcbc8c774384876f677740cda65f8a983", 6UL << 30, 4UL << 30),
        new("qwen35-2b", "Qwen3.5 2B", "TranslationQualityBasic", "bartowski/Qwen_Qwen3.5-2B-GGUF", "7d26695454df6de5fbcce2e58681e62dae06ce43", "Qwen_Qwen3.5-2B-Q4_K_M.gguf", 1396198496, "57a1085840f497d764a7fc5d346922dbde961efb54cc792ea81d694fd846a1d8", 0, 0),
        new("qwen35-9b", "Qwen3.5 9B", "TranslationQualityHigh", "bartowski/Qwen_Qwen3.5-9B-GGUF", "182be2fd6c7bc44887d88a91cb03ff009cc9f549", "Qwen_Qwen3.5-9B-Q4_K_M.gguf", 6169341984, "d784ce9eda1a5a7b51e8f705a9e6310844bf4f173654d115823c775fdea56d43", 12UL << 30, 8UL << 30),
        new("qwen35-27b-q4", "Qwen3.5 27B", "TranslationQualityHighest", "bartowski/Qwen_Qwen3.5-27B-GGUF", "d7b113c40283f4d99f4eb0ec20d126ad653cc736", "Qwen_Qwen3.5-27B-Q4_K_M.gguf", 17984872928, "81657841d62f1821c748d0fea6c260b7d3508844fe4e9250253ef81c4e4d9edf", 24UL << 30, 20UL << 30),
        new("qwen35-27b-q5", "Qwen3.5 27B", "TranslationQualityHighestEnhanced", "bartowski/Qwen_Qwen3.5-27B-GGUF", "d7b113c40283f4d99f4eb0ec20d126ad653cc736", "Qwen_Qwen3.5-27B-Q5_K_M.gguf", 20965122528, "960ba023bdb406e3531f937bea2c4f68507e80df7ec18b9aa459470c98a1abdd", ulong.MaxValue, Q5MinimumVideoMemory)
    ];
    internal static IReadOnlyList<Model> AvailableModels(string device, ulong dedicatedVideoMemory, ulong availableMemory) =>
        Models.Where(model => device == "Vulkan" ? dedicatedVideoMemory >= model.MinimumGpuMemory : availableMemory >= model.MinimumCpuMemory).ToArray();
    internal static ulong DetectAvailableMemory() => (ulong)Math.Max(0, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
    internal static ulong DetectDedicatedVideoMemory()
    {
        try
        {
            using var factory = Vortice.DXGI.DXGI.CreateDXGIFactory1<Vortice.DXGI.IDXGIFactory1>();
            ulong maximum = 0;
            for (uint index = 0; factory.EnumAdapters1(index, out var adapter).Success; index++)
            using (adapter) maximum = Math.Max(maximum, (ulong)adapter.Description1.DedicatedVideoMemory);
            return maximum;
        }
        catch { return 0; }
    }
    internal sealed record Installation(string Runtime, string Model);
    internal sealed class InlineProgress(Action<string> report) : IProgress<string>
    { public void Report(string value) => report(value); }
    private static readonly SemaphoreSlim Gate = new(1);

    public static async Task<Installation> Install(string root, string device, Model model, IProgress<string> progress, CancellationToken token, IProgress<double?>? percentage = null, string sourceKind = ModelDownloadSource.Official)
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
            var hash = gpu ? "a919533a508275949fcc72854c0055c8c6cb5a3c09cd6604ba0b489206a4ad09" : "fc83371644b501647c253ca1e42277795eca7ed15851a218b20294275b45d67f";
            var zipPath = Path.Combine(root, package);
            await ResumableModelDownload.Download($"https://github.com/ggml-org/llama.cpp/releases/download/{Version}/{package}", zipPath,
                hash, gpu ? 38354227 : 25272394, progress, token, percentage, sourceKind: sourceKind);
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
            var modelPath = Path.Combine(root, model.FileName);
            await ResumableModelDownload.Download(model.Url, modelPath, model.Hash, model.Size, progress, token, percentage, sourceKind: sourceKind);
            ThirdPartyLicenseService.CopyTo(root, "Apache-2.0.txt");
            progress.Report(L.Format("LocalTranslationConfigured", device));
            return new(server, modelPath);
        }
        finally { Gate.Release(); }
    }

    internal static async Task<bool> VerifyModel(string path, CancellationToken token)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) return false;
        var name = Path.GetFileName(path);
        var model = Models.FirstOrDefault(x => string.Equals(x.FileName, name, StringComparison.OrdinalIgnoreCase));
        var size = model?.Size ?? (string.Equals(name, ModelName, StringComparison.OrdinalIgnoreCase) ? ModelSize : -1);
        var hash = model?.Hash ?? (size == ModelSize ? ModelHash : "");
        return size > 0 && new FileInfo(path).Length == size &&
            string.Equals(await FileHash(path, token), hash, StringComparison.OrdinalIgnoreCase);
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
