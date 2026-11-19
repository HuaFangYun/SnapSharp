using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using SkiaSharp;
using NimCap.Screenshot.Models;
using NimCap.Screenshot.Views;

namespace NimCap.Screenshot.Services;

internal static class OutputSelfTest
{
    public static bool Run(BitmapSource source, string testDirectory)
    {
        var folder = Path.Combine(testDirectory, "输出测试 with spaces");
        var time = new DateTime(2026, 11, 15, 9, 8, 7, 123);
        if (OutputFileService.ExpandTemplate("{date}_{time}_{width}x{height}_{seq}", time, 96, 64, 2) != "2026-11-15_090807-123_96x64_0002") return false;
        if (OutputFileService.ExpandTemplate("a:b/c*?", time, 1, 1, 1) != "a_b_c__") return false;
        if (OutputFileService.ExpandTemplate("CON", time, 1, 1, 1) != "_CON") return false;
        foreach (var template in new[] { "", " ", "..", "{bad}", "{date", "date}", "{{date}}" })
        {
            try { OutputFileService.ExpandTemplate(template, time, 1, 1, 1); return false; }
            catch (ArgumentException) { }
        }
        var settings = new AppSettings { SaveDirectory = folder, ArchiveByDate = true };
        if (OutputFileService.AutomaticDirectory(settings, time) != Path.Combine(folder, "2026-11-15")) return false;
        settings.ArchiveByDate = false;
        if (OutputFileService.AutomaticDirectory(settings, time) != folder) return false;
        var names = Enumerable.Range(0, 100).Select(_ => OutputFileService.NewName(OutputFileService.DefaultTemplate, 96, 64)).ToArray();
        if (names.Distinct().Count() != names.Length) return false;
        var first = OutputFileService.WriteUnique(folder, "同名", ".png", [1, 2, 3]);
        var second = OutputFileService.WriteUnique(folder, "同名", ".png", [4, 5]);
        if (first == second || !File.ReadAllBytes(first).SequenceEqual(new byte[] { 1, 2, 3 })) return false;
        try { OutputFileService.Write(first, [9]); return false; } catch (IOException) { }
        if (!File.ReadAllBytes(first).SequenceEqual(new byte[] { 1, 2, 3 })) return false;
        OutputFileService.Write(first, [9], true);
        if (!File.ReadAllBytes(first).SequenceEqual(new byte[] { 9 })) return false;
        var writes = Enumerable.Range(0, 8).Select(i => Task.Run(() => OutputFileService.WriteUnique(folder, "并发", ".png", [(byte)i]))).ToArray();
        Task.WaitAll(writes);
        if (writes.Select(x => x.Result).Distinct().Count() != 8 || Directory.GetFiles(folder, "*.tmp").Length != 0) return false;

        var images = new SkiaImageService();
        using var bitmap = new SKBitmap(new SKImageInfo(96, 64, SKColorType.Bgra8888, SKAlphaType.Premul));
        for (var y = 0; y < 64; y++)
        for (var x = 0; x < 96; x++) bitmap.SetPixel(x, y, new SKColor((byte)(x * 17), (byte)(y * 29), (byte)(x * y), (byte)(x < 20 ? 0 : x < 40 ? 128 : 255)));
        var transparent = images.ToBitmapSource(bitmap);
        foreach (var extension in new[] { ".tiff", ".tif", ".bmp" })
        {
            var bytes = images.Encode(transparent, extension, width: 48, height: 32);
            var decoded = images.Decode(bytes);
            if (decoded.PixelWidth != 48 || decoded.PixelHeight != 32) return false;
            if (extension != ".bmp" && (Math.Abs(decoded.DpiX - 300) > .1 || Math.Abs(decoded.DpiY - 300) > .1)) return false;
            if (extension == ".bmp" && (bytes[0] != 'B' || bytes[1] != 'M')) return false;
            if (extension != ".bmp" && !((bytes[0] == 'I' && bytes[1] == 'I') || (bytes[0] == 'M' && bytes[1] == 'M'))) return false;
            var pixel = new byte[4];
            var bgra = new FormatConvertedBitmap(decoded, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            bgra.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
            if (extension == ".bmp" ? pixel[0] != 255 || pixel[3] != 255 : pixel[3] != 0) return false;
        }
        var pdf = images.Encode(transparent, ".pdf", width: 96, height: 2000);
        var pdfText = System.Text.Encoding.ASCII.GetString(pdf);
        if (!pdfText.StartsWith("%PDF-") || !pdfText.Contains("%%EOF") ||
            System.Text.RegularExpressions.Regex.Matches(pdfText, @"/Type /Page\b").Count != 2) return false;
        if (OutputFormat.IsSupported(".avif") || !OutputFormat.IsSupported(".tif") || !OutputFormat.IsSupported(".pdf")) return false;
        try { images.Encode(transparent, ".avif"); return false; } catch (ArgumentException) { }
        foreach (var extension in new[] { ".png", ".jpg", ".webp" })
        {
            var bytes = images.Encode(transparent, extension, 75, false, 48, 32);
            using var decoded = SKBitmap.Decode(bytes);
            if (decoded.Width != 48 || decoded.Height != 32) return false;
            if (extension == ".png" && (bytes[0] != 137 || bytes[1] != 80)) return false;
            if (extension == ".jpg" && (bytes[0] != 255 || bytes[1] != 216 || decoded.GetPixel(0, 0).Red < 245)) return false;
            if (extension != ".jpg" && decoded.GetPixel(0, 0).Alpha != 0) return false;
        }
        var lossless = images.Encode(transparent, ".webp", 10, true);
        var lossy = images.Encode(transparent, ".webp", 30, false);
        // Container may include VP8X/ALPH; inspect the actual codec chunk as well.
        if (!System.Text.Encoding.ASCII.GetString(lossless).Contains("VP8L") || !System.Text.Encoding.ASCII.GetString(lossy).Contains("VP8 ")) return false;
        using var decodedLossless = SKBitmap.Decode(lossless);
        using var decodedLossy = SKBitmap.Decode(lossy);
        if (decodedLossless.GetPixel(25, 20).Alpha != 128 || decodedLossy.GetPixel(25, 20).Alpha != 128) return false;
        if (decodedLossless.GetPixel(70, 20) != bitmap.GetPixel(70, 20)) return false;
        if (images.Encode(transparent, ".jpg", 10).Length >= images.Encode(transparent, ".jpg", 100).Length) return false;
        if (images.Encode(transparent, ".webp", 10).Length >= images.Encode(transparent, ".webp", 100).Length) return false;
        foreach (var dimensions in new[] { (0, 1), (1, -1), (16385, 1), (10000, 10000) })
        {
            try { images.Encode(transparent, ".png", width: dimensions.Item1, height: dimensions.Item2); return false; }
            catch (ArgumentException) { }
        }
        try { images.Encode(source, ".gif"); return false; } catch (ArgumentException) { }
        var fit = OutputActions.FitPrintRect(96, 64, new Rect(10, 20, 600, 800));
        var landscape = OutputActions.FitPrintRect(96, 64, new Rect(20, 10, 800, 600));
        if (Math.Abs(fit.Width / fit.Height - 1.5) > .001 || fit.Width > 600 || fit.Height > 800 ||
            Math.Abs(landscape.Width / landscape.Height - 1.5) > .001 || landscape.Width > 800 || landscape.Height > 600) return false;
        return SaveImageWindow.RunLayoutSelfTest(source);
    }
}
