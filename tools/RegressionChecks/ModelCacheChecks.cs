using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using NimCap.Screenshot.Models;
using NimCap.Screenshot.Services;

namespace NimCap.Screenshot.RegressionChecks;

internal static partial class Program
{
    private static async Task ModelCacheChecks(string? fixtures)
    {
        var root = Path.Combine(Root, "model-cache");
        var payload = new byte[] { 1, 3, 5, 7, 9 };
        var sha = Convert.ToHexString(SHA256.HashData(payload));
        var asset = new ModelInstallCache.Asset("model.onnx", sha, payload.Length, payload.Length);
        var progress = new Inline<string>(_ => { });
        var incomplete = Path.Combine(root, "fixture-incomplete");
        var corrupt = Path.Combine(root, "fixture-corrupt");
        var valid = Path.Combine(root, "fixture-legacy-guid");
        foreach (var path in new[] { incomplete, corrupt, valid }) Directory.CreateDirectory(path);
        File.WriteAllBytes(Path.Combine(corrupt, asset.Name), [1, 3, 5, 7, 8]);
        File.WriteAllBytes(Path.Combine(valid, asset.Name), payload);
        Require(await ModelInstallCache.Find(root, "fixture-", [asset], progress, default) == valid,
            "Cache did not skip incomplete/corrupt installations and reuse legacy directories.");
        Require(await ModelInstallCache.Find(root, "fixture-", [asset, new("missing.txt", sha, 5, 5)], progress, default) is null,
            "An incomplete model set was reused.");
        var md5 = asset with { Hash = Convert.ToHexString(MD5.HashData(payload)) };
        Require(await ModelInstallCache.Matches(Path.Combine(valid, asset.Name), md5, default), "Legacy MD5 cutout models cannot be reused.");
        using (var locked = new FileStream(Path.Combine(valid, asset.Name), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Require(!await ModelInstallCache.Matches(Path.Combine(valid, asset.Name), asset, default), "Unreadable cache entry was accepted.");
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            try { await ModelInstallCache.Find(root, "fixture-", [asset], progress, canceled.Token); throw new Exception("Canceled cache scan completed."); }
            catch (OperationCanceledException) { }
            using var held = await ModelInstallCache.Lock(root, "fixture-", default);
            try { using var ignored = await ModelInstallCache.Lock(root, "fixture-", canceled.Token); throw new Exception("Canceled lock completed."); }
            catch (OperationCanceledException) { }
        }
        using (var released = await ModelInstallCache.Lock(root, "fixture-", default)) { }

        var cutoutRoot = Path.Combine(root, "cutout");
        var cutoutDirectory = Path.Combine(cutoutRoot, "fixture-cutout-old-guid");
        Directory.CreateDirectory(cutoutDirectory);
        var cutoutPath = Path.Combine(cutoutDirectory, "fixture-cutout.onnx");
        File.WriteAllBytes(cutoutPath, payload);
        var definition = new CutoutModelDefinition("fixture-cutout", "", "U2Net", 320, "unused.onnx", sha, 5);
        var noNetwork = new Handler((_, _) => throw new Exception("Reusing a model must not make HTTP requests."));
        using var offline = new HttpClient(noNetwork);
        var cutouts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => new CutoutModelDownloadService()
            .Download(definition, cutoutRoot, progress, default, ModelDownloadSource.Domestic, offline)));
        Require(cutouts.All(path => path == cutoutPath) && Directory.GetDirectories(cutoutRoot).Length == 1 && noNetwork.Calls == 0,
            "Concurrent cutout downloads created duplicate installations or contacted the network.");
        Console.WriteLine("PASS: cache integrity, incomplete/corrupt/unreadable files, SHA256/MD5, cancellation and concurrent cutout reuse without HTTP.");
        if (fixtures is null) return;

        var realRoot = Path.Combine(root, "real-models");
        Directory.CreateDirectory(realRoot);
        void CopyFixture(string prefix, IEnumerable<string> files)
        {
            var source = Directory.GetDirectories(fixtures, prefix + "*").First();
            var target = Path.Combine(realRoot, Path.GetFileName(source));
            Directory.CreateDirectory(target);
            foreach (var name in files) File.Copy(Path.Combine(source, name), Path.Combine(target, name));
        }
        var ocrPackage = OcrModelDownloadService.DefaultPackage;
        CopyFixture(ocrPackage.DirectoryPrefix, ocrPackage.Assets.Select(x => x.Name));
        CopyFixture("tesseract-fast-", TesseractModelDownloadService.Models.Select(x => x.Language + ".traineddata"));
        CopyFixture("slanet-zh-", [SpecialistModelDownloadService.Table.Name]);
        CopyFixture("latex-ocr-", SpecialistModelDownloadService.Formula.Select(x => x.Name));
        var before = Directory.GetDirectories(realRoot).Order().ToArray();
        foreach (var source in new[] { ModelDownloadSource.Official, ModelDownloadSource.Domestic })
        {
            var ocr = await new OcrModelDownloadService().Download(realRoot, progress, default, source, offline);
            Require(File.Exists(ocr.Dictionary), "OCR cache did not return complete paths.");
            foreach (var languages in new[] { "eng", "chi_sim", "chi_sim+eng" })
                await new TesseractModelDownloadService().Download(realRoot, languages, progress, default, source, offline);
            await SpecialistModelDownloadService.Install(realRoot, true, progress, default, sourceKind: source, httpClient: offline);
            await SpecialistModelDownloadService.Install(realRoot, false, progress, default, sourceKind: source, httpClient: offline);
        }
        Require(before.SequenceEqual(Directory.GetDirectories(realRoot).Order()) && noNetwork.Calls == 0,
            "Repeating downloads or switching mirrors created duplicate model folders.");

