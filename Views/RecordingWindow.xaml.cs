using L = SnapSharp.Localization.UiText;
using SnapSharp.Models;
using SnapSharp.Services;
using ScreenRecorderLib;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using ComboBox = System.Windows.Controls.ComboBox;

namespace SnapSharp.Views;

public partial class RecordingWindow : Window
{
    private readonly System.Drawing.Rectangle _region;
    private readonly SettingsService _settings;
    private RecordingService? _service;
    private bool _starting, _refreshing;
    private Task? _deviceRefresh;
    private CancellationTokenSource? _countdown;
    private bool _terminalHandled;
    private RecordingRegionWindow? _regionIndicator;
    private RecordingToolbarWindow? _toolbar;
    private bool _closingSession;
    public bool IsRecording => _starting || _service?.IsActive == true;
    public RecordingWindow(System.Drawing.Rectangle region, SettingsService settings)
    {
        _region = region; _settings = settings; InitializeComponent();
        var p = settings.Current.Recording;
        FormatBox.SelectedIndex = p.Format == "GIF" ? 1 : 0; FrameRateBox.SelectedIndex = p.FrameRate switch { 15 => 0, 60 => 2, _ => 1 };
        QualityBox.SelectedIndex = p.Quality switch { "流畅" => 0, "高清" => 2, _ => 1 };
        HardwareBox.IsChecked = p.HardwareEncoding; CursorBox.IsChecked = p.IncludeCursor; ClicksBox.IsChecked = p.HighlightClicks;
        StopAfterBox.Text = p.StopAfterSeconds.ToString(); GifEdgeBox.Text = p.GifMaximumEdge.ToString();
        CaptureBackendBox.SelectedIndex = p.CaptureBackend == "DXGI" ? 1 : 0;
        BitrateBox.Text = p.BitrateKbps.ToString(); CountdownBox.Text = p.CountdownSeconds.ToString();
        SpeakerBox.IsChecked = p.RecordSystemAudio; MicBox.IsChecked = p.RecordMicrophone; CameraBox.IsChecked = p.RecordCamera;
        Format_Changed(this, null!);
        RegionLabel.Text = L.Format("PhysicalPixelRegionX0Y1", _region.X, _region.Y, _region.Width, _region.Height);
        Closed += (_, _) => CloseTools();
        Loaded += async (_, _) =>
        {
            var environment = RecordingSetupService.Check();
            if (!environment.Ready) { StatusLabel.Text = L.Get("ForFirstUseRunRecordingSetupTo"); return; }
            try { Recorder.SetExcludeFromCapture(new WindowInteropHelper(this).Handle, true); await RefreshDevices(); }
            catch (Exception ex) { StatusLabel.Text = L.Get("RecordingComponentsUnavailable") + ex.GetBaseException().Message + L.Get("CheckTheVCX64RuntimeAndWindows"); }
        };
    }
    private RecordingPreferences Candidate()
    {
        if (!int.TryParse(StopAfterBox.Text, out var stop) || !int.TryParse(GifEdgeBox.Text, out var edge)) throw new ArgumentException(L.Get("StopTimeAndGIFDimensionsMustBe"));
        if (!int.TryParse(BitrateBox.Text, out var bitrate) || !int.TryParse(CountdownBox.Text, out var countdown)) throw new ArgumentException(L.Get("BitrateAndCountdownMustBeIntegers"));
        var value = new RecordingPreferences { Format = ((ComboBoxItem)FormatBox.SelectedItem).Tag.ToString()!, FrameRate = int.Parse(((ComboBoxItem)FrameRateBox.SelectedItem).Tag.ToString()!), Quality = ((ComboBoxItem)QualityBox.SelectedItem).Tag.ToString()!, HardwareEncoding = HardwareBox.IsChecked == true, IncludeCursor = CursorBox.IsChecked == true, HighlightClicks = ClicksBox.IsChecked == true, StopAfterSeconds = stop, GifMaximumEdge = edge };
        value.CaptureBackend = ((ComboBoxItem)CaptureBackendBox.SelectedItem).Tag.ToString()!;
        value.BitrateKbps = bitrate; value.CountdownSeconds = countdown;
        value.RecordSystemAudio = SpeakerBox.IsChecked == true;
        value.RecordMicrophone = MicBox.IsChecked == true;
        value.RecordCamera = CameraBox.IsChecked == true;
        value.SpeakerDeviceId = (SpeakerDevices.SelectedItem as RecordingDevice)?.Id ?? _settings.Current.Recording.SpeakerDeviceId;
        value.MicrophoneDeviceId = (MicDevices.SelectedItem as RecordingDevice)?.Id ?? _settings.Current.Recording.MicrophoneDeviceId;
        value.CameraDeviceId = (CameraDevices.SelectedItem as RecordingDevice)?.Id ?? _settings.Current.Recording.CameraDeviceId;
        value.Validate(); return value;
    }
    private void Format_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (MicBox is null) return; var gif = FormatBox.SelectedIndex == 1;
        MicBox.IsEnabled = SpeakerBox.IsEnabled = !gif;
        if (BitrateBox is not null) BitrateBox.IsEnabled = QualityBox.IsEnabled = FrameRateBox.IsEnabled = HardwareBox.IsEnabled = !gif;
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshDevices();
    private Task RefreshDevices() => _deviceRefresh is { IsCompleted: false } ? _deviceRefresh : _deviceRefresh = RefreshDevicesCore();
    private async Task RefreshDevicesCore()
    {
        if (_refreshing || _service?.IsActive == true) return; _refreshing = true; RefreshButton.IsEnabled = StartButton.IsEnabled = false;
        try
        {
            var devices = await Task.Run(() => (ReadDevices(RecordingService.Microphones), ReadDevices(RecordingService.Speakers), ReadDevices(RecordingService.Cameras)));
            var preferences = _settings.Current.Recording;
            SetDevices(MicDevices, [new RecordingDevice("", L.Get("FollowSystemDefaultDevice")), .. devices.Item1.Devices], preferences.MicrophoneDeviceId);
            SetDevices(SpeakerDevices, [new RecordingDevice("", L.Get("FollowSystemDefaultDevice")), .. devices.Item2.Devices], preferences.SpeakerDeviceId);
            SetDevices(CameraDevices, devices.Item3.Devices, preferences.CameraDeviceId);
            var errors = new[] { devices.Item1.Error, devices.Item2.Error, devices.Item3.Error }.Where(x => x is not null);
            StatusLabel.Text = L.Format("Found0Microphones1SystemAudioDevices", devices.Item1.Devices.Length, devices.Item2.Devices.Length, devices.Item3.Devices.Length) + string.Join("；", errors);
        }
        catch (Exception ex) { StatusLabel.Text = L.Get("DeviceEnumerationFailed") + ex.GetBaseException().Message; }
        finally { _refreshing = false; RefreshButton.IsEnabled = true; UpdateState(); }
    }
    private static (RecordingDevice[] Devices, string? Error) ReadDevices(Func<RecordingDevice[]> read)
    {
        try { return (read(), null); } catch (Exception ex) { return ([], ex.GetBaseException().Message); }
    }
    private static void SetDevices(ComboBox box, RecordingDevice[] devices, string? remembered = null)
    {
        var previous = (box.SelectedItem as RecordingDevice)?.Id ?? (string.IsNullOrEmpty(remembered) ? null : remembered);
        box.ItemsSource = devices;
        box.SelectedIndex = previous is null ? (devices.Length > 0 ? 0 : -1) : Array.FindIndex(devices, x => x.Id == previous);
        // Keep an enabled but unavailable device visible as an error at Start,
        // rather than silently producing a recording without the requested audio.
    }
    private async void Setup_Click(object sender, RoutedEventArgs e)
    {
        if (IsRecording) return;
        RecordingSetupWindow.Open(this);
        if (!RecordingSetupService.Check().Ready) return;
        try { Recorder.SetExcludeFromCapture(new WindowInteropHelper(this).Handle, true); await RefreshDevices(); }
        catch (Exception ex) { StatusLabel.Text = L.Get("RecordingComponentsFailedToLoad") + ex.GetBaseException().Message + L.Get("RestartingSnapSharpOrWindowsMayBeRequired"); }
    }
    private static string? Selected(System.Windows.Controls.CheckBox check, System.Windows.Controls.ComboBox devices, Func<string>? defaultDevice = null)
    {
        if (check.IsChecked != true) return null;
        var id = (devices.SelectedItem as RecordingDevice)?.Id ?? throw new ArgumentException(L.Get("TheSelectedAudioCameraDeviceIsUnavailable"));
        return id.Length == 0 && defaultDevice is not null ? defaultDevice() : id;
    }
    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (IsRecording || _refreshing) return;
        ShowRecordingTools();
        if (_service is not null) _service.Changed -= UpdateState;
        _service = null; _terminalHandled = false;
        FolderButton.IsEnabled = RecoveryButton.IsEnabled = false;
        _starting = true; UpdateState();
        try
        {
            if (!RecordingSetupService.Check().Ready)
            {
                if (!IsVisible) ShowConfiguration();
                RecordingSetupWindow.Open(this);
                if (!RecordingSetupService.Check().Ready) throw new InvalidOperationException(L.Get("RecordingRequirementsAreNotReadyCompleteSetup"));
            }
            await RefreshDevices();
            var p = Candidate();
            var screen = System.Windows.Forms.Screen.FromRectangle(_region);
            if (!screen.Bounds.Contains(_region)) throw new ArgumentException(L.Get("RecordingRegionMustFitWithinASingle"));
            var destination = RecordingOutputPath.Create(_settings.Current.RecordingSaveDirectory, p.Format, DateTime.Now);
            SavePreferences(p);
            _service = null;
            Hide(); ShowRecordingTools();
            using var countdown = new CancellationTokenSource(); _countdown = countdown; UpdateState();
            for (var seconds = p.CountdownSeconds; seconds > 0; seconds--)
            {
                StatusLabel.Text = L.Format("RecordingStartsIn0SecondsClickCancel", seconds);
                _toolbar?.Countdown(seconds);
                await Task.Delay(1000, countdown.Token);
            }
            await Task.Delay(250, countdown.Token);
            _countdown = null;
            var currentScreen = System.Windows.Forms.Screen.FromRectangle(_region);
            if (currentScreen.DeviceName != screen.DeviceName || currentScreen.Bounds != screen.Bounds || !currentScreen.Bounds.Contains(_region))
                throw new InvalidOperationException(L.Get("MonitorLayoutChangedDuringTheCountdownSelect"));
            _service = new RecordingService(Dispatcher); _service.Changed += UpdateState;
            // Resolve default audio after the countdown so recent Windows output changes apply.
            var mic = p.Format == "GIF" ? null : Selected(MicBox, MicDevices, RecordingDeviceService.DefaultMicrophone);
            var speaker = p.Format == "GIF" ? null : Selected(SpeakerBox, SpeakerDevices, RecordingDeviceService.DefaultSpeaker);
            var camera = Selected(CameraBox, CameraDevices);
            _service.Start(_region, screen.Bounds, screen.DeviceName, destination, p, mic, speaker, camera);
        }
        catch (OperationCanceledException) { StatusLabel.Text = L.Get("RecordingStartCanceledScreenAndDevicesWere"); ShowConfiguration(); }
        catch (Exception ex)
        {
            if (_service?.State == RecordingState.Ready) { _service.Changed -= UpdateState; _service = null; }
            StatusLabel.Text = ex.GetBaseException().Message; ShowConfiguration();
        }
        finally { _countdown = null; _starting = false; UpdateState(); }
    }
    private void UpdateState()
    {
        UpdateTools();
        RunningConfigurationHint.Visibility = IsRecording ? Visibility.Visible : Visibility.Collapsed;
        SetupButton.IsEnabled = !IsRecording;
        StopButton.Content = _countdown is null ? L.Get("StopAndSave") : L.Get("CancelStart");
        if (_starting || _service is null)
        {
            StartButton.IsEnabled = !_starting && !_refreshing; OptionsPanel.IsEnabled = !_starting;
            PauseButton.IsEnabled = false; StopButton.IsEnabled = _countdown is not null; return;
        }
        ClockLabel.Text = _service.Elapsed.ToString(@"hh\:mm\:ss"); FrameLabel.Text = L.Format("0N0Frames1F12Fps", _service.FrameCount, _service.AverageFrameRate, _service.TargetFrameRate, _service.OutputBytes / 1048576d) +
            (_service.IsFrameRateLimited ? L.Get("LowFrameRateReduceFrameRateDimensions") : "");
        StatusLabel.Text = _service.Status; StartButton.IsEnabled = !_service.IsActive; OptionsPanel.IsEnabled = !_service.IsActive;
        PauseButton.IsEnabled = _service.State is RecordingState.Recording or RecordingState.Paused;
        StopButton.IsEnabled = _service.State is RecordingState.Starting or RecordingState.Recording or RecordingState.Paused;
        FolderButton.IsEnabled = _service.SavedPath is not null;
        RecoveryButton.IsEnabled = _service.RecoveryDirectory is not null;
        if (!_terminalHandled && _service.State is RecordingState.Completed or RecordingState.Failed)
        {
            CloseTools();
            _terminalHandled = true;
            ShowConfiguration();
        }
    }
    public void ShowRecordingTools()
    {
        if (_regionIndicator is null)
        {
            var effective = new System.Drawing.Rectangle(_region.X, _region.Y, _region.Width & ~1, _region.Height & ~1);
            _regionIndicator = new RecordingRegionWindow(effective);
        }
        if (_toolbar is null)
        {
            _toolbar = new RecordingToolbarWindow();
            _toolbar.RecordRequested += () => Start_Click(this, new RoutedEventArgs());
            _toolbar.PauseRequested += PauseRecording;
            _toolbar.StopRequested += StopRecording;
            _toolbar.ConfigurationRequested += ShowConfiguration;
            _toolbar.CloseRequested += CloseSession;
            _toolbar.Closed += (_, _) => _toolbar = null;
        }
        if (IsVisible && WindowState != WindowState.Minimized) HideTools();
        else { _regionIndicator.Show(); _toolbar.ShowForRegion(_region); }
        UpdateTools();
    }
    private void ShowConfiguration()
    {
        HideTools();
        Show(); WindowState = WindowState.Normal; Activate();
    }
    private void HideTools() { _regionIndicator?.Hide(); _toolbar?.Hide(); }
    private void SavePreferences(RecordingPreferences preferences)
    {
        if (System.Text.Json.JsonSerializer.Serialize(preferences) == System.Text.Json.JsonSerializer.Serialize(_settings.Current.Recording)) return;
        var candidate = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(_settings.Current))!;
        candidate.Recording = preferences.Copy();
        _settings.Update(candidate);
    }
    private void UpdateTools()
    {
        _toolbar?.Update(_service?.State ?? RecordingState.Ready, _starting, _countdown is not null,
            _service?.Elapsed ?? TimeSpan.Zero, _service?.AverageFrameRate ?? 0);
        _regionIndicator?.SetPaused(_service?.State == RecordingState.Paused);
    }
    private void CloseTools()
    {
        _regionIndicator?.Close(); _regionIndicator = null;
        _toolbar?.Close(); _toolbar = null;
    }
    public void StopRecording() { if (_countdown is not null) _countdown.Cancel(); else _service?.Stop(); }
    public void PauseRecording() { try { _service?.PauseOrResume(); } catch (Exception ex) { StatusLabel.Text = ex.Message; } }
    public async Task<bool> StopAndWait()
    {
        if (_starting) { MessageBox.Show(this, L.Get("RecordingIsStartingTryAgainShortly")); return false; }
        if (_service?.IsActive != true) return true;
        if (MessageBox.Show(this, L.Get("StopRecordingWaitForTheFileTo"), L.Get("EndRecording"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return false;
        _service.Stop(); await _service.Completion; return _service.SavedPath is not null;
    }
    private void Pause_Click(object sender, RoutedEventArgs e) => PauseRecording();
    private void Stop_Click(object sender, RoutedEventArgs e) => StopRecording();
    private void Folder_Click(object sender, RoutedEventArgs e) { if (_service?.SavedPath is { } path) Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true }); }
    private void Recovery_Click(object sender, RoutedEventArgs e)
    {
        try { if (_service?.RecoveryDirectory is { } path) Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true }); }
        catch (Exception ex) { StatusLabel.Text = ex.Message; }
    }
    public void CloseSession()
    {
        _closingSession = true;
        try { Close(); } finally { _closingSession = false; }
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!IsRecording)
        {
            try { SavePreferences(Candidate()); }
            catch (Exception ex) { StatusLabel.Text = ex.GetBaseException().Message; e.Cancel = true; return; }
        }
        if (IsRecording || (!_closingSession && _toolbar is not null))
        {
            e.Cancel = true; Hide(); ShowRecordingTools();
        }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    internal static bool RunLayoutSelfTest()
    {
        var window = new RecordingWindow(new System.Drawing.Rectangle(-100, 20, 640, 480),
            new SettingsService(Path.Combine(Path.GetTempPath(), "SnapSharp", "RecordingSelfTests", Guid.NewGuid().ToString("N"), "settings.json")));
        try
        {
            var content = (FrameworkElement)window.Content; content.Measure(new System.Windows.Size(700, 740)); content.Arrange(new Rect(0, 0, 700, 740)); content.UpdateLayout();
            if (window.Candidate().FrameRate != 30 || window.StartButton.ActualWidth < 90 || window.OptionsPanel.ActualWidth <= 450) return false;
            window.CaptureBackendBox.SelectedIndex = 1; window.BitrateBox.Text = "8000"; window.CountdownBox.Text = "5";
            var candidate = window.Candidate();
            if (candidate.CaptureBackend != "DXGI" || candidate.BitrateKbps != 8000 || candidate.CountdownSeconds != 5) return false;
            window.FormatBox.SelectedIndex = 1;
            if (window.MicBox.IsEnabled || window.SpeakerBox.IsEnabled || window.FrameRateBox.IsEnabled || window.BitrateBox.IsEnabled) return false;
            var devices = new[] { new RecordingDevice("a", "first"), new RecordingDevice("b", "second") };
            SetDevices(window.CameraDevices, devices); window.CameraDevices.SelectedIndex = 1; window.CameraBox.IsChecked = true;
            SetDevices(window.CameraDevices, devices.Reverse().ToArray());
            if ((window.CameraDevices.SelectedItem as RecordingDevice)?.Id != "b") return false;
            SetDevices(window.CameraDevices, [devices[0]]);
            if (window.CameraBox.IsChecked != true || window.CameraDevices.SelectedIndex != -1) return false;
            using var countdown = new CancellationTokenSource(); window._starting = true; window._countdown = countdown; window.UpdateState();
            if (!window.StopButton.IsEnabled || window.StartButton.IsEnabled || window.SetupButton.IsEnabled) return false;
            window.StopRecording();
            return countdown.IsCancellationRequested && RecordingToolbarWindow.RunLayoutSelfTest();
        }
        finally { window._starting = false; window._countdown = null; window.Close(); }
    }
    internal static async Task TestFloatingConfiguration()
    {
        var window = new RecordingWindow(new System.Drawing.Rectangle(40, 40, 320, 192),
            new SettingsService(Path.Combine(Path.GetTempPath(), "SnapSharp", "RecordingSelfTests", Guid.NewGuid().ToString("N"), "settings.json")));
        try
        {
            window.ShowRecordingTools();
            if (window.IsVisible || window._regionIndicator?.IsVisible != true || window._toolbar?.IsVisible != true)
                throw new InvalidOperationException("录屏没有先显示区域和悬浮工具条。");
            var configuration = (System.Windows.Controls.Button)window._toolbar.FindName("ConfigurationButton");
            configuration.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (!window.IsVisible || window.WindowState != WindowState.Normal)
                throw new InvalidOperationException("工具条配置按钮没有打开配置窗口。");
            if (window._toolbar.IsVisible || window._regionIndicator?.IsVisible == true)
                throw new InvalidOperationException("录屏悬浮层遮挡了配置窗口。");
            var timeout = Stopwatch.StartNew();
            while (window._refreshing && timeout.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
            if (window._refreshing) throw new TimeoutException("配置窗口设备刷新超时。");
            window.Close();
            if (window.IsVisible || window._toolbar?.IsVisible != true || window._regionIndicator?.IsVisible != true)
                throw new InvalidOperationException("收起配置窗口后丢失了录屏工具条。");
            configuration.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (!window.IsVisible) throw new InvalidOperationException("无法重新打开录制配置。");
        }
        finally { window.CloseSession(); }
        if (window._toolbar is not null || window._regionIndicator is not null)
            throw new InvalidOperationException("录屏关闭后区域或工具条残留。");
    }
}
