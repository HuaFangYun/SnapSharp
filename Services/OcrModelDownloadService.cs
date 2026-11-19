using L = NimCap.Screenshot.Localization.UiText;
using Microsoft.ML.OnnxRuntime;
using System.Net.Http;

namespace NimCap.Screenshot.Services;

internal sealed class OcrModelDownloadService
{
    private const string PaddleOcrRevision = "dab3fe35379033fdcb2d0e9572fac0b36c9a9ebf";
    internal const string DefaultPackageId = "small";

    internal sealed record ModelPaths(string Detection, string Recognition, string Dictionary);
    internal sealed record DownloadAsset(string Name, string Source, string Hash, long Size);
    internal sealed record ModelPackage(string Id, string Version, string Profile, DownloadAsset Detection,
        DownloadAsset Recognition, DownloadAsset Dictionary)
    {
        internal string DirectoryPrefix => $"ppocr-v6-{Id}-";
        internal DownloadAsset[] Assets => [Detection, Recognition, Dictionary];
        internal ModelInstallCache.Asset[] InstalledAssets => Assets
            .Select(asset => new ModelInstallCache.Asset(asset.Name, asset.Hash, asset.Size, asset.Size)).ToArray();
    }

    internal static readonly ModelPackage[] Packages =
    [
        Package("tiny", "Tiny",
            "2ba1506c0380b8f0b03dd142459aac66d4421f6c", 1_780_590, "193bab7a04fca699a6c82e6abb5b81bdb28177f0abd4062552b04908dafb19f8",
            "2612ab37152ae0a677521bae4e1e3d4fb4cf7c30", 4_462_639, "9ef676d6ed3c88256a2d92c640c44f25b0c40947e111b14b8be8f594091563e6",
            "ppocrv6_tiny_dict.txt", 27_156, "c5cbe34ef40c29c4df07ed012bf96569cb69a2d2a01a07027e9f13cb832bd9cd"),
        Package("small", "Small",
            "28fe5895c24fd108c19eb3e8479f4ab385fbfc62", 9_880_512, "d73e0058b7a8086bbd57f3d10b8bcd4ff95363f67e06e2762b5e814fe9c9410e",
            "b8f84f0b80c529de40b4fbb3544b84fa7233a513", 21_159_378, "5435fd747c9e0efe15a96d0b378d5bd157e9492ed8fd80edf08f30d02fa24634",
            "ppocrv6_dict.txt", 74_947, "b5f2bfe2bdd9448429e3e82b51c789775d9b42f2403d082b00662eb77e401c5d"),
        Package("medium", "Medium",
            "61323801669c338b7891481ec7bac61ce31b576a", 62_032_837, "eb13b44b25bb36f89528b68720af8a61d9cf381176107f465db1757b65d086e1",
            "50c7eacafc52fa7bcf4194e8cd08e46f8558504b", 76_554_979, "9c09abf0957f7968c7586464b7397b84ad2387a0497a351af40e9acc71b673ba",
            "ppocrv6_dict.txt", 74_947, "b5f2bfe2bdd9448429e3e82b51c789775d9b42f2403d082b00662eb77e401c5d")
    ];

    internal static ModelPackage DefaultPackage => FindPackage(DefaultPackageId);
    internal static string Source => DefaultPackage.Detection.Source;
    internal static ModelInstallCache.Asset[] InstalledAssets => DefaultPackage.InstalledAssets;

    private static ModelPackage Package(string id, string profile, string detectionRevision, long detectionSize,
        string detectionHash, string recognitionRevision, long recognitionSize, string recognitionHash,
        string dictionaryName, long dictionarySize, string dictionaryHash) =>
        new(id, "PP-OCRv6", profile,
            new("detection.onnx", HuggingFace(profile, "det", detectionRevision), detectionHash, detectionSize),
            new("recognition.onnx", HuggingFace(profile, "rec", recognitionRevision), recognitionHash, recognitionSize),
            new("dictionary.txt", $"https://raw.githubusercontent.com/PaddlePaddle/PaddleOCR/{PaddleOcrRevision}/ppocr/utils/dict/{dictionaryName}", dictionaryHash, dictionarySize));

