namespace SnapSharp.Models;

public sealed class AppSettings
{
    // Empty means follow the OS UI language. This is separate from OCR/translation languages.
    public string InterfaceLanguage { get; set; } = "";
    public string ModelDownloadSource { get; set; } = "Official";
    public RecordingPreferences Recording { get; set; } = new();
    public string RecordingSaveDirectory { get; set; } = DefaultRecordingSaveDirectory;
    public static string DefaultRecordingSaveDirectory
    {
        get
        {
            var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos, Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrWhiteSpace(videos)) videos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos");
            return Path.Combine(videos, "SnapSharp");
        }
    }
    public string SaveDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    public int ImageQuality { get; set; } = 92;
    public bool AutoSaveImages { get; set; }
    public bool ArchiveByDate { get; set; }
    public string FileNameTemplate { get; set; } = "SnapSharp-{date}-{time}-{seq}";
    public string OutputFormat { get; set; } = ".png";
    public bool WebpLossless { get; set; } = true;
    public string OcrBackend { get; set; } = "PaddleOCR";
    public string OcrModelPackage { get; set; } = "small";
    public string OnnxDevice { get; set; } = "CPU";
    public int OnnxDeviceId { get; set; }
    public string CutoutOnnxDevice { get; set; } = "";
    public int CutoutOnnxDeviceId { get; set; } = -1;
    public string OcrDetectionModel { get; set; } = "";
    public string OcrRecognitionModel { get; set; } = "";
    public string OcrDictionary { get; set; } = "";
    public string WindowsOcrLanguage { get; set; } = "zh-Hans";
    public string TesseractDataDirectory { get; set; } = "";
    public string TesseractLanguages { get; set; } = "chi_sim+eng";
    public int OcrMaximumEdge { get; set; } = 960;
    public int RecognitionThreads { get; set; } = 2;
    public int RecognitionTimeoutSeconds { get; set; } = 30;
    public bool BarcodeRotate { get; set; } = true;
    public string CutoutModel { get; set; } = "";
    public string CutoutProfile { get; set; } = "U2Net";
    public int CutoutInputSize { get; set; } = 320;
    public string CutoutDefaultModelId { get; set; } = "birefnet-general-lite";
    public Dictionary<string, CutoutModelConfiguration> CutoutModels { get; set; } = new();
    public string FormulaRecognizerId { get; set; } = "";
    public string TableRecognizerId { get; set; } = "";
    public string TableStructureModel { get; set; } = "";
    public string FormulaModelDirectory { get; set; } = "";
    public bool AllowOnlineTranslation { get; set; }
    public string TranslationEndpoint { get; set; } = "";
    public string TranslationProvider { get; set; } = "Local";
    public string TranslationApiProvider { get; set; } = "MyMemory";
    public string LocalTranslationModel { get; set; } = "";
    public string LocalTranslationRuntime { get; set; } = "";
    public string LocalTranslationDevice { get; set; } = "CPU";
    public string TranslationTarget { get; set; } = "zh";
    public string TranslationSource { get; set; } = "en";
    public bool AllowImageSearchUpload { get; set; }
    public string ImageSearchEndpoint { get; set; } = "";
    public int HistoryLimit { get; set; } = 100;
    public double DefaultPinOpacity { get; set; } = 1.0;
    public bool PinSnapToEdges { get; set; } = true;
    public double PinSnapDistance { get; set; } = 12;
    public string TextPinFontFamily { get; set; } = "Microsoft YaHei UI";
    public double TextPinFontSize { get; set; } = 20;
    public int TextPinWidth { get; set; } = 480;
    public string TextPinForeground { get; set; } = "#202124";
    public string TextPinBackground { get; set; } = "#FFF6CE";
    public bool SaveHistory { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool IncludeCursor { get; set; }
    public bool PreferWgcCapture { get; set; } = true;
    public string ScrollingCaptureBackend { get; set; } = "Auto";
    public bool EnableExternalPlugins { get; set; }
    public bool RequireSignedPlugins { get; set; } = true;
    public bool ShowCaptureRulers { get; set; }
    public bool ShowCaptureCrosshair { get; set; }
    public string RegionHotkey { get; set; } = "Ctrl+Shift+A";
    public string FullScreenHotkey { get; set; } = "Ctrl+Shift+F";
    public string ActiveWindowHotkey { get; set; } = "Ctrl+Shift+W";
    public string ScreenRulerHotkey { get; set; } = "";
    public bool ScreenRulerAutoCopy { get; set; }
    public string ClipboardPinHotkey { get; set; } = "Ctrl+Shift+V";
    public string TogglePinsHotkey { get; set; } = "Ctrl+Shift+H";
    public string RestorePinHotkey { get; set; } = "";
    public string RestorePinInteractionHotkey { get; set; } = "";
}

public sealed class CutoutModelConfiguration
{
    public string Path { get; set; } = "";
    public string Profile { get; set; } = "U2Net";
    public int InputSize { get; set; } = 320;
}

public sealed record HistoryEntry(
    string Id,
    DateTimeOffset CreatedAt,
    string ImagePath,
    string ThumbnailPath,
    int Width,
    int Height);
