using L = SnapSharp.Localization.UiText;
using SnapSharp.Models;
using SnapSharp.Services;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using TextBox = System.Windows.Controls.TextBox;

namespace SnapSharp.Views;

public partial class SettingsWindow : Window
{
    private CancellationTokenSource? _downloadCancellation;
    private CancellationTokenSource? ModelDownload
    {
        get => _downloadCancellation;
        set { _downloadCancellation = value; SetValue(DownloadSourceEnabledProperty, value is null); }
    }
    public static readonly DependencyProperty DownloadSourceEnabledProperty = DependencyProperty.Register(nameof(DownloadSourceEnabled), typeof(bool), typeof(SettingsWindow), new PropertyMetadata(true));
    public bool DownloadSourceEnabled => (bool)GetValue(DownloadSourceEnabledProperty);
    public static readonly DependencyProperty SelectedModelDownloadSourceProperty = DependencyProperty.Register(nameof(SelectedModelDownloadSource), typeof(string), typeof(SettingsWindow), new PropertyMetadata(ModelDownloadSource.Official));
    public string SelectedModelDownloadSource { get => (string)GetValue(SelectedModelDownloadSourceProperty); set => SetValue(SelectedModelDownloadSourceProperty, value); }
    private CancellationTokenSource? _translationTest;
    public static readonly DependencyProperty CompactLayoutProperty = DependencyProperty.Register(nameof(CompactLayout), typeof(bool), typeof(SettingsWindow), new PropertyMetadata(false));
    public bool CompactLayout { get => (bool)GetValue(CompactLayoutProperty); private set => SetValue(CompactLayoutProperty, value); }
    private readonly SettingsService _settings;
    private readonly StartupRegistrationService _startup;
    private readonly GlobalHotkeyHost _hotkey;
    private bool _ready;
    private Dictionary<string, CutoutModelConfiguration> _cutoutModels = new();
    private CutoutModelDefinition? _editingCutout;

