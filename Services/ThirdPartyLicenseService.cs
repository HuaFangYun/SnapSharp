using L = SnapSharp.Localization.UiText;
namespace SnapSharp.Services;

using System.Text.Json;

internal static class ThirdPartyLicenseService
{
    internal const string DirectoryName = "ThirdPartyLicenses";

    internal static IReadOnlyList<string> CopyTo(string targetDirectory, params string[] names)
    {
        var sourceDirectory = Path.Combine(AppContext.BaseDirectory, DirectoryName);
        var sources = names.Select(name =>
        {
            if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name)
                throw new ArgumentException(L.Get("InvalidLicenseFileName"), nameof(names));
            var source = Path.Combine(sourceDirectory, name);
            if (!File.Exists(source) || new FileInfo(source).Length == 0)
                throw new InvalidDataException(L.Get("DistributionIsMissingAThirdPartyLicense") + name);
            return (Name: name, Source: source);
        }).ToArray();

        Directory.CreateDirectory(targetDirectory);
        var copied = new List<string>();
        try
        {
            foreach (var item in sources)
            {
                var destination = Path.Combine(targetDirectory, item.Name);
                File.Copy(item.Source, destination, true);
                copied.Add(destination);
            }
            return copied;
        }
        catch
        {
            foreach (var path in copied) if (File.Exists(path)) File.Delete(path);
            throw;
        }
    }

    internal static bool ValidateDistribution(string root)
    {
        var directory = Path.Combine(root, DirectoryName);
        if (!File.Exists(Path.Combine(root, "LICENSE")) ||
            !File.Exists(Path.Combine(root, "THIRD-PARTY-NOTICES.md")) ||
            !File.Exists(Path.Combine(root, "README.md"))) return false;
        var runtimeConfigPath = Path.Combine(root, "SnapSharp.runtimeconfig.json");
        if (!File.Exists(runtimeConfigPath)) return false;
        using (var document = JsonDocument.Parse(File.ReadAllText(runtimeConfigPath)))
        {
            var options = document.RootElement.GetProperty("runtimeOptions");
            if (options.TryGetProperty("includedFrameworks", out var frameworks) &&
                frameworks.EnumerateArray().Any(item => item.GetProperty("version").GetString() != "10.0.14")) return false;
        }
        var required = new[]
        {
            "PACKAGE-NOTICES.md", "Apache-2.0.txt",
            "DotNet-Runtime-LICENSE.txt", "DotNet-Runtime-THIRD-PARTY-NOTICES.txt",
            "WindowsDesktop-Runtime-LICENSE.txt", "WindowsDesktop-Runtime-THIRD-PARTY-NOTICES.txt",
            "Microsoft-Windows-SDK-LICENSE.rtf",
            "ONNXRuntime-LICENSE.txt", "ONNXRuntime-THIRD-PARTY-NOTICES.txt",
            "DirectML-LICENSE.txt", "DirectML-THIRD-PARTY-NOTICES.txt",
            "SkiaSharp-LICENSE.txt", "SkiaSharp-Native-THIRD-PARTY-NOTICES.txt",
            "ScreenRecorderLib-LICENSE.txt", "Lucide-LICENSE.txt", "Leptonica-LICENSE.txt",
            "BiRefNet-LICENSE.txt", "BiRefNet-Matting-Export-MODEL-CARD.md",
            "BiRefNet-HR-Export-LICENSE.txt", "BiRefNet-HR-Export-MODEL-CARD.md",
            "rembg-LICENSE.txt", "LaTeX-OCR-LICENSE.txt", "llama.cpp-LICENSE.txt"
        };
        if (!required.All(name => File.Exists(Path.Combine(directory, name)) && new FileInfo(Path.Combine(directory, name)).Length > 0)) return false;
        var mapping = File.ReadAllText(Path.Combine(directory, "PACKAGE-NOTICES.md"));
        return Directory.GetFiles(directory).Select(Path.GetFileName)
            .Where(name => name != "PACKAGE-NOTICES.md")
            .All(name => mapping.Contains('`' + name + '`', StringComparison.Ordinal));
    }

    internal static IReadOnlyList<string> BackfillManagedModelLicenses() =>
        BackfillManagedModelLicenses(Path.Combine(AppPaths.Root, "models"));

    private static IReadOnlyList<string> BackfillManagedModelLicenses(string root)
    {
        if (!Directory.Exists(root)) return Array.Empty<string>();
        var failures = new List<string>();
        void Copy(string directory, params string[] names)
        {
            try { CopyTo(directory, names); }
            catch (Exception ex) { failures.Add(Path.GetFileName(directory) + ": " + ex.GetBaseException().Message); }
        }

        foreach (var directory in Directory.GetDirectories(root))
        {
            var name = Path.GetFileName(directory);
            if (name.StartsWith("ppocr-v4-zh-en-", StringComparison.Ordinal) ||
                name.StartsWith("tesseract-fast-", StringComparison.Ordinal) ||
                name.StartsWith("slanet-zh-", StringComparison.Ordinal))
                Copy(directory, "Apache-2.0.txt");
            else if (name.StartsWith("latex-ocr-", StringComparison.Ordinal))
                Copy(directory, "LaTeX-OCR-LICENSE.txt", "RapidLaTeXOCR-LICENSE.txt");
            else if (name.StartsWith("u2netp-", StringComparison.Ordinal))
                Copy(directory, "U-2-Net-LICENSE.txt", "rembg-LICENSE.txt");
            else if (name.StartsWith("birefnet-matting-", StringComparison.Ordinal))
                Copy(directory, "BiRefNet-LICENSE.txt", "BiRefNet-Matting-Export-MODEL-CARD.md");
            else if (name.StartsWith("birefnet-hr-matting-", StringComparison.Ordinal))
                Copy(directory, "BiRefNet-LICENSE.txt", "BiRefNet-HR-Export-LICENSE.txt", "BiRefNet-HR-Export-MODEL-CARD.md");
            else if (name.StartsWith("birefnet-general-lite-", StringComparison.Ordinal) ||
                     name.StartsWith("birefnet-general-", StringComparison.Ordinal) ||
                     name.StartsWith("birefnet-portrait-", StringComparison.Ordinal))
                Copy(directory, "BiRefNet-LICENSE.txt", "rembg-LICENSE.txt");
        }

        var translation = Path.Combine(root, "local-translation");
        if (Directory.Exists(translation))
        {
            if (File.Exists(Path.Combine(translation, LocalTranslationSetupService.ModelName))) Copy(translation, "Apache-2.0.txt");
            foreach (var directory in Directory.GetDirectories(translation, "llama-b12396-*")) Copy(directory, "llama.cpp-LICENSE.txt");
        }
        return failures;
    }

    internal static bool RunSelfTest()
    {
        if (!ValidateDistribution(AppContext.BaseDirectory)) return false;
        var directory = Path.Combine(Path.GetTempPath(), "SnapSharp-LicenseTest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var names = new[] { "Apache-2.0.txt", "BiRefNet-LICENSE.txt", "LaTeX-OCR-LICENSE.txt", "llama.cpp-LICENSE.txt" };
            var direct = Path.Combine(directory, "direct");
            CopyTo(direct, names);
            if (!names.All(name => new FileInfo(Path.Combine(direct, name)).Length > 0)) return false;

            string ModelDirectory(string name)
            {
                var path = Path.Combine(directory, name);
                Directory.CreateDirectory(path);
                return path;
            }
            var ocr = ModelDirectory("ppocr-v4-zh-en-test");
            var formula = ModelDirectory("latex-ocr-test");
            var u2net = ModelDirectory("u2netp-test");
            var matting = ModelDirectory("birefnet-matting-test");
            var hr = ModelDirectory("birefnet-hr-matting-test");
            var general = ModelDirectory("birefnet-general-lite-test");
            var translation = ModelDirectory("local-translation");
            File.WriteAllBytes(Path.Combine(translation, LocalTranslationSetupService.ModelName), [0]);
            var runtime = Path.Combine(translation, "llama-b12396-CPU");
            Directory.CreateDirectory(runtime);

            if (BackfillManagedModelLicenses(directory).Count != 0) return false;
            return File.Exists(Path.Combine(ocr, "Apache-2.0.txt")) &&
                File.Exists(Path.Combine(formula, "LaTeX-OCR-LICENSE.txt")) &&
                File.Exists(Path.Combine(u2net, "rembg-LICENSE.txt")) &&
                File.Exists(Path.Combine(matting, "BiRefNet-Matting-Export-MODEL-CARD.md")) &&
                File.Exists(Path.Combine(hr, "BiRefNet-HR-Export-LICENSE.txt")) &&
                File.Exists(Path.Combine(general, "BiRefNet-LICENSE.txt")) &&
                File.Exists(Path.Combine(translation, "Apache-2.0.txt")) &&
                File.Exists(Path.Combine(runtime, "llama.cpp-LICENSE.txt"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
