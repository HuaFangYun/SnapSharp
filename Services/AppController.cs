using L = SnapSharp.Localization.UiText;
using SnapSharp.Models;
using SnapSharp.Views;
using SnapSharp.Native;
using SnapSharp.Plugins;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SnapSharp.Services;

public sealed class AppController : IDisposable
{
    private readonly TrayService _tray;
    private readonly GlobalHotkeyHost _hotkey;
    private readonly CaptureCoordinator _capture;
    private readonly SettingsService _settings;
    private readonly SkiaImageService _images;
    private readonly HistoryService _history;
    private readonly IClipboardService _clipboard;
    private readonly PluginCatalog _plugins;
    private readonly StartupRegistrationService _startup = new();
    private readonly List<Window> _openWindows = [];
    private readonly List<PinWindow> _pins = [];
    private readonly Stack<(PinContent Content, string? Group)> _closedPins = [];
    private readonly PinWorkspace _pinWorkspace = new();
    private CancellationTokenSource? _delayedScreenshot;
    private SettingsWindow? _settingsWindow;
    private ScreenWordService? _screenWords;
    private RecordingWindow? _recordingWindow;

    public AppController()
    {
        _settings = new SettingsService();
        _settings.Load();
        LocalizationService.Apply(_settings.Current.InterfaceLanguage);
        _tray = new TrayService();
        _hotkey = new GlobalHotkeyHost();
        var licenseFailures = ThirdPartyLicenseService.BackfillManagedModelLicenses();
        if (licenseFailures.Count > 0)
        {
            Directory.CreateDirectory(AppPaths.DiagnosticsRoot);
            File.WriteAllLines(Path.Combine(AppPaths.DiagnosticsRoot, "license-backfill.txt"), licenseFailures);
        }
        _plugins = new PluginCatalog(new PluginLoadOptions(AppPaths.PluginsRoot,
            _settings.Current.EnableExternalPlugins, _settings.Current.RequireSignedPlugins));
        _plugins.DiscoverExternalPlugins();
        _plugins.RegisterBuiltIn(new OfflineRecognitionPlugin(() => _settings.Current));
        _plugins.RegisterBuiltIn(new OfflineRecognitionPlugin(() => _settings.Current, true));
        RecognitionUi.OpenRequested = OpenRecognition;
        _capture = new CaptureCoordinator(new ResilientCaptureService(
            _settings.Current.PreferWgcCapture, _settings.Current.IncludeCursor));
        _images = new SkiaImageService();
        _history = new HistoryService(_images, _settings);
        _clipboard = new ClipboardService();
    }