    public SettingsWindow(SettingsService settings, StartupRegistrationService startup, GlobalHotkeyHost hotkey)
    {
        _settings = settings; _startup = startup; _hotkey = hotkey;
        InitializeComponent();
        var informationalVersion = typeof(SettingsWindow).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
        AboutVersion.Text = L.Format("AboutVersion", informationalVersion?.Split('+')[0] ?? typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? L.Get("Unknown"));
        DialogLayoutService.Attach(this);
        SizeChanged += (_, _) => { CompactLayout = ActualWidth < 840; FooterHint.Visibility = ActualWidth < 700 ? Visibility.Collapsed : Visibility.Visible; };
        Categories.SelectionChanged += (_, e) => { if (ReferenceEquals(e.OriginalSource, Categories)) BringSelectedCategoryIntoView(); };
        Loaded += (_, _) => BringSelectedCategoryIntoView();
        var current = settings.Current;
        SelectedModelDownloadSource = current.ModelDownloadSource == ModelDownloadSource.Domestic ? ModelDownloadSource.Domestic : ModelDownloadSource.Official;
        InterfaceLanguageBox.ItemsSource = LocalizationService.Languages;
        InterfaceLanguageBox.SelectedValue = LocalizationService.Languages.Any(x => x.Code == current.InterfaceLanguage)
            ? current.InterfaceLanguage : "";
        SaveDirectory.Text = current.SaveDirectory;
        RecordingSaveDirectory.Text = current.RecordingSaveDirectory;
        Quality.Value = current.ImageQuality;
        OutputFormatBox.ItemsSource = OutputFormat.All.Where(x => x.IsAvailable).ToArray();
        OutputFormatBox.SelectedItem = OutputFormat.Find(current.OutputFormat);
        WebpLossless.IsChecked = current.WebpLossless;
        AutoSaveImages.IsChecked = current.AutoSaveImages;
        ArchiveByDate.IsChecked = current.ArchiveByDate;
        FileNameTemplate.Text = current.FileNameTemplate;
        HistoryLimit.Text = current.HistoryLimit.ToString();
        PinOpacity.Value = current.DefaultPinOpacity;
        PinSnapToEdges.IsChecked = current.PinSnapToEdges;
        PinSnapDistance.Text = current.PinSnapDistance.ToString(CultureInfo.InvariantCulture);
        TextFontFamily.Text = current.TextPinFontFamily;
        TextFontSize.Text = current.TextPinFontSize.ToString(CultureInfo.InvariantCulture);
        TextWidth.Text = current.TextPinWidth.ToString();
        TextForeground.Text = current.TextPinForeground;
        TextBackground.Text = current.TextPinBackground;
        SaveHistory.IsChecked = current.SaveHistory;
        StartWithWindows.IsChecked = startup.IsEnabled();
        IncludeCursor.IsChecked = current.IncludeCursor;
        PreferWgcCapture.IsChecked = current.PreferWgcCapture;
        EnableExternalPlugins.IsChecked = current.EnableExternalPlugins;
        RequireSignedPlugins.IsChecked = current.RequireSignedPlugins;
        RegionHotkey.Text = current.RegionHotkey;
        FullScreenHotkey.Text = current.FullScreenHotkey;
        ActiveWindowHotkey.Text = current.ActiveWindowHotkey;
        ClipboardPinHotkey.Text = current.ClipboardPinHotkey;
        TogglePinsHotkey.Text = current.TogglePinsHotkey;
        RestorePinHotkey.Text = current.RestorePinHotkey;
        RestoreInteractionHotkey.Text = current.RestorePinInteractionHotkey;
        DataDirectory.Text = AppPaths.DataLocation.SelectedDirectory;
        ActiveDataDirectory.Text = L.Format("ActiveDataDirectory", AppPaths.Root);
        OcrBackendBox.SelectedIndex = current.OcrBackend switch { "Windows" => 1, "Tesseract" => 2, _ => 0 };
        OnnxDeviceBox.SelectedIndex = current.OnnxDevice == "DirectML" ? 1 : 0;
        OnnxGpuId.Text = current.OnnxDeviceId.ToString();
        TesseractDirectory.Text = _settings.StoreModelPath(current.TesseractDataDirectory); TesseractLanguages.Text = current.TesseractLanguages;
        OcrDetPath.Text = _settings.StoreModelPath(current.OcrDetectionModel); OcrRecPath.Text = _settings.StoreModelPath(current.OcrRecognitionModel); OcrDictPath.Text = _settings.StoreModelPath(current.OcrDictionary);
        OcrLanguage.Text = current.WindowsOcrLanguage; OcrMaxEdge.Text = current.OcrMaximumEdge.ToString(); ModelThreads.Text = current.RecognitionThreads.ToString(); ModelTimeout.Text = current.RecognitionTimeoutSeconds.ToString();
        BarcodeRotate.IsChecked = current.BarcodeRotate; CutoutPath.Text = _settings.StoreModelPath(current.CutoutModel); CutoutSize.Text = current.CutoutInputSize.ToString();
        CutoutProfileBox.SelectedIndex = current.CutoutProfile switch { "ISNet" => 1, "BiRefNet" => 2, "BiRefNetMatting" => 3, "BiRefNetAlpha" => 4, _ => 0 };
        CutoutModels.Migrate(current);
        _cutoutModels = JsonSerializer.Deserialize<Dictionary<string, CutoutModelConfiguration>>(JsonSerializer.Serialize(current.CutoutModels))!;
        CutoutDefaultBox.ItemsSource = CutoutModels.All;
        CutoutDefaultBox.SelectedItem = CutoutModels.All.First(x => x.Id == current.CutoutDefaultModelId);
        CutoutPackageBox.ItemsSource = CutoutModels.All;
        CutoutPackageBox.SelectedItem = CutoutDefaultBox.SelectedItem;
        TablePluginId.Text = current.TableRecognizerId; FormulaPluginId.Text = current.FormulaRecognizerId;
        TableModelPath.Text = _settings.StoreModelPath(current.TableStructureModel); FormulaDirectory.Text = _settings.StoreModelPath(current.FormulaModelDirectory);
        AllowTranslation.IsChecked = current.AllowOnlineTranslation; TranslateUrl.Text = current.TranslationEndpoint; TranslateTarget.Text = current.TranslationTarget; TranslateSource.Text = current.TranslationSource;
        TranslationProviderBox.SelectedIndex = current.TranslationProvider switch { "Local" => 3, "MyMemory" => 0, "Google" => 2, _ => 1 };
        LocalTranslationModelPath.Text = _settings.StoreModelPath(current.LocalTranslationModel);
        LocalTranslationRuntimePath.Text = _settings.StoreModelPath(current.LocalTranslationRuntime);
        LocalTranslationDeviceBox.SelectedIndex = current.LocalTranslationDevice == "Vulkan" ? 1 : 0;
        AllowSearch.IsChecked = current.AllowImageSearchUpload; SearchUrl.Text = current.ImageSearchEndpoint;
        _ready = true;
        TranslationProvider_Changed(this, null!);
        var recording = current.Recording;
        RecordingCaptureBackend.SelectedIndex = recording.CaptureBackend == "DXGI" ? 1 : 0;
        RecordingBitrate.Text = recording.BitrateKbps.ToString(); RecordingCountdown.Text = recording.CountdownSeconds.ToString();
        RecordingFormat.SelectedIndex = recording.Format == "GIF" ? 1 : 0; RecordingFps.SelectedIndex = recording.FrameRate switch { 15 => 0, 60 => 2, _ => 1 };
        RecordingQuality.SelectedIndex = recording.Quality switch { "流畅" => 0, "高清" => 2, _ => 1 };
        RecordingHardware.IsChecked = recording.HardwareEncoding; RecordingCursor.IsChecked = recording.IncludeCursor; RecordingClicks.IsChecked = recording.HighlightClicks;
        RecordingSystemAudio.IsChecked = recording.RecordSystemAudio;
        RecordingMicrophone.IsChecked = recording.RecordMicrophone;
        RecordingCamera.IsChecked = recording.RecordCamera;
        RecordingStop.Text = recording.StopAfterSeconds.ToString(); RecordingGifEdge.Text = recording.GifMaximumEdge.ToString();
        Output_Changed(this, null!); Template_Changed(this, null!);
        Loaded += (_, _) => _hotkey.Suspend();
        Closed += (_, _) =>
        {
            ModelDownload?.Cancel();
            _translationTest?.Cancel();
            try { _hotkey.Resume(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, L.Get("ShortcutRestorationFailedReopenSettingsToCheck"), MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = SaveDirectory.Text, Title = L.Get("ChooseDefaultSaveFolder") };
        if (dialog.ShowDialog(this) == true) SaveDirectory.Text = dialog.FolderName;
    }
    private void BrowseDataDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = DataDirectory.Text, Title = L.Get("DataFolder") };
        if (dialog.ShowDialog(this) == true) DataDirectory.Text = dialog.FolderName;
    }
    private void DefaultDataDirectory_Click(object sender, RoutedEventArgs e) =>
        DataDirectory.Text = AppPaths.DataLocation.DefaultDirectory;
    private void RecordingBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = RecordingSaveDirectory.Text, Title = L.Get("RecordingSaveFolder") };
        if (dialog.ShowDialog(this) == true) RecordingSaveDirectory.Text = dialog.FolderName;
    }
    private void Output_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        var extension = ((OutputFormat)OutputFormatBox.SelectedItem).Extension;
        WebpLossless.Visibility = extension == ".webp" ? Visibility.Visible : Visibility.Collapsed;
        QualitySection.Visibility = extension == ".jpg" || extension == ".webp" && WebpLossless.IsChecked != true ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Template_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_ready) return;
        try { TemplatePreview.Text = L.Get("Example") + OutputFileService.ExpandTemplate(FileNameTemplate.Text, DateTime.Now, 1920, 1080, 1); }
        catch (Exception ex) { TemplatePreview.Text = ex.Message; }
    }
    private void PickColor_Click(object sender, RoutedEventArgs e)
    {
        var field = sender is FrameworkElement { Tag: "Foreground" } ? TextForeground : TextBackground;
        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
        if (PinContentService.TryParseColor(field.Text, out var color)) dialog.Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B);
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) field.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
    }
    private void Hotkey_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab or Key.Escape) return;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) { e.Handled = true; return; }
        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            if (key is Key.Delete or Key.Back) ((TextBox)sender).Clear();
            e.Handled = true;
            return;
        }
        var parts = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        if (HotkeyGesture.TryParse(string.Join('+', parts), out var gesture, out _)) ((TextBox)sender).Text = gesture!.DisplayText;
        e.Handled = true;
    }

    internal AppSettings BuildCandidate()
    {
        var candidate = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(_settings.Current))!;
        candidate.ModelDownloadSource = SelectedModelDownloadSource;
        candidate.InterfaceLanguage = InterfaceLanguageBox.SelectedValue as string ?? "";
        if (!System.IO.Path.IsPathFullyQualified(SaveDirectory.Text)) throw new ArgumentException(L.Get("OutputAndFilesChooseAnAbsoluteSave"));
        _ = OutputFileService.ExpandTemplate(FileNameTemplate.Text, DateTime.Now, 1920, 1080, 1);
        candidate.SaveDirectory = SaveDirectory.Text.Trim();
        if (!Path.IsPathFullyQualified(RecordingSaveDirectory.Text.Trim())) throw new ArgumentException(L.Get("RecordingFolderMustBeAbsolute"));
        candidate.RecordingSaveDirectory = RecordingSaveDirectory.Text.Trim();
        candidate.ImageQuality = (int)Quality.Value;
        candidate.Recording = new RecordingPreferences
        {
            RecordSystemAudio = RecordingSystemAudio.IsChecked == true,
            RecordMicrophone = RecordingMicrophone.IsChecked == true,
            RecordCamera = RecordingCamera.IsChecked == true,
            SpeakerDeviceId = candidate.Recording.SpeakerDeviceId,
            MicrophoneDeviceId = candidate.Recording.MicrophoneDeviceId,
            CameraDeviceId = candidate.Recording.CameraDeviceId,
            CaptureBackend = ((System.Windows.Controls.ComboBoxItem)RecordingCaptureBackend.SelectedItem).Tag.ToString()!,
            BitrateKbps = (int)Number(RecordingBitrate, 0, 100000, L.Get("RecordingBitrate"), true), CountdownSeconds = (int)Number(RecordingCountdown, 0, 10, L.Get("StartCountdown"), true),
            Format = ((System.Windows.Controls.ComboBoxItem)RecordingFormat.SelectedItem).Content.ToString()!,
            FrameRate = int.Parse(((System.Windows.Controls.ComboBoxItem)RecordingFps.SelectedItem).Content.ToString()!),
            Quality = ((System.Windows.Controls.ComboBoxItem)RecordingQuality.SelectedItem).Tag.ToString()!,
            HardwareEncoding = RecordingHardware.IsChecked == true, IncludeCursor = RecordingCursor.IsChecked == true, HighlightClicks = RecordingClicks.IsChecked == true,
            StopAfterSeconds = (int)Number(RecordingStop, 0, 86400, L.Get("RecordingStopTime"), true), GifMaximumEdge = (int)Number(RecordingGifEdge, 160, 800, L.Get("GIFLongestEdge"), true)
        };
        candidate.Recording.Validate();
        candidate.OutputFormat = ((OutputFormat)OutputFormatBox.SelectedItem).Extension;
        candidate.WebpLossless = WebpLossless.IsChecked == true;
        candidate.AutoSaveImages = AutoSaveImages.IsChecked == true;
        candidate.ArchiveByDate = ArchiveByDate.IsChecked == true;
        candidate.FileNameTemplate = FileNameTemplate.Text;
        candidate.HistoryLimit = (int)Number(HistoryLimit, 0, 1000, L.Get("HistoryLimit"), integer: true);
        candidate.DefaultPinOpacity = PinOpacity.Value;
        candidate.PinSnapToEdges = PinSnapToEdges.IsChecked == true;
        candidate.PinSnapDistance = Number(PinSnapDistance, 0, 48, L.Get("SnapDistance"));
        candidate.TextPinFontSize = Number(TextFontSize, 10, 64, L.Get("TextFontSize"));
        candidate.TextPinWidth = (int)Number(TextWidth, 200, 1200, L.Get("TextWidth"), integer: true);
        candidate.TextPinFontFamily = TextFontFamily.Text.Trim();
        if (string.IsNullOrWhiteSpace(candidate.TextPinFontFamily)) throw new ArgumentException(L.Get("PinsFontNameCannotBeEmpty"));
        if (!PinContentService.TryParseColor(TextForeground.Text, out var fg) || !PinContentService.TryParseColor(TextBackground.Text, out var bg)) throw new ArgumentException(L.Get("PinsInvalidTextOrBackgroundColor"));
        candidate.TextPinForeground = PinContentService.Hex(fg); candidate.TextPinBackground = PinContentService.Hex(bg);
        candidate.SaveHistory = SaveHistory.IsChecked == true;
        candidate.StartWithWindows = StartWithWindows.IsChecked == true;
        candidate.IncludeCursor = IncludeCursor.IsChecked == true;
        candidate.PreferWgcCapture = PreferWgcCapture.IsChecked == true;
        candidate.EnableExternalPlugins = EnableExternalPlugins.IsChecked == true;
        candidate.RequireSignedPlugins = RequireSignedPlugins.IsChecked == true;
        candidate.OcrBackend = ((System.Windows.Controls.ComboBoxItem)OcrBackendBox.SelectedItem).Tag.ToString()!;
        candidate.OnnxDevice = ((System.Windows.Controls.ComboBoxItem)OnnxDeviceBox.SelectedItem).Tag.ToString()!;
        candidate.OnnxDeviceId = (int)Number(OnnxGpuId, 0, 15, L.Get("GPUIndex"), true);
        candidate.OcrDetectionModel = ModelPath(OcrDetPath, candidate.OcrBackend == "PaddleOCR"); candidate.OcrRecognitionModel = ModelPath(OcrRecPath, candidate.OcrBackend == "PaddleOCR"); candidate.OcrDictionary = ModelPath(OcrDictPath, candidate.OcrBackend == "PaddleOCR");
        candidate.TesseractDataDirectory = _settings.ResolveModelPath(TesseractDirectory.Text); candidate.TesseractLanguages = TesseractLanguages.Text.Trim();
        if (candidate.OcrBackend == "Tesseract") LocalRecognitionService.ValidateTesseractData(candidate.TesseractDataDirectory, candidate.TesseractLanguages);
        candidate.WindowsOcrLanguage = OcrLanguage.Text.Trim(); candidate.OcrMaximumEdge = (int)Number(OcrMaxEdge, 320, 2048, L.Get("OCRLongestEdge"), true);
        candidate.RecognitionThreads = (int)Number(ModelThreads, 1, 8, L.Get("ModelThreads"), true); candidate.RecognitionTimeoutSeconds = (int)Number(ModelTimeout, 1, 120, L.Get("InferenceTimeout"), true);
        candidate.BarcodeRotate = BarcodeRotate.IsChecked == true; candidate.CutoutModel = ModelPath(CutoutPath); candidate.CutoutInputSize = (int)Number(CutoutSize, 32, 2048, L.Get("BackgroundRemovalInputSize"), true);
        candidate.CutoutProfile = ((System.Windows.Controls.ComboBoxItem)CutoutProfileBox.SelectedItem).Tag.ToString()!;
        StoreCutoutConfiguration();
        candidate.CutoutModels = JsonSerializer.Deserialize<Dictionary<string, CutoutModelConfiguration>>(JsonSerializer.Serialize(_cutoutModels))!;
        candidate.CutoutDefaultModelId = ((CutoutModelDefinition)CutoutDefaultBox.SelectedItem).Id;
        var defaultCutout = CutoutModels.ForModel(candidate, candidate.CutoutDefaultModelId);
        candidate.CutoutModel = defaultCutout.CutoutModel; candidate.CutoutProfile = defaultCutout.CutoutProfile; candidate.CutoutInputSize = defaultCutout.CutoutInputSize;
        candidate.FormulaRecognizerId = FormulaPluginId.Text.Trim(); candidate.TableRecognizerId = TablePluginId.Text.Trim();
        candidate.TableStructureModel = ModelPath(TableModelPath);
        candidate.FormulaModelDirectory = _settings.ResolveModelPath(FormulaDirectory.Text);
        if (candidate.FormulaModelDirectory.Length > 0)
        {
            foreach (var name in new[] { "encoder.onnx", "decoder.onnx", "image_resizer.onnx", "tokenizer.json" })
                OnnxImageRuntime.RequireFile(System.IO.Path.Combine(candidate.FormulaModelDirectory, name), L.Get("Formula") + name);
        }
        candidate.AllowOnlineTranslation = AllowTranslation.IsChecked == true; candidate.TranslationEndpoint = TranslateUrl.Text.Trim(); candidate.TranslationTarget = TranslateTarget.Text.Trim(); candidate.TranslationSource = TranslateSource.Text.Trim();
        candidate.TranslationProvider = ((System.Windows.Controls.ComboBoxItem)TranslationProviderBox.SelectedItem).Tag.ToString()!;
        candidate.LocalTranslationModel = _settings.ResolveModelPath(LocalTranslationModelPath.Text);
        candidate.LocalTranslationRuntime = _settings.ResolveModelPath(LocalTranslationRuntimePath.Text);
        candidate.LocalTranslationDevice = ((System.Windows.Controls.ComboBoxItem)LocalTranslationDeviceBox.SelectedItem).Tag.ToString()!;
        if (!System.Text.RegularExpressions.Regex.IsMatch(candidate.TranslationSource, "^[a-zA-Z-]{2,20}$")) throw new ArgumentException(L.Get("InvalidSourceLanguageCodeUseEnZh"));
        candidate.AllowImageSearchUpload = AllowSearch.IsChecked == true; candidate.ImageSearchEndpoint = SearchUrl.Text.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(candidate.TranslationTarget, "^[a-zA-Z-]{2,20}$")) throw new ArgumentException(L.Get("InvalidTargetLanguageCode"));
        if (candidate.AllowOnlineTranslation && candidate.TranslationProvider != "Local")
        {
            OnlineRecognitionService.TranslationUri(candidate);
            if (candidate.TranslationProvider == "MyMemory" && candidate.TranslationSource == "auto") throw new ArgumentException(L.Get("MyMemoryRequiresASourceLanguageSuchAs"));
            if (!System.Text.RegularExpressions.Regex.IsMatch(candidate.TranslationTarget, "^[a-zA-Z-]{2,20}$")) throw new ArgumentException(L.Get("InvalidTargetLanguageCode"));
        }
        if (candidate.AllowImageSearchUpload) OnlineRecognitionService.Endpoint(candidate.ImageSearchEndpoint);
        candidate.RegionHotkey = NormalizeHotkey(RegionHotkey.Text, L.Get("RegionCapture"));
        candidate.FullScreenHotkey = NormalizeHotkey(FullScreenHotkey.Text, L.Get("FullScreenCapture"));
        candidate.ActiveWindowHotkey = NormalizeHotkey(ActiveWindowHotkey.Text, L.Get("ActiveWindowCapture"));
        candidate.ClipboardPinHotkey = NormalizeHotkey(ClipboardPinHotkey.Text, L.Get("PinClipboardContent"), true);
        candidate.TogglePinsHotkey = NormalizeHotkey(TogglePinsHotkey.Text, L.Get("TogglePins"), true);
        candidate.RestorePinHotkey = NormalizeHotkey(RestorePinHotkey.Text, L.Get("RestorePin"), true);
        candidate.RestorePinInteractionHotkey = NormalizeHotkey(RestoreInteractionHotkey.Text, L.Get("RestoreInteraction"), true);
        GlobalHotkeyHost.CreateRegistrations(candidate);
        return candidate;
        
    }
    private static double Number(TextBox field, double min, double max, string label, bool integer = false)
    {
        if (double.TryParse(field.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) && number >= min && number <= max && (!integer || number == Math.Truncate(number))) return number;
        throw new ArgumentException(L.Format("0MustBeAValid3Between", label, min, max, (integer ? L.Get("IntegerValue") : L.Get("NumericValue"))));
    }
    private string ModelPath(TextBox field, bool validate = true)
    {
        var path = _settings.ResolveModelPath(field.Text);
        if (validate && path.Length != 0 && (!System.IO.Path.IsPathFullyQualified(path) || !System.IO.File.Exists(path))) throw new ArgumentException(L.Get("ModelOrDictionaryFileNotFound") + path);
        return path;
    }
    private void ModelBrowse_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name } || FindName(name) is not TextBox field) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = name == "OcrDictPath" ? L.Get("UTF8DictionaryTxtAllFiles") : L.Get("ONNXModelOnnx") };
        if (dialog.ShowDialog(this) == true) field.Text = _settings.StoreModelPath(dialog.FileName);
    }
    private void CancelOcrDownload_Click(object sender, RoutedEventArgs e) => ModelDownload?.Cancel();
    private void BrowseFormulaDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = L.Get("ChooseLaTeXOCRModelFolder") };
        if (dialog.ShowDialog(this) == true) FormulaDirectory.Text = _settings.StoreModelPath(dialog.FolderName);
    }
    private async void DownloadSpecialist_Click(object sender, RoutedEventArgs e)
    {
        if (ModelDownload is not null) { SpecialistStatus.Text = L.Get("ADownloadIsInProgressWaitOr"); return; }
        var table = (sender as FrameworkElement)?.Tag as string == "Table";
        using var cancellation = new CancellationTokenSource(); ModelDownload = cancellation;
        DownloadTableButton.IsEnabled = DownloadFormulaButton.IsEnabled = false; CancelSpecialistButton.IsEnabled = true;
        SpecialistStatus.Text = L.Get("DownloadingAndVerifyingMatchingLocalModels");
        try
        {
            var root = System.IO.Path.Combine(AppPaths.Root, "models");
            var progress = new Progress<string>(value => SpecialistStatus.Text = value);
            var path = await SpecialistModelDownloadService.Install(root, table, progress, cancellation.Token, sourceKind: SelectedModelDownloadSource);
            OcrModelDownloadService.ModelPaths? ocr = null;
            if (table && ((System.Windows.Controls.ComboBoxItem)OcrBackendBox.SelectedItem).Tag.ToString() == "PaddleOCR" &&
                new[] { OcrDetPath.Text, OcrRecPath.Text, OcrDictPath.Text }.Any(p => !System.IO.File.Exists(_settings.ResolveModelPath(p))))
                ocr = await new OcrModelDownloadService().Download(root, progress, cancellation.Token, SelectedModelDownloadSource);
            if (!IsLoaded) return;
            if (table)
            {
                TableModelPath.Text = _settings.StoreModelPath(path); TablePluginId.Text = "";
                if (ocr is not null) { OcrDetPath.Text = _settings.StoreModelPath(ocr.Detection); OcrRecPath.Text = _settings.StoreModelPath(ocr.Recognition); OcrDictPath.Text = _settings.StoreModelPath(ocr.Dictionary); }
            }
            else { FormulaDirectory.Text = _settings.StoreModelPath(path); FormulaPluginId.Text = ""; }
            SpecialistStatus.Text = L.Get("DownloadSHA256AndModelLoadingChecksPassed");
        }
        catch (OperationCanceledException) { SpecialistStatus.Text = L.Get("CanceledExistingConfigurationPreserved"); }
        catch (Exception ex) { SpecialistStatus.Text = L.Get("SetupIncompleteExistingConfigurationPreservedYouCan") + ex.GetBaseException().Message; }
        finally { ModelDownload = null; DownloadTableButton.IsEnabled = DownloadFormulaButton.IsEnabled = true; CancelSpecialistButton.IsEnabled = false; }
    }
    private void RecordingSetup_Click(object sender, RoutedEventArgs e) => RecordingSetupWindow.Open(this);
    private void ConfigureWindowsOcr_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var languages = Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages;
            var language = languages.FirstOrDefault(x => x.LanguageTag.Equals(OcrLanguage.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? languages.FirstOrDefault(x => x.LanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) ?? languages.FirstOrDefault();
            if (language is null) { WindowsOcrStatus.Text = L.Get("NoWindowsOCRLanguagePacksAvailableInstall"); return; }
            OcrLanguage.Text = language.LanguageTag; OcrBackendBox.SelectedIndex = 1;
            WindowsOcrStatus.Text = L.Format("SelectedOcrLanguage", language.DisplayName, language.LanguageTag);
        }
        catch (Exception ex) { WindowsOcrStatus.Text = L.Get("CouldNotReadSystemOCRLanguages") + ex.GetBaseException().Message; }
    }
    private void OpenLanguageSettings_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:regionlanguage") { UseShellExecute = true }); }
        catch (Exception ex) { WindowsOcrStatus.Text = L.Get("CouldNotOpenSystemSettingsGoTo") + ex.Message; }
    }
    private void ConfigureTranslation_Click(object sender, RoutedEventArgs e)
    {
        TranslateSource.Text = "en"; TranslateTarget.Text = "zh";
        var provider = ((System.Windows.Controls.ComboBoxItem)TranslationProviderBox.SelectedItem).Tag.ToString();
        if (provider == "Local")
        {
            DownloadLocalTranslation_Click(sender, e);
            return;
        }
        AllowTranslation.IsChecked = true;
        TranslationStatus.Text = provider == "LibreTranslate" ? L.Get("CustomEnglishToChineseServiceSelectedEnter")
            : L.Format("Configured0ForEnglishToChineseTranslation", provider);
    }
    private void TranslationProvider_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        var provider = ((System.Windows.Controls.ComboBoxItem)TranslationProviderBox.SelectedItem).Tag.ToString();
        TranslateUrl.IsEnabled = provider == "LibreTranslate";
        LocalTranslationPanel.Visibility = provider == "Local" ? Visibility.Visible : Visibility.Collapsed;
        AllowTranslation.IsEnabled = provider != "Local";
        TranslationPricing.Text = RecognitionProviderCatalog.Translation(provider!).Notice;
        TranslationStatus.Text = provider switch
        {
            "Local" => L.Get("LocalTranslationChooseCPUOrVulkanGPU"),
            "Google" => L.Get("GoogleGTranslateIncludedNoModelOrKey"),
            "MyMemory" => L.Get("MyMemoryNoModelOrKeyNeededRequires"),
            _ => L.Get("LibreTranslateUsesYourCompatibleEndpointWithoutDownloading")
        };
    }
    private void OpenTranslationWebsite_Click(object sender, RoutedEventArgs e) =>
        OpenWebsite(RecognitionProviderCatalog.Translation(((System.Windows.Controls.ComboBoxItem)TranslationProviderBox.SelectedItem).Tag.ToString()!).Website);
    private void OpenProviderWebsite_Click(object sender, RoutedEventArgs e) =>
        OpenWebsite(RecognitionProviderCatalog.Website(((FrameworkElement)sender).Tag.ToString()!));
    private void AboutLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Documents.Hyperlink { NavigateUri: not null } link) return;
        var website = link.NavigateUri.AbsoluteUri;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(website) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, L.Get("CouldNotOpenTheLinkVisitManually") + website + Environment.NewLine + ex.Message, L.Get("CouldNotOpenLink"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private void AboutDocument_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string document }) return;
        var path = document switch
        {
            "LICENSE" => Path.Combine(AppContext.BaseDirectory, "LICENSE"),
            "THIRD-PARTY-NOTICES.md" => Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md"),
            "ThirdPartyLicenses" => Path.Combine(AppContext.BaseDirectory, "ThirdPartyLicenses"),
            _ => ""
        };
        try
        {
            if (path.Length == 0 || !File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException(L.Get("TheCorrespondingLicenseMaterialWasNotFound"), path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.GetBaseException().Message, L.Get("CouldNotOpenLicenseMaterial"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private void OpenWebsite(string website)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(website) { UseShellExecute = true }); }
        catch (Exception ex) { TranslationStatus.Text = L.Get("CouldNotOpenTheWebsiteVisitManually") + website + " · " + ex.Message; }
    }
    private async void DownloadLocalTranslation_Click(object sender, RoutedEventArgs e)
    {
        if (ModelDownload is not null) { TranslationStatus.Text = L.Get("ADownloadIsInProgressWaitFor"); return; }
        using var cancellation = new CancellationTokenSource(); ModelDownload = cancellation;
        var device = ((System.Windows.Controls.ComboBoxItem)LocalTranslationDeviceBox.SelectedItem).Tag.ToString()!;
        DownloadLocalTranslationButton.IsEnabled = false; CancelLocalTranslationButton.IsEnabled = true;
        LocalTranslationDeviceBox.IsEnabled = TranslationProviderBox.IsEnabled = false;
        LocalTranslationProgress.Visibility = Visibility.Visible;
        LocalTranslationProgress.IsIndeterminate = true;
        LocalTranslationProgress.Value = 0;
        LocalTranslationDownloadStatus.Text = L.Get("LocalDownloadPreparing");
        LocalTranslationDownloadStatus.BringIntoView();
        try
        {
            var root = Path.Combine(AppPaths.Root, "models", "local-translation");
            var install = await LocalTranslationSetupService.Install(root, device,
                new Progress<string>(text =>
                {
                    if (IsLoaded && ReferenceEquals(ModelDownload, cancellation)) LocalTranslationDownloadStatus.Text = text;
                }), cancellation.Token, new Progress<double?>(percent =>
                {
                    if (!IsLoaded || !ReferenceEquals(ModelDownload, cancellation)) return;
                    LocalTranslationProgress.IsIndeterminate = !percent.HasValue;
                    if (percent.HasValue) LocalTranslationProgress.Value = percent.Value;
                }), SelectedModelDownloadSource);
            if (!IsLoaded) return;
            LocalTranslationModelPath.Text = _settings.StoreModelPath(install.Model); LocalTranslationRuntimePath.Text = _settings.StoreModelPath(install.Runtime);
            LocalTranslationDeviceBox.SelectedIndex = device == "Vulkan" ? 1 : 0;
            TranslationProviderBox.SelectedIndex = 3;
            LocalTranslationDownloadStatus.Text = TranslationStatus.Text = L.Format("LocalTranslationDownloaded", device);
            LocalTranslationProgress.Value = 100;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { LocalTranslationDownloadStatus.Text = L.Get("LocalDownloadCanceledResume"); }
        catch (Exception ex)
        {
            ResumableModelDownload.Log("Local translation setup", ex);
            var message = L.Format("LocalDownloadFailureDetails", ex.Message, ResumableModelDownload.LogPath);
            LocalTranslationDownloadStatus.Text = message;
            if (IsLoaded) MessageBox.Show(this, message, L.Get("LocalDownloadFailureTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ModelDownload = null;
            LocalTranslationProgress.IsIndeterminate = false;
            DownloadLocalTranslationButton.IsEnabled = LocalTranslationDeviceBox.IsEnabled = TranslationProviderBox.IsEnabled = true;
            CancelLocalTranslationButton.IsEnabled = false;
        }
    }
    private void TesseractBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = L.Get("ChooseATesseractLanguageDataFolderContaining") };
        if (dialog.ShowDialog(this) == true) TesseractDirectory.Text = _settings.StoreModelPath(dialog.FolderName);
    }
    private async void DownloadTesseract_Click(object sender, RoutedEventArgs e)
    {
        if (ModelDownload is not null) return;
        using var cancellation = new CancellationTokenSource(); ModelDownload = cancellation;
        var languages = ((System.Windows.Controls.ComboBoxItem)TesseractPackageBox.SelectedItem).Tag.ToString()!;
        DownloadTesseractButton.IsEnabled = DownloadCutoutButton.IsEnabled = DownloadOcrButton.IsEnabled = TesseractPackageBox.IsEnabled = false;
        CancelTesseractButton.IsEnabled = true;
        try
        {
            var root = System.IO.Path.Combine(AppPaths.Root, "models");
            var directory = await new TesseractModelDownloadService().Download(root, languages, new Progress<string>(text => TesseractDownloadStatus.Text = text), cancellation.Token, SelectedModelDownloadSource);
            if (!IsLoaded) return;
            TesseractDirectory.Text = _settings.StoreModelPath(directory); TesseractLanguages.Text = languages; OcrBackendBox.SelectedIndex = 2;
            TesseractDownloadStatus.Text = L.Get("DownloadSHA256AndEngineLoadingChecksPassed");
        }
        catch (OperationCanceledException) { TesseractDownloadStatus.Text = L.Get("DownloadCanceledExistingConfigurationPreserved"); }
        catch (Exception ex) { TesseractDownloadStatus.Text = L.Get("DownloadFailedExistingConfigurationPreservedYouCan") + ex.GetBaseException().Message; }
        finally
        {
            ModelDownload = null;
            DownloadTesseractButton.IsEnabled = DownloadCutoutButton.IsEnabled = DownloadOcrButton.IsEnabled = TesseractPackageBox.IsEnabled = true;
            CancelTesseractButton.IsEnabled = false;
        }
    }
    private async void TestTranslation_Click(object sender, RoutedEventArgs e)
    {
        if (_translationTest is not null) return;
        using var cancellation = new CancellationTokenSource();
        _translationTest = cancellation;
        var button = (System.Windows.Controls.Button)sender;
        try
        {
            var candidate = BuildCandidate();
            if (candidate.TranslationProvider == "Local")
            {
                button.IsEnabled = false; TranslationStatus.Text = L.Get("TestLocalTranslationLoading");
                var local = await LocalTranslationService.Translate("Hello", candidate, cancellation.Token);
                if (IsLoaded) TranslationStatus.Text = L.Format("TestLocalTranslationSucceeded", candidate.LocalTranslationDevice, local);
                return;
            }
            if (!candidate.AllowOnlineTranslation) throw new InvalidOperationException(L.Get("AllowTranslationBeforeTesting"));
            if (!PrivacyConsentDialog.Confirm(this, OnlineRecognitionService.TranslationUri(candidate), "Hello", null)) return;
            button.IsEnabled = false; TranslationStatus.Text = L.Get("TestTranslationSendingHello");
            using var service = new OnlineRecognitionService();
            var translated = await service.Translate("Hello", candidate, true, cancellation.Token);
            if (IsLoaded) TranslationStatus.Text = L.Format("TestTranslationSucceeded", translated);
        }
        catch (Exception ex) { if (IsLoaded) TranslationStatus.Text = L.Format("TestTranslationFailed", ex.GetBaseException().Message); }
        finally { _translationTest = null; button.IsEnabled = true; }
    }
    private async void DownloadCutout_Click(object sender, RoutedEventArgs e)
    {
        if (ModelDownload is not null) return;
        var model = (CutoutModelDefinition)CutoutPackageBox.SelectedItem;
        CutoutPackageBox.IsEnabled = false;
        using var cancellation = new CancellationTokenSource(); ModelDownload = cancellation;
        DownloadTesseractButton.IsEnabled = DownloadCutoutButton.IsEnabled = DownloadOcrButton.IsEnabled = false; CancelCutoutButton.IsEnabled = true;
        CutoutDownloadStatus.Text = L.Get("ConnectingToTheOfficialModelSource");
        try
        {
            var root = System.IO.Path.Combine(AppPaths.Root, "models");
            var path = await new CutoutModelDownloadService().Download(model, root, new Progress<string>(text => CutoutDownloadStatus.Text = text), cancellation.Token, SelectedModelDownloadSource);
            if (!IsLoaded) return;
            CutoutPath.Text = _settings.StoreModelPath(path); CutoutProfileBox.SelectedIndex = model.Profile switch { "BiRefNet" => 2, "BiRefNetMatting" => 3, "BiRefNetAlpha" => 4, _ => 0 }; CutoutSize.Text = model.InputSize.ToString();
            StoreCutoutConfiguration();
            CutoutDownloadStatus.Text = L.Format("0DownloadAndVerificationCompleteModelPath", model.Id);
        }
        catch (OperationCanceledException) { CutoutDownloadStatus.Text = L.Get("DownloadCanceledExistingConfigurationPreserved"); }
        catch (Exception ex) { CutoutDownloadStatus.Text = L.Get("DownloadFailedExistingConfigurationPreservedYouCan") + ex.GetBaseException().Message; }
        finally { ModelDownload = null; CutoutPackageBox.IsEnabled = true; DownloadTesseractButton.IsEnabled = DownloadCutoutButton.IsEnabled = DownloadOcrButton.IsEnabled = true; CancelCutoutButton.IsEnabled = false; }
    }
    private void StoreCutoutConfiguration()
    {
        if (_editingCutout is null) return;
        _cutoutModels[_editingCutout.Id] = new()
        {
            Path = ModelPath(CutoutPath),
            Profile = ((System.Windows.Controls.ComboBoxItem)CutoutProfileBox.SelectedItem).Tag.ToString()!,
            InputSize = (int)Number(CutoutSize, 32, 2048, L.Get("BackgroundRemovalInputSize"), true)
        };
    }
    private void CutoutPackage_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CutoutPackageBox.SelectedItem is not CutoutModelDefinition model) return;
        try { StoreCutoutConfiguration(); }
        catch (Exception ex)
        {
            CutoutDownloadStatus.Text = ex.Message;
            CutoutPackageBox.SelectionChanged -= CutoutPackage_Changed;
            CutoutPackageBox.SelectedItem = _editingCutout;
            CutoutPackageBox.SelectionChanged += CutoutPackage_Changed;
            return;
        }
        _editingCutout = model;
        var config = _cutoutModels.GetValueOrDefault(model.Id);
        CutoutPath.Text = _settings.StoreModelPath(config?.Path);
        CutoutProfileBox.SelectedIndex = (config?.Profile ?? model.Profile) switch { "ISNet" => 1, "BiRefNet" => 2, "BiRefNetMatting" => 3, "BiRefNetAlpha" => 4, _ => 0 };
        CutoutSize.Text = (config?.InputSize ?? model.InputSize).ToString();
    }
    private async void DownloadOcr_Click(object sender, RoutedEventArgs e)
    {
        if (ModelDownload is not null) return;
        using var cancellation = new CancellationTokenSource(); ModelDownload = cancellation;
        DownloadTesseractButton.IsEnabled = DownloadCutoutButton.IsEnabled = DownloadOcrButton.IsEnabled = OcrModelPackage.IsEnabled = false; CancelOcrDownloadButton.IsEnabled = true;
        OcrDownloadStatus.Text = L.Get("ConnectingToThePyPIModelSource");
        try
        {
            var root = System.IO.Path.Combine(AppPaths.Root, "models");
            var paths = await new OcrModelDownloadService().Download(root, new Progress<string>(text => OcrDownloadStatus.Text = text), cancellation.Token, SelectedModelDownloadSource);
            if (!IsLoaded) return;
            OcrDetPath.Text = _settings.StoreModelPath(paths.Detection); OcrRecPath.Text = _settings.StoreModelPath(paths.Recognition); OcrDictPath.Text = _settings.StoreModelPath(paths.Dictionary);
            OcrBackendBox.SelectedIndex = 0;
            OcrDownloadStatus.Text = L.Get("ChineseEnglishModelsAndDictionaryDownloadedAnd");
        }
        catch (OperationCanceledException) { OcrDownloadStatus.Text = L.Get("DownloadCanceledExistingConfigurationPreserved"); }
        catch (Exception ex) { OcrDownloadStatus.Text = L.Get("DownloadIncompleteExistingConfigurationPreserved") + ex.GetBaseException().Message; }
        finally { ModelDownload = null; DownloadTesseractButton.IsEnabled = DownloadCutoutButton.IsEnabled = DownloadOcrButton.IsEnabled = OcrModelPackage.IsEnabled = true; CancelOcrDownloadButton.IsEnabled = false; }
    }
    private async void CheckModels_Click(object sender, RoutedEventArgs e)
    {
        var button = (System.Windows.Controls.Button)sender; button.IsEnabled = false;
        try
        {
            var candidate = BuildCandidate();
            if (candidate.OcrBackend == "Tesseract")
            {
                await Task.Run(() => LocalRecognitionService.CheckTesseract(candidate.TesseractDataDirectory, candidate.TesseractLanguages));
                ModelStatus.Text = L.Get("TesseractEngineAndSelectedLanguageDataCan"); return;
            }
            if (candidate.OcrBackend == "Windows")
            {
                var engine = string.IsNullOrWhiteSpace(candidate.WindowsOcrLanguage) ? Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()
                    : Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(candidate.WindowsOcrLanguage));
                ModelStatus.Text = engine is null ? L.Get("WindowsOCRLanguagePackIsUnavailableInstall") : L.Get("WindowsOCRAvailable") + engine.RecognizerLanguage.DisplayName;
                return;
            }
            await OnnxImageRuntime.Shared.Execute(candidate, (models, token) =>
            { models.Open(candidate.OcrDetectionModel, L.Get("DetectionModel")); models.Open(candidate.OcrRecognitionModel, L.Get("RecognitionModel")); OnnxImageRuntime.RequireFile(candidate.OcrDictionary, L.Get("Dictionary")); return true; }, CancellationToken.None);
            ModelStatus.Text = L.Get("LocalONNX") + candidate.OnnxDevice + " #" + candidate.OnnxDeviceId + L.Get("ModelsCanBeLoadedAndTheDictionary");
        }
        catch (Exception ex) { ModelStatus.Text = ex.GetBaseException().Message; }
        finally { button.IsEnabled = true; }
    }
    public void SelectCategory(string header)
    {
        if (header == L.Get("OnlineServicesAndPrivacy")) header = L.Get("TranslationAPIsAndPrivacy");
        var item = Categories.Items.Cast<System.Windows.Controls.TabItem>().FirstOrDefault(x => Equals(x.Tag, header) || Equals(x.Header, header));
        if (item is not null) Categories.SelectedItem = item;
    }
    private void BringSelectedCategoryIntoView()
    {
        if (!IsLoaded) return;
        Dispatcher.BeginInvoke(() => { if (IsLoaded && Categories.SelectedItem is FrameworkElement item) item.BringIntoView(); }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ModelDownload is not null)
        {
            MessageBox.Show(this, L.Get("WaitForTheModelDownloadToFinish"), L.Get("ModelDownloadInProgress"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var previous = _settings.Current;
        var previousStartup = _startup.IsEnabled();
        var settingsSaved = false;
        try
        {
            var candidate = BuildCandidate();
            var dataDirectory = DataDirectory.Text.Trim();
            var changeDirectory = !DataDirectoryService.SameDirectory(dataDirectory, AppPaths.DataLocation.SelectedDirectory);
            if (changeDirectory)
            {
                dataDirectory = AppPaths.DataLocation.ValidateChange(dataDirectory);
                if (!DataDirectoryService.SameDirectory(dataDirectory, AppPaths.Root) &&
                    MessageBox.Show(this, L.Format("ConfirmDataMigration", AppPaths.Root, dataDirectory),
                        L.Get("DataFolder"), MessageBoxButton.OKCancel, MessageBoxImage.Question,
                        MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            }
            _hotkey.Register(candidate);
            if (candidate.StartWithWindows != previousStartup) _startup.SetEnabled(candidate.StartWithWindows);
            _settings.Update(candidate);
            settingsSaved = true;
            if (changeDirectory) AppPaths.DataLocation.ScheduleChange(dataDirectory);
            LocalizationService.Apply(candidate.InterfaceLanguage);
            if (changeDirectory && !DataDirectoryService.SameDirectory(dataDirectory, AppPaths.Root))
                MessageBox.Show(this, L.Get("DataDirectoryRestartRequired"), L.Get("DataFolder"), MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            if (settingsSaved) { try { _settings.Update(previous); } catch { } }
            try { _hotkey.Register(previous); } catch { }
            try { if (_startup.IsEnabled() != previousStartup) _startup.SetEnabled(previousStartup); } catch { }
            MessageBox.Show(this, ex.Message, L.Get("CouldNotSaveSettings"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private static string NormalizeHotkey(string value, string label, bool optional = false)
    {
        if (optional && string.IsNullOrWhiteSpace(value)) return "";
        if (HotkeyGesture.TryParse(value, out var gesture, out var error)) return gesture!.DisplayText;
        throw new ArgumentException(L.Format("InvalidShortcutFor01", label, error));
    }

    internal static bool RunLayoutSelfTest()
    {
        var settings = new SettingsService();
        using var host = new GlobalHotkeyHost();
        var window = new SettingsWindow(settings, new StartupRegistrationService(), host);
        try
        {
            var originalDefault = window.CutoutDefaultBox.SelectedItem;
            window.CutoutPackageBox.SelectedItem = CutoutModels.All.Single(x => x.Id == "birefnet-hr-matting");
            if (window.CutoutSize.Text != "2048" || ((System.Windows.Controls.ComboBoxItem)window.CutoutProfileBox.SelectedItem).Tag.ToString() != "BiRefNetAlpha" || window.CutoutDefaultBox.SelectedItem != originalDefault)
                throw new InvalidOperationException("HR model selection must set its contract without changing the saved default.");
            window.CutoutPackageBox.SelectedItem = CutoutModels.All.Single(x => x.Id == "birefnet-general");
            if (window.CutoutSize.Text != "1024" || ((System.Windows.Controls.ComboBoxItem)window.CutoutProfileBox.SelectedItem).Tag.ToString() != "BiRefNet")
                throw new InvalidOperationException("General full model selection failed.");
            new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            if (!DialogLayoutService.IsPerMonitorV2) throw new InvalidOperationException("Settings window is not running in Per-Monitor V2 DPI mode.");
            var content = (FrameworkElement)window.Content;
            foreach (var scale in new[] { 1d, 1.25, 1.5, 1.75, 2, 2.5, 3 })
            for (var category = 0; category < window.Categories.Items.Count; category++)
            {
                var available = DialogLayoutService.AvailableSize(1920, 1080, scale, scale);
                var size = new System.Windows.Size(Math.Min(1040, available.Width), Math.Min(780, available.Height));
                if (available.Width * scale > 1920 || available.Height * scale > 1080) throw new InvalidOperationException("Dialog DIP limits exceed monitor bounds.");
                window.CompactLayout = size.Width < 840;
                window.FooterHint.Visibility = size.Width < 700 ? Visibility.Collapsed : Visibility.Visible;
                window.Categories.SelectedIndex = category;
                content.Measure(size); content.Arrange(new Rect(size)); content.UpdateLayout();
                var presenter = (FrameworkElement)window.Categories.Template.FindName("PART_SelectedContentHost", window.Categories);
                if (presenter.ActualWidth < 380 || presenter.ActualHeight < 150) throw new InvalidOperationException($"Settings page too small: {scale} / {category} / {presenter.RenderSize}");
                var first = (FrameworkElement)window.Categories.Items[0];
                var second = (FrameworkElement)window.Categories.Items[1];
                if (first.ActualWidth > 204 || second.TranslatePoint(new Point(), content).Y <= first.TranslatePoint(new Point(), content).Y) return false;
                var saveBounds = new Rect(window.SaveSettingsButton.TranslatePoint(new Point(), content), window.SaveSettingsButton.RenderSize);
                if (saveBounds.Right > size.Width + 1 || saveBounds.Bottom > size.Height + 1 || saveBounds.Top < 0) throw new InvalidOperationException("Save action is outside the viewport.");
                foreach (var field in VisualChildren(presenter).OfType<TextBox>().Where(x => x.IsVisible))
                    if (field.ActualWidth < 85 || field.ActualHeight < 39) throw new InvalidOperationException($"Settings field clipped: {field.Name} at {scale}: {field.RenderSize}");
                var render = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale), 96 * scale, 96 * scale, System.Windows.Media.PixelFormats.Pbgra32);
                render.Render(content);
                if ((scale == 1 && category is 2 or 5 or 7) || scale == 3 && category is 5 or 7)
                {
                    var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(render));
                    using var stream = System.IO.File.Create(System.IO.Path.Combine(AppPaths.DiagnosticsRoot, $"settings-preview-{category}-{scale:F0}x.png")); png.Save(stream);
                }
            }
            if (window.BuildCandidate().TextPinWidth != settings.Current.TextPinWidth) return false;
            window.TranslationProviderBox.SelectedIndex = 2;
            window.ConfigureTranslation_Click(window, new RoutedEventArgs());
            var translation = window.BuildCandidate();
            if (translation.TranslationProvider != "Google" || !translation.AllowOnlineTranslation || translation.TranslationTarget != "zh" || window.TranslateUrl.IsEnabled) return false;
            window.TranslationProviderBox.SelectedIndex = 0;
            window.ConfigureTranslation_Click(window, new RoutedEventArgs());
            if (window.BuildCandidate().TranslationProvider != "MyMemory" || window.TranslateUrl.IsEnabled) return false;
            window.TranslationProviderBox.SelectedIndex = 1; window.TranslateUrl.Text = "https://example.com/translate";
            if (!window.TranslateUrl.IsEnabled || window.BuildCandidate().TranslationProvider != "LibreTranslate") return false;
            window.AllowTranslation.IsChecked = false;
            window.TranslationProviderBox.SelectedIndex = 3;
            window.OnnxDeviceBox.SelectedIndex = 1; window.OnnxGpuId.Text = "1";
            window.LocalTranslationDeviceBox.SelectedIndex = 1;
            var local = window.BuildCandidate();
            if (local.TranslationProvider != "Local" || local.AllowOnlineTranslation ||
                local.OnnxDevice != "DirectML" || local.OnnxDeviceId != 1 ||
                local.LocalTranslationDevice != "Vulkan" || window.AllowTranslation.IsEnabled ||
                window.LocalTranslationPanel.Visibility != Visibility.Visible || window.TranslateUrl.IsEnabled) return false;
            if (!window.TranslationPricing.Text.Contains("无 API 费用")) return false;
            window.TextWidth.Text = "600"; window.PinSnapDistance.Text = "18";
            window.ClipboardPinHotkey.Text = ""; window.TogglePinsHotkey.Text = "";
            var candidate = window.BuildCandidate();
            if (candidate.TextPinWidth != 600 || candidate.PinSnapDistance != 18 || GlobalHotkeyHost.CreateRegistrations(candidate).Count != 3 || settings.Current.TextPinWidth != 480) return false;
            window.ClipboardPinHotkey.Text = window.RegionHotkey.Text;
            try { window.BuildCandidate(); return false; } catch (InvalidOperationException) { }
            window.ClipboardPinHotkey.Text = "";
            foreach (var invalid in new[] { "NaN", "Infinity", "-1", "65", "abc" })
            {
                window.TextFontSize.Text = invalid;
                try { window.BuildCandidate(); return false; } catch (ArgumentException) { }
            }
            window.TextFontSize.Text = "24";
            window.OutputFormatBox.SelectedItem = OutputFormat.Find(".webp"); window.WebpLossless.IsChecked = true;
            if (window.QualitySection.Visibility != Visibility.Collapsed) return false;
            window.WebpLossless.IsChecked = false;
            return window.QualitySection.Visibility == Visibility.Visible && window.BuildCandidate().TextPinFontSize == 24;
        }
        finally { window.Close(); }
    }
    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in VisualChildren(child)) yield return descendant;
        }
    }
}
