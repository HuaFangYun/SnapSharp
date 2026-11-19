using SnapSharp.Models;
using SnapSharp.Views;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapSharp.Services;

internal static class SlbrSelfTest
{
    internal static string Run(string fixtures, string output)
    {
        var log = new StringBuilder();
        Require(ModelDownloadSource.Allowed(new Uri(WatermarkModels.Detector.Source)), "Official SLBR URL rejected.");
        Require(!ModelDownloadSource.Allowed(new Uri("https://drive.usercontent.google.com/download?id=other")), "Unpinned Google download accepted.");
        var image = BitmapSource.Create(256, 256, 96, 96, PixelFormats.Bgra32, null,
            Enumerable.Repeat(new byte[] { 120, 100, 80, 255 }, 256 * 256).SelectMany(x => x).ToArray(), 1024);
        image.Freeze();
        using var handler = new FixtureHandler(fixtures);
        using var client = new HttpClient(handler);
        Task.Run(async () =>
        {
            foreach (var kind in new[] { ModelDownloadSource.Official, ModelDownloadSource.Domestic })
            {
                var root = Path.Combine(output, kind);
                var installed = await SlbrModelDownloadService.Download(root, new Progress<string>(), default, kind, client);
                var asset = new ModelInstallCache.Asset("slbr.onnx", WatermarkModels.Detector.Sha256,
                    WatermarkModels.Detector.Size, WatermarkModels.Detector.Size);
                Require(await ModelInstallCache.Matches(installed, asset, default), "Canonical ONNX hash/size mismatch.");
                var requests = handler.Requests;
                var other = kind == ModelDownloadSource.Official ? ModelDownloadSource.Domestic : ModelDownloadSource.Official;
                Require(await SlbrModelDownloadService.Download(root, new Progress<string>(), default, other, client) == installed
                    && requests == handler.Requests, "Source switch downloaded a verified model again.");
                Require(!Directory.GetFiles(Path.GetDirectoryName(installed)!, "*.pth").Any(), "Installed cache retains checkpoint.");
                var config = new AppSettings { WatermarkModels = new() { ["slbr"] = installed }, WatermarkDevice = "CPU", RecognitionTimeoutSeconds = 120 };
                var detected = await OnnxImageRuntime.Shared.DetectWatermark(image, null, config);
                config.WatermarkModels["slbr"] = Path.Combine(fixtures, "slbr.onnx");
                var reference = await OnnxImageRuntime.Shared.DetectWatermark(image, null, config);
                Require(detected.SequenceEqual(reference), "Native installation differs from official reference detection.");
                log.AppendLine($"PASS: {kind} URL, real checkpoint download, native reconstruction, canonical SHA256/size, worker contract/inference, official-reference mask and cross-source cache reuse.");
            }
            var invalid = Path.Combine(output, "invalid-checkpoint"); File.WriteAllText(invalid, "invalid");
            var target = Path.Combine(output, "invalid.onnx");
            try { await SlbrModelDownloadService.Reconstruct(invalid, target, ModelDownloadSource.Official, default); throw new Exception("Invalid checkpoint accepted."); }
            catch (InvalidDataException) { Require(!File.Exists(target) && !File.Exists(target + ".building"), "Bad checkpoint left output."); }
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            try { await SlbrModelDownloadService.Download(Path.Combine(output, "canceled"), new Progress<string>(), canceled.Token, httpClient: client); throw new Exception("Canceled setup accepted."); }
            catch (OperationCanceledException) { }
            log.AppendLine("PASS: corrupt checkpoint rejected, canceled installation produces no usable output.");
        }).GetAwaiter().GetResult();
        LocalizationService.Apply("zh-CN");
        RecognitionWindow.RunSlbrWorkflowSelfTest(image, output, client);
        log.AppendLine("PASS: combined missing repair/detector prompt, source selection, cancellation/failure preserve defaults, successful setup persists both paths and repair default on current page.");
        return log.ToString();
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class FixtureHandler(string fixtures) : HttpMessageHandler
    {
        internal int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Requests++;
            var uri = request.RequestUri!;
            string path;
            if (uri.Host == "drive.usercontent.google.com")
            {
                Require(uri.AbsoluteUri == WatermarkModels.Detector.Source, "Unexpected official checkpoint URL.");
                path = "slbr-official.pth.tar";
            }
            else if (uri.AbsolutePath.Contains("slbr.pth.tar"))
            {
                Require(uri.Host == "hf-mirror.com", "China checkpoint did not use China mirror.");
                path = "slbr-mirror.pth.tar";
            }
            else path = uri.AbsolutePath.Contains("migan") ? "migan.onnx" : "lama.onnx";
            var stream = File.OpenRead(Path.Combine(fixtures, path));
            var content = new StreamContent(stream); content.Headers.ContentLength = stream.Length;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
