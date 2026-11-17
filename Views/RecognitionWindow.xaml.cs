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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapSharp.Views;

public partial class RecognitionWindow : Window
{
    private readonly BitmapSource _image;
    private readonly SettingsService _settings;
    private readonly PluginCatalog? _plugins;
    private RecognitionDocument? _document;
    private ObservableCollection<RecognizedCell> _cells = [];
    private BitmapSource? _cutout;
    private CancellationTokenSource? _operation;
    private string _baseline = "";
    private string _tableBaseline = "[]";
    private string TableSnapshot => System.Text.Json.JsonSerializer.Serialize(_cells);
    private bool HasEdits => TextEditor.Text != _baseline || TableSnapshot != _tableBaseline;
    private bool _busy, _closed, _syncing;
    public event Action<BitmapSource, CaptureAction>? OutputRequested;
    public event Action<string>? SettingsRequested;
    private string Mode => ((ComboBoxItem)ModeBox.SelectedItem).Tag.ToString()!;
    public RecognitionWindow(BitmapSource image, SettingsService settings, PluginCatalog? plugins = null, string mode = "OCR", bool autoRun = true)
    {
        _image = image.IsFrozen ? image : image.Clone(); if (!_image.IsFrozen) _image.Freeze();
        _settings = settings; _plugins = plugins;
        InitializeComponent();
        ModeBox.SelectedIndex = mode switch { "Barcode" => 1, "Table" => 2, "Formula" => 3, "Cutout" => 4, _ => 0 };
        CutoutModels.Migrate(settings.Current);
        CutoutModelBox.ItemsSource = CutoutModels.All;
        CutoutModelBox.SelectedItem = CutoutModels.All.First(x => x.Id == settings.Current.CutoutDefaultModelId);
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
                if (_closed) return; _cutout = result; _document = null; Boxes.Children.Clear(); ShowOriginal.IsChecked = false; SetPicture(result); CutoutButtons.IsEnabled = true; ResultTabs.SelectedIndex = 2;
                Status.Text = L.Format("01BackgroundRemovedPreservingOriginalResolution", model.Id, settings.OnnxDevice); return;
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
            _document = document; SetPicture(_image); _syncing = true; TextEditor.Text = document.Text; _baseline = TextEditor.Text; _syncing = false;
            BlocksGrid.ItemsSource = document.Blocks; _cells = new ObservableCollection<RecognizedCell>(_pluginCells ?? RecognitionExport.Draft(document)); _pluginCells = null; CellsGrid.ItemsSource = _cells;
            _tableBaseline = TableSnapshot; _cutout = null; CutoutButtons.IsEnabled = false;
            ResultTabs.SelectedIndex = Mode == "Table" ? 1 : 0; DrawBoxes();
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
        _busy = busy; if (_closed) return; RunButton.IsEnabled = !busy; ModeBox.IsEnabled = !busy; CancelButton.IsEnabled = busy; TextEditor.IsReadOnly = busy; CellsGrid.IsReadOnly = busy;
        CutoutModelBox.IsEnabled = !busy;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();
    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(Mode == "Cutout" ? "Scissors" : "ScanText");
    private void Original_Changed(object sender, RoutedEventArgs e) { if (Picture is not null) SetPicture(Mode != "Cutout" || ShowOriginal.IsChecked == true || _cutout is null ? _image : _cutout); }
    private void DrawBoxes()
    {
        Boxes.Children.Clear(); if (_document is null) return;
        foreach (var block in _document.Blocks.Take(1000))
        {
            var rectangle = new System.Windows.Shapes.Rectangle { Width = block.Bounds.Width, Height = block.Bounds.Height,
                Stroke = Equals(BlocksGrid.SelectedItem, block) ? Brushes.Orange : Brushes.DodgerBlue, StrokeThickness = Math.Max(1, 1.5 / Zoom.Value), Fill = Brushes.Transparent };
            Canvas.SetLeft(rectangle, block.Bounds.X); Canvas.SetTop(rectangle, block.Bounds.Y); Boxes.Children.Add(rectangle);
        }
    }
    private void Picture_Click(object sender, MouseButtonEventArgs e)
    {
        if (_document is null || Mode == "Cutout") return;
        var point = e.GetPosition(PictureCanvas); var block = _document.Blocks.Where(x => x.Bounds.Contains(point)).OrderBy(x => x.Bounds.Width * x.Bounds.Height).FirstOrDefault();
        BlocksGrid.SelectedItem = block; if (block is not null) BlocksGrid.ScrollIntoView(block);
    }
    private void Block_Changed(object sender, SelectionChangedEventArgs e)
    {
        DrawBoxes(); if (_syncing || BlocksGrid.SelectedItem is not RecognizedBlock block) return;
        var index = TextEditor.Text == _document?.Text ? _document.Blocks.TakeWhile(x => !ReferenceEquals(x, block)).Sum(x => x.Text.Length + Environment.NewLine.Length) : -1;
        if (index >= 0 && block.Text.Length > 0) { _syncing = true; TextEditor.Select(index, block.Text.Length); _syncing = false; }
        PictureScroll.ScrollToVerticalOffset(Math.Max(0, block.Bounds.Top * Zoom.Value - PictureScroll.ViewportHeight / 2));
        PictureScroll.ScrollToHorizontalOffset(Math.Max(0, block.Bounds.Left * Zoom.Value - PictureScroll.ViewportWidth / 2));
    }
    private void Text_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing || _document is null || TextEditor.Text != _document.Text) return;
        var offset = 0;
        foreach (var block in _document.Blocks)
        {
            if (TextEditor.SelectionStart <= offset + block.Text.Length) { _syncing = true; BlocksGrid.SelectedItem = block; _syncing = false; DrawBoxes(); break; }
            offset += block.Text.Length + Environment.NewLine.Length;
        }
    }
    private void Text_Changed(object sender, TextChangedEventArgs e) { if (!_syncing && Status is not null && !_busy) Status.Text = L.Get("TextEditedCopyExportUsesTheEdited"); }
    private void CopyText_Click(object sender, RoutedEventArgs e) { try { OutputActions.CopyText(TextEditor.Text); _baseline = TextEditor.Text; Status.Text = L.Get("CurrentTextCopied"); } catch (Exception ex) { Status.Text = ex.Message; } }
    private void CopyTranslation_Click(object sender, RoutedEventArgs e) { try { OutputActions.CopyText(TranslatedText.Text); } catch (Exception ex) { Status.Text = ex.Message; } }
    private void ExportText_Click(object sender, RoutedEventArgs e)
    {
        if (SaveBytes(Encoding.UTF8.GetBytes(TextEditor.Text), Mode == "Formula" ? L.Get("LaTeXTexTextTxt") : L.Get("TextTxt"), "SnapSharp-text")) _baseline = TextEditor.Text;
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
            var settings = _settings.Current;
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
            var text = TextEditor.SelectionLength > 0 ? TextEditor.SelectedText : TextEditor.Text;
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException(L.Get("NoTextToTranslateRunOCROr"));
            if (!local && !PrivacyConsentDialog.Confirm(this, OnlineRecognitionService.TranslationUri(settings), text, null)) return;
            SetBusy(true); _operation = new CancellationTokenSource(); using var service = new OnlineRecognitionService();
            Status.Text = local ? L.Get("LoadingModelAndTranslatingLocally") + settings.LocalTranslationDevice + L.Get("FirstLoadMayBeSlow") : L.Get("CallingTheConfirmedTranslationAPI");
            var translated = local ? await LocalTranslationService.Translate(text, settings, _operation.Token)
                : await service.Translate(text, settings, true, _operation.Token);
            if (!_closed) { TranslatedText.Text = translated; ResultTabs.SelectedIndex = 3; Status.Text = (local ? L.Get("LocalTranslationCompleteNothingWasUploadedTo") : L.Get("APITranslationComplete")) + L.Get("SourceTextWasPreserved"); }
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
            window.TextEditor.Text = window._document.Text; window._baseline = window.TextEditor.Text;
            window.BlocksGrid.ItemsSource = window._document.Blocks;
            window.BlocksGrid.SelectedItem = window._document.Blocks[1];
            if (window.Boxes.Children.Count != 2 || window.TextEditor.SelectionStart != 2 + Environment.NewLine.Length || window.PictureScroll.ActualWidth <= 200 || window.TextEditor.ActualWidth <= 200)
                throw new InvalidOperationException($"Recognition layout: boxes={window.Boxes.Children.Count}, selection={window.TextEditor.SelectionStart}, preview={window.PictureScroll.ActualWidth}, editor={window.TextEditor.ActualWidth}");
            return true;
        }
        finally { window._baseline = window.TextEditor.Text; window._tableBaseline = window.TableSnapshot; window.Close(); }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
