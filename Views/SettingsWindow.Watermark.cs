using SnapSharp.Models;
using SnapSharp.Services;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using L = SnapSharp.Localization.UiText;

namespace SnapSharp.Views;

public partial class SettingsWindow
{
    private Dictionary<string, string> _watermarkModels = new();

    private void StoreWatermarkSettings(AppSettings candidate)
    {
        candidate.WatermarkModels = new(_watermarkModels);
        candidate.WatermarkDefaultModelId = ((WatermarkModelDefinition)WatermarkDefaultBox.SelectedItem).Id;
        candidate.WatermarkDevice = WatermarkSettingsDeviceBox.SelectedValue as string ?? "CPU";
    }

    internal static void RunWatermarkSettingsSelfTest(SettingsService settings, string screenshot)
    {
        using var host = new GlobalHotkeyHost();
        var window = new SettingsWindow(settings, new StartupRegistrationService(), host);
        try
        {
            window.SelectCategory("Eraser");
            if ((window.Categories.SelectedItem as TabItem)?.Tag?.ToString() != "Eraser")
                throw new InvalidOperationException("Watermark settings navigation failed.");
            var originalDefault = window.WatermarkDefaultBox.SelectedItem;
            window.WatermarkPackageBox.SelectedItem = WatermarkModels.Find("lama");
            if (window.WatermarkDefaultBox.SelectedItem != originalDefault)
                throw new InvalidOperationException("Download selection changed the default model.");
            window.SelectedModelDownloadSource = ModelDownloadSource.Domestic;
            window.ModelDownload = new CancellationTokenSource();
            if (window.DownloadSourceEnabled) throw new InvalidOperationException("Source changes allowed during download.");
            window.ModelDownload.Dispose(); window.ModelDownload = null;
            window.WatermarkDefaultBox.SelectedItem = WatermarkModels.Find("lama");
            window.WatermarkSettingsDeviceBox.SelectedValue = "DirectML";
            var candidate = new AppSettings(); window.StoreWatermarkSettings(candidate);
            if (candidate.WatermarkDefaultModelId != "lama" || candidate.WatermarkDevice != "DirectML"
                || !candidate.WatermarkModels.ContainsKey("slbr") || settings.Current.WatermarkDefaultModelId != "lama")
                throw new InvalidOperationException("Watermark settings staging failed.");
            var root = (FrameworkElement)window.Content;
            root.Measure(new System.Windows.Size(960, 720)); root.Arrange(new Rect(0, 0, 960, 720)); root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(960, 720, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
            File.WriteAllBytes(screenshot, OnnxWorker.Encode(bitmap));
        }
        finally { window.Close(); }
    }

    private void InitializeWatermarkSettings(AppSettings current)
    {
        _watermarkModels = new(current.WatermarkModels);
        WatermarkPackageBox.ItemsSource = WatermarkModels.All;
        WatermarkDefaultBox.ItemsSource = WatermarkModels.All;
        WatermarkPackageBox.SelectedItem = WatermarkDefaultBox.SelectedItem =
            WatermarkModels.All.FirstOrDefault(x => x.Id == current.WatermarkDefaultModelId) ?? WatermarkModels.All[0];
        WatermarkSettingsDeviceBox.SelectedValue = current.WatermarkDevice;
        RefreshWatermarkSettings();
    }

    private void RefreshWatermarkSettings()
    {
        if (WatermarkPackageBox.SelectedItem is not WatermarkModelDefinition model) return;
        WatermarkSettingsStatus.Text = L.Format(_watermarkModels.TryGetValue(model.Id, out var path) && WatermarkModelDownloadService.IsInstalled(model.Id, path)
            ? "WatermarkModelReady" : "WatermarkModelNotReady", model.Size / 1048576d);
        WatermarkSettingsPath.Text = _settings.StoreModelPath(path);
        WatermarkSlbrPath.Text = _watermarkModels.TryGetValue(WatermarkModels.DetectorId, out var detector)
            ? _settings.StoreModelPath(detector) : "";
    }

    private void WatermarkPackage_Changed(object sender, SelectionChangedEventArgs e) => RefreshWatermarkSettings();

    private async void DownloadWatermark_Click(object sender, RoutedEventArgs e)
    {
        if (ModelDownload is not null) return;
        var model = (sender as FrameworkElement)?.Tag?.ToString() == "Slbr" ? WatermarkModels.Detector
            : WatermarkPackageBox.SelectedItem as WatermarkModelDefinition;
        if (model is null) return;
        using var cancellation = new CancellationTokenSource();
        ModelDownload = cancellation;
        CancelWatermarkButton.IsEnabled = true;
        try
        {
            var path = await WatermarkModelDownloadService.Download(model, Path.Combine(AppPaths.Root, "models"),
                new Progress<string>(message => { if (IsLoaded) WatermarkSettingsStatus.Text = message; }),
                cancellation.Token, SelectedModelDownloadSource);
            if (!IsLoaded) return;
            _watermarkModels[model.Id] = path;
            RefreshWatermarkSettings();
            WatermarkSettingsStatus.Text = L.Get("ModelSetupReady");
        }
        catch (OperationCanceledException) { WatermarkSettingsStatus.Text = L.Get("DownloadCanceledExistingConfigurationPreserved"); }
        catch (Exception ex) { WatermarkSettingsStatus.Text = L.Get("DownloadFailedExistingConfigurationPreservedYouCan") + ex.GetBaseException().Message; }
        finally { ModelDownload = null; CancelWatermarkButton.IsEnabled = false; }
    }

    private async void ImportWatermark_Click(object sender, RoutedEventArgs e)
    {
        if (ModelDownload is not null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "ONNX|*.onnx", CheckFileExists = true };
        if (WindowDialogService.ShowDialog(dialog, this) != true) return;
        var id = (sender as FrameworkElement)?.Tag?.ToString() == "Slbr" ? WatermarkModels.DetectorId
            : ((WatermarkModelDefinition)WatermarkPackageBox.SelectedItem).Id;
        using var cancellation = new CancellationTokenSource();
        ModelDownload = cancellation;
        CancelWatermarkButton.IsEnabled = true;
        try
        {
            WatermarkSettingsStatus.Text = L.Get("VerifyingFilesAndONNXContractLargeModels");
            var candidate = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(_settings.Current))!;
            candidate.WatermarkModels = new(_watermarkModels) { [id] = dialog.FileName };
            candidate.WatermarkDefaultModelId = id;
            var dummy = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Gray8, null, new byte[256], 16);
            dummy.Freeze();
            await OnnxWorker.Execute("watermark-check", dummy, candidate, cancellation.Token);
            if (!IsLoaded) return;
            _watermarkModels[id] = dialog.FileName;
            RefreshWatermarkSettings();
            WatermarkSettingsStatus.Text = L.Get("ModelSetupReady");
        }
        catch (OperationCanceledException) { WatermarkSettingsStatus.Text = L.Get("DownloadCanceledExistingConfigurationPreserved"); }
        catch (Exception ex) { WatermarkSettingsStatus.Text = L.Get("ProcessingFailed") + ex.GetBaseException().Message; }
        finally { ModelDownload = null; CancelWatermarkButton.IsEnabled = false; }
    }
}
