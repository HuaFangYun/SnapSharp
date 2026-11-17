using L = SnapSharp.Localization.UiText;
using SnapSharp.Models;
using SnapSharp.Plugins;
using SnapSharp.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapSharp.Views;

public partial class RecognitionWindow : Window
{
    private sealed record TranslationLanguage(string Code, string Name)
    {
        public override string ToString() => Name;
    }
    private static readonly TranslationLanguage[] TranslationLanguages =
    [
        new("cs", "Čeština"), new("de", "Deutsch"), new("de-AT", "Deutsch (Österreich)"), new("de-CH", "Deutsch (Schweiz)"),
        new("en", "English"), new("es", "Español"), new("es-ES", "Español (España)"), new("es-419", "Español (Latinoamérica)"),
        new("fr", "Français"), new("fr-FR", "Français (France)"), new("fr-CA", "Français (Canada)"), new("it", "Italiano"),
        new("ja", "日本語"), new("ko", "한국어"), new("pl", "Polski"), new("pt-BR", "Português (Brasil)"),
        new("pt-PT", "Português (Portugal)"), new("ru", "Русский"), new("tr", "Türkçe"), new("zh-CN", "中文（中国大陆）"),
        new("zh-Hans", "简体中文"), new("zh-Hant", "繁體中文")
    ];
    private readonly BitmapSource _image;
    private readonly SettingsService _settings;
    private readonly PluginCatalog? _plugins;
    private RecognitionDocument? _document;
    private ObservableCollection<RecognizedCell> _cells = [];
    private BitmapSource? _cutout;
    private CancellationTokenSource? _operation;
    private string _baseline = "";
    private string _sourceText = "";
    private string _translatedText = "";
    private bool _hasTranslation;
    private string _tableBaseline = "[]";
    private string TableSnapshot => System.Text.Json.JsonSerializer.Serialize(_cells);
    private bool HasEdits => _sourceText != _baseline || TableSnapshot != _tableBaseline;
    private bool _busy, _closed, _syncing;
    public event Action<BitmapSource, CaptureAction>? OutputRequested;
    public event Action<string>? SettingsRequested;
    private string Mode => ((ComboBoxItem)ModeBox.SelectedItem).Tag.ToString()!;
    private string CurrentTextView => (TextViewBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Source";
    public RecognitionWindow(BitmapSource image, SettingsService settings, PluginCatalog? plugins = null, string mode = "OCR", bool autoRun = true)
    {
        _image = image.IsFrozen ? image : image.Clone(); if (!_image.IsFrozen) _image.Freeze();
        _settings = settings; _plugins = plugins;
        InitializeComponent();
        TargetLanguageBox.ItemsSource = TranslationLanguages;
        TargetLanguageBox.SelectedItem = TranslationLanguages.FirstOrDefault(x => x.Code.Equals(settings.Current.TranslationTarget, StringComparison.OrdinalIgnoreCase))
            ?? TranslationLanguages.First(x => x.Code == "zh-CN");
        TextViewBox.SelectedIndex = 0;
        ModeBox.SelectedIndex = mode switch { "Barcode" => 1, "Table" => 2, "Formula" => 3, "Cutout" => 4, _ => 0 };
        CutoutModels.Migrate(settings.Current);
        var cutoutModels = CutoutModels.ForDevice(settings.Current.CutoutOnnxDevice);
        CutoutModelBox.ItemsSource = cutoutModels;
        CutoutModelBox.SelectedItem = cutoutModels.FirstOrDefault(x => x.Id == settings.Current.CutoutDefaultModelId) ?? cutoutModels[0];
        void UpdateMode()
        {
            CutoutModelBox.Visibility = Mode == "Cutout" ? Visibility.Visible : Visibility.Collapsed;
            RunButton.Content = Mode == "Cutout" ? L.Get("RemoveBackgroundRunAgain") : L.Get("StartRunAgain");
            if (Mode == "Cutout") ResultTabs.SelectedIndex = 2;
        }
        ModeBox.SelectionChanged += (_, _) => UpdateMode();
        UpdateMode();
        SetPicture(_image); CellsGrid.ItemsSource = _cells;
        Loaded += async (_, _) =>
        {
            // Fit after the viewport's first layout, otherwise a zero-sized viewport
            // can shrink a large clipboard picture to an apparently blank preview.
            await Dispatcher.InvokeAsync(FitImage, System.Windows.Threading.DispatcherPriority.Loaded);
            if (_closed) return;
            if (autoRun) await Run();
            else Status.Text = L.Get("ClipboardImageLoadedChooseAModeAbove");
        };
        Closed += (_, _) => { _closed = true; _operation?.Cancel(); };
    }
    private void SetPicture(BitmapSource image) { Picture.Source = image; PictureCanvas.Width = image.PixelWidth; PictureCanvas.Height = image.PixelHeight; }
    private void FitImage() => Zoom.Value = Math.Clamp(Math.Min(Math.Max(1, PictureScroll.ViewportWidth - 8) / _image.PixelWidth, Math.Max(1, PictureScroll.ViewportHeight - 8) / _image.PixelHeight), .02, 4);
    private void Fit_Click(object sender, RoutedEventArgs e) => FitImage();
    private async void Run_Click(object sender, RoutedEventArgs e) => await Run();
    private async Task Run()
    {
        if (_busy) return;
        CellsGrid.CommitEdit();
        if (HasEdits && MessageBox.Show(this, L.Get("RunningAgainWillReplaceManuallyEditedText"), L.Get("Confirm"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        SetBusy(true); _operation = new CancellationTokenSource();
        _pluginCells = null;
        try
        {
            var settings = _settings.Current; var service = new LocalRecognitionService(); Status.Text = L.Get("ProcessingLocallyClickStopToCancel");
            if (Mode == "Cutout")
            {
                var model = (CutoutModelDefinition)CutoutModelBox.SelectedItem;
                settings = CutoutModels.ForModel(settings, model.Id);
                if (string.IsNullOrWhiteSpace(settings.CutoutModel) || !File.Exists(settings.CutoutModel))
                {
                    Status.Text = L.Format("0IsNotConfiguredOrItsFile", model.Id);
                    return;
                }
                var result = await OnnxImageRuntime.Shared.RemoveBackground(_image, settings, _operation.Token);
                if (_closed) return; _cutout = result; _document = null; ShowOriginal.IsChecked = false; SetPicture(result); CutoutButtons.IsEnabled = true; ResultTabs.SelectedIndex = 2;
                Status.Text = L.Format("01BackgroundRemovedPreservingOriginalResolution", model.Id,
                    settings.CutoutOnnxDevice is "CPU" or "DirectML" ? settings.CutoutOnnxDevice : settings.OnnxDevice); return;
            }
            RecognitionDocument document;
            if (Mode == "Formula") document = string.IsNullOrWhiteSpace(settings.FormulaRecognizerId)
                ? await service.RecognizeFormula(_image, settings, _operation.Token) : await PluginDocument(settings, _operation.Token);
            else if (Mode == "Table" && !string.IsNullOrWhiteSpace(settings.TableRecognizerId)) document = await PluginDocument(settings, _operation.Token);
            else if (Mode == "Table")
            {
                var table = await service.RecognizeTable(_image, settings, _operation.Token);
                document = table.Document; _pluginCells = table.Cells;
            }
            else
            {
                if (Mode != "Barcode" && settings.OcrBackend == "PaddleOCR" &&
                    new[] { settings.OcrDetectionModel, settings.OcrRecognitionModel, settings.OcrDictionary }.Any(path => string.IsNullOrWhiteSpace(path) || !File.Exists(path)))
                {
                    Status.Text = L.Get("LocalONNXPaddleOCRIsTheDefaultOpen");
                    return;
                }
                document = await (Mode == "Barcode" ? service.Barcodes(_image, settings, _operation.Token) : service.Recognize(_image, settings, _operation.Token));
            }
            if (_closed) return;
            _document = document; SetPicture(_image); SetSourceText(document.Text); _baseline = _sourceText;
            _cells = new ObservableCollection<RecognizedCell>(_pluginCells ?? RecognitionExport.Draft(document)); _pluginCells = null; CellsGrid.ItemsSource = _cells;
            _tableBaseline = TableSnapshot; _cutout = null; CutoutButtons.IsEnabled = false;
            ResultTabs.SelectedIndex = Mode == "Table" ? 1 : 0;
            Status.Text = document.Blocks.Count == 0 ? L.Get("NoContentRecognizedTryAnotherEngineCheck") : L.Format("01RegionsClickAnImageRegion", document.Backend, document.Blocks.Count);
            if (Mode == "Table") Status.Text += L.Get("RowsColumnsAndMergedCellsRecognizedReview");
        }
        catch (OperationCanceledException) { if (!_closed) Status.Text = L.Get("ProcessingStoppedOrTimedOutExistingResults"); }
        catch (Exception ex) { if (!_closed) Status.Text = L.Get("ProcessingFailed") + ex.GetBaseException().Message; }
        finally { _operation?.Dispose(); _operation = null; SetBusy(false); }
    }
    private RecognizedCell[]? _pluginCells;
    private sealed class EmptyServices : IServiceProvider { public object? GetService(Type serviceType) => null; }
    private async Task<RecognitionDocument> PluginDocument(AppSettings settings, CancellationToken token)
    {
        var mode = Mode;
        var id = mode == "Formula" ? settings.FormulaRecognizerId : settings.TableRecognizerId;
        if (string.IsNullOrWhiteSpace(id) || _plugins is null || !_plugins.TryGetRecognizer(id, out var plugin) || plugin is null)
            throw new InvalidOperationException(Mode == "Formula" ? L.Get("NoFormulaRecognitionPluginIsConfiguredInstall") : L.Get("TableModelPluginIsNotLoadedCheck"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(settings.RecognitionTimeoutSeconds));
        var result = await Task.Run(async () => await plugin.RecognizeAsync(_image, new PluginExecutionContext(new EmptyServices(), new Dictionary<string, object?> { ["task"] = mode }), timeout.Token), timeout.Token).WaitAsync(timeout.Token);
        if (result is null || result.Any(x => x is null || x.Text is null || x.Kind is null || x.Metadata is null)) throw new InvalidDataException(L.Get("ThePluginReturnedAnInvalidResult"));
        if (result.Count > 10000 || result.Sum(x => (long)x.Text.Length) > 500000) throw new InvalidDataException(L.Get("PluginResultIsTooLarge"));
        if (Mode == "Formula" && !result.Any(x => x.Kind.Equals("latex", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException(L.Get("TheFormulaPluginDidNotReturnA"));
        double Meta(RecognitionResult value, string key, double fallback) => value.Metadata.TryGetValue(key, out var item) && double.TryParse(item?.ToString(), out var number) && double.IsFinite(number) ? number : fallback;
        if (Mode == "Table")
            _pluginCells = result.Select(x => new RecognizedCell { Text = x.Text, Row = (int)Meta(x, "row", 1), Column = (int)Meta(x, "column", 1), RowSpan = (int)Meta(x, "rowSpan", 1), ColumnSpan = (int)Meta(x, "columnSpan", 1) }).ToArray();
        var blocks = result.Where(x => Mode != "Formula" || x.Kind.Equals("latex", StringComparison.OrdinalIgnoreCase)).Select(value =>
        {
            var x = Math.Clamp(Meta(value, "x", 0), 0, _image.PixelWidth - 1); var y = Math.Clamp(Meta(value, "y", 0), 0, _image.PixelHeight - 1);
            return new RecognizedBlock(value.Text, new Rect(x, y, Math.Clamp(Meta(value, "width", _image.PixelWidth), 1, _image.PixelWidth - x), Math.Clamp(Meta(value, "height", _image.PixelHeight), 1, _image.PixelHeight - y)), Kind: value.Kind);
        }).ToArray();
        return new RecognitionDocument(blocks, L.Get("ModelPlugin") + id);
    }
    private void SetBusy(bool busy)
    {
        _busy = busy; if (_closed) return; RunButton.IsEnabled = !busy; ModeBox.IsEnabled = !busy; CancelButton.IsEnabled = busy; CellsGrid.IsReadOnly = busy;
        CutoutModelBox.IsEnabled = TargetLanguageBox.IsEnabled = TextViewBox.IsEnabled = TranslateButton.IsEnabled = !busy;
        RefreshTextView();
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();
    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(Mode == "Cutout" ? "Scissors" : "ScanText");
    private void Original_Changed(object sender, RoutedEventArgs e) { if (Picture is not null) SetPicture(Mode != "Cutout" || ShowOriginal.IsChecked == true || _cutout is null ? _image : _cutout); }
    private void SetSourceText(string text)
    {
        _sourceText = text;
        InvalidateTranslation();
        TextViewBox.SelectedIndex = 0;
        RefreshTextView();
    }
    private void InvalidateTranslation()
    {
        _translatedText = "";
        _hasTranslation = false;
        if (CurrentTextView != "Source") TextViewBox.SelectedIndex = 0;
    }
    private void TargetLanguage_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (TargetLanguageBox.SelectedItem is not TranslationLanguage || TextResultTab is null) return;
        InvalidateTranslation();
    }
    private void TextView_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CurrentTextView != "Source" && !_hasTranslation)
        {
            TextViewBox.SelectedIndex = 0;
            if (Status is not null) Status.Text = L.Get("TranslateBeforeChoosingResultView");
            return;
        }
        RefreshTextView();
    }
    private void RefreshTextView()
    {
        if (TextEditor is null || TextViewBox?.SelectedItem is not ComboBoxItem selected) return;
        var view = selected.Tag?.ToString() ?? "Source";
        var text = view switch { "Translation" => _translatedText, "Bilingual" => BilingualText(_sourceText, _translatedText), _ => _sourceText };
        _syncing = true; TextEditor.Text = text; TextEditor.IsReadOnly = _busy || view != "Source"; _syncing = false;
    }
    private static string BilingualText(string source, string translation)
    {
        var sourceLines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var translatedLines = translation.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var lines = new List<string>(Math.Max(sourceLines.Length, translatedLines.Length) * 2);
        for (var index = 0; index < Math.Max(sourceLines.Length, translatedLines.Length); index++)
        {
            if (index < sourceLines.Length) lines.Add(sourceLines[index]);
            if (index < translatedLines.Length) lines.Add(translatedLines[index]);
        }
        return string.Join(Environment.NewLine, lines);
    }
    private static string NormalizeTranslatedLine(string text) => string.Join(" ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    private void Text_Changed(object sender, TextChangedEventArgs e)
    {
        if (_syncing || CurrentTextView != "Source") return;
        _sourceText = TextEditor.Text;
        InvalidateTranslation();
        if (Status is not null && !_busy) Status.Text = L.Get("TextEditedCopyExportUsesTheEdited");
    }
    private void CopyText_Click(object sender, RoutedEventArgs e) { try { OutputActions.CopyText(TextEditor.Text); if (CurrentTextView == "Source") _baseline = _sourceText; Status.Text = L.Get("CurrentTextCopied"); } catch (Exception ex) { Status.Text = ex.Message; } }
    private void ExportText_Click(object sender, RoutedEventArgs e)
    {
        if (SaveBytes(Encoding.UTF8.GetBytes(TextEditor.Text), Mode == "Formula" ? L.Get("LaTeXTexTextTxt") : L.Get("TextTxt"), "SnapSharp-text") && CurrentTextView == "Source") _baseline = _sourceText;
    }
    private void ExportTable_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!CellsGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !CellsGrid.CommitEdit(DataGridEditingUnit.Row, true)) throw new ArgumentException(L.Get("CorrectInvalidTableValuesFirst"));
            var csv = ((FrameworkElement)sender).Tag.ToString() == "csv";
            if (SaveBytes(csv ? RecognitionExport.Csv(_cells) : RecognitionExport.Xlsx(_cells), csv ? "CSV|*.csv" : "Excel|*.xlsx", "SnapSharp-table")) _tableBaseline = TableSnapshot;
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    private bool SaveBytes(byte[] bytes, string filter, string name)
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = filter, FileName = name, OverwritePrompt = false };
            if (dialog.ShowDialog(this) != true) return false;
            var overwrite = File.Exists(dialog.FileName);
            if (overwrite && MessageBox.Show(this, L.Get("TheFileAlreadyExistsReplaceIt"), L.Get("ConfirmOverwrite"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return false;
            OutputFileService.Write(dialog.FileName, bytes, overwrite); Status.Text = L.Get("Exported") + dialog.FileName; return true;
        }
        catch (Exception ex) { Status.Text = ex.Message; return false; }
    }
    private void CutoutOutput_Click(object sender, RoutedEventArgs e)
    {
        if (_cutout is null) return;
        try
        {
            var action = Enum.Parse<CaptureAction>(((FrameworkElement)sender).Tag.ToString()!);
            if (action == CaptureAction.Save) SaveImageWindow.Open(_cutout, new SkiaImageService(), _settings, this, ".png");
            else OutputRequested?.Invoke(_cutout, action);
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    private void TranslationSettings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke("Shield");
    private async void Translate_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(_settings.Current))
                ?? throw new InvalidOperationException("Could not prepare translation settings.");
            settings.TranslationTarget = ((TranslationLanguage)TargetLanguageBox.SelectedItem).Code;
            var local = settings.TranslationProvider == "Local";
            if (local && (!File.Exists(settings.LocalTranslationModel) || !File.Exists(settings.LocalTranslationRuntime)))
            {
                Status.Text = L.Get("LocalTranslationUsesQwenLlamaCppNot");
                SettingsRequested?.Invoke("Shield"); return;
            }
            if (!local && (!settings.AllowOnlineTranslation || settings.TranslationProvider == "LibreTranslate" && string.IsNullOrWhiteSpace(settings.TranslationEndpoint)))
            {
                Status.Text = L.Get("InSettingsChooseATranslationEngineClick");
                SettingsRequested?.Invoke("Shield"); return;
            }
            var text = _sourceText;
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException(L.Get("NoTextToTranslateRunOCROr"));
            if (!local && !PrivacyConsentDialog.Confirm(this, OnlineRecognitionService.TranslationUri(settings), text, null)) return;
            SetBusy(true); _operation = new CancellationTokenSource(); using var service = new OnlineRecognitionService();
            Status.Text = local ? L.Get("LoadingModelAndTranslatingLocally") + settings.LocalTranslationDevice + L.Get("FirstLoadMayBeSlow") : L.Get("CallingTheConfirmedTranslationAPI");
            var sourceLines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var translatedLines = new string[sourceLines.Length];
            var linesToTranslate = sourceLines.Select((line, index) => (line, index)).Where(item => !string.IsNullOrWhiteSpace(item.line)).ToArray();
            if (local)
            {
                var translatedBatch = await LocalTranslationService.TranslateLines(linesToTranslate.Select(item => item.line).ToArray(), settings, _operation.Token);
                var values = translatedBatch.Split('\n');
                if (values.Length != linesToTranslate.Length) throw new InvalidDataException("Local translation returned an unexpected number of lines.");
                for (var index = 0; index < values.Length; index++) translatedLines[linesToTranslate[index].index] = NormalizeTranslatedLine(values[index]);
            }
            else
            {
                foreach (var item in linesToTranslate)
                    translatedLines[item.index] = NormalizeTranslatedLine(await service.Translate(item.line, settings, true, _operation.Token));
            }
            var translated = string.Join(Environment.NewLine, translatedLines);
            if (!_closed)
            {
                _translatedText = translated;
                _hasTranslation = true;
                TextViewBox.SelectedIndex = 1;
                Status.Text = (local ? L.Get("LocalTranslationCompleteNothingWasUploadedTo") : L.Get("APITranslationComplete")) + L.Get("SourceTextWasPreserved");
            }
        }
        catch (Exception ex) { if (!_closed) Status.Text = L.Get("TranslationIncompleteSourceTextPreserved") + ex.GetBaseException().Message; }
        finally { _operation?.Dispose(); _operation = null; SetBusy(false); }
    }
    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            var settings = _settings.Current;
            if (!settings.AllowImageSearchUpload) throw new InvalidOperationException(L.Get("ConfigureAndAllowImageSearchUploadsIn"));
            var endpoint = OnlineRecognitionService.Endpoint(settings.ImageSearchEndpoint);
            if (!PrivacyConsentDialog.Confirm(this, endpoint, null, _image)) return;
            SetBusy(true); _operation = new CancellationTokenSource(); using var service = new OnlineRecognitionService();
            var png = await Task.Run(() => new SkiaImageService().Encode(_image, ".png"));
            var url = await service.Search(png, settings, true, _operation.Token);
            if (!_closed) Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) { if (!_closed) Status.Text = L.Get("ImageSearchIncomplete") + ex.GetBaseException().Message; }
        finally { _operation?.Dispose(); _operation = null; SetBusy(false); }
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        CellsGrid.CommitEdit();
        if (HasEdits && MessageBox.Show(this, L.Get("EditedTextOrTablesHaveNotBeen"), L.Get("DiscardChanges"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) e.Cancel = true;
    }
    internal static bool RunLayoutSelfTest(BitmapSource source)
    {
        var window = new RecognitionWindow(source, new SettingsService(), autoRun: false);
        try
        {
            var content = (FrameworkElement)window.Content;
            content.Measure(new System.Windows.Size(1140, 790)); content.Arrange(new Rect(0, 0, 1140, 790)); content.UpdateLayout();
            if (window.Picture.Source is not BitmapSource preview || preview.PixelWidth != source.PixelWidth || preview.PixelHeight != source.PixelHeight || window.Mode != "OCR" || window._busy)
                throw new InvalidOperationException("Clipboard entry must show the imported image before running a selected tool.");
            window.ModeBox.SelectedIndex = 4;
            if (window.CutoutModelBox.Visibility != Visibility.Visible || window.ResultTabs.SelectedIndex != 2)
                throw new InvalidOperationException("Clipboard image cutout mode selection failed.");
            window.ModeBox.SelectedIndex = 0;
            window._document = new RecognitionDocument([new("重复", new Rect(0, 0, 20, 10)), new("重复", new Rect(25, 15, 20, 10))], "test");
            window.SetSourceText(window._document.Text); window._baseline = window._sourceText;
            if (window.CurrentTextView != "Source" || window.TextEditor.IsReadOnly || window.TargetLanguageBox.Text != ((TranslationLanguage)window.TargetLanguageBox.SelectedItem).Name || window.PictureScroll.ActualWidth <= 200 || window.TextEditor.ActualWidth <= 200)
                throw new InvalidOperationException($"Recognition layout: view={window.CurrentTextView}, preview={window.PictureScroll.ActualWidth}, editor={window.TextEditor.ActualWidth}");
            window.TextViewBox.SelectedIndex = 1;
            if (window.CurrentTextView != "Source") throw new InvalidOperationException("Untranslated result view must return to the source text.");
            window._translatedText = "one" + Environment.NewLine + "two";
            window._hasTranslation = true;
            window.TextViewBox.SelectedIndex = 2;
            if (window.TextEditor.Text != string.Join(Environment.NewLine, window._document.Blocks[0].Text, "one", window._document.Blocks[1].Text, "two") || !window.TextEditor.IsReadOnly)
                throw new InvalidOperationException("Bilingual OCR result layout failed.");
            return true;
        }
        finally { window._baseline = window._sourceText; window._tableBaseline = window.TableSnapshot; window.Close(); }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
