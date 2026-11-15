using SnapSharp.Models;
using System.Text.Json;

namespace SnapSharp.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        Directory.CreateDirectory(AppPaths.Root);
        if (!File.Exists(AppPaths.SettingsFile)) return;
        try
        {
            Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), JsonOptions) ?? new();
            Normalize();
        }
        catch
        {
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        Normalize();
        Directory.CreateDirectory(AppPaths.Root);
        var temporary = AppPaths.SettingsFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Current, JsonOptions));
        File.Move(temporary, AppPaths.SettingsFile, true);
    }

    public void Update(AppSettings candidate)
    {
        var previous = Current;
        Current = candidate;
        try { Save(); }
        catch { Current = previous; throw; }
    }

    private void Normalize()
    {
        Current.Recording ??= new();
        try { Current.Recording.Validate(); } catch (ArgumentException) { Current.Recording = new(); }
        Current.ImageQuality = Math.Clamp(Current.ImageQuality, 1, 100);
        if (Current.OcrBackend is not ("PaddleOCR" or "Windows" or "Tesseract")) Current.OcrBackend = "PaddleOCR";
        Current.TesseractDataDirectory ??= "";
        if (string.IsNullOrWhiteSpace(Current.TesseractLanguages)) Current.TesseractLanguages = "chi_sim+eng";
        if (Current.TranslationProvider is not ("MyMemory" or "Google" or "LibreTranslate" or "Local"))
        {
            Current.TranslationProvider = "LibreTranslate";
            Current.AllowOnlineTranslation = false;
        }
        Current.OcrMaximumEdge = Math.Clamp(Current.OcrMaximumEdge, 320, 2048);
        Current.TableStructureModel ??= "";
        Current.FormulaModelDirectory ??= "";
        if (Current.OnnxDevice is not ("CPU" or "DirectML")) Current.OnnxDevice = "CPU";
        Current.OnnxDeviceId = Math.Clamp(Current.OnnxDeviceId, 0, 15);
        if (Current.LocalTranslationDevice is not ("CPU" or "Vulkan")) Current.LocalTranslationDevice = "CPU";
        Current.LocalTranslationModel ??= "";
        Current.LocalTranslationRuntime ??= "";
        Current.RecognitionThreads = Math.Clamp(Current.RecognitionThreads, 1, 8);
        Current.RecognitionTimeoutSeconds = Math.Clamp(Current.RecognitionTimeoutSeconds, 1, 120);
        Current.CutoutInputSize = Math.Clamp(Current.CutoutInputSize, 32, 2048);
        CutoutModels.Migrate(Current);
        if (Current.CutoutProfile is not ("U2Net" or "ISNet" or "BiRefNet" or "BiRefNetMatting" or "BiRefNetAlpha")) Current.CutoutProfile = "U2Net";
        if (string.IsNullOrWhiteSpace(Current.FileNameTemplate)) Current.FileNameTemplate = OutputFileService.DefaultTemplate;
        var extension = Current.OutputFormat ?? "";
        Current.OutputFormat = OutputFormat.IsSupported(extension) ? OutputFormat.NormalizeExtension(extension) : ".png";
        Current.HistoryLimit = Math.Clamp(Current.HistoryLimit, 0, 1000);
        Current.DefaultPinOpacity = Math.Clamp(Current.DefaultPinOpacity, 0.2, 1.0);
        Current.PinSnapDistance = double.IsFinite(Current.PinSnapDistance) ? Math.Clamp(Current.PinSnapDistance, 0, 48) : 12;
        Current.TextPinFontSize = double.IsFinite(Current.TextPinFontSize) ? Math.Clamp(Current.TextPinFontSize, 10, 64) : 20;
        Current.TextPinWidth = Math.Clamp(Current.TextPinWidth, 200, 1200);
        if (string.IsNullOrWhiteSpace(Current.TextPinFontFamily)) Current.TextPinFontFamily = "Microsoft YaHei UI";
        if (string.IsNullOrWhiteSpace(Current.SaveDirectory))
            Current.SaveDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    }
}
