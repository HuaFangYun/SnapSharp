using NimCap.Screenshot.Models;
using NimCap.Screenshot.Views;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NimCap.Screenshot.Services;

internal static class WatermarkSelfTest
{
    internal static string Run(string fixtures, string output)
    {
        var log = new StringBuilder();
        var mask = new WatermarkMask(100, 60);
        mask.Rectangle(new(-10, -10), new(20, 10));
        Require(mask.Bounds() == new Int32Rect(0, 0, 20, 10), "Clipped rectangle");
        mask.Rectangle(new(0, 0), new(100, 60), erase: true);
        mask.Stroke(new(10, 30), new(90, 30), 3);
        Require(mask.Pixels[30 * 100 + 50] == 255 && mask.Pixels[10 * 100 + 50] == 0, "Continuous brush stroke");
        mask.Stroke(new(45, 30), new(55, 30), 4, erase: true);
        Require(mask.Pixels[30 * 100 + 50] == 0 && mask.Pixels[30 * 100 + 20] == 255, "Erase mask");
        log.AppendLine("PASS: mask clipping, continuous strokes and erase.");

        const int width = 640, height = 420;
        var originalPixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4;
            originalPixels[i] = (byte)(110 + y / 4); originalPixels[i + 1] = (byte)(80 + x / 5); originalPixels[i + 2] = 90;
            originalPixels[i + 3] = x < 8 ? (byte)100 : (byte)255;
            if (x is >= 240 and < 360 && y is >= 180 and < 220) { originalPixels[i] = 20; originalPixels[i + 1] = 40; originalPixels[i + 2] = 240; }
        }
        var image = BitmapSource.Create(width, height, 144, 144, PixelFormats.Bgra32, null, originalPixels, width * 4); image.Freeze();
        var repairMask = new WatermarkMask(width, height); repairMask.Rectangle(new(238, 178), new(362, 222));
        var blankPatch = BitmapSource.Create(124, 44, 96, 96, PixelFormats.Bgra32, null, new byte[124 * 44 * 4], 124 * 4); blankPatch.Freeze();
        var composite = OnnxImageRuntime.CompositeRepair(image, blankPatch, repairMask, repairMask.Bounds());
        CheckPreserved(image, composite, repairMask);
        Require(composite.DpiX == 144 && composite.DpiY == 144, "DPI preservation");
        log.AppendLine("PASS: exact unmasked pixels, original alpha, dimensions and DPI.");

        var settingsPath = Path.Combine(output, "settings-test.json");
        var settings = new SettingsService(settingsPath, output);
        settings.Update(new AppSettings { WatermarkModels = new() { ["migan"] = Path.Combine(output, "models", "migan.onnx"), ["slbr"] = Path.Combine(output, "models", "slbr.onnx") } });
        Require(!File.ReadAllText(settingsPath).Contains(output.Replace("\\", "\\\\"), StringComparison.OrdinalIgnoreCase), "Portable model paths");
        settings.Load(); Require(Path.IsPathFullyQualified(settings.Current.WatermarkModels["slbr"]), "Resolved detector path");
        log.AppendLine("PASS: portable repair and detector settings.");
        foreach (var culture in new[] { "en", "zh-CN" })
        {
            LocalizationService.Apply(culture);
            RecognitionWindow.RunWatermarkLayoutSelfTest(image, settings, Path.Combine(output, "watermark-" + culture + ".png"));
            SettingsWindow.RunWatermarkSettingsSelfTest(settings, Path.Combine(output, "watermark-settings-" + culture + ".png"));
            RecognitionWindow.RunModelToolbarSelfTest(image, settings, output);
        }
        log.AppendLine("PASS: English and Chinese window modes, mask edits, undo, comparison and output.");
        log.AppendLine("PASS: watermark settings navigation, separate download/default selections, staged configuration and source locking.");
        log.AppendLine("PASS: all seven tool layouts at minimum/default sizes, model/device inference snapshots, CPU/GPU compatibility and unchanged saved defaults.");
        log.AppendLine("PASS: compact toolbars, original/result comparison, linked zoom/pan and cutout/repair undo.");
        RecognitionWindow.RunLargeWatermarkImageSelfTest(settings);
        log.AppendLine("PASS: oversized image preview remains available; only watermark processing is disabled.");
        using (var handler = new FixtureHandler(fixtures))
        using (var client = new HttpClient(handler))
            RecognitionWindow.RunWatermarkWorkflowSelfTest(image, fixtures, output, client);
        log.AppendLine("PASS: download information/source selection, decline preserves defaults, verified installation configures and saves default/path/source; installed model skips prompt.");
        log.AppendLine("PASS: single-action region/smart detection and inpainting, marching ants, contextual controls and result undo.");

