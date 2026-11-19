using L = SnapSharp.Localization.UiText;
using SnapSharp.Models;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Text.Json;

namespace SnapSharp.Services;

/// <summary>Session-only opt-in. One local recognition per stationary pointer position.</summary>
internal sealed class ScreenWordService(Func<AppSettings> settings, Func<bool> suspended) : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private System.Drawing.Point _last;
    private int _stable, _generation;
    private int _attemptedGeneration = -1;
    private bool _busy, _enabled, _disposed;
    private CancellationTokenSource? _operation;
    private Window? _popup;
    private string? _cacheKey, _cacheText;
    public event Action<string>? Failed;
    public bool IsEnabled => _enabled;
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled; _generation++; _stable = 0; _operation?.Cancel(); Hide();
        _timer.Stop(); _timer.Tick -= Tick;
        if (enabled) { _timer.Tick += Tick; _timer.Start(); }
    }
    private async void Tick(object? sender, EventArgs e)
    {
        if (_disposed || !_enabled) return;
        var point = System.Windows.Forms.Cursor.Position;
        if (suspended() || point != _last)
        { _generation++; _stable = 0; _last = point; _operation?.Cancel(); Hide(); return; }
        _stable = Math.Min(3, _stable + 1);
        if (_stable < 2 || _busy || _attemptedGeneration == _generation) return;
        _busy = true; var generation = _generation; _attemptedGeneration = generation;
        _operation = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(settings().RecognitionTimeoutSeconds, 15, 120)));
        try
        {
            var screen = System.Windows.Forms.Screen.FromPoint(point).Bounds;
            var roi = Region(point, screen);
            Hide();
            var image = new ResilientCaptureService(settings().PreferWgcCapture, false).CaptureRegion(roi);
            var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[roi.Width * roi.Height * 4]; converted.CopyPixels(pixels, roi.Width * 4, 0);
            var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels)) + System.Text.Json.JsonSerializer.Serialize(settings());
            string text;
            if (key == _cacheKey) text = _cacheText ?? "";
            else { text = (await RecognizeImage(image, _operation.Token)).Text; _cacheKey = key; _cacheText = text; }
            if (!_disposed && _enabled && generation == _generation && point == System.Windows.Forms.Cursor.Position && !suspended())
            {
                var body = new TextBlock { Text = string.IsNullOrWhiteSpace(text) ? L.Get("ScreenWordsNoText") : text[..Math.Min(text.Length, 3000)], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) };
                body.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                var border = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = new ScrollViewer { Content = body } };
                border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                _popup = new Window { Title = L.Get("ScreenTextCapture"), Width = 440, Height = 130, ShowActivated = false, ShowInTaskbar = false, Topmost = true,
                    WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize, Content = border };
                ThemeService.ApplyToWindow(_popup);
                _popup.SourceInitialized += (_, _) =>
                {
                    var handle = new WindowInteropHelper(_popup).Handle;
                    SetWindowLongPtr(handle, -20, GetWindowLongPtr(handle, -20) | 0x08000080); // TOOLWINDOW | NOACTIVATE
                };
                _popup.Show();
                var hwnd = new WindowInteropHelper(_popup).Handle;
                // Move to the target monitor first so GetDpiForWindow returns that monitor's DPI.
                SetWindowPos(hwnd, new nint(-1), roi.Left, roi.Top, 0, 0, 0x0011);
                var placement = PopupRegion(roi, System.Windows.Forms.Screen.FromPoint(point).WorkingArea, GetDpiForWindow(hwnd) / 96d);
                if (placement is not { } bounds) { Hide(); return; }
                SetWindowPos(hwnd, new nint(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010);
            }
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && _enabled && generation == _generation)
            { SetEnabled(false); Failed?.Invoke(L.Get("ScreenWordsTimeout")); }
        }
        catch (Exception ex)
        {
            if (!_disposed && generation == _generation) { SetEnabled(false); Failed?.Invoke(ex.GetBaseException().Message); }
        }
        finally { _operation?.Dispose(); _operation = null; _busy = false; }
    }
    internal async Task<RecognitionDocument> RecognizeImage(BitmapSource image, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var candidate = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings()))!;
        if (candidate.OcrBackend == "PaddleOCR" && !PaddleReady(candidate))
        {
            var package = OcrModelDownloadService.FindPackage(candidate.OcrModelPackage);
            var cached = await ModelInstallCache.Find(Path.Combine(AppPaths.Root, "models"), package.DirectoryPrefix, package.InstalledAssets, null, token);
            if (cached is not null)
            {
                candidate.OcrDetectionModel = Path.Combine(cached, "detection.onnx"); candidate.OcrRecognitionModel = Path.Combine(cached, "recognition.onnx");
                candidate.OcrDictionary = Path.Combine(cached, "dictionary.txt");
            }
        }
        var language = WindowsLanguage(candidate.WindowsOcrLanguage);
        candidate = ResolveSettings(candidate, language);
        return await new LocalRecognitionService().Recognize(image, candidate, token);
    }

    internal static AppSettings ResolveSettings(AppSettings source, string? availableWindowsLanguage)
    {
        var candidate = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(source))!;
        var ready = candidate.OcrBackend switch
        {
            "PaddleOCR" => PaddleReady(candidate),
            "Tesseract" => candidate.TesseractLanguages.Split('+').All(x => File.Exists(Path.Combine(candidate.TesseractDataDirectory, x + ".traineddata"))),
            "Windows" => availableWindowsLanguage is not null,
            _ => false
        };
        if (!ready || candidate.OcrBackend == "Windows")
        {
            if (availableWindowsLanguage is null) throw new InvalidOperationException(L.Get("ScreenWordsNeedsOcr"));
            candidate.OcrBackend = "Windows"; candidate.WindowsOcrLanguage = availableWindowsLanguage;
        }
        candidate.RecognitionTimeoutSeconds = Math.Clamp(candidate.RecognitionTimeoutSeconds, 15, 120);
        return candidate;
    }

    private static bool PaddleReady(AppSettings candidate) => new[] { candidate.OcrDetectionModel, candidate.OcrRecognitionModel, candidate.OcrDictionary }.All(File.Exists);
    internal static string? WindowsLanguage(string requested)
    {
        var languages = Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages;
        return languages.FirstOrDefault(x => x.LanguageTag.Equals(requested, StringComparison.OrdinalIgnoreCase))?.LanguageTag
            ?? Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()?.RecognizerLanguage.LanguageTag
            ?? languages.FirstOrDefault()?.LanguageTag;
    }
    private void Hide() { _popup?.Close(); _popup = null; }
    internal static System.Drawing.Rectangle Region(System.Drawing.Point point, System.Drawing.Rectangle screen)
    {
        var width = Math.Min(480, screen.Width); var height = Math.Min(140, screen.Height);
        return new System.Drawing.Rectangle(Math.Clamp(point.X - width / 2, screen.Left, screen.Right - width), Math.Clamp(point.Y - height / 2, screen.Top, screen.Bottom - height), width, height);
    }
    internal static System.Drawing.Rectangle? PopupRegion(System.Drawing.Rectangle roi, System.Drawing.Rectangle work, double dpiScale)
    {
        var width = Math.Min(work.Width, (int)Math.Ceiling(440 * dpiScale));
        var height = (int)Math.Ceiling(130 * dpiScale); var gap = (int)Math.Ceiling(6 * dpiScale);
        var y = roi.Bottom + gap;
        if (y + height > work.Bottom) y = roi.Top - gap - height;
        if (y < work.Top || y + height > work.Bottom) return null;
        return new System.Drawing.Rectangle(Math.Clamp(roi.Left, work.Left, work.Right - width), y, width, height);
    }
    public void Dispose() { _disposed = true; SetEnabled(false); }
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
}