    public void Start()
    {
        _tray.CaptureRequested += RequestCapture;
        _tray.ScrollingCaptureRequested += (_, _) => RequestCapture(CaptureMode.Region, true);
        _screenWords = new ScreenWordService(() => _settings.Current, () => _capture.IsActive || _settingsWindow is not null || _openWindows.OfType<ScrollCaptureWindow>().Any(x => x.IsCapturing) || Application.Current.Windows.OfType<Window>().Any(x => x.IsActive));
        _tray.ScreenWordsRequested += enabled => _screenWords.SetEnabled(enabled);
        _screenWords.Failed += error => { _tray.SetScreenWordsChecked(false); _tray.ShowMessage(L.Get("ScreenTextCaptureStopped"), error, System.Windows.Forms.ToolTipIcon.Warning); };
        _tray.RecognitionClipboardRequested += (_, _) =>
        {
            try
            {
                var image = _clipboard.GetImage() ?? throw new InvalidOperationException(L.Get("NoReadableImageInTheClipboardCopy"));
                OnnxImageRuntime.ValidateImage(image);
                OpenRecognitionCore(image, null, "OCR", false);
            }
            catch (Exception ex) { _tray.ShowMessage(L.Get("RecognizeClipboardImage"), ex.GetBaseException().Message); }
        };
        _tray.DelayedScreenshotRequested += ScheduleDelayedScreenshot;
        _tray.CancelDelayedScreenshotRequested += CancelDelayedScreenshot;
        _tray.HistoryRequested += (_, _) => ShowHistory();
        _tray.SettingsRequested += (_, _) => ShowSettings();
        _tray.RecordingRequested += (_, _) => OpenRecording(System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).Bounds);
        _tray.PauseRecordingRequested += (_, _) => _recordingWindow?.PauseRecording();
        _tray.StopRecordingRequested += (_, _) => _recordingWindow?.StopRecording();
        _tray.OpenScrollProjectRequested += (_, _) => OpenScrollProject();
        _tray.StopScrollingRequested += (_, _) => { foreach (var window in _openWindows.OfType<ScrollCaptureWindow>()) window.StopCapture(); };
        _tray.TogglePinsRequested += (_, _) => TogglePins();
        _tray.RestorePinRequested += (_, _) => RestorePin();
        _tray.RestorePinInteractionRequested += (_, _) => RestorePinInteraction();
        _tray.PinRequested += CreatePin;
        _tray.PinGroupRequested += (group, action) =>
        {
            if (action == "close") _pinWorkspace.CloseGroup(group);
            else _pinWorkspace.SetGroupVisible(group, action == "show");
        };
        _pinWorkspace.Changed += (_, _) => _tray.UpdatePinGroups(_pinWorkspace.Groups);
        _hotkey.PinActionRequested += action =>
        {
            switch (action)
            {
                case GlobalAction.ClipboardPin: CreatePin(PinCommand.Clipboard); break;
                case GlobalAction.TogglePins: TogglePins(); break;
                case GlobalAction.RestorePin: RestorePin(); break;
                case GlobalAction.RestoreInteraction: RestorePinInteraction(); break;
            }
        };
        _tray.ExitRequested += async (_, _) => { if (_recordingWindow is null || await _recordingWindow.StopAndWait()) Application.Current.Shutdown(); };
        _hotkey.CaptureRequested += RequestCapture;
        _capture.CaptureCompleted += OnCaptureCompleted;
        _tray.UpdateHotkeyLabels(_settings.Current);
        if (_plugins.Failures.Count > 0)
            _tray.ShowMessage(L.Get("PluginLoading"), L.Format("0PluginsCouldNotBeLoaded", _plugins.Failures.Count),
                System.Windows.Forms.ToolTipIcon.Warning);
        Application.Current.Dispatcher.BeginInvoke(_tray.Show,
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        try
        {
            _hotkey.Register(_settings.Current);
            _tray.ShowMessage("SnapSharp", L.Format("StartedPress0ToCaptureARegion", _settings.Current.RegionHotkey));
        }
        catch (Exception ex)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                File.WriteAllText(Path.Combine(AppPaths.Root, "startup-error.log"), ex.ToString());
            }
            catch { }
            _tray.ShowMessage(L.Get("ShortcutRegistrationFailed"), ex.Message, System.Windows.Forms.ToolTipIcon.Warning);
        }
    }

    private void RequestCapture(object? sender, CaptureModeEventArgs e) => RequestCapture(e.Mode);

    private async void RequestCapture(CaptureMode mode, bool scrolling = false)
    {
        if (_recordingWindow?.IsRecording == true) { _tray.ShowMessage(L.Get("Recording"), L.Get("RecordingOrSavingIsInProgressStop")); return; }
        if (_openWindows.OfType<ScrollCaptureWindow>().Any(x => x.IsCapturing)) { _tray.ShowMessage(L.Get("ScrollingCapture"), L.Get("ScrollingCaptureIsInProgressPressEsc")); return; }
        if (_settingsWindow is not null) { _tray.ShowMessage(L.Get("Settings"), L.Get("CloseSettingsBeforeStartingACapture")); return; }
        CancelDelayedScreenshot(showMessage: false);
        if (_capture.IsActive) return;
        try
        {
            if (!Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(() => RequestCapture(mode, scrolling));
                return;
            }

            if (scrolling)
            {
                var selection = await CaptureCoordinator.SelectScrollingRegionAsync();
                if (selection is not null) OpenScrolling(selection);
                return;
            }

            var targetWindow = mode == CaptureMode.ActiveWindow ? GetCaptureTargetWindow() : nint.Zero;
            var hiddenWindows = Application.Current.Windows.Cast<Window>()
                .Where(window => window.IsVisible && window is not SelectionWindow)
                .ToArray();
            foreach (var window in hiddenWindows) window.Hide();
            if (hiddenWindows.Length > 0) await Task.Delay(200);

            try
            {
                _capture.Begin(mode, targetWindow);
            }
            finally
            {
                foreach (var window in hiddenWindows.Where(window => window.IsLoaded)) window.Show();
            }
        }
        catch (Exception ex)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                File.WriteAllText(Path.Combine(AppPaths.Root, "capture-error.log"), ex.ToString());
            }
            catch { }
            _tray.ShowMessage(L.Get("CouldNotStartCapture"), ex.Message, System.Windows.Forms.ToolTipIcon.Error);
        }
    }

    private nint GetCaptureTargetWindow()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
        if (foreground != nint.Zero && processId != (uint)Environment.ProcessId) return foreground;
        return new ScreenCaptureService().EnumerateTopLevelWindows().FirstOrDefault()?.Handle ?? nint.Zero;
    }

    private void ScheduleDelayedScreenshot(object? sender, DelayedScreenshotEventArgs e) =>
        ScheduleDelayedCapture(CaptureMode.Region, e.Seconds);

    private async void ScheduleDelayedCapture(CaptureMode mode, int seconds)
    {
        CancelDelayedScreenshot(showMessage: false);
        var cancellation = new CancellationTokenSource();
        _delayedScreenshot = cancellation;
        _tray.SetDelayedScreenshotPending(true);
        _tray.ShowMessage(L.Get("DelayedCapture"), L.Format("CaptureStartsIn0SecondsCancelFrom", seconds));

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), cancellation.Token);
            if (_delayedScreenshot == cancellation)
            {
                _delayedScreenshot = null;
                _tray.SetDelayedScreenshotPending(false);
                RequestCapture(mode);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    public void HandleLaunchArguments(IReadOnlyList<string> args, bool fromSecondaryInstance)
    {
        if (!LaunchCommand.TryParse(args, out var command, out var error))
        {
            _tray.ShowMessage(L.Get("InvalidCommandLineArguments"), error, System.Windows.Forms.ToolTipIcon.Warning);
            return;
        }

        var mode = command.Mode;
        if (mode is null && fromSecondaryInstance) mode = CaptureMode.Region;
        if (mode is null) return;
        if (command.DelaySeconds > 0) ScheduleDelayedCapture(mode.Value, command.DelaySeconds);
        else RequestCapture(mode.Value);
    }

    private void CancelDelayedScreenshot(object? sender, EventArgs e) => CancelDelayedScreenshot(showMessage: true);

    private void CancelDelayedScreenshot(bool showMessage)
    {
        var cancellation = Interlocked.Exchange(ref _delayedScreenshot, null);
        if (cancellation is null) return;
        cancellation.Cancel();
        _tray.SetDelayedScreenshotPending(false);
        if (showMessage) _tray.ShowMessage(L.Get("DelayedCapture"), L.Get("DelayedCaptureCanceled"));
    }

    private void OnCaptureCompleted(object? sender, CaptureResult result)
    {
        if (result.Action == CaptureAction.Recording)
        {
            OpenRecording(new System.Drawing.Rectangle(result.ScreenBounds.Left + result.PixelRegion.Left, result.ScreenBounds.Top + result.PixelRegion.Top, result.PixelRegion.Width, result.PixelRegion.Height)); return;
        }
        if (result.Action is CaptureAction.OCR or CaptureAction.Cutout) { OpenRecognition(result.Image, null, result.Action == CaptureAction.Cutout ? "Cutout" : "OCR"); return; }
        if (result.Action == CaptureAction.Scrolling) { OpenScrolling(result); return; }
        if (_settings.Current.AutoSaveImages)
        {
            try
            {
                var options = _settings.Current;
                var name = OutputFileService.NewName(options.FileNameTemplate, result.Image.PixelWidth, result.Image.PixelHeight);
                var bytes = _images.Encode(result.Image, options.OutputFormat, options.ImageQuality, options.WebpLossless);
                OutputFileService.WriteUnique(OutputFileService.AutomaticDirectory(options, DateTime.Now), name, options.OutputFormat, bytes);
            }
            catch (Exception ex) { _tray.ShowMessage(L.Get("AutomaticSaveFailed"), ex.Message, System.Windows.Forms.ToolTipIcon.Warning); }
        }
        try { _history.Add(result.Image); }
        catch (Exception ex) { _tray.ShowMessage(L.Get("HistoryCouldNotBeSaved"), ex.Message, System.Windows.Forms.ToolTipIcon.Warning); }

        if (result.Action == CaptureAction.Copy)
        {
            _clipboard.SetImage(result.Image);
            _tray.ShowMessage(L.Get("CaptureComplete"), L.Get("ImageCopiedToClipboard"));
            return;
        }

        if (result.Action == CaptureAction.Save)
        {
            SaveImageWindow.Open(result.Image, _images, _settings);
            return;
        }

        OpenPin(result.Image);
    }

    private ScrollCaptureWindow CreateScrollWindow(System.Drawing.Rectangle region, BitmapSource initial)
    {
        var window = new ScrollCaptureWindow(region, initial);
        _openWindows.Add(window);
        window.Closed += (_, _) => _openWindows.Remove(window);
        window.OutputRequested += (image, action) => OnCaptureCompleted(window, new CaptureResult(image, region,
            new System.Drawing.Rectangle(0, 0, region.Width, region.Height), action));
        window.EditRequested += image =>
        {
            try { _history.Add(image); } catch (Exception ex) { _tray.ShowMessage(L.Get("HistoryCouldNotBeSaved"), ex.Message, System.Windows.Forms.ToolTipIcon.Warning); }
            var editor = new EditorWindow(new CaptureResult(image, region, new System.Drawing.Rectangle(0, 0, region.Width, region.Height)), OpenPin, _clipboard);
            _openWindows.Add(editor); editor.Closed += (_, _) => _openWindows.Remove(editor); editor.Show();
        };
        return window;
    }
    private void OpenScrolling(CaptureResult result)
    {
        if (_recordingWindow?.IsRecording == true) { _tray.ShowMessage(L.Get("Recording"), L.Get("StopRecordingBeforeStartingAScrollingCapture")); return; }
        var region = new System.Drawing.Rectangle(result.ScreenBounds.Left + result.PixelRegion.Left,
            result.ScreenBounds.Top + result.PixelRegion.Top, result.PixelRegion.Width, result.PixelRegion.Height);
        CreateScrollWindow(region, result.Image).BeginManualCapture();
    }
    private async void OpenScrollProject()
    {
        if (_openWindows.OfType<ScrollCaptureWindow>().Any(x => x.IsCapturing)) { _tray.ShowMessage(L.Get("ScrollingCapture"), L.Get("StopTheCurrentCaptureFirst")); return; }
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = L.Get("SnapSharpScrollingCaptureProjectSsscroll") };
        if (dialog.ShowDialog() != true) return;
        var placeholder = BitmapSource.Create(32, 32, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[32 * 32 * 4], 32 * 4); placeholder.Freeze();
        var window = CreateScrollWindow(System.Drawing.Rectangle.Empty, placeholder);
        window.Show(); await window.LoadProject(dialog.FileName);
    }

    private void OpenPin(BitmapSource image)
        => OpenPinContent(new PinContent(image));

    private void OpenPinContent(PinContent content, string? group = null)
    {
        var pin = new PinWindow(content.Image, _images, _settings, _clipboard, _pinWorkspace, content);
        if (group is not null) _pinWorkspace.Assign(pin, group);
        _pins.Add(pin);
        _openWindows.Add(pin);
        pin.Closed += (_, _) =>
        {
            _pins.Remove(pin);
            _openWindows.Remove(pin);
            if (Application.Current.ShutdownMode == ShutdownMode.OnExplicitShutdown) _closedPins.Push((pin.ContentInfo with { Image = pin.Image }, pin.GroupName));
        };
        pin.Show();
    }

    private void CreatePin(PinCommand command)
    {
        if (_openWindows.OfType<ScrollCaptureWindow>().Any(x => x.IsCapturing)) { _tray.ShowMessage(L.Get("ScrollingCapture"), L.Get("StopCaptureBeforeUsingPins")); return; }
        try
        {
            if (command == PinCommand.CloseAll) { foreach (var pin in _pins.ToArray()) pin.Close(); return; }
            if (command == PinCommand.Clipboard && System.Windows.Clipboard.ContainsImage())
            {
                var image = System.Windows.Clipboard.GetImage() ?? throw new InvalidOperationException(L.Get("CouldNotReadTheClipboardImage"));
                image.Freeze(); OpenPin(image); return;
            }
            var text = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : "";
            if (command == PinCommand.Clipboard)
            {
                if (PinContentService.TryParseColor(text, out var color)) OpenPinContent(PinContentService.FromColor(color));
                else OpenPinContent(PinContentService.FromText(text, _settings.Current));
                return;
            }
            var dialog = new TextInputDialog
            {
                Title = command == PinCommand.Text ? L.Get("NewTextPin") : L.Get("NewColorPinHEXOrRgbR"),
                InitialText = command == PinCommand.Text ? text : PinContentService.TryParseColor(text, out _) ? text : "#3B82F6"
            };
            if (dialog.ShowDialog() != true) return;
            if (command == PinCommand.Text) OpenPinContent(PinContentService.FromText(dialog.ResultText, _settings.Current));
            else
            {
                if (!PinContentService.TryParseColor(dialog.ResultText, out var color)) throw new ArgumentException(L.Get("EnterRGBRRGGBBAARRGGBBOrRgb0"));
                OpenPinContent(PinContentService.FromColor(color));
            }
        }
        catch (Exception ex) { _tray.ShowMessage(L.Get("CouldNotCreatePin"), ex.Message, System.Windows.Forms.ToolTipIcon.Warning); }
    }

    private void ShowHistory()
    {
        if (_openWindows.OfType<ScrollCaptureWindow>().Any(x => x.IsCapturing)) return;
        var existing = _openWindows.OfType<HistoryWindow>().FirstOrDefault();
        if (existing is not null) { existing.Activate(); return; }
        var window = new HistoryWindow(_history, _images, _settings, _clipboard, OpenPin);
        _openWindows.Add(window);
        window.Closed += (_, _) => _openWindows.Remove(window);
        window.Show();
    }

    private void OpenRecognition(BitmapSource image, Window? owner, string mode)
        => OpenRecognitionCore(image, owner, mode, true);

    private void OpenRecognitionCore(BitmapSource image, Window? owner, string mode, bool autoRun)
    {
        var window = new RecognitionWindow(image, _settings, _plugins, mode, autoRun);
        if (owner?.IsVisible == true) window.Owner = owner;
        window.SettingsRequested += category => ShowSettings(category);
        window.OutputRequested += (output, action) => OnCaptureCompleted(window, new CaptureResult(output, System.Drawing.Rectangle.Empty, System.Drawing.Rectangle.Empty, action));
        _openWindows.Add(window); window.Closed += (_, _) => _openWindows.Remove(window); window.Show();
    }

    private void OpenRecording(System.Drawing.Rectangle region)
    {
        if (_recordingWindow is not null) { _recordingWindow.ShowRecordingTools(); return; }
        if (_openWindows.OfType<ScrollCaptureWindow>().Any(x => x.IsCapturing)) { _tray.ShowMessage(L.Get("Recording"), L.Get("StopTheScrollingCaptureFirst")); return; }
        var window = new RecordingWindow(region, _settings); _recordingWindow = window;
        _screenWords?.SetEnabled(false); _tray.SetScreenWordsChecked(false);
        _openWindows.Add(window); window.Closed += (_, _) => { _recordingWindow = null; _openWindows.Remove(window); }; window.ShowRecordingTools();
    }

    private void ShowSettings(string? category = null)
    {
        if (_openWindows.OfType<ScrollCaptureWindow>().Any(x => x.IsCapturing)) { _tray.ShowMessage(L.Get("ScrollingCapture"), L.Get("StopCaptureBeforeOpeningSettings")); return; }
        if (_settingsWindow is not null) { _settingsWindow.Activate(); return; }
        var window = new SettingsWindow(_settings, _startup, _hotkey);
        if (category is not null) window.SelectCategory(category);
        _settingsWindow = window;
        try
        {
            if (window.ShowDialog() == true)
            {
                _tray.UpdateHotkeyLabels(_settings.Current);
                _capture.SetCaptureService(new ResilientCaptureService(
                    _settings.Current.PreferWgcCapture, _settings.Current.IncludeCursor));
            }
        }
        finally { _settingsWindow = null; }
    }

    private void TogglePins()
    {
        var shouldShow = _pins.All(x => !x.IsVisible);
        foreach (var pin in _pins)
        {
            if (shouldShow) pin.Show(); else pin.Hide();
        }
    }

    private void RestorePin()
    {
        if (_closedPins.TryPop(out var pin)) OpenPinContent(pin.Content, pin.Group);
        else _tray.ShowMessage(L.Get("Pins"), L.Get("NoClosedPinToRestore"));
    }

    private void RestorePinInteraction()
    {
        foreach (var pin in _pins.Where(x => x.IsClickThrough)) pin.SetClickThrough(false);
    }

    public void Dispose()
    {
        RecognitionUi.OpenRequested = null;
        _screenWords?.Dispose();
        CancelDelayedScreenshot(showMessage: false);
        foreach (var window in _openWindows.ToArray())
        {
            if (window is RecordingWindow recording) recording.CloseSession(); else window.Close();
        }
        _capture.CaptureCompleted -= OnCaptureCompleted;
        _hotkey.Dispose();
        _tray.Dispose();
    }
}
