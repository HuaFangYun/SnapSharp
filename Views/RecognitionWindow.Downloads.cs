using NimCap.Screenshot.Models;
using NimCap.Screenshot.Services;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using L = NimCap.Screenshot.Localization.UiText;

namespace NimCap.Screenshot.Views;

public partial class RecognitionWindow
{
    private bool _modelSetupActive;
    private bool HasSelectedModel => Mode == "Barcode" || (Mode == "Watermark" ? WatermarkModelBox.SelectedItem is not null
        : Mode == "Cutout" ? CutoutModelBox.SelectedItem is not null : QuickModelBox.SelectedItem is not null);

    private List<WatermarkModelDefinition> MissingWatermarkModels(AppSettings settings)
    {
        var models = new List<WatermarkModelDefinition>();
        if (!settings.WatermarkModels.TryGetValue(WatermarkModel.Id, out var repair) || !WatermarkModelDownloadService.IsInstalled(WatermarkModel.Id, repair))
            models.Add(WatermarkModel);
        if (WatermarkMethodValue != "Brush" && (!settings.WatermarkModels.TryGetValue(WatermarkModels.DetectorId, out var detector) || !File.Exists(detector)))
            models.Add(WatermarkModels.Detector);
        return models;
    }

    private async Task<bool> EnsureSelectedModel(System.Net.Http.HttpClient? httpClient = null,
        Func<string, string, long, bool, string?>? confirm = null, string? modelRoot = null)
    {
        if (_closed || _busy || _modelSetupActive || !HasSelectedModel) return false;
        if (Mode == "Barcode") return true;
        var scope = ModelScope;
        var id = scope == "Watermark" ? WatermarkModel.Id : scope == "Cutout"
            ? ((CutoutModelDefinition)CutoutModelBox.SelectedItem).Id : ((QuickModel)QuickModelBox.SelectedItem).Id;
        var candidate = scope == "Watermark" ? WatermarkSettings() : QuickSettings();
        var watermarkDownloads = scope == "Watermark" ? MissingWatermarkModels(candidate) : [];
        if (id == "plugin") return true;
        if (id == "Windows")
        {
            try
            {
                var available = string.IsNullOrWhiteSpace(candidate.WindowsOcrLanguage)
                    ? Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()
                    : Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(candidate.WindowsOcrLanguage));
                if (available is not null) return true;
            }
            catch (ArgumentException) { }
            Status.Text = L.Get("WindowsOCRLanguagePackUnavailableInstallThe");
            return false;
        }
        bool ready = scope switch
        {
            "Watermark" => watermarkDownloads.Count == 0,
            "Cutout" => File.Exists(candidate.CutoutModel),
            "OCR" when id == "Tesseract" => candidate.TesseractLanguages.Split('+').All(x => File.Exists(Path.Combine(candidate.TesseractDataDirectory, x + ".traineddata"))),
            "OCR" => new[] { candidate.OcrDetectionModel, candidate.OcrRecognitionModel, candidate.OcrDictionary }.All(File.Exists),
            "Table" => File.Exists(candidate.TableStructureModel) && OcrConfigured(candidate),
            "Formula" => SpecialistModelDownloadService.Formula.All(x => File.Exists(Path.Combine(candidate.FormulaModelDirectory, x.Name))),
            _ => false
        };
        if (ready) return true;
        var languages = candidate.TesseractLanguages is "eng" or "chi_sim" or "chi_sim+eng" ? candidate.TesseractLanguages : "chi_sim+eng";
        var needsTableOcr = scope == "Table" && !OcrConfigured(candidate);
        long bytes = scope switch
        {
            "Watermark" => watermarkDownloads.Sum(x => x.Id == WatermarkModels.DetectorId ? SlbrModelDownloadService.MaximumDownloadBytes : x.Size),
            "Cutout" => ((CutoutModelDefinition)CutoutModelBox.SelectedItem).MaximumBytes,
            "OCR" when id == "Tesseract" => TesseractModelDownloadService.Package(languages).Sum(x => (long)x.Size),
            "OCR" => OcrModelDownloadService.FindPackage(id).Assets.Sum(x => x.Size),
            "Table" => SpecialistModelDownloadService.Table.Size + (needsTableOcr ? OcrModelDownloadService.DefaultPackage.Assets.Sum(x => x.Size) : 0),
            "Formula" => SpecialistModelDownloadService.Formula.Sum(x => x.Size),
            _ => 0
        };
        var name = scope == "Watermark" ? string.Join(" / ", watermarkDownloads.Select(x => x.Label)) : scope == "Cutout" ? ((CutoutModelDefinition)CutoutModelBox.SelectedItem).Label : ((QuickModel)QuickModelBox.SelectedItem).Label;
        _modelSetupActive = true;
        try
        {
            // A previously downloaded OCR package may no longer be the saved default.
            // Resolve its verified cache before asking for another download.
            if (scope == "OCR" && id != "Tesseract")
            {
                using var lookup = new CancellationTokenSource();
                _operation = lookup; SetBusy(true);
                try
                {
                    if (await ResolveQuickOcr(candidate, lookup.Token))
                    {
                        lookup.Token.ThrowIfCancellationRequested();
                        if (_closed) return false;
                        _settings.Update(candidate);
                        Status.Text = L.Get("RecognitionDownloadReady");
                        return true;
                    }
                }
                finally { _operation = null; SetBusy(false); }
            }
            var source = (confirm ?? ConfirmModelDownload)(name, scope, bytes, needsTableOcr);
            if (source is null) return false;
            SetBusy(true);
            using var cancellation = new CancellationTokenSource(); _operation = cancellation;
            var progress = new Progress<string>(message => { if (!_closed && ReferenceEquals(_operation, cancellation) && !cancellation.IsCancellationRequested) Status.Text = message; });
            var root = modelRoot ?? Path.Combine(AppPaths.Root, "models");
            // Only persist a complete, verified installation. Existing defaults survive failures and cancellation.
            if (scope == "Watermark")
            {
                foreach (var model in watermarkDownloads)
                    candidate.WatermarkModels[model.Id] = await WatermarkModelDownloadService.Download(model, root, progress, cancellation.Token, source, httpClient);
                candidate.WatermarkDefaultModelId = id;
            }
            else if (scope == "Cutout")
            {
                var model = CutoutModels.All.First(x => x.Id == id);
                var path = await new CutoutModelDownloadService().Download(model, root, progress, cancellation.Token, source, httpClient);
                candidate.CutoutModels[id] = new() { Path = path, Profile = model.Profile, InputSize = model.InputSize };
                candidate.CutoutDefaultModelId = id; candidate.CutoutModel = path;
                candidate.CutoutProfile = model.Profile; candidate.CutoutInputSize = model.InputSize;
            }
            else if (scope == "OCR" && id == "Tesseract")
            {
                candidate.TesseractDataDirectory = await new TesseractModelDownloadService().Download(root, languages, progress, cancellation.Token, source, httpClient);
                candidate.TesseractLanguages = languages;
            }
            else if (scope == "OCR")
            {
                SetOcrPaths(candidate, await new OcrModelDownloadService().Download(root, progress, cancellation.Token, source, httpClient, packageId: id));
            }
            else
            {
                var path = await SpecialistModelDownloadService.Install(root, scope == "Table", progress, cancellation.Token, sourceKind: source, httpClient: httpClient);
                if (scope == "Table")
                {
                    candidate.TableStructureModel = path;
                    if (needsTableOcr)
                    {
                        SetOcrPaths(candidate, await new OcrModelDownloadService().Download(root, progress, cancellation.Token, source, httpClient));
                        candidate.OcrBackend = "PaddleOCR"; candidate.OcrModelPackage = "small";
                        // Table OCR and structure inference share the selected device.
                    }
                }
                else candidate.FormulaModelDirectory = path;
            }
            cancellation.Token.ThrowIfCancellationRequested();
            if (_closed) return false;
            candidate.ModelDownloadSource = source;
            _settings.Update(candidate);
            RefreshWatermarkModels();
            Status.Text = L.Get("RecognitionDownloadReady");
            return true;
        }
        catch (OperationCanceledException) { if (!_closed) Status.Text = L.Get("DownloadCanceledExistingConfigurationPreserved"); return false; }
        catch (Exception ex) { if (!_closed) Status.Text = L.Get("DownloadFailedExistingConfigurationPreservedYouCan") + ex.GetBaseException().Message; return false; }
        finally
        {
            _operation = null;
            _modelSetupActive = false;
            if (_busy) SetBusy(false);
        }
    }

    private static bool OcrConfigured(AppSettings settings) => settings.OcrBackend switch
    {
        "PaddleOCR" => new[] { settings.OcrDetectionModel, settings.OcrRecognitionModel, settings.OcrDictionary }.All(File.Exists),
        "Tesseract" => settings.TesseractLanguages.Split('+').All(x => File.Exists(Path.Combine(settings.TesseractDataDirectory, x + ".traineddata"))),
        "Windows" => Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages.Count > 0,
        _ => false
    };

    private static void SetOcrPaths(AppSettings settings, OcrModelDownloadService.ModelPaths paths)
    { settings.OcrDetectionModel = paths.Detection; settings.OcrRecognitionModel = paths.Recognition; settings.OcrDictionary = paths.Dictionary; }

    private string? ConfirmModelDownload(string name, string scope, long bytes, bool includesOcr)
    {
        var dialog = CreateModelDownloadDialog(name, scope, bytes, includesOcr, out var source);
        return dialog.ShowDialog() == true ? source.SelectedValue as string : null;
    }

    private Window CreateModelDownloadDialog(string name, string scope, long bytes, bool includesOcr, out ComboBox source)
    {
        var dialog = new Window { Owner = IsLoaded ? this : null, Title = L.Get("RecognitionDownloadTitle"), Width = 440, SizeToContent = SizeToContent.Height,
            MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 40),
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Style = (Style)FindResource("AppWindow") };
        var root = new DockPanel { Margin = new Thickness(20) };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = L.Get("RecognitionMissingModel") + name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = L.Get("RecognitionDownloadUse" + scope), TextWrapping = TextWrapping.Wrap });
        var sizeLabel = new TextBlock { Text = L.Format("RecognitionDownloadSize", bytes / 1000000d), Margin = new Thickness(0, 8, 0, 8) };
        panel.Children.Add(sizeLabel);
        if (includesOcr) panel.Children.Add(new TextBlock { Text = L.Get("RecognitionDownloadIncludesOcr"), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = L.Get("RecognitionDownloadDefault"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        source = new ComboBox { SelectedValuePath = "Tag", Style = (Style)FindResource("ToolbarComboBox"), MinHeight = 30 };
        source.Items.Add(new ComboBoxItem { Content = L.Get("ModelDownloadOfficial"), Tag = ModelDownloadSource.Official });
        source.Items.Add(new ComboBoxItem { Content = L.Get("ModelDownloadDomestic"), Tag = ModelDownloadSource.Domestic });
        source.SelectedValue = _settings.Current.ModelDownloadSource;
        if (source.SelectedIndex < 0) source.SelectedIndex = 0;
        panel.Children.Add(source);
        if (scope == "Watermark")
        {
            var models = MissingWatermarkModels(_settings.Current);
            if (models.Count == 0) models.Add(WatermarkModel);
            if (models.Any(x => x.Id == WatermarkModels.DetectorId))
                panel.Children.Add(new TextBlock { Text = L.Get("WatermarkDetectorPurpose"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
            var info = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 8, 0, 0) };
            var sourceBox = source;
            void UpdateSource()
            {
                var kind = sourceBox.SelectedValue as string ?? ModelDownloadSource.Official;
                sizeLabel.Text = L.Format("RecognitionDownloadSize", models.Sum(x => x.Id == WatermarkModels.DetectorId ? SlbrModelDownloadService.Checkpoint(kind).MaximumBytes : x.Size) / 1000000d);
                info.Text = string.Join("\n", models.SelectMany(model => model.Id == "moebius"
                    ? WatermarkModels.MoebiusAssets.Select(x => x.Name + " - " + ModelDownloadSource.Resolve(WatermarkModels.MoebiusSource(x.Name), kind))
                    : new[] { model.Id == WatermarkModels.DetectorId
                        ? ModelDownloadSource.Resolve(SlbrModelDownloadService.Source(kind), kind)
                        : ModelDownloadSource.Resolve(model.Source, kind) }));
            }
            source.SelectionChanged += (_, _) => UpdateSource();
            UpdateSource();
            panel.Children.Add(info);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var yes = new Button { Content = L.Get("RecognitionDownloadConfirm"), IsDefault = true, Style = (Style)FindResource("DialogButton"), MinWidth = 100 };
        var no = new Button { Content = L.Get("RecognitionCancelDownload"), IsCancel = true, Style = (Style)FindResource("DialogButton"), MinWidth = 70, Margin = new Thickness(8, 0, 0, 0) };
        yes.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(yes); buttons.Children.Add(no);
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        dialog.Content = root;
        return dialog;
    }
}
