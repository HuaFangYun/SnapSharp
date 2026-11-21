using NimCap.Screenshot.Models;
using NimCap.Screenshot.Services;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using L = NimCap.Screenshot.Localization.UiText;

namespace NimCap.Screenshot.Views;

public partial class RecognitionWindow
{
    private sealed record QuickModel(string Id, string Label)
    {
        public override string ToString() => Label;
    }
    private readonly Dictionary<string, string> _quickModels = new();
    private readonly Dictionary<string, string> _quickDevices = new();
    private bool _modelControlsUpdating;
    private string ModelScope => Mode is "Document" or "Summary" ? "OCR" : Mode;

    private void RefreshModelToolbar(bool reset = false)
    {
        if (reset) { _quickModels.Clear(); _quickDevices.Clear(); }
        var settings = _settings.Current;
        var scope = ModelScope;
        var watermark = scope == "Watermark";
        var cutout = scope == "Cutout";
        ModelNameColumn.Width = new GridLength(1, GridUnitType.Star);
        ModelDeviceColumn.Width = new GridLength(122);
        EngineSettingsButton.Visibility = Visibility.Visible;
        QuickModelBox.Visibility = scope is "Watermark" or "Cutout" ? Visibility.Collapsed : Visibility.Visible;
        CutoutModelBox.Visibility = cutout ? Visibility.Visible : Visibility.Collapsed;
        WatermarkModelBox.Visibility = WatermarkDeviceBox.Visibility = watermark ? Visibility.Visible : Visibility.Collapsed;
        WatermarkModelStatus.Visibility = watermark ? Visibility.Visible : Visibility.Collapsed;
        ProcessingDeviceBox.Visibility = watermark ? Visibility.Collapsed : Visibility.Visible;
        QuickModelBox.IsEnabled = !_busy && scope != "Barcode";
        ProcessingDeviceBox.IsEnabled = !_busy && scope != "Barcode";
        if (scope == "Barcode")
        {
            _modelControlsUpdating = true;
            try { QuickModelBox.ItemsSource = new[] { new QuickModel("none", "ZXing.Net") }; QuickModelBox.SelectedIndex = 0; ProcessingDeviceBox.SelectedValue = "CPU"; }
            finally { _modelControlsUpdating = false; }
            return;
        }
        if (watermark) return;
        _modelControlsUpdating = true;
        try
        {
            var device = _quickDevices.GetValueOrDefault(scope, cutout
                ? (settings.CutoutOnnxDevice is "CPU" or "DirectML" ? settings.CutoutOnnxDevice : settings.OnnxDevice)
                : settings.OnnxDevice);
            if (cutout)
            {
                ProcessingDeviceBox.SelectedValue = device;
                var models = CutoutModels.ForDevice(device);
                var id = _quickModels.GetValueOrDefault(scope, settings.CutoutDefaultModelId);
                CutoutModelBox.ItemsSource = models;
                CutoutModelBox.SelectedItem = models.FirstOrDefault(x => x.Id == id) ?? models[0];
                _quickModels[scope] = ((CutoutModelDefinition)CutoutModelBox.SelectedItem).Id;
                ConfigureQuickDevice(device, true);
                return;
            }
            var choices = new List<QuickModel>();
            string selected;
            if (scope == "OCR")
            {
                choices.AddRange(OcrModelDownloadService.Packages.Select(x => new QuickModel(x.Id, $"PP-OCRv6 {x.Profile}")));
                choices.Add(new("Windows", "Windows OCR"));
                choices.Add(new("Tesseract", "Tesseract"));
                selected = settings.OcrBackend == "PaddleOCR" ? settings.OcrModelPackage : settings.OcrBackend;
            }
            else
            {
                choices.Add(new("builtin", scope == "Table" ? "SLANet" : "RapidLaTeXOCR"));
                var pluginId = scope == "Table" ? settings.TableRecognizerId : settings.FormulaRecognizerId;
                // Only expose the configured plugin for this task; recognizers do not declare task/device capabilities.
                if (!string.IsNullOrWhiteSpace(pluginId)) choices.Add(new("plugin", L.Get("ModelPlugin") + pluginId));
                selected = string.IsNullOrWhiteSpace(pluginId) ? "builtin" : "plugin";
            }
            QuickModelBox.ItemsSource = choices;
            var saved = _quickModels.GetValueOrDefault(scope, selected);
            QuickModelBox.SelectedItem = choices.FirstOrDefault(x => x.Id == saved) ?? choices[0];
            var choice = (QuickModel)QuickModelBox.SelectedItem;
            _quickModels[scope] = choice.Id;
            if (choice.Id == "plugin") device = "Plugin";
            else if (choice.Id is "Windows" or "Tesseract" or "tiny") device = "CPU";
            else if (choice.Id == "medium") device = "DirectML";
            else if (device == "Plugin") device = settings.OnnxDevice;
            ConfigureQuickDevice(device, choice.Id is not ("plugin" or "Windows" or "Tesseract"));
        }
        finally { _modelControlsUpdating = false; }
    }

