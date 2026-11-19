using NimCap.Screenshot.Models;
using NimCap.Screenshot.Services;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using Control = System.Windows.Controls.Control;
using L = NimCap.Screenshot.Localization.UiText;

namespace NimCap.Screenshot.Views;

public partial class SettingsWindow
{
    private async Task RunModelDownload<T>(TextBlock status, Button cancelButton, Control[] selectors,
        Func<IProgress<string>, CancellationToken, Task<T>> download, Action<T> apply, string? initialStatus = null,
        string failureMessage = "DownloadFailedExistingConfigurationPreservedYouCan")
    {
        if (ModelDownload is not null)
        {
            status.Text = L.Get("ADownloadIsInProgressWaitOr");
            return;
        }
        using var cancellation = new CancellationTokenSource();
        var controls = new Control[] { DownloadTesseractButton, DownloadCutoutButton,
            DownloadTableButton, DownloadFormulaButton, DownloadLocalTranslationButton }
            .Concat(selectors).Distinct().Select(control => (Control: control, Enabled: control.IsEnabled)).ToArray();
        ModelDownload = cancellation;
        foreach (var (control, _) in controls) control.IsEnabled = false;
        cancelButton.IsEnabled = true;
        if (initialStatus is not null) status.Text = L.Get(initialStatus);
        var progress = new Progress<string>(message =>
        {
            if (IsLoaded && ReferenceEquals(ModelDownload, cancellation) && !cancellation.IsCancellationRequested)
                status.Text = message;
        });
        try
        {
            var result = await download(progress, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!IsLoaded) return;
            apply(result);
            status.Text = L.Get("ModelSetupReady");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { if (IsLoaded) status.Text = L.Get("DownloadCanceledExistingConfigurationPreserved"); }
        catch (Exception ex)
        { if (IsLoaded) status.Text = L.Get(failureMessage) + ex.GetBaseException().Message; }
        finally
        {
            ModelDownload = null;
            foreach (var (control, enabled) in controls) control.IsEnabled = enabled;
            cancelButton.IsEnabled = false;
        }
    }

    private async void DownloadSpecialist_Click(object sender, RoutedEventArgs e)
    {
        var table = (sender as FrameworkElement)?.Tag as string == "Table";
        await RunModelDownload(SpecialistStatus, CancelSpecialistButton, [], async (progress, token) =>
        {
            var root = Path.Combine(AppPaths.Root, "models");
            var path = await SpecialistModelDownloadService.Install(root, table, progress, token, sourceKind: SelectedModelDownloadSource);
            OcrModelDownloadService.ModelPaths? ocr = null;
            if (table && ((ComboBoxItem)OcrBackendBox.SelectedItem).Tag.ToString() == "PaddleOCR" &&
                new[] { OcrDetPath.Text, OcrRecPath.Text, OcrDictPath.Text }.Any(p => !File.Exists(_settings.ResolveModelPath(p))))
                ocr = await new OcrModelDownloadService().Download(root, progress, token, SelectedModelDownloadSource);
            return (Path: path, Ocr: ocr);
        }, result =>
        {
            if (table)
            {
                TableModelPath.Text = _settings.StoreModelPath(result.Path); TablePluginId.Text = "";
                if (result.Ocr is { } ocr)
                {
                    OcrDetPath.Text = _settings.StoreModelPath(ocr.Detection);
                    OcrRecPath.Text = _settings.StoreModelPath(ocr.Recognition);
                    OcrDictPath.Text = _settings.StoreModelPath(ocr.Dictionary);
                }
            }
            else { FormulaDirectory.Text = _settings.StoreModelPath(result.Path); FormulaPluginId.Text = ""; }
        }, "DownloadingAndVerifyingMatchingLocalModels", "SetupIncompleteExistingConfigurationPreservedYouCan");
    }

    private async void DownloadTesseract_Click(object sender, RoutedEventArgs e)
    {
        var languages = ((ComboBoxItem)TesseractPackageBox.SelectedItem).Tag.ToString()!;
        await RunModelDownload(TesseractDownloadStatus, CancelTesseractButton, [TesseractPackageBox],
            (progress, token) => new TesseractModelDownloadService().Download(Path.Combine(AppPaths.Root, "models"),
                languages, progress, token, SelectedModelDownloadSource), directory =>
            {
                TesseractDirectory.Text = _settings.StoreModelPath(directory);
                TesseractLanguages.Text = languages; OcrBackendBox.SelectedIndex = 2;
            });
    }

    private async void DownloadCutout_Click(object sender, RoutedEventArgs e)
    {
        var model = (CutoutModelDefinition)CutoutPackageBox.SelectedItem;
        await RunModelDownload(CutoutDownloadStatus, CancelCutoutButton, [CutoutPackageBox, CutoutDeviceBox],
            (progress, token) => new CutoutModelDownloadService().Download(model, Path.Combine(AppPaths.Root, "models"),
                progress, token, SelectedModelDownloadSource), path =>
            {
                CutoutPath.Text = _settings.StoreModelPath(path);
                CutoutProfileBox.SelectedIndex = model.Profile switch { "BiRefNet" => 2, "BiRefNetMatting" => 3, "BiRefNetAlpha" => 4, _ => 0 };
                CutoutSize.Text = model.InputSize.ToString();
                StoreCutoutConfiguration();
            }, "ConnectingToTheOfficialModelSource");
    }

    private async void DownloadOcr_Click(object sender, RoutedEventArgs e)
    {
        var packageId = OcrModelPackage.SelectedValue as string ?? OcrModelDownloadService.DefaultPackageId;
        await RunModelDownload(OcrDownloadStatus, CancelOcrDownloadButton, [OcrModelPackage, OcrBackendBox, OnnxDeviceBox],
            (progress, token) => new OcrModelDownloadService().Download(Path.Combine(AppPaths.Root, "models"),
                progress, token, SelectedModelDownloadSource, packageId: packageId), paths =>
            {
                OcrDetPath.Text = _settings.StoreModelPath(paths.Detection);
                OcrRecPath.Text = _settings.StoreModelPath(paths.Recognition);
                OcrDictPath.Text = _settings.StoreModelPath(paths.Dictionary);
                OcrBackendBox.SelectedIndex = 0;
            }, "ConnectingToThePyPIModelSource", "DownloadIncompleteExistingConfigurationPreserved");
    }
}
