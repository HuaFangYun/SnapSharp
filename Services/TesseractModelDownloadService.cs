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
    public async Task<string> Download(string root, string languages, IProgress<string> progress, CancellationToken token, string sourceKind = ModelDownloadSource.Official)
    {
        var models = Package(languages);
        var payload = new Dictionary<string, byte[]>();
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromMinutes(5) };
        foreach (var model in models)
        {
            progress.Report(L.Format("ModelDownloadConnectingSource", new Uri(ModelDownloadSource.Resolve(model.Url, sourceKind)).Host));
            using var response = await ModelDownloadSource.Get(client, model.Url, sourceKind, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long size && size != model.Size) throw new InvalidDataException(L.Get("LanguageDataDownloadSizeMismatch"));
            using var data = new MemoryStream(model.Size);
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            var buffer = new byte[81920]; int read;
            while ((read = await stream.ReadAsync(buffer, token)) != 0)
            {
                if (data.Length + read > model.Size) throw new InvalidDataException(L.Get("LanguageDataExceedsItsExpectedSize"));
                await data.WriteAsync(buffer.AsMemory(0, read), token);
                progress.Report(L.Format("0Download12F13F1", model.Language, data.Length * 100 / model.Size, data.Length / 1048576d, model.Size / 1048576d));
            }
            var bytes = data.ToArray(); Validate(model, bytes); payload.Add(model.Language, bytes);
        }
        progress.Report(L.Get("SHA256VerifiedCheckingThatTesseractCanLoad"));
        return await Task.Run(() => Install(payload, root, languages, token), token);
    }
    internal static string Install(IReadOnlyDictionary<string, byte[]> payload, string root, string languages, CancellationToken token)
    {
        var models = Package(languages);
        foreach (var model in models) Validate(model, payload[model.Language]);
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(root);
        var directory = Path.Combine(root, "tesseract-fast-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var sourceFile = Path.Combine(directory, "source.txt");
        var files = models.Select(x => Path.Combine(directory, x.Language + ".traineddata"))
            .Append(sourceFile).Append(Path.Combine(directory, "Apache-2.0.txt")).ToArray();
        try
        {
            foreach (var model in models)
            {
                token.ThrowIfCancellationRequested();
                File.WriteAllBytes(Path.Combine(directory, model.Language + ".traineddata"), payload[model.Language]);
            }
            File.WriteAllText(sourceFile, "Tesseract tessdata_fast / Apache-2.0\nRevision: " + Revision + "\n" + string.Join("\n", models.Select(x => x.Url + "\nSHA256: " + x.Sha256)));
            ThirdPartyLicenseService.CopyTo(directory, "Apache-2.0.txt");
            LocalRecognitionService.CheckTesseract(directory, languages);
            token.ThrowIfCancellationRequested();
            return directory;
        }
        catch
        {
            // This attempt owns only these explicit files in a freshly created directory.
            foreach (var file in files) if (File.Exists(file)) File.Delete(file);
            Directory.Delete(directory, false);
            throw;
        }
    }
}
