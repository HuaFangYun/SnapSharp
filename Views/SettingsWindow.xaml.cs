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
    private CancellationTokenSource? _modelDownload;
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
        DialogLayoutService.Attach(this);
        SizeChanged += (_, _) => { CompactLayout = ActualWidth < 840; FooterHint.Visibility = ActualWidth < 700 ? Visibility.Collapsed : Visibility.Visible; };
        Categories.SelectionChanged += (_, e) => { if (ReferenceEquals(e.OriginalSource, Categories)) BringSelectedCategoryIntoView(); };
        Loaded += (_, _) => BringSelectedCategoryIntoView();
        var current = settings.Current;
        SaveDirectory.Text = current.SaveDirectory;
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
        DataDirectory.Text = AppPaths.Root;
        OcrBackendBox.SelectedIndex = current.OcrBackend switch { "Windows" => 1, "Tesseract" => 2, _ => 0 };
        OnnxDeviceBox.SelectedIndex = current.OnnxDevice == "DirectML" ? 1 : 0;
        OnnxGpuId.Text = current.OnnxDeviceId.ToString();
        TesseractDirectory.Text = current.TesseractDataDirectory; TesseractLanguages.Text = current.TesseractLanguages;
        OcrDetPath.Text = current.OcrDetectionModel; OcrRecPath.Text = current.OcrRecognitionModel; OcrDictPath.Text = current.OcrDictionary;
        OcrLanguage.Text = current.WindowsOcrLanguage; OcrMaxEdge.Text = current.OcrMaximumEdge.ToString(); ModelThreads.Text = current.RecognitionThreads.ToString(); ModelTimeout.Text = current.RecognitionTimeoutSeconds.ToString();
        BarcodeRotate.IsChecked = current.BarcodeRotate; CutoutPath.Text = current.CutoutModel; CutoutSize.Text = current.CutoutInputSize.ToString();
        CutoutProfileBox.SelectedIndex = current.CutoutProfile switch { "ISNet" => 1, "BiRefNet" => 2, "BiRefNetMatting" => 3, "BiRefNetAlpha" => 4, _ => 0 };
        CutoutModels.Migrate(current);
        _cutoutModels = JsonSerializer.Deserialize<Dictionary<string, CutoutModelConfiguration>>(JsonSerializer.Serialize(current.CutoutModels))!;
        CutoutDefaultBox.ItemsSource = CutoutModels.All;
        CutoutDefaultBox.SelectedItem = CutoutModels.All.First(x => x.Id == current.CutoutDefaultModelId);
        CutoutPackageBox.ItemsSource = CutoutModels.All;
        CutoutPackageBox.SelectedItem = CutoutDefaultBox.SelectedItem;
        TablePluginId.Text = current.TableRecognizerId; FormulaPluginId.Text = current.FormulaRecognizerId;
        TableModelPath.Text = current.TableStructureModel; FormulaDirectory.Text = current.FormulaModelDirectory;
        AllowTranslation.IsChecked = current.AllowOnlineTranslation; TranslateUrl.Text = current.TranslationEndpoint; TranslateTarget.Text = current.TranslationTarget; TranslateSource.Text = current.TranslationSource;
        TranslationProviderBox.SelectedIndex = current.TranslationProvider switch { "Local" => 3, "MyMemory" => 0, "Google" => 2, _ => 1 };
        LocalTranslationModelPath.Text = current.LocalTranslationModel;
        LocalTranslationRuntimePath.Text = current.LocalTranslationRuntime;
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
        RecordingStop.Text = recording.StopAfterSeconds.ToString(); RecordingGifEdge.Text = recording.GifMaximumEdge.ToString();
        Output_Changed(this, null!); Template_Changed(this, null!);
        Loaded += (_, _) => _hotkey.Suspend();
        Closed += (_, _) =>
        {
            _modelDownload?.Cancel();
            _translationTest?.Cancel();
            try { _hotkey.Resume(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "快捷键恢复失败，请重新打开设置检查冲突", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = SaveDirectory.Text, Title = "选择默认保存目录" };
        if (dialog.ShowDialog(this) == true) SaveDirectory.Text = dialog.FolderName;
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
        try { TemplatePreview.Text = "示例：" + OutputFileService.ExpandTemplate(FileNameTemplate.Text, DateTime.Now, 1920, 1080, 1); }
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
        if (!System.IO.Path.IsPathFullyQualified(SaveDirectory.Text)) throw new ArgumentException("输出与文件：请选择完整的保存目录路径。");
        _ = OutputFileService.ExpandTemplate(FileNameTemplate.Text, DateTime.Now, 1920, 1080, 1);
        candidate.SaveDirectory = SaveDirectory.Text.Trim();
        candidate.ImageQuality = (int)Quality.Value;
        candidate.Recording = new RecordingPreferences
        {
            CaptureBackend = ((System.Windows.Controls.ComboBoxItem)RecordingCaptureBackend.SelectedItem).Tag.ToString()!,
            BitrateKbps = (int)Number(RecordingBitrate, 0, 100000, "录屏码率", true), CountdownSeconds = (int)Number(RecordingCountdown, 0, 10, "开始倒计时", true),
            Format = ((System.Windows.Controls.ComboBoxItem)RecordingFormat.SelectedItem).Content.ToString()!,
            FrameRate = int.Parse(((System.Windows.Controls.ComboBoxItem)RecordingFps.SelectedItem).Content.ToString()!),
            Quality = ((System.Windows.Controls.ComboBoxItem)RecordingQuality.SelectedItem).Content.ToString()!,
            HardwareEncoding = RecordingHardware.IsChecked == true, IncludeCursor = RecordingCursor.IsChecked == true, HighlightClicks = RecordingClicks.IsChecked == true,
            StopAfterSeconds = (int)Number(RecordingStop, 0, 86400, "录屏定时停止", true), GifMaximumEdge = (int)Number(RecordingGifEdge, 160, 800, "GIF 最长边", true)
        };
        candidate.Recording.Validate();
        candidate.OutputFormat = ((OutputFormat)OutputFormatBox.SelectedItem).Extension;
        candidate.WebpLossless = WebpLossless.IsChecked == true;
        candidate.AutoSaveImages = AutoSaveImages.IsChecked == true;
        candidate.ArchiveByDate = ArchiveByDate.IsChecked == true;
        candidate.FileNameTemplate = FileNameTemplate.Text;
        candidate.HistoryLimit = (int)Number(HistoryLimit, 0, 1000, "历史条数", integer: true);
        candidate.DefaultPinOpacity = PinOpacity.Value;
        candidate.PinSnapToEdges = PinSnapToEdges.IsChecked == true;
        candidate.PinSnapDistance = Number(PinSnapDistance, 0, 48, "吸附距离");
        candidate.TextPinFontSize = Number(TextFontSize, 10, 64, "文本字号");
        candidate.TextPinWidth = (int)Number(TextWidth, 200, 1200, "文本宽度", integer: true);
        candidate.TextPinFontFamily = TextFontFamily.Text.Trim();
        if (string.IsNullOrWhiteSpace(candidate.TextPinFontFamily)) throw new ArgumentException("贴图：字体名称不能为空。");
        if (!PinContentService.TryParseColor(TextForeground.Text, out var fg) || !PinContentService.TryParseColor(TextBackground.Text, out var bg)) throw new ArgumentException("贴图：文本或背景颜色无效。");
        candidate.TextPinForeground = PinContentService.Hex(fg); candidate.TextPinBackground = PinContentService.Hex(bg);
        candidate.SaveHistory = SaveHistory.IsChecked == true;
        candidate.StartWithWindows = StartWithWindows.IsChecked == true;
        candidate.IncludeCursor = IncludeCursor.IsChecked == true;
        candidate.PreferWgcCapture = PreferWgcCapture.IsChecked == true;
        candidate.EnableExternalPlugins = EnableExternalPlugins.IsChecked == true;
        candidate.RequireSignedPlugins = RequireSignedPlugins.IsChecked == true;
        candidate.OcrBackend = ((System.Windows.Controls.ComboBoxItem)OcrBackendBox.SelectedItem).Tag.ToString()!;
        candidate.OnnxDevice = ((System.Windows.Controls.ComboBoxItem)OnnxDeviceBox.SelectedItem).Tag.ToString()!;
        candidate.OnnxDeviceId = (int)Number(OnnxGpuId, 0, 15, "GPU 编号", true);
        candidate.OcrDetectionModel = ModelPath(OcrDetPath, candidate.OcrBackend == "PaddleOCR"); candidate.OcrRecognitionModel = ModelPath(OcrRecPath, candidate.OcrBackend == "PaddleOCR"); candidate.OcrDictionary = ModelPath(OcrDictPath, candidate.OcrBackend == "PaddleOCR");
        candidate.TesseractDataDirectory = TesseractDirectory.Text.Trim(); candidate.TesseractLanguages = TesseractLanguages.Text.Trim();
        if (candidate.OcrBackend == "Tesseract") LocalRecognitionService.ValidateTesseractData(candidate.TesseractDataDirectory, candidate.TesseractLanguages);
        candidate.WindowsOcrLanguage = OcrLanguage.Text.Trim(); candidate.OcrMaximumEdge = (int)Number(OcrMaxEdge, 320, 2048, "OCR 最长边", true);
        candidate.RecognitionThreads = (int)Number(ModelThreads, 1, 8, "模型线程数", true); candidate.RecognitionTimeoutSeconds = (int)Number(ModelTimeout, 1, 120, "推理超时", true);
        candidate.BarcodeRotate = BarcodeRotate.IsChecked == true; candidate.CutoutModel = ModelPath(CutoutPath); candidate.CutoutInputSize = (int)Number(CutoutSize, 32, 2048, "抠图输入尺寸", true);
        candidate.CutoutProfile = ((System.Windows.Controls.ComboBoxItem)CutoutProfileBox.SelectedItem).Tag.ToString()!;
        StoreCutoutConfiguration();
        candidate.CutoutModels = JsonSerializer.Deserialize<Dictionary<string, CutoutModelConfiguration>>(JsonSerializer.Serialize(_cutoutModels))!;
        candidate.CutoutDefaultModelId = ((CutoutModelDefinition)CutoutDefaultBox.SelectedItem).Id;
        var defaultCutout = CutoutModels.ForModel(candidate, candidate.CutoutDefaultModelId);
        candidate.CutoutModel = defaultCutout.CutoutModel; candidate.CutoutProfile = defaultCutout.CutoutProfile; candidate.CutoutInputSize = defaultCutout.CutoutInputSize;
        candidate.FormulaRecognizerId = FormulaPluginId.Text.Trim(); candidate.TableRecognizerId = TablePluginId.Text.Trim();
        candidate.TableStructureModel = ModelPath(TableModelPath);
        candidate.FormulaModelDirectory = FormulaDirectory.Text.Trim();
        if (candidate.FormulaModelDirectory.Length > 0)
        {
            foreach (var name in new[] { "encoder.onnx", "decoder.onnx", "image_resizer.onnx", "tokenizer.json" })
                OnnxImageRuntime.RequireFile(System.IO.Path.Combine(candidate.FormulaModelDirectory, name), "公式 " + name);
        }
        candidate.AllowOnlineTranslation = AllowTranslation.IsChecked == true; candidate.TranslationEndpoint = TranslateUrl.Text.Trim(); candidate.TranslationTarget = TranslateTarget.Text.Trim(); candidate.TranslationSource = TranslateSource.Text.Trim();
        candidate.TranslationProvider = ((System.Windows.Controls.ComboBoxItem)TranslationProviderBox.SelectedItem).Tag.ToString()!;
        candidate.LocalTranslationModel = LocalTranslationModelPath.Text.Trim();
        candidate.LocalTranslationRuntime = LocalTranslationRuntimePath.Text.Trim();
        candidate.LocalTranslationDevice = ((System.Windows.Controls.ComboBoxItem)LocalTranslationDeviceBox.SelectedItem).Tag.ToString()!;
        if (!System.Text.RegularExpressions.Regex.IsMatch(candidate.TranslationSource, "^[a-zA-Z-]{2,20}$")) throw new ArgumentException("源语言代码无效，可填写 en、zh 或 auto。");
        candidate.AllowImageSearchUpload = AllowSearch.IsChecked == true; candidate.ImageSearchEndpoint = SearchUrl.Text.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(candidate.TranslationTarget, "^[a-zA-Z-]{2,20}$")) throw new ArgumentException("目标语言代码无效。");
        if (candidate.AllowOnlineTranslation && candidate.TranslationProvider != "Local")
        {
            OnlineRecognitionService.TranslationUri(candidate);
            if (candidate.TranslationProvider == "MyMemory" && candidate.TranslationSource == "auto") throw new ArgumentException("MyMemory 需指定源语言，例如 en 或 zh；auto 仅适用于支持它的自定义服务。");
            if (!System.Text.RegularExpressions.Regex.IsMatch(candidate.TranslationTarget, "^[a-zA-Z-]{2,20}$")) throw new ArgumentException("目标语言代码无效。");
        }
        if (candidate.AllowImageSearchUpload) OnlineRecognitionService.Endpoint(candidate.ImageSearchEndpoint);
        candidate.RegionHotkey = NormalizeHotkey(RegionHotkey.Text, "区域截图");
        candidate.FullScreenHotkey = NormalizeHotkey(FullScreenHotkey.Text, "全屏截图");
        candidate.ActiveWindowHotkey = NormalizeHotkey(ActiveWindowHotkey.Text, "当前窗口截图");
        candidate.ClipboardPinHotkey = NormalizeHotkey(ClipboardPinHotkey.Text, "剪贴板贴图", true);
        candidate.TogglePinsHotkey = NormalizeHotkey(TogglePinsHotkey.Text, "切换贴图", true);
        candidate.RestorePinHotkey = NormalizeHotkey(RestorePinHotkey.Text, "恢复贴图", true);
        candidate.RestorePinInteractionHotkey = NormalizeHotkey(RestoreInteractionHotkey.Text, "恢复交互", true);
        GlobalHotkeyHost.CreateRegistrations(candidate);
        return candidate;
        
    }
    private static double Number(TextBox field, double min, double max, string label, bool integer = false)
    {
        if (double.TryParse(field.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) && number >= min && number <= max && (!integer || number == Math.Truncate(number))) return number;
        throw new ArgumentException($"{label}需为 {min}–{max} 的有效{(integer ? "整数" : "数值")}。");
    }
    private static string ModelPath(TextBox field, bool validate = true)
    {
        var path = field.Text.Trim();
        if (validate && path.Length != 0 && (!System.IO.Path.IsPathFullyQualified(path) || !System.IO.File.Exists(path))) throw new ArgumentException("模型或字典文件不存在：" + path);
        return path;
    }
    private void ModelBrowse_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name } || FindName(name) is not TextBox field) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = name == "OcrDictPath" ? "UTF-8 字典|*.txt|所有文件|*.*" : "ONNX 模型|*.onnx" };
        if (dialog.ShowDialog(this) == true) field.Text = dialog.FileName;
    }
    private void CancelOcrDownload_Click(object sender, RoutedEventArgs e) => _modelDownload?.Cancel();
    private void BrowseFormulaDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择 LaTeX-OCR 模型目录" };
        if (dialog.ShowDialog(this) == true) FormulaDirectory.Text = dialog.FolderName;
    }
    private async void DownloadSpecialist_Click(object sender, RoutedEventArgs e)
    {
        if (_modelDownload is not null) { SpecialistStatus.Text = "已有下载进行中，请等待或取消后重试。"; return; }
        var table = (sender as FrameworkElement)?.Tag as string == "Table";
        using var cancellation = new CancellationTokenSource(); _modelDownload = cancellation;
        DownloadTableButton.IsEnabled = DownloadFormulaButton.IsEnabled = false; CancelSpecialistButton.IsEnabled = true;
        SpecialistStatus.Text = "正在下载并校验配套本地模型…";
        try
        {
            var root = System.IO.Path.Combine(AppPaths.Root, "models");
            var progress = new Progress<string>(value => SpecialistStatus.Text = value);
            var path = await SpecialistModelDownloadService.Install(root, table, progress, cancellation.Token);
            OcrModelDownloadService.ModelPaths? ocr = null;
            if (table && ((System.Windows.Controls.ComboBoxItem)OcrBackendBox.SelectedItem).Tag.ToString() == "PaddleOCR" &&
                new[] { OcrDetPath.Text, OcrRecPath.Text, OcrDictPath.Text }.Any(p => !System.IO.File.Exists(p)))
                ocr = await new OcrModelDownloadService().Download(root, progress, cancellation.Token);
            if (!IsLoaded) return;
            if (table)
            {
                TableModelPath.Text = path; TablePluginId.Text = "";
                if (ocr is not null) { OcrDetPath.Text = ocr.Detection; OcrRecPath.Text = ocr.Recognition; OcrDictPath.Text = ocr.Dictionary; }
            }
            else { FormulaDirectory.Text = path; FormulaPluginId.Text = ""; }
            SpecialistStatus.Text = "下载及 SHA256、模型加载校验通过，已选择内置本地识别并填好路径。点击保存后使用。";
        }
        catch (OperationCanceledException) { SpecialistStatus.Text = "已取消，原配置保留。"; }
        catch (Exception ex) { SpecialistStatus.Text = "配置未完成，原配置保留，可重试：" + ex.GetBaseException().Message; }
        finally { _modelDownload = null; DownloadTableButton.IsEnabled = DownloadFormulaButton.IsEnabled = true; CancelSpecialistButton.IsEnabled = false; }
    }
    private void RecordingSetup_Click(object sender, RoutedEventArgs e) => RecordingSetupWindow.Open(this);
    private void ConfigureWindowsOcr_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var languages = Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages;
            var language = languages.FirstOrDefault(x => x.LanguageTag.Equals(OcrLanguage.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? languages.FirstOrDefault(x => x.LanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) ?? languages.FirstOrDefault();
            if (language is null) { WindowsOcrStatus.Text = "没有可用 Windows OCR 语言包。请点击安装语言包，或使用 PaddleOCR 一键下载。"; return; }
            OcrLanguage.Text = language.LanguageTag; OcrBackendBox.SelectedIndex = 1;
            WindowsOcrStatus.Text = "已选择可用语言：" + language.DisplayName + "（" + language.LanguageTag + "）。请保存设置。";
        }
        catch (Exception ex) { WindowsOcrStatus.Text = "读取系统 OCR 语言失败：" + ex.GetBaseException().Message; }
    }
    private void OpenLanguageSettings_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:regionlanguage") { UseShellExecute = true }); }
        catch (Exception ex) { WindowsOcrStatus.Text = "无法打开系统设置，请手动进入 Windows 设置 → 时间和语言 → 语言：" + ex.Message; }
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
        TranslationStatus.Text = provider == "LibreTranslate" ? "已设为自定义服务英译中，请填写可用端点，再测试并保存设置。此选项不会自动安装服务器。"
            : $"已配置 {provider} 英译中，无需下载模型或密钥。可先测试连接，再保存设置；每次发送仍需确认。";
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
            "Local" => "本地翻译：请选择 CPU 或 Vulkan GPU，然后一键下载、测试并保存。无需允许在线翻译；推理不会向外部服务发送文字。",
            "Google" => "Google / GTranslate：包已内置，无需模型或密钥。支持 auto；单次最多 5000 字符。需联网，接口可能限流或不可达。选择不会发送内容。",
            "MyMemory" => "MyMemory：无需模型或密钥；需指定源语言，不支持 auto。单次最多 500 UTF-8 字节，有公共免费额度。选择不会发送内容。",
            _ => "LibreTranslate：使用你的兼容端点，不会下载或部署服务器。服务商可能要求密钥（当前不支持）；支持 auto 取决于服务能力。"
        };
    }
    private void OpenTranslationWebsite_Click(object sender, RoutedEventArgs e) =>
        OpenWebsite(RecognitionProviderCatalog.Translation(((System.Windows.Controls.ComboBoxItem)TranslationProviderBox.SelectedItem).Tag.ToString()!).Website);
    private void OpenProviderWebsite_Click(object sender, RoutedEventArgs e) =>
        OpenWebsite(RecognitionProviderCatalog.Website(((FrameworkElement)sender).Tag.ToString()!));
    private void OpenWebsite(string website)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(website) { UseShellExecute = true }); }
        catch (Exception ex) { TranslationStatus.Text = "无法打开官网，请手动访问：" + website + " · " + ex.Message; }
    }
    private async void DownloadLocalTranslation_Click(object sender, RoutedEventArgs e)
    {
        if (_modelDownload is not null) { TranslationStatus.Text = "已有下载进行中，请等待完成或先取消。"; return; }
        using var cancellation = new CancellationTokenSource(); _modelDownload = cancellation;
        var device = ((System.Windows.Controls.ComboBoxItem)LocalTranslationDeviceBox.SelectedItem).Tag.ToString()!;
        DownloadLocalTranslationButton.IsEnabled = false; CancelLocalTranslationButton.IsEnabled = true;
        try
        {
            var root = Path.Combine(AppPaths.Root, "models", "local-translation");
            var install = await LocalTranslationSetupService.Install(root, device,
                new Progress<string>(text => { if (IsLoaded) TranslationStatus.Text = text; }), cancellation.Token);
            if (!IsLoaded) return;
            LocalTranslationModelPath.Text = install.Model; LocalTranslationRuntimePath.Text = install.Runtime;
            LocalTranslationDeviceBox.SelectedIndex = device == "Vulkan" ? 1 : 0;
            TranslationProviderBox.SelectedIndex = 3;
            TranslationStatus.Text = "已下载并配置本地 " + device + " 翻译，SHA256 校验通过。请测试并保存设置。";
        }
        catch (OperationCanceledException) { TranslationStatus.Text = "本地翻译下载已取消，原配置保留。"; }
        catch (Exception ex) { TranslationStatus.Text = "本地翻译下载失败，可重试：" + ex.GetBaseException().Message; }
        finally { _modelDownload = null; DownloadLocalTranslationButton.IsEnabled = true; CancelLocalTranslationButton.IsEnabled = false; }
    }
    private void TesseractBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择包含 .traineddata 的 Tesseract 字库目录" };
        if (dialog.ShowDialog(this) == true) TesseractDirectory.Text = dialog.FolderName;
    }
    private async void DownloadTesseract_Click(object sender, RoutedEventArgs e)
    {
        if (_modelDownload is not null) return;
        using var cancellation = new CancellationTokenSource(); _modelDownload = cancellation;
        var languages = ((System.Windows.Controls.ComboBoxItem)TesseractPackageBox.SelectedItem).Tag.ToString()!;
        DownloadTesseractButton.IsEnabled = DownloadCutoutButton.IsEnabled = DownloadOcrButton.IsEnabled = TesseractPackageBox.IsEnabled = false;
        CancelTesseractButton.IsEnabled = true;
        try
        {
            var root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapSharp", "models");
            var directory = await new TesseractModelDownloadService().Download(root, languages, new Progress<string>(text => TesseractDownloadStatus.Text = text), cancellation.Token);
            if (!IsLoaded) return;
            TesseractDirectory.Text = directory; TesseractLanguages.Text = languages; OcrBackendBox.SelectedIndex = 2;
            TesseractDownloadStatus.Text = "下载、SHA256 与引擎加载检查通过，已配置 Tesseract 路径和语言。请保存设置。";
        }
        catch (OperationCanceledException) { TesseractDownloadStatus.Text = "下载已取消，原配置未更改。"; }
        catch (Exception ex) { TesseractDownloadStatus.Text = "下载失败，原配置未更改，可重试：" + ex.GetBaseException().Message; }
        finally
        {
            _modelDownload = null;
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
                button.IsEnabled = false; TranslationStatus.Text = "正在本机加载模型并翻译 Hello，不向外部 API 发送内容…";
                var local = await LocalTranslationService.Translate("Hello", candidate, cancellation.Token);
                if (IsLoaded) TranslationStatus.Text = "本地 " + candidate.LocalTranslationDevice + " 测试成功：Hello → " + local + "。请保存设置。";
                return;
            }
            if (!candidate.AllowOnlineTranslation) throw new InvalidOperationException("请先一键配置或勾选允许翻译。");
            if (!PrivacyConsentDialog.Confirm(this, OnlineRecognitionService.TranslationUri(candidate), "Hello", null)) return;
            button.IsEnabled = false; TranslationStatus.Text = "正在用 Hello 测试翻译，不发送截图或识别文本…";
            using var service = new OnlineRecognitionService();
            var translated = await service.Translate("Hello", candidate, true, cancellation.Token);
            if (IsLoaded) TranslationStatus.Text = "连接成功：Hello → " + translated + "。请保存设置后使用。";
        }
        catch (Exception ex) { if (IsLoaded) TranslationStatus.Text = "连接测试失败：" + ex.GetBaseException().Message; }
        finally { _translationTest = null; button.IsEnabled = true; }
    }
    private async void DownloadCutout_Click(object sender, RoutedEventArgs e)
    {
        if (_modelDownload is not null) return;
        var model = (CutoutModelDefinition)CutoutPackageBox.SelectedItem;
        CutoutPackageBox.IsEnabled = false;
        using var cancellation = new CancellationTokenSource(); _modelDownload = cancellation;
        DownloadTesseractButton.IsEnabled = DownloadCutoutButton.IsEnabled = DownloadOcrButton.IsEnabled = false; CancelCutoutButton.IsEnabled = true;
        CutoutDownloadStatus.Text = "正在连接官方模型源…";
        try
        {
            var root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapSharp", "models");
            var path = await new CutoutModelDownloadService().Download(model, root, new Progress<string>(text => CutoutDownloadStatus.Text = text), cancellation.Token);
            if (!IsLoaded) return;
            CutoutPath.Text = path; CutoutProfileBox.SelectedIndex = model.Profile switch { "BiRefNet" => 2, "BiRefNetMatting" => 3, "BiRefNetAlpha" => 4, _ => 0 }; CutoutSize.Text = model.InputSize.ToString();
            StoreCutoutConfiguration();
            CutoutDownloadStatus.Text = $"{model.Id} 下载与校验完成，模型路径、预处理和尺寸已配置。请点击“保存设置”。";
        }
        catch (OperationCanceledException) { CutoutDownloadStatus.Text = "下载已取消，原配置未更改。"; }
        catch (Exception ex) { CutoutDownloadStatus.Text = "下载失败，原配置未更改，可重试：" + ex.GetBaseException().Message; }
        finally { _modelDownload = null; CutoutPackageBox.IsEnabled = true; DownloadTesseractButton.IsEnabled = DownloadCutoutButton.IsEnabled = DownloadOcrButton.IsEnabled = true; CancelCutoutButton.IsEnabled = false; }
    }
    private void StoreCutoutConfiguration()
    {
        if (_editingCutout is null) return;
        _cutoutModels[_editingCutout.Id] = new()
        {
            Path = ModelPath(CutoutPath),
            Profile = ((System.Windows.Controls.ComboBoxItem)CutoutProfileBox.SelectedItem).Tag.ToString()!,
            InputSize = (int)Number(CutoutSize, 32, 2048, "抠图输入尺寸", true)
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
        CutoutPath.Text = config?.Path ?? "";
        CutoutProfileBox.SelectedIndex = (config?.Profile ?? model.Profile) switch { "ISNet" => 1, "BiRefNet" => 2, "BiRefNetMatting" => 3, "BiRefNetAlpha" => 4, _ => 0 };
        CutoutSize.Text = (config?.InputSize ?? model.InputSize).ToString();
    }
    private async void DownloadOcr_Click(object sender, RoutedEventArgs e)
    {
        if (_modelDownload is not null) return;
        using var cancellation = new CancellationTokenSource(); _modelDownload = cancellation;
        DownloadTesseractButton.IsEnabled = DownloadCutoutButton.IsEnabled = DownloadOcrButton.IsEnabled = OcrModelPackage.IsEnabled = false; CancelOcrDownloadButton.IsEnabled = true;
        OcrDownloadStatus.Text = "正在连接 PyPI 模型源…";
        try
        {
            var root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapSharp", "models");
            var paths = await new OcrModelDownloadService().Download(root, new Progress<string>(text => OcrDownloadStatus.Text = text), cancellation.Token);
            if (!IsLoaded) return;
            OcrDetPath.Text = paths.Detection; OcrRecPath.Text = paths.Recognition; OcrDictPath.Text = paths.Dictionary;
            OcrBackendBox.SelectedIndex = 0;
            OcrDownloadStatus.Text = "中英文模型与字典已下载并通过校验，路径已填入。点击“保存设置”后生效。";
        }
        catch (OperationCanceledException) { OcrDownloadStatus.Text = "下载已取消，原配置未更改。"; }
        catch (Exception ex) { OcrDownloadStatus.Text = "下载未完成，原配置未更改：" + ex.GetBaseException().Message; }
        finally { _modelDownload = null; DownloadTesseractButton.IsEnabled = DownloadCutoutButton.IsEnabled = DownloadOcrButton.IsEnabled = OcrModelPackage.IsEnabled = true; CancelOcrDownloadButton.IsEnabled = false; }
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
                ModelStatus.Text = "Tesseract 引擎和所选语言字库可加载。真实截图精度需实际识别确认。"; return;
            }
            if (candidate.OcrBackend == "Windows")
            {
                var engine = string.IsNullOrWhiteSpace(candidate.WindowsOcrLanguage) ? Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()
                    : Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(candidate.WindowsOcrLanguage));
                ModelStatus.Text = engine is null ? "Windows OCR 语言包不可用，请安装语言包或选择其他引擎。" : "Windows OCR 可用：" + engine.RecognizerLanguage.DisplayName;
                return;
            }
            await OnnxImageRuntime.Shared.Execute(candidate, (models, token) =>
            { models.Open(candidate.OcrDetectionModel, "检测模型"); models.Open(candidate.OcrRecognitionModel, "识别模型"); OnnxImageRuntime.RequireFile(candidate.OcrDictionary, "字典"); return true; }, CancellationToken.None);
            ModelStatus.Text = "本地 ONNX " + candidate.OnnxDevice + " #" + candidate.OnnxDeviceId + " 可加载模型，字典存在。GPU 不支持的算子可能在 CPU 执行；精度需实际识别确认。";
        }
        catch (Exception ex) { ModelStatus.Text = ex.GetBaseException().Message; }
        finally { button.IsEnabled = true; }
    }
    public void SelectCategory(string header)
    {
        if (header == "在线服务与隐私") header = "翻译、API 与隐私";
        var item = Categories.Items.Cast<System.Windows.Controls.TabItem>().FirstOrDefault(x => Equals(x.Header, header));
        if (item is not null) Categories.SelectedItem = item;
    }
    private void BringSelectedCategoryIntoView()
    {
        if (!IsLoaded) return;
        Dispatcher.BeginInvoke(() => { if (IsLoaded && Categories.SelectedItem is FrameworkElement item) item.BringIntoView(); }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_modelDownload is not null)
        {
            MessageBox.Show(this, "请等待模型下载完成，或先取消下载，再保存设置。", "模型下载中", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var previous = _settings.Current;
        var previousStartup = _startup.IsEnabled();
        try
        {
            var candidate = BuildCandidate();
            _hotkey.Register(candidate);
            if (candidate.StartWithWindows != previousStartup) _startup.SetEnabled(candidate.StartWithWindows);
            _settings.Update(candidate);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            try { _hotkey.Register(previous); } catch { }
            try { if (_startup.IsEnabled() != previousStartup) _startup.SetEnabled(previousStartup); } catch { }
            MessageBox.Show(this, ex.Message, "设置保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private static string NormalizeHotkey(string value, string label, bool optional = false)
    {
        if (optional && string.IsNullOrWhiteSpace(value)) return "";
        if (HotkeyGesture.TryParse(value, out var gesture, out var error)) return gesture!.DisplayText;
        throw new ArgumentException($"快捷键：{label}无效：{error}");
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
