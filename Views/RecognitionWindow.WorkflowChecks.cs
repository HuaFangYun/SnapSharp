using NimCap.Screenshot.Models;
using NimCap.Screenshot.Services;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NimCap.Screenshot.Views;

public partial class RecognitionWindow
{
    internal static void RunSlbrWorkflowSelfTest(BitmapSource image, string output, HttpClient client)
    {
        var frame = new DispatcherFrame(); Exception? failure = null;
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
        {
            void Trace(string phase) => File.AppendAllText(Path.Combine(output, "workflow-progress.txt"), DateTime.Now.ToString("O") + " " + phase + "\n");
            var settings = new SettingsService(Path.Combine(output, "slbr-workflow-settings.json"), output);
            settings.Update(new AppSettings { WatermarkDefaultModelId = "lama", WatermarkDevice = "CPU" });
            var window = new RecognitionWindow(image, settings, mode: "Watermark", autoRun: false);
            try
            {
                void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
                window.RaiseEvent(new RoutedEventArgs(LoadedEvent));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                window.WatermarkMethod.SelectedIndex = 1;
                var snapshot = JsonSerializer.Serialize(settings.Current);
                var missing = window.MissingWatermarkModels(settings.Current);
                Require(missing.Count == 2 && missing.Any(x => x.Id == "slbr"), "Missing detector not included with repair model.");
                var dialog = window.CreateModelDownloadDialog(string.Join(" / ", missing.Select(x => x.Label)), "Watermark", 1, false, out var sources);
                try
                {
                    Require(sources.Items.Count == 2, "Both sources required."); sources.SelectedValue = ModelDownloadSource.Domestic;
                    var root = (FrameworkElement)dialog.Content;
                    root.Measure(new System.Windows.Size(440, 520)); root.Arrange(new Rect(0, 0, 440, 520)); root.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(440, 520, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                    File.WriteAllBytes(Path.Combine(output, "slbr-download-confirmation.png"), OnnxWorker.Encode(bitmap));
                }
                finally { dialog.Close(); }
                Require(!await window.EnsureSelectedModel(client, (_, _, _, _) => null), "Declined setup succeeded.");
                Require(snapshot == JsonSerializer.Serialize(settings.Current) && window.Mode == "Watermark", "Cancel changed settings/page.");
                Trace("cancel verified");
                using var broken = new HttpClient(new FailingDownloadHandler(client));
                Require(!await window.EnsureSelectedModel(broken, (_, _, _, _) => ModelDownloadSource.Official, Path.Combine(output, "failed-workflow")), "Failed setup succeeded.");
                Trace("partial failure returned");
                Require(snapshot == JsonSerializer.Serialize(settings.Current), "Failed setup changed defaults.");
                var confirmations = 0;
                Require(await window.EnsureSelectedModel(client, (name, scope, size, _) =>
                {
                    Require(name.Contains("SLBR") && name.Contains("LaMa") && scope == "Watermark"
                        && size == WatermarkModels.Find("lama").Size + SlbrModelDownloadService.MaximumDownloadBytes, "Incomplete model confirmation.");
                    Trace("confirm combined install"); confirmations++; return ModelDownloadSource.Domestic;
                }, Path.Combine(output, "workflow-models")), "Combined installation failed: " + window.Status.Text);
                Trace("combined installed"); settings.Load();
                Require(confirmations == 1 && settings.Current.WatermarkDefaultModelId == "lama"
                    && File.Exists(settings.Current.WatermarkModels["slbr"]) && File.Exists(settings.Current.WatermarkModels["lama"])
                    && settings.Current.ModelDownloadSource == ModelDownloadSource.Domestic && window.Mode == "Watermark", "Paths/default/page were not preserved.");
                Require(await window.EnsureSelectedModel(client, (_, _, _, _) => throw new Exception("Installed models prompt again.")), "Installed pair not ready.");
                var detectorOnly = new AppSettings { WatermarkModels = new(settings.Current.WatermarkModels) };
                detectorOnly.WatermarkModels.Remove("slbr");
                Require(window.MissingWatermarkModels(detectorOnly) is [{ Id: "slbr" }], "Installed repair still prompts for repair.");
            }
            catch (Exception ex) { Trace(ex.ToString()); failure = ex; }
            finally { window.Close(); frame.Continue = false; }
        }));
        Dispatcher.PushFrame(frame);
        if (failure is not null) throw new InvalidOperationException("SLBR workflow regression.", failure);
    }

    private sealed class FailingDownloadHandler(HttpClient client) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => request.RequestUri!.Host == "drive.usercontent.google.com"
                ? Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable))
                : client.SendAsync(new HttpRequestMessage(request.Method, request.RequestUri), token);
    }

    internal static void RunWatermarkWorkflowSelfTest(BitmapSource image, string fixtures, string output, HttpClient client)
    {
        var frame = new DispatcherFrame();
        Exception? failure = null;
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
        {
            try { await Check(); }
            catch (Exception ex) { failure = ex; }
            finally { frame.Continue = false; }
        }));
        Dispatcher.PushFrame(frame);
        if (failure is not null) throw new InvalidOperationException("Watermark workflow regression.", failure);

        async Task Check()
        {
            var settings = new SettingsService(Path.Combine(output, "workflow-settings.json"), output);
            settings.Update(new AppSettings { WatermarkDefaultModelId = "migan", WatermarkDevice = "CPU", RecognitionTimeoutSeconds = 120,
                WatermarkModels = new() { ["slbr"] = Path.Combine(fixtures, "slbr-fixture.onnx") } });
            var window = new RecognitionWindow(image, settings, mode: "Watermark", autoRun: true);
            try
            {
                void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
                window.RaiseEvent(new RoutedEventArgs(LoadedEvent));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Require(window._operation is null && window._document is null && window._watermarkResult is null,
                    "Opening the window must wait for an explicit processing action.");
                Require(window.WatermarkModelBox.Items.Count == WatermarkModels.All.Count, "All downloadable models must remain selectable.");
                var snapshot = JsonSerializer.Serialize(settings.Current);
                var dialog = window.CreateModelDownloadDialog(window.WatermarkModel.Label, "Watermark", window.WatermarkModel.Size, false, out var sources);
                try
                {
                    Require(sources.Items.Count == 2, "Both download sources must be offered.");
                    sources.SelectedValue = ModelDownloadSource.Domestic;
                    Require(sources.SelectedValue as string == ModelDownloadSource.Domestic, "Cannot select the China source.");
                    var root = (FrameworkElement)dialog.Content;
                    root.Measure(new System.Windows.Size(440, 340)); root.Arrange(new Rect(0, 0, 440, 340)); root.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(440, 340, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                    File.WriteAllBytes(Path.Combine(output, "model-download-confirmation.png"), OnnxWorker.Encode(bitmap));
                }
                finally { dialog.Close(); }
                Require(!await window.EnsureSelectedModel(client, (_, _, _, _) => null), "Declined download unexpectedly succeeded.");
                Require(snapshot == JsonSerializer.Serialize(settings.Current), "Declining a download changed saved defaults.");
                var confirmations = 0;
                Require(await window.EnsureSelectedModel(client, (name, scope, size, includesOcr) =>
                {
                    Require(name.Contains("MI-GAN") && scope == "Watermark" && size == WatermarkModels.Find("migan").Size && !includesOcr,
                        "Download confirmation must describe the chosen model and pinned size.");
                    confirmations++;
                    return ModelDownloadSource.Domestic;
                }, Path.Combine(output, "setup-models")), "Model installation failed: " + window.Status.Text);
                Require(confirmations == 1 && File.Exists(settings.Current.WatermarkModels["migan"])
                    && settings.Current.WatermarkDefaultModelId == "migan" && settings.Current.ModelDownloadSource == ModelDownloadSource.Domestic,
                    "Download did not configure model path, source and default.");
                settings.Load();
                Require(File.Exists(settings.Current.WatermarkModels["migan"]), "Saved model path does not survive reload.");
                Require(await window.EnsureSelectedModel(client, (_, _, _, _) => throw new InvalidOperationException("Installed model prompted again.")),
                    "Installed model was not immediately usable.");
                window.WatermarkMethod.SelectedIndex = 1;
                Require(window.WatermarkMaskTools.Visibility == Visibility.Collapsed, "Region mode exposes brush controls.");
                window.ShowWatermarkRegion(new(238, 178), new(362, 222));
                Require(window.WatermarkRegionOutline.StrokeDashArray.Count > 0, "Region outline must use marching ants.");
                await window.RepairWatermark();
                var first = window._watermarkResult;
                Require(first is not null && !window._watermarkMask.Bounds().IsEmpty && ReferenceEquals(window.Picture.Source, image),
                    "Single region action did not detect and remove the watermark: " + window.Status.Text);
                window.WatermarkMethod.SelectedIndex = 2;
                Require(window.WatermarkMaskTools.Visibility == Visibility.Collapsed, "Smart mode exposes brush controls.");
                await window.RepairWatermark();
                Require(window._watermarkResult is not null && !ReferenceEquals(first, window._watermarkResult),
                    "Single smart action did not detect and remove the watermark: " + window.Status.Text);
                window.WatermarkUndoRepair_Click(window, new());
                Require(ReferenceEquals(first, window._watermarkResult), "Undo did not restore the preceding region result.");
            }
            finally { window.Close(); }
        }
    }
}