    private void ConfigureQuickDevice(string device, bool selectable)
    {
        var id = (QuickModelBox.SelectedItem as QuickModel)?.Id;
        foreach (var item in ProcessingDeviceBox.Items.OfType<ComboBoxItem>())
        {
            var tag = item.Tag?.ToString();
            var visible = device == "Plugin" ? tag == "Plugin" : tag != "Plugin";
            if (ModelScope == "OCR" && id is ("tiny" or "medium")) visible &= tag == device;
            item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
        ProcessingDeviceBox.SelectedValue = device;
        ProcessingDeviceBox.IsEnabled = !_busy && selectable;
        _quickDevices[ModelScope] = device;
    }

    private async void QuickModel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_modelControlsUpdating || QuickModelBox.SelectedItem is not QuickModel choice) return;
        _quickModels[ModelScope] = choice.Id;
        RefreshModelToolbar();
        if (IsLoaded) await EnsureSelectedModel();
    }

    private void ProcessingDevice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_modelControlsUpdating || ProcessingDeviceBox.SelectedValue is not string device) return;
        if (Mode == "Cutout" && CutoutModelBox.SelectedItem is CutoutModelDefinition current) _quickModels["Cutout"] = current.Id;
        _quickDevices[ModelScope] = device;
        RefreshModelToolbar();
    }

    private AppSettings QuickSettings()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(_settings.Current))!;
        var device = ProcessingDeviceBox.SelectedValue as string ?? "CPU";
        if (Mode == "Cutout")
        {
            var model = (CutoutModelDefinition)CutoutModelBox.SelectedItem;
            settings = CutoutModels.ForModel(settings, model.Id);
            if (!settings.CutoutModels.ContainsKey(model.Id))
            { settings.CutoutProfile = model.Profile; settings.CutoutInputSize = model.InputSize; }
            settings.CutoutOnnxDevice = device;
        }
        else if (Mode is "OCR" or "Document" or "Summary")
        {
            var id = ((QuickModel)QuickModelBox.SelectedItem).Id;
            settings.OcrBackend = id is "Windows" or "Tesseract" ? id : "PaddleOCR";
            if (settings.OcrBackend == "PaddleOCR")
            {
                if (settings.OcrModelPackage != id)
                    settings.OcrDetectionModel = settings.OcrRecognitionModel = settings.OcrDictionary = "";
                settings.OcrModelPackage = id;
            }
            settings.OnnxDevice = device;
        }
        else if (Mode is "Table" or "Formula")
        {
            if ((QuickModelBox.SelectedItem as QuickModel)?.Id == "builtin")
            {
                if (Mode == "Table") settings.TableRecognizerId = "";
                else settings.FormulaRecognizerId = "";
            }
            if (device != "Plugin") settings.OnnxDevice = device;
            // Table structure inference and its OCR stage share this device.
            if (Mode == "Table" && settings.OcrBackend == "PaddleOCR" && !ModelDeviceSupport.OcrSupports(settings.OcrModelPackage, device))
            {
                settings.OcrModelPackage = "small";
                settings.OcrDetectionModel = settings.OcrRecognitionModel = settings.OcrDictionary = "";
            }
        }
        return settings;
    }

    private static async Task<bool> ResolveQuickOcr(AppSettings settings, CancellationToken token)
    {
        if (settings.OcrBackend != "PaddleOCR") return true;
        if (new[] { settings.OcrDetectionModel, settings.OcrRecognitionModel, settings.OcrDictionary }.All(File.Exists)) return true;
        var package = OcrModelDownloadService.FindPackage(settings.OcrModelPackage);
        var directory = await ModelInstallCache.Find(Path.Combine(AppPaths.Root, "models"), package.DirectoryPrefix,
            package.InstalledAssets, null, token);
        if (directory is null) return false;
        settings.OcrDetectionModel = Path.Combine(directory, "detection.onnx");
        settings.OcrRecognitionModel = Path.Combine(directory, "recognition.onnx");
        settings.OcrDictionary = Path.Combine(directory, "dictionary.txt");
        return true;
    }

    private void RefreshWorkspaceLayout()
    {
        var imageOnly = Mode is "Watermark" or "Cutout";
        ResultTabs.Visibility = imageOnly ? Visibility.Collapsed : Visibility.Visible;
        ComparisonPanel.Visibility = SourcePreviewHeader.Visibility = imageOnly ? Visibility.Visible : Visibility.Collapsed;
        ResultSplitter.Visibility = Visibility.Visible;
        PreviewColumn.Width = new GridLength(imageOnly ? 1 : 1.2, GridUnitType.Star);
        PreviewColumn.MinWidth = 280;
        ResultColumn.MinWidth = 260;
        ResultColumn.Width = new GridLength(1, GridUnitType.Star);
        WorkspaceGap.Width = new GridLength(8);
        RefreshModelToolbar();
        RefreshComparison();
        if (IsLoaded) Dispatcher.BeginInvoke(FitImage, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    internal static void RunModelToolbarSelfTest(System.Windows.Media.Imaging.BitmapSource image, SettingsService settings, string output)
    {
        var window = new RecognitionWindow(image, settings, autoRun: false);
        try
        {
            var root = (FrameworkElement)window.Content;
            void SelectMode(string mode) => window.ModeBox.SelectedItem = window.ModeBox.Items.OfType<ListBoxItem>().First(x => x.Tag?.ToString() == mode);
            void SelectModel(string id) => window.QuickModelBox.SelectedItem = window.QuickModelBox.Items.OfType<QuickModel>().First(x => x.Id == id);
            var saved = JsonSerializer.Serialize(settings.Current);
            SelectModel("medium");
            if (window.QuickSettings().OnnxDevice != "DirectML" || window.QuickSettings().OcrModelPackage != "medium")
                throw new InvalidOperationException("OCR model and device are not applied to inference.");
            window.Settings_Click(window, new());
            if (window.QuickSettings().OcrModelPackage != "medium") throw new InvalidOperationException("Canceled settings reset the quick model.");
            SelectModel("tiny");
            if (window.QuickSettings().OnnxDevice != "CPU") throw new InvalidOperationException("Tiny must use CPU.");
            SelectModel("Windows");
            if (window.ProcessingDeviceBox.IsEnabled || window.QuickSettings().OcrBackend != "Windows")
                throw new InvalidOperationException("System OCR must not expose a selectable GPU.");
            SelectMode("Document");
            if (window.QuickSettings().OcrBackend != "Windows") throw new InvalidOperationException("Document OCR selection was not shared.");
            SelectMode("Cutout");
            window.ProcessingDeviceBox.SelectedValue = "DirectML";
            window.CutoutModelBox.SelectedItem = CutoutModels.All.First(x => x.Id == "birefnet-hr-matting");
            if (window.QuickSettings().CutoutOnnxDevice != "DirectML" || window.QuickSettings().CutoutInputSize != 2048)
                throw new InvalidOperationException("Cutout selection or GPU device was lost.");
            window.ProcessingDeviceBox.SelectedValue = "CPU";
            if (((CutoutModelDefinition)window.CutoutModelBox.SelectedItem).SupportsCpu != true)
                throw new InvalidOperationException("CPU offered a GPU-only cutout model.");
            var firstCutout = image.Clone(); firstCutout.Freeze();
            var secondCutout = image.Clone(); secondCutout.Freeze();
            SaveResultUndo(window._cutoutUndo, null); window._cutout = firstCutout;
            SaveResultUndo(window._cutoutUndo, firstCutout); window._cutout = secondCutout;
            window.RefreshComparison();
            if (!ReferenceEquals(window.Picture.Source, image) || !ReferenceEquals(window.ComparisonImage.Source, secondCutout))
                throw new InvalidOperationException("Cutout must keep the original on the left and result on the right.");
            window.SetBusy(true); window.CutoutUndo_Click(window, new());
            if (!ReferenceEquals(window._cutout, secondCutout)) throw new InvalidOperationException("Cutout undo accepted while busy.");
            window.SetBusy(false); window.CutoutUndo_Click(window, new());
            if (!ReferenceEquals(window._cutout, firstCutout)) throw new InvalidOperationException("Cutout undo lost the previous result.");
            window.CutoutUndo_Click(window, new());
            if (window._cutout is not null || window.ComparisonImage.Source is not null || window.CommonCopyButton.IsEnabled || window.CommonUndoButton.IsEnabled)
                throw new InvalidOperationException("Cutout undo did not clear the initial result.");
            foreach (var mode in new[] { "Table", "Formula" })
            {
                SelectMode(mode); SelectModel("builtin"); window.ProcessingDeviceBox.SelectedValue = "DirectML";
                var candidate = window.QuickSettings();
                if (candidate.OnnxDevice != "DirectML" || (mode == "Table" ? candidate.TableRecognizerId : candidate.FormulaRecognizerId) != "")
                    throw new InvalidOperationException("Specialist selection was not passed to inference.");
            }
            if (saved != JsonSerializer.Serialize(settings.Current)) throw new InvalidOperationException("Quick selection modified saved defaults.");
            foreach (var size in new[] { new System.Windows.Size(980, 560), new System.Windows.Size(1240, 790) })
            foreach (var mode in new[] { "OCR", "Document", "Table", "Barcode", "Formula", "Cutout", "Watermark" })
            {
                SelectMode(mode);
                root.Measure(size); root.Arrange(new Rect(new Point(), size)); root.UpdateLayout();
                var actions = new FrameworkElement[] { window.RunButton, window.CancelButton, window.CommonUndoButton,
                    window.CommonCopyButton, window.CommonExportButton, window.CommonPinButton };
                var positions = actions.Select(x => x.TranslatePoint(new Point(), root)).ToArray();
                if (actions.Any(x => x.Visibility != Visibility.Visible || x.ActualWidth < 25)
                    || positions.Any(x => Math.Abs(x.Y - positions[0].Y) > 1)
                    || positions.Zip(positions.Skip(1), (a, b) => a.X < b.X).Any(x => !x)
                    || positions[^1].X + window.CommonPinButton.ActualWidth > size.Width - 9)
                    throw new InvalidOperationException($"Common actions moved, clipped or reordered: {mode}, {size}.");
                if (window.PictureScroll.ActualHeight < 150 || window.PictureScroll.ActualWidth < 280)
                    throw new InvalidOperationException($"Preview too small: {mode}, {size}, {window.PictureScroll.RenderSize}.");
                var imageOnly = mode is "Cutout" or "Watermark";
                if (imageOnly && (window.ResultTabs.Visibility != Visibility.Collapsed || window.ComparisonPanel.Visibility != Visibility.Visible
                    || Math.Abs(window.PreviewColumn.ActualWidth - window.ResultColumn.ActualWidth) > 1 || window.ComparisonScroll.ActualHeight < 150))
                    throw new InvalidOperationException("Image processing must show equal side-by-side previews.");
                if (window.ModelToolbar.ActualHeight > 32 || window.ImageTools.ActualHeight > 65)
                    throw new InvalidOperationException("Image controls use too much vertical space.");
                if (!imageOnly && (window.ResultTabs.ActualWidth < 250 || window.ResultSplitter.Visibility != Visibility.Visible))
                    throw new InvalidOperationException("Text output or result splitter missing.");
                if (mode == "Barcode" && (window.QuickModelBox.Visibility != Visibility.Visible || window.ProcessingDeviceBox.Visibility != Visibility.Visible || window.QuickModelBox.IsEnabled || window.ProcessingDeviceBox.IsEnabled))
                    throw new InvalidOperationException("Barcode has no model to select.");
                if (mode != "Barcode")
                {
                    FrameworkElement model = mode == "Watermark" ? window.WatermarkModelBox : mode == "Cutout" ? window.CutoutModelBox : window.QuickModelBox;
                    FrameworkElement device = mode == "Watermark" ? window.WatermarkDeviceBox : window.ProcessingDeviceBox;
                    if (Math.Abs(model.TranslatePoint(new Point(), root).Y - device.TranslatePoint(new Point(), root).Y) > 1 || model.ActualWidth < 160)
                        throw new InvalidOperationException("Model and device must share a usable row.");
                }
                if (mode == "Watermark")
                {
                    window.WatermarkMethod.SelectedIndex = 1;
                    root.Measure(size); root.Arrange(new Rect(new Point(), size)); root.UpdateLayout();
                    if (window.WatermarkMaskTools.TranslatePoint(new Point(), root).Y < window.PictureScroll.TranslatePoint(new Point(0, window.PictureScroll.ActualHeight), root).Y)
                        throw new InvalidOperationException("Mask tools must be below the preview.");
                }
                if (imageOnly)
                {
                    window.FitImage();
                    window.SetPreviewScale(2);
                    root.UpdateLayout();
                    if (window.ComparisonScale.ScaleX != window.PictureScale.ScaleX)
                        throw new InvalidOperationException("Comparison zoom is not synchronized.");
                    window.PictureScroll.ScrollToHorizontalOffset(60);
                    window.PictureScroll.ScrollToVerticalOffset(40);
                    root.UpdateLayout();
                    if (Math.Abs(window.ComparisonScroll.HorizontalOffset - 60) > 1 || Math.Abs(window.ComparisonScroll.VerticalOffset - 40) > 1)
                        throw new InvalidOperationException("Source pan was not synchronized to the result.");
                    window.ComparisonScroll.ScrollToHorizontalOffset(90);
                    root.UpdateLayout();
                    if (Math.Abs(window.PictureScroll.HorizontalOffset - 90) > 1)
                        throw new InvalidOperationException("Result pan was not synchronized to the source.");
                    window.FitImage(); root.UpdateLayout();
                }
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(root);
                File.WriteAllBytes(Path.Combine(output, $"layout-{System.Globalization.CultureInfo.CurrentUICulture.Name}-{mode}-{size.Width}.png"), OnnxWorker.Encode(bitmap));
            }
        }
        finally { window.Close(); }
    }
}