    private static string HuggingFace(string profile, string kind, string revision) =>
        $"https://huggingface.co/PaddlePaddle/PP-OCRv6_{profile.ToLowerInvariant()}_{kind}_onnx/resolve/{revision}/inference.onnx";

    internal static ModelPackage FindPackage(string? id) =>
        Packages.FirstOrDefault(package => package.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? DefaultPackage;

    // Tiny avoids GPU startup overhead; Medium is intentionally GPU-only in the settings workflow.
    internal static bool SupportsDevice(string packageId, string device) => NimCap.Screenshot.Models.ModelDeviceSupport.OcrSupports(packageId, device);

    public async Task<ModelPaths> Download(string root, IProgress<string> progress, CancellationToken token,
        string sourceKind = ModelDownloadSource.Official, HttpClient? httpClient = null, string packageId = DefaultPackageId)
    {
        var package = FindPackage(packageId);
        using var lease = await ModelInstallCache.Lock(root, package.DirectoryPrefix, token);
        var existing = await ModelInstallCache.Find(root, package.DirectoryPrefix, package.InstalledAssets, progress, token);
        if (existing is not null) return Paths(existing);

        Directory.CreateDirectory(root);
        var staging = Path.Combine(root, "." + package.DirectoryPrefix + "download");
        Directory.CreateDirectory(staging);
        foreach (var asset in package.Assets)
            await ResumableModelDownload.Download(asset.Source, Path.Combine(staging, asset.Name), asset.Hash, asset.Size,
                progress, token, httpClient: httpClient, sourceKind: sourceKind);

        Validate(staging, package);
        File.WriteAllText(Path.Combine(staging, "source.txt"),
            $"RapidOCR / PaddleOCR {package.Version} {package.Profile} ONNX\n" +
            $"Detection: {package.Detection.Source}\nRecognition: {package.Recognition.Source}\nDictionary: {package.Dictionary.Source}\n" +
            "All files are pinned by size and SHA256. Models: Apache-2.0; see upstream attribution.\n");
        ThirdPartyLicenseService.CopyTo(staging, "Apache-2.0.txt");
        token.ThrowIfCancellationRequested();
        var directory = Path.Combine(root, package.DirectoryPrefix + Guid.NewGuid().ToString("N"));
        Directory.Move(staging, directory);
        return Paths(directory);
    }

    private static ModelPaths Paths(string directory) => new(Path.Combine(directory, "detection.onnx"),
        Path.Combine(directory, "recognition.onnx"), Path.Combine(directory, "dictionary.txt"));

    private static void Validate(string directory, ModelPackage package)
    {
        var paths = Paths(directory);
        var labels = File.ReadAllLines(paths.Dictionary, System.Text.Encoding.UTF8).Where(line => line.Length > 0).ToArray();
        if (labels.Length < 1_000 || labels.Length > 100_000)
            throw new InvalidDataException(L.Get("InvalidOCRDictionaryEntryCount"));
        using var options = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1 };
        using var detector = new InferenceSession(paths.Detection, options);
        using var recognizer = new InferenceSession(paths.Recognition, options);
        var detectorOutput = detector.OutputMetadata.First().Value.Dimensions;
        var recognitionOutput = recognizer.OutputMetadata.First().Value.Dimensions;
        if (detectorOutput.Length != 4 || recognitionOutput.Length != 3 ||
            recognitionOutput[2] > 0 && recognitionOutput[2] != labels.Length + 1 && recognitionOutput[2] != labels.Length + 2)
            throw new InvalidDataException($"{package.Version} {package.Profile}: " + L.Get("ChineseEnglishDictionaryDoesNotMatchThe"));
    }
}
