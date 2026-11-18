using L = SnapSharp.Localization.UiText;
using SnapSharp.Models;
using SnapSharp.Views;

namespace SnapSharp.Services;

public sealed class CaptureCoordinator
{
    private ICaptureService _captureService;
    private readonly SettingsService? _settings;
    private readonly List<SelectionWindow> _windows = [];
    private static bool _selectingScrollingRegion;

    public event EventHandler<CaptureResult>? CaptureCompleted;
    public event EventHandler? CaptureCanceled;
    public bool IsActive => _windows.Count > 0 || _selectingScrollingRegion;

    public CaptureCoordinator(ICaptureService? captureService = null, SettingsService? settings = null)
    {
        _captureService = captureService ?? new ScreenCaptureService();
        _settings = settings;
    }

    public void SetCaptureService(ICaptureService captureService)
    {
        ArgumentNullException.ThrowIfNull(captureService);
        if (IsActive) throw new InvalidOperationException("Cannot change capture backends during a capture.");
        _captureService = captureService;
    }

    public void Begin(CaptureMode mode, nint targetWindow = default, bool scrollingSelection = false)
    {
        if (_windows.Count > 0) return;
        if (mode == CaptureMode.FullScreen)
        {
            CompleteDirectCapture(_captureService.CaptureVirtualScreen());
            return;
        }
        if (mode == CaptureMode.ActiveWindow)
        {
            var capture = _captureService.CaptureWindow(targetWindow);
            if (capture is null) throw new InvalidOperationException(L.Get("CouldNotGetTheActiveWindow"));
            CompleteDirectCapture(capture);
            return;
        }

        var screens = _captureService.CaptureAllScreens();
        var candidates = _captureService.EnumerateTopLevelWindows();
        foreach (var screen in screens)
        {
            var window = new SelectionWindow(screen, candidates, _captureService, scrollingSelection, settings: _settings);
            window.PrecisionPreferencesChanged += OnPrecisionPreferencesChanged;
            window.CaptureAccepted += OnAccepted;
            window.CaptureCanceled += OnCanceled;
            window.Closed += OnCanceled;
            _windows.Add(window);
        }
        foreach (var window in _windows) window.Show();
        if (SnapSharp.Native.NativeMethods.GetCursorPos(out var cursor))
        {
            var index = screens.ToList().FindIndex(screen => screen.Bounds.Contains(cursor.X, cursor.Y));
            if (index >= 0) { _windows[index].Activate(); _windows[index].Focus(); }
        }
    }

    private void CompleteDirectCapture(CapturedScreen capture)
    {
        CaptureCompleted?.Invoke(this, new CaptureResult(capture.Image, capture.Bounds,
            new System.Drawing.Rectangle(0, 0, capture.Bounds.Width, capture.Bounds.Height)));
    }

    private void OnPrecisionPreferencesChanged(object? sender, EventArgs e)
    {
        foreach (var window in _windows) window.RefreshPrecisionPreferences();
    }

    private void OnAccepted(object? sender, CaptureResult result)
    {
        CloseAll();
        CaptureCompleted?.Invoke(this, result);
    }

    private void OnCanceled(object? sender, EventArgs e)
    {
        CloseAll();
        CaptureCanceled?.Invoke(this, EventArgs.Empty);
    }

    // Use the ordinary multi-monitor region overlay, keeping our own windows out of the screenshot.
    public static async Task<CaptureResult?> SelectScrollingRegionAsync()
    {
        var app = System.Windows.Application.Current;
        if (_selectingScrollingRegion || app.Windows.OfType<SelectionWindow>().Any()) return null;
        if (app.Windows.OfType<ScrollCaptureWindow>().Any(x => x.IsCapturing) ||
            app.Windows.OfType<RecordingWindow>().Any(x => x.IsRecording))
            throw new InvalidOperationException(L.Get("StopTheCurrentScrollingCaptureOrRecording"));
        _selectingScrollingRegion = true;
        var hidden = app.Windows.OfType<System.Windows.Window>().Where(x => x.IsVisible).ToArray();
        var settings = new SettingsService();
        var coordinator = new CaptureCoordinator();
        try
        {
            settings.Load();
            coordinator = new CaptureCoordinator(new ResilientCaptureService(settings.Current.PreferWgcCapture, false), settings);
            var completion = new TaskCompletionSource<CaptureResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
            coordinator.CaptureCompleted += (_, result) => completion.TrySetResult(result);
            coordinator.CaptureCanceled += (_, _) => completion.TrySetResult(null);
            foreach (var window in hidden) window.Hide();
            await Task.Delay(200);
            coordinator.Begin(CaptureMode.Region, scrollingSelection: true);
            if (coordinator._windows.Count == 0) return null;
            return await completion.Task;
        }
        finally
        {
            coordinator.CloseAll();
            _selectingScrollingRegion = false;
            foreach (var window in hidden.Where(x => x.IsLoaded)) window.Show();
        }
    }

    private void CloseAll()
    {
        var windows = _windows.ToArray();
        _windows.Clear();
        foreach (var window in windows)
        {
            window.CaptureAccepted -= OnAccepted;
            window.CaptureCanceled -= OnCanceled;
            window.Closed -= OnCanceled;
            window.PrecisionPreferencesChanged -= OnPrecisionPreferencesChanged;
            window.Close();
        }
    }
}