        Task.Run(async () =>
        {
            using var handler = new FixtureHandler(fixtures); using var client = new HttpClient(handler);
            foreach (var model in WatermarkModels.All)
            {
                if (model.Id == "moebius" && !File.Exists(Path.Combine(fixtures, "moebius", "unet.onnx")))
                { log.AppendLine("SKIP: Moebius real-weight inference (three graph fixtures absent)."); continue; }
                if (model.Id != "moebius" && !File.Exists(Path.Combine(fixtures, model.Id + ".onnx"))) throw new FileNotFoundException("Missing real model fixture: " + model.Id);
                foreach (var asset in model.Id == "moebius" ? WatermarkModels.MoebiusAssets : new[] { new WatermarkModels.Asset(model.Id + ".onnx", model.Sha256, model.Size) })
                {
                    var url = model.Id == "moebius" ? WatermarkModels.MoebiusSource(asset.Name) : model.Source;
                    Require(ModelDownloadSource.Resolve(url, ModelDownloadSource.Official) == url, "Official model URL changed");
                    Require(new Uri(ModelDownloadSource.Resolve(url, ModelDownloadSource.Domestic)).Host == "hf-mirror.com", "Missing Chinese model mirror");
                }
                var root = Path.Combine(output, "download-cache");
                var installed = await WatermarkModelDownloadService.Download(model, root, new Progress<string>(), default, httpClient: client);
                var requests = handler.Requests;
                Require(await WatermarkModelDownloadService.Download(model, root, new Progress<string>(), default, httpClient: client) == installed && handler.Requests == requests, "Verified cache reuse");
                var config = new AppSettings { WatermarkDefaultModelId = model.Id, WatermarkModels = new() { [model.Id] = installed }, RecognitionTimeoutSeconds = 120 };
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var result = await OnnxImageRuntime.Shared.Inpaint(image, repairMask.Pixels, config);
                CheckPreserved(image, result, repairMask);
                var bytes = Pixels(result);
                Require(repairMask.Pixels.Select((value, index) => value != 0 && bytes[index * 4 + 2] != originalPixels[index * 4 + 2]).Any(changed => changed), "Repair actually changes the masked region");
                File.WriteAllBytes(Path.Combine(output, model.Id + "-result.png"), OnnxWorker.Encode(result));
                log.AppendLine($"PASS: real {model.Id} CPU worker, download hash/contract/cache, original-pixel composition ({watch.Elapsed.TotalSeconds:F2}s).");
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                try { await OnnxImageRuntime.Shared.Inpaint(image, repairMask.Pixels, config, cancelled.Token); throw new Exception("Canceled repair accepted."); }
                catch (OperationCanceledException) { }
            }
            var broken = WatermarkModels.All[0] with { Sha256 = new string('0', 64) };
            var cancelSettings = new AppSettings { WatermarkDefaultModelId = "lama", RecognitionTimeoutSeconds = 120,
                WatermarkModels = new() { ["lama"] = Path.Combine(fixtures, "lama.onnx"), ["migan"] = Path.Combine(fixtures, "migan.onnx") } };
            using (var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(250)))
            {
                try { await OnnxImageRuntime.Shared.Inpaint(image, repairMask.Pixels, cancelSettings, stop.Token); throw new Exception("Running repair ignored cancellation."); }
                catch (OperationCanceledException) { }
            }
            cancelSettings.WatermarkDefaultModelId = "migan";
            await OnnxImageRuntime.Shared.Inpaint(image, repairMask.Pixels, cancelSettings);
            log.AppendLine("PASS: cancel an active repair worker, then run another repair successfully.");
            try
            {
                await WatermarkModelDownloadService.Download(broken, Path.Combine(output, "bad-download"), new Progress<string>(), default, httpClient: client);
                throw new Exception("Bad hash accepted.");
            }
            catch (IOException) { log.AppendLine("PASS: corrupted download rejected."); }
            var fixture = Path.Combine(fixtures, "slbr-fixture.onnx");
            if (File.Exists(fixture))
            {
                var config = new AppSettings { WatermarkModels = new() { ["slbr"] = fixture }, RecognitionTimeoutSeconds = 120 };
                var detected = await OnnxImageRuntime.Shared.DetectWatermark(image, repairMask.Pixels, config);
                Require(detected.AsSpan().SequenceEqual(repairMask.Pixels), "Detection respects region restriction");
                var full = await OnnxImageRuntime.Shared.DetectWatermark(image, null, config);
                Require(full.All(x => x == 255), "Full-image detection");
                config.WatermarkDetectionThreshold = .9;
                var empty = await OnnxImageRuntime.Shared.DetectWatermark(image, null, config);
                Require(empty.All(x => x == 0), "Detection threshold");
                log.AppendLine("PASS: synthetic SLBR contract, region restriction, full-image detection and threshold (not real SLBR quality validation).");
            }
            var detector = Path.Combine(fixtures, "slbr.onnx");
            if (File.Exists(detector))
            {
                var config = new AppSettings { WatermarkDefaultModelId = "slbr", WatermarkModels = new() { ["slbr"] = detector }, RecognitionTimeoutSeconds = 120 };
                await OnnxWorker.Execute("watermark-check", image, config, default);
                var detected = await OnnxImageRuntime.Shared.DetectWatermark(image, repairMask.Pixels, config);
                Require(detected.Length == repairMask.Pixels.Length && detected.All(x => x is 0 or 255), "Real SLBR binary mask");
                Require(detected.Select((value, index) => repairMask.Pixels[index] == 0 && value != 0).All(outside => !outside), "Real SLBR region restriction");
                var full = await OnnxImageRuntime.Shared.DetectWatermark(image, null, config);
                Require(full.Length == detected.Length && full.All(x => x is 0 or 255), "Real SLBR full-image mask");
                var maskImage = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, full, width); maskImage.Freeze();
                File.WriteAllBytes(Path.Combine(output, "slbr-detection.png"), OnnxWorker.Encode(maskImage));
                log.AppendLine($"PASS: real official SLBR export loads and detects scoped / full-image masks ({full.Count(x => x != 0)} selected pixels); real-image accuracy remains to be assessed.");
            }
        }).GetAwaiter().GetResult();
        File.WriteAllBytes(Path.Combine(output, "original.png"), OnnxWorker.Encode(image));
        return log.ToString();
    }
    private static byte[] Pixels(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var data = new byte[image.PixelWidth * image.PixelHeight * 4]; converted.CopyPixels(data, image.PixelWidth * 4, 0); return data;
    }
    private static void CheckPreserved(BitmapSource original, BitmapSource result, WatermarkMask mask)
    {
        Require(result.PixelWidth == original.PixelWidth && result.PixelHeight == original.PixelHeight, "Output dimensions");
        var a = Pixels(original); var b = Pixels(result);
        for (var i = 0; i < mask.Pixels.Length; i++)
        {
            Require(a[i * 4 + 3] == b[i * 4 + 3], "Source alpha changed");
            if (mask.Pixels[i] == 0) Require(a.AsSpan(i * 4, 4).SequenceEqual(b.AsSpan(i * 4, 4)), "Unmasked pixel changed");
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class FixtureHandler(string fixtures) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Requests++;
            var url = request.RequestUri!.AbsolutePath;
            var path = url.Contains("Moebius-ONNX", StringComparison.Ordinal)
                ? Path.Combine(fixtures, "moebius", Path.GetFileName(url))
                : Path.Combine(fixtures, url.Contains("migan", StringComparison.Ordinal) ? "migan.onnx" : "lama.onnx");
            var stream = File.OpenRead(path);
            var content = new StreamContent(stream); content.Headers.ContentLength = stream.Length;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
