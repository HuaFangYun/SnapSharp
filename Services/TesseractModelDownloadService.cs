using L = SnapSharp.Localization.UiText;
using System.Net.Http;
using System.Security.Cryptography;

namespace SnapSharp.Services;

internal sealed class TesseractModelDownloadService
{
    internal const string Revision = "87416418657359cb625c412a48b6e1d6d41c29bd";
    internal sealed record Model(string Language, int Size, string Sha256)
    {
        public string Url => $"https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/{Revision}/{Language}.traineddata";
    }
    internal static readonly Model[] Models =
    [
        new("eng", 4113088, "7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2"),
        new("chi_sim", 2469156, "a5fcb6f0db1e1d6d8522f39db4e848f05984669172e584e8d76b6b3141e1f730")
    ];
    internal static Model[] Package(string languages)
    {
        if (languages is not ("eng" or "chi_sim" or "chi_sim+eng")) throw new ArgumentException(L.Get("BuiltInDownloadsSupportEnglishSimplifiedChinese"));
        return languages.Split('+').Select(x => Models.Single(m => m.Language == x)).ToArray();
    }
    internal static void Validate(Model model, byte[] data)
    {
        if (data.Length != model.Size || !Convert.ToHexString(SHA256.HashData(data)).Equals(model.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(model.Language + L.Get("LanguageDataSHA256SizeVerificationFailedConfiguration"));
    }
    internal static ModelInstallCache.Asset[] CacheAssets(string languages) => Package(languages)
        .Select(model => new ModelInstallCache.Asset(model.Language + ".traineddata", model.Sha256, model.Size, model.Size)).ToArray();

    public async Task<string> Download(string root, string languages, IProgress<string> progress, CancellationToken token, string sourceKind = ModelDownloadSource.Official, HttpClient? httpClient = null)
    {
        var models = Package(languages);
        using var lease = await ModelInstallCache.Lock(root, "tesseract-fast-", token);
        var existing = await ModelInstallCache.Find(root, "tesseract-fast-", CacheAssets(languages), progress, token);
        if (existing is not null) return existing;
        var partial = await FindExtendable(root, languages, progress, token);
        var payload = new Dictionary<string, byte[]>();
        using var ownedClient = httpClient is null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromMinutes(5) } : null;
        var client = httpClient ?? ownedClient!;
        foreach (var model in models)
        {
            var cachedDirectory = partial is not null && File.Exists(Path.Combine(partial, model.Language + ".traineddata"))
                ? partial : await ModelInstallCache.Find(root, "tesseract-fast-", CacheAssets(model.Language), progress, token);
            if (cachedDirectory is not null)
            {
                var cached = await File.ReadAllBytesAsync(Path.Combine(cachedDirectory, model.Language + ".traineddata"), token);
                Validate(model, cached);
                payload.Add(model.Language, cached);
                continue;
            }
            progress.Report(L.Format("ModelDownloadConnectingSource", new Uri(ModelDownloadSource.Resolve(model.Url, sourceKind)).Host));
            using var data = new MemoryStream(model.Size);
            await ModelDownloadTransfer.CopyTo(client, model.Url, sourceKind, data, model.Size,
                total => progress.Report(L.Format("0Download12F13F1", model.Language, total * 100 / model.Size, total / 1048576d, model.Size / 1048576d)),
                token, expectedBytes: model.Size);
            var bytes = data.ToArray(); Validate(model, bytes); payload.Add(model.Language, bytes);
        }
        progress.Report(L.Get("SHA256VerifiedCheckingThatTesseractCanLoad"));
        return await Task.Run(() => InstallCore(payload, root, languages, token, partial), token);
    }
    // Keep all languages for the pinned version together, including local imports.
    private static async Task<string?> FindExtendable(string root, string languages, IProgress<string>? progress, CancellationToken token)
    {
        foreach (var model in Models)
        {
            var candidate = await ModelInstallCache.Find(root, "tesseract-fast-", CacheAssets(model.Language), progress, token).ConfigureAwait(false);
            if (candidate is null) continue;
            var safeToExtend = true;
            foreach (var asset in CacheAssets(languages))
            {
                var path = Path.Combine(candidate, asset.Name);
                if (File.Exists(path) && !await ModelInstallCache.Matches(path, asset, token).ConfigureAwait(false)) { safeToExtend = false; break; }
            }
            if (safeToExtend) return candidate;
        }
        return null;
    }

    internal static string Install(IReadOnlyDictionary<string, byte[]> payload, string root, string languages, CancellationToken token) =>
        InstallLocal(payload, root, languages, token).GetAwaiter().GetResult();

    private static async Task<string> InstallLocal(IReadOnlyDictionary<string, byte[]> payload, string root, string languages, CancellationToken token)
    {
        foreach (var model in Package(languages)) Validate(model, payload[model.Language]);
        using var lease = await ModelInstallCache.Lock(root, "tesseract-fast-", token).ConfigureAwait(false);
        var existing = await ModelInstallCache.Find(root, "tesseract-fast-", CacheAssets(languages), null, token).ConfigureAwait(false);
        if (existing is not null) return existing;
        var partial = await FindExtendable(root, languages, null, token).ConfigureAwait(false);
        return InstallCore(payload, root, languages, token, partial);
    }

    private static string InstallCore(IReadOnlyDictionary<string, byte[]> payload, string root, string languages, CancellationToken token, string? existingDirectory = null)
    {
        var models = Package(languages);
        foreach (var model in models) Validate(model, payload[model.Language]);
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(root);
        var directory = existingDirectory ?? Path.Combine(root, "tesseract-fast-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var sourceFile = Path.Combine(directory, "source.txt");
        var temporarySource = sourceFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var files = new List<string>();
        try
        {
            foreach (var model in models)
            {
                token.ThrowIfCancellationRequested();
                var path = Path.Combine(directory, model.Language + ".traineddata");
                if (File.Exists(path)) { Validate(model, File.ReadAllBytes(path)); continue; }
                using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    files.Add(path);
                    output.Write(payload[model.Language]);
                }
            }
            if (!File.Exists(Path.Combine(directory, "Apache-2.0.txt")))
                files.AddRange(ThirdPartyLicenseService.CopyTo(directory, "Apache-2.0.txt"));
            LocalRecognitionService.CheckTesseract(directory, languages);
            token.ThrowIfCancellationRequested();
            File.WriteAllText(temporarySource, "Tesseract tessdata_fast / Apache-2.0\nRevision: " + Revision + "\n" +
                string.Join("\n", Models.Where(x => File.Exists(Path.Combine(directory, x.Language + ".traineddata")))
                    .Select(x => x.Url + "\nSHA256: " + x.Sha256)));
            File.Move(temporarySource, sourceFile, true);
            return directory;
        }
        catch
        {
            // Roll back only files added by this attempt; keep all pre-existing language data.
            foreach (var file in files) if (File.Exists(file)) File.Delete(file);
            if (File.Exists(temporarySource)) File.Delete(temporarySource);
            if (existingDirectory is null) Directory.Delete(directory, false);
            throw;
        }
        finally { if (File.Exists(temporarySource)) File.Delete(temporarySource); }
    }
}