        // Adding Chinese to an English installation must keep its directory and download only Chinese.
        var extensionRoot = Path.Combine(root, "tesseract-extension");
        var englishDirectory = Path.Combine(extensionRoot, "tesseract-fast-existing-english");
        Directory.CreateDirectory(englishDirectory);
        var tesseract = Directory.GetDirectories(realRoot, "tesseract-fast-*").Single();
        File.Copy(Path.Combine(tesseract, "eng.traineddata"), Path.Combine(englishDirectory, "eng.traineddata"));
        var chinese = File.ReadAllBytes(Path.Combine(tesseract, "chi_sim.traineddata"));
        var languageHandler = new Handler((request, _) =>
        {
            Require(request.RequestUri!.AbsolutePath.EndsWith("chi_sim.traineddata"), "English language data was downloaded again.");
            return Reply(chinese);
        });
        using (var languageClient = new HttpClient(languageHandler))
        {
            var directory = await new TesseractModelDownloadService().Download(extensionRoot, "chi_sim", progress, default, httpClient: languageClient);
            Require(directory == englishDirectory && languageHandler.Calls == 1 && Directory.GetDirectories(extensionRoot).Length == 1,
                "Adding a language created a duplicate installation.");
        }
        var rollbackRoot = Path.Combine(root, "tesseract-rollback");
        var rollbackDirectory = Path.Combine(rollbackRoot, "tesseract-fast-existing-english");
        Directory.CreateDirectory(rollbackDirectory);
        File.Copy(Path.Combine(tesseract, "eng.traineddata"), Path.Combine(rollbackDirectory, "eng.traineddata"));
        var manifest = Path.Combine(rollbackDirectory, "source.txt");
        File.WriteAllText(manifest, "original metadata");
        using (var lockedManifest = new FileStream(manifest, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var languageClient = new HttpClient(new Handler((_, _) => Reply(chinese))))
        {
            try
            {
                await new TesseractModelDownloadService().Download(rollbackRoot, "chi_sim+eng", progress, default, httpClient: languageClient);
                throw new Exception("Replacing a locked manifest should fail.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        Require(File.ReadAllText(manifest) == "original metadata" &&
            File.Exists(Path.Combine(rollbackDirectory, "eng.traineddata")) &&
            !File.Exists(Path.Combine(rollbackDirectory, "chi_sim.traineddata")) &&
            !Directory.GetFiles(rollbackDirectory, "*.tmp").Any(), "Failed language extension changed pre-existing data or kept partial files.");
        await new TesseractModelDownloadService().Download(extensionRoot, "chi_sim+eng", progress, default, httpClient: offline);
        var locallyInstalled = TesseractModelDownloadService.Install(new Dictionary<string, byte[]>
        {
            ["eng"] = File.ReadAllBytes(Path.Combine(tesseract, "eng.traineddata")), ["chi_sim"] = chinese
        }, extensionRoot, "chi_sim+eng", default);
        Require(locallyInstalled == englishDirectory && Directory.GetDirectories(extensionRoot).Length == 1,
            "Local language installation created a duplicate directory.");
        var localRoot = Path.Combine(root, "tesseract-local-extension");
        var localDirectory = Path.Combine(localRoot, "tesseract-fast-existing-english");
        Directory.CreateDirectory(localDirectory);
        File.Copy(Path.Combine(tesseract, "eng.traineddata"), Path.Combine(localDirectory, "eng.traineddata"));
        locallyInstalled = TesseractModelDownloadService.Install(new Dictionary<string, byte[]>
        {
            ["eng"] = File.ReadAllBytes(Path.Combine(tesseract, "eng.traineddata")), ["chi_sim"] = chinese
        }, localRoot, "chi_sim+eng", default);
        Require(locallyInstalled == localDirectory && Directory.GetDirectories(localRoot).Length == 1,
            "Local language extension created a duplicate directory.");
        Console.WriteLine("PASS: real OCR/table/formula/language cache, mirror switching, language subsets and adding a language without duplicate directories or downloads.");

        var packageDirectory = Directory.GetDirectories(realRoot, ocrPackage.DirectoryPrefix + "*").Single();
        var assets = ocrPackage.Assets.ToDictionary(x => ModelDownloadSource.Resolve(x.Source, ModelDownloadSource.Official),
            x => File.ReadAllBytes(Path.Combine(packageDirectory, x.Name)));
        var freshRoot = Path.Combine(root, "fresh-ocr");
        var downloads = new Handler((request, _) => Reply(assets[request.RequestUri!.AbsoluteUri]));
        using var downloadClient = new HttpClient(downloads);
        var installed = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => new OcrModelDownloadService()
            .Download(freshRoot, progress, default, httpClient: downloadClient)));
        Require(downloads.Calls == assets.Count && installed.Distinct().Count() == 1 && Directory.GetDirectories(freshRoot).Length == 1,
            "Concurrent first-time OCR downloads were not serialized through installation.");
        File.WriteAllText(installed[0].Dictionary, "corrupt dictionary");
        var repaired = await new OcrModelDownloadService().Download(freshRoot, progress, default, httpClient: downloadClient);
        Require(downloads.Calls == 2 * assets.Count && repaired.Dictionary != installed[0].Dictionary, "A corrupt dictionary was reused.");
        await new OcrModelDownloadService().Download(freshRoot, progress, default, ModelDownloadSource.Domestic, downloadClient);
        Require(downloads.Calls == 2 * assets.Count && Directory.GetDirectories(freshRoot).Length == 2,
            "A repaired OCR package was downloaded again.");
        Console.WriteLine("PASS: concurrent initial OCR downloads install once; corrupted dictionary is replaced and subsequent downloads reuse the repaired package.");
    }
}
