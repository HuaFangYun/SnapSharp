using L = SnapSharp.Localization.UiText;
using Microsoft.ML.OnnxRuntime;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace SnapSharp.Services;

/// <summary>Downloads data only: the pinned wheel is a ZIP, never installed or executed.</summary>
internal sealed class OcrModelDownloadService
{
    internal const string Source = "https://files.pythonhosted.org/packages/54/08/6b3c1762c9800faa4346d6141b88af9f94f3680c9d83774e700b1b75c256/rapidocr_onnxruntime-1.3.24-py3-none-any.whl";
    internal const string Sha256 = "4282ff0b8db05ad2a53afc8d0ef2e7d879c53022fc61a4f8e84c58d737822cd2";
    internal const int PackageSize = 14910742;
    internal sealed record ModelPaths(string Detection, string Recognition, string Dictionary);

    public async Task<ModelPaths> Download(string root, IProgress<string> progress, CancellationToken token)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        using var response = await client.GetAsync(Source, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length != PackageSize) throw new InvalidDataException(L.Get("ModelPackageSizeMismatch"));
        using var data = new MemoryStream(PackageSize);
        await using (var stream = await response.Content.ReadAsStreamAsync(token))
        {
            var buffer = new byte[81920]; int read;
            while ((read = await stream.ReadAsync(buffer, token)) != 0)
            {
                if (data.Length + read > PackageSize) throw new InvalidDataException(L.Get("ModelPackageExceedsItsExpectedSize"));
                await data.WriteAsync(buffer.AsMemory(0, read), token);
                progress.Report(L.Format("Downloading01F12F1MB", data.Length * 100 / PackageSize, data.Length / 1048576d, PackageSize / 1048576d));
            }
        }
        ValidatePackage(data.ToArray());
        progress.Report(L.Get("SHA256VerifiedCheckingModelsAndExtractingThe"));
        return await Task.Run(() => Install(data, root, token), token);
    }

    internal static void ValidatePackage(byte[] data)
    {
        if (data.Length != PackageSize || !Convert.ToHexString(SHA256.HashData(data)).Equals(Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(L.Get("ModelPackageSHA256VerificationFailedNoConfiguration"));
    }

    private static ModelPaths Install(MemoryStream data, string root, CancellationToken token)
    {
        Directory.CreateDirectory(root);
        var directory = Path.Combine(root, "ppocr-v4-zh-en-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var paths = new ModelPaths(Path.Combine(directory, "detection.onnx"), Path.Combine(directory, "recognition.onnx"), Path.Combine(directory, "dictionary.txt"));
        var files = new[] { paths.Detection, paths.Recognition, paths.Dictionary, Path.Combine(directory, "source.txt"), Path.Combine(directory, "Apache-2.0.txt") };
        try
        {
            data.Position = 0;
            using var zip = new ZipArchive(data, ZipArchiveMode.Read, true);
            void Extract(string name, string destination)
            {
                token.ThrowIfCancellationRequested();
                var entry = zip.GetEntry("rapidocr_onnxruntime/models/" + name) ?? throw new InvalidDataException(L.Get("ModelPackageIsMissing") + name);
                if (entry.Length is < 1000 or > 20_000_000) throw new InvalidDataException(L.Get("InvalidModelFileSize"));
                using var source = entry.Open(); using var target = new FileStream(destination, FileMode.CreateNew); source.CopyTo(target);
            }
            Extract("ch_PP-OCRv4_det_infer.onnx", paths.Detection);
            Extract("ch_PP-OCRv4_rec_infer.onnx", paths.Recognition);
            using var options = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1 };
            using var detector = new InferenceSession(paths.Detection, options);
            using var recognizer = new InferenceSession(paths.Recognition, options);
            if (!recognizer.ModelMetadata.CustomMetadataMap.TryGetValue("character", out var characters)) throw new InvalidDataException(L.Get("RecognitionModelIsMissingItsEmbeddedDictionary"));
            var labels = characters.Split('\n').Select(x => x.TrimEnd('\r')).Where(x => x.Length > 0).ToArray();
            var shape = recognizer.OutputMetadata.First().Value.Dimensions;
            if (labels.Length < 100 || !labels.Contains("中") || !labels.Contains("A") || shape.Length != 3 || shape[2] != labels.Length + 1 && shape[2] != labels.Length + 2)
                throw new InvalidDataException(L.Get("ChineseEnglishDictionaryDoesNotMatchThe"));
            File.WriteAllLines(paths.Dictionary, labels, new UTF8Encoding(false));
            File.WriteAllText(files[3], $"RapidOCR 1.3.24 / PP-OCRv4 Chinese + English\nSource: {Source}\nSHA256: {Sha256}\nUpstream: https://github.com/RapidAI/RapidOCR/tree/v1.3.24\nLicense: Apache-2.0; see upstream distribution.\nDictionary extracted from recognition model metadata.\n");
            ThirdPartyLicenseService.CopyTo(directory, "Apache-2.0.txt");
            token.ThrowIfCancellationRequested();
            return paths;
        }
        catch
        {
            // Only the explicitly created files in our unique directory are removed.
            foreach (var file in files) if (File.Exists(file)) File.Delete(file);
            Directory.Delete(directory, false);
            throw;
        }
    }
}
