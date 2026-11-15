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
        RegionLabel.Text = $"物理像素区域：X {_region.X}，Y {_region.Y} · {_region.Width} × {_region.Height}";
        Closed += (_, _) => CloseTools();
        Loaded += async (_, _) =>
        {
            var environment = RecordingSetupService.Check();
            if (!environment.Ready) { StatusLabel.Text = "首次使用请点击“一键配置录屏环境”，安装缺失组件。"; return; }
            try { Recorder.SetExcludeFromCapture(new WindowInteropHelper(this).Handle, true); await RefreshDevices(); }
            catch (Exception ex) { StatusLabel.Text = "录屏组件不可用：" + ex.GetBaseException().Message + "。请检查 VC++ x64 运行库及 Windows Media Foundation。"; }
        };
    }
    private RecordingPreferences Candidate()
    {
        if (!int.TryParse(StopAfterBox.Text, out var stop) || !int.TryParse(GifEdgeBox.Text, out var edge)) throw new ArgumentException("定时停止和 GIF 尺寸必须为整数。");
        if (!int.TryParse(BitrateBox.Text, out var bitrate) || !int.TryParse(CountdownBox.Text, out var countdown)) throw new ArgumentException("码率和倒计时必须为整数。");
        var value = new RecordingPreferences { Format = ((ComboBoxItem)FormatBox.SelectedItem).Tag.ToString()!, FrameRate = int.Parse(((ComboBoxItem)FrameRateBox.SelectedItem).Tag.ToString()!), Quality = ((ComboBoxItem)QualityBox.SelectedItem).Content.ToString()!, HardwareEncoding = HardwareBox.IsChecked == true, IncludeCursor = CursorBox.IsChecked == true, HighlightClicks = ClicksBox.IsChecked == true, StopAfterSeconds = stop, GifMaximumEdge = edge };
        value.CaptureBackend = ((ComboBoxItem)CaptureBackendBox.SelectedItem).Tag.ToString()!;
        value.BitrateKbps = bitrate; value.CountdownSeconds = countdown;
        value.Validate(); return value;
    }
    private void Format_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (MicBox is null) return; var gif = FormatBox.SelectedIndex == 1;
        MicBox.IsEnabled = SpeakerBox.IsEnabled = !gif;
        if (BitrateBox is not null) BitrateBox.IsEnabled = QualityBox.IsEnabled = FrameRateBox.IsEnabled = HardwareBox.IsEnabled = !gif;
        if (gif) { MicBox.IsChecked = SpeakerBox.IsChecked = false; }
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshDevices();
    private async Task RefreshDevices()
    {
        if (_refreshing || IsRecording) return; _refreshing = true; RefreshButton.IsEnabled = StartButton.IsEnabled = false;
        try
        {
            var devices = await Task.Run(() => (ReadDevices(RecordingService.Microphones), ReadDevices(RecordingService.Speakers), ReadDevices(RecordingService.Cameras)));
            SetDevices(MicDevices, MicBox, devices.Item1.Devices); SetDevices(SpeakerDevices, SpeakerBox, devices.Item2.Devices); SetDevices(CameraDevices, CameraBox, devices.Item3.Devices);
            var errors = new[] { devices.Item1.Error, devices.Item2.Error, devices.Item3.Error }.Where(x => x is not null);
            StatusLabel.Text = $"已找到麦克风 {devices.Item1.Devices.Length}、系统声音 {devices.Item2.Devices.Length}、摄像头 {devices.Item3.Devices.Length}。仅录屏无需勾选设备。" + string.Join("；", errors);
        }
        catch (Exception ex) { StatusLabel.Text = "设备枚举失败：" + ex.GetBaseException().Message; }
        finally { _refreshing = false; RefreshButton.IsEnabled = true; UpdateState(); }
    }
    private static (RecordingDevice[] Devices, string? Error) ReadDevices(Func<RecordingDevice[]> read)
    {
        try { return (read(), null); } catch (Exception ex) { return ([], ex.GetBaseException().Message); }
    }
    private static void SetDevices(ComboBox box, System.Windows.Controls.CheckBox enabled, RecordingDevice[] devices)
    {
        var previous = (box.SelectedItem as RecordingDevice)?.Id;
        box.ItemsSource = devices;
        box.SelectedIndex = previous is null ? (devices.Length > 0 ? 0 : -1) : Array.FindIndex(devices, x => x.Id == previous);
        if (box.SelectedIndex < 0) enabled.IsChecked = false;
    }
    private async void Setup_Click(object sender, RoutedEventArgs e)
    {
        if (IsRecording) return;
        RecordingSetupWindow.Open(this);
        if (!RecordingSetupService.Check().Ready) return;
        try { Recorder.SetExcludeFromCapture(new WindowInteropHelper(this).Handle, true); await RefreshDevices(); }
        catch (Exception ex) { StatusLabel.Text = "录屏组件加载失败：" + ex.GetBaseException().Message + "。安装后可能需要重启程序或 Windows。"; }
    }
    private static string? Selected(System.Windows.Controls.CheckBox check, System.Windows.Controls.ComboBox devices)
    {
        if (check.IsChecked != true) return null;
        return (devices.SelectedItem as RecordingDevice)?.Id ?? throw new ArgumentException("所选音频／摄像头设备不可用，请刷新或关闭该设备。");
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
                if (!IsVisible) { Show(); WindowState = WindowState.Normal; }
                RecordingSetupWindow.Open(this);
                if (!RecordingSetupService.Check().Ready) throw new InvalidOperationException("录屏环境尚未就绪，请完成一键配置后重试。");
            }
            var p = Candidate(); var mic = Selected(MicBox, MicDevices); var speaker = Selected(SpeakerBox, SpeakerDevices); var camera = Selected(CameraBox, CameraDevices);
            var screen = System.Windows.Forms.Screen.FromRectangle(_region);
            if (!screen.Bounds.Contains(_region)) throw new ArgumentException("录屏选区必须位于单个显示器内，请重新选择。");
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = p.Format == "GIF" ? "GIF 动图|*.gif" : "MP4 视频|*.mp4", FileName = "SnapSharp-record-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), InitialDirectory = _settings.Current.SaveDirectory, OverwritePrompt = true };
            var accepted = IsVisible ? dialog.ShowDialog(this) : _toolbar?.IsVisible == true ? dialog.ShowDialog(_toolbar) : dialog.ShowDialog();
            if (accepted != true) return;
            if (File.Exists(dialog.FileName)) throw new IOException("为保护已有录屏，请选择一个新的文件名。");
            var candidate = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(_settings.Current))!;
            candidate.Recording = p.Copy(); _settings.Update(candidate);
            _service = null;
            using var countdown = new CancellationTokenSource(); _countdown = countdown; UpdateState();
            for (var seconds = p.CountdownSeconds; seconds > 0; seconds--)
            {
                StatusLabel.Text = $"{seconds} 秒后开始录制。点击“取消开始”可取消；尚未采集屏幕或设备。";
                _toolbar?.Countdown(seconds);
                await Task.Delay(1000, countdown.Token);
            }
            WindowState = WindowState.Minimized; await Task.Delay(250, countdown.Token);
            _countdown = null;
            var currentScreen = System.Windows.Forms.Screen.FromRectangle(_region);
            if (currentScreen.DeviceName != screen.DeviceName || currentScreen.Bounds != screen.Bounds || !currentScreen.Bounds.Contains(_region))
                throw new InvalidOperationException("倒计时期间显示器布局已改变，请重新选择录屏区域。");
            _service = new RecordingService(Dispatcher); _service.Changed += UpdateState;
            _service.Start(_region, screen.Bounds, screen.DeviceName, dialog.FileName, p, mic, speaker, camera);
        }
        catch (OperationCanceledException) { StatusLabel.Text = "录屏开始已取消，未采集屏幕或设备。"; WindowState = WindowState.Normal; }
        catch (Exception ex)
        {
            if (_service?.State == RecordingState.Ready) { _service.Changed -= UpdateState; _service = null; }
            StatusLabel.Text = ex.GetBaseException().Message; WindowState = WindowState.Normal; Show(); Activate();
        }
        finally { _countdown = null; _starting = false; UpdateState(); }
    }
    private void UpdateState()
    {
        UpdateTools();
        RunningConfigurationHint.Visibility = IsRecording ? Visibility.Visible : Visibility.Collapsed;
        SetupButton.IsEnabled = !IsRecording;
        StopButton.Content = _countdown is null ? "停止并保存" : "取消开始";
        if (_starting || _service is null)
        {
            StartButton.IsEnabled = !_starting && !_refreshing; OptionsPanel.IsEnabled = !_starting;
            PauseButton.IsEnabled = false; StopButton.IsEnabled = _countdown is not null; return;
        }
        ClockLabel.Text = _service.Elapsed.ToString(@"hh\:mm\:ss"); FrameLabel.Text = $"{_service.FrameCount:N0} 帧 · {_service.AverageFrameRate:F1} / {_service.TargetFrameRate} fps · {_service.OutputBytes / 1048576d:F1} MB" +
            (_service.IsFrameRateLimited ? " · 帧率不足，建议降低帧率／尺寸或更换编码方式" : "");
        StatusLabel.Text = _service.Status; StartButton.IsEnabled = !_service.IsActive; OptionsPanel.IsEnabled = !_service.IsActive;
        PauseButton.IsEnabled = _service.State is RecordingState.Recording or RecordingState.Paused;
        StopButton.IsEnabled = _service.State is RecordingState.Starting or RecordingState.Recording or RecordingState.Paused;
        FolderButton.IsEnabled = _service.SavedPath is not null;
        RecoveryButton.IsEnabled = _service.RecoveryDirectory is not null;
        if (!_terminalHandled && _service.State is RecordingState.Completed or RecordingState.Failed)
        {
            CloseTools();
            _terminalHandled = true; MicBox.IsChecked = SpeakerBox.IsChecked = CameraBox.IsChecked = false;
            WindowState = WindowState.Normal; Show();
        }
    }
    public void ShowRecordingTools()
    {
        if (_regionIndicator is null)
        {
            var effective = new System.Drawing.Rectangle(_region.X, _region.Y, _region.Width & ~1, _region.Height & ~1);
            _regionIndicator = new RecordingRegionWindow(effective);
            _regionIndicator.Show();
        }
        if (_toolbar is null)
        {
            _toolbar = new RecordingToolbarWindow();
            _toolbar.RecordRequested += () => Start_Click(this, new RoutedEventArgs());
            _toolbar.PauseRequested += PauseRecording;
            _toolbar.StopRequested += StopRecording;
            _toolbar.ConfigurationRequested += () => { Show(); WindowState = WindowState.Normal; Activate(); };
            _toolbar.CloseRequested += CloseSession;
            _toolbar.Closed += (_, _) => _toolbar = null;
        }
        _toolbar.ShowForRegion(_region);
        UpdateTools();
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
        if (_starting) { MessageBox.Show(this, "录屏正在启动，请稍后再试。"); return false; }
        if (_service?.IsActive != true) return true;
        if (MessageBox.Show(this, "停止当前录屏并等待文件保存后退出？", "结束录屏", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return false;
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
        if (IsRecording || (!_closingSession && _toolbar is not null)) { e.Cancel = true; Hide(); }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    internal static bool RunLayoutSelfTest()
    {
        var window = new RecordingWindow(new System.Drawing.Rectangle(-100, 20, 640, 480), new SettingsService());
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
            SetDevices(window.CameraDevices, window.CameraBox, devices); window.CameraDevices.SelectedIndex = 1; window.CameraBox.IsChecked = true;
            SetDevices(window.CameraDevices, window.CameraBox, devices.Reverse().ToArray());
            if ((window.CameraDevices.SelectedItem as RecordingDevice)?.Id != "b") return false;
            SetDevices(window.CameraDevices, window.CameraBox, [devices[0]]);
            if (window.CameraBox.IsChecked == true || window.CameraDevices.SelectedIndex != -1) return false;
            using var countdown = new CancellationTokenSource(); window._starting = true; window._countdown = countdown; window.UpdateState();
            if (!window.StopButton.IsEnabled || window.StartButton.IsEnabled || window.SetupButton.IsEnabled) return false;
            window.StopRecording();
            return countdown.IsCancellationRequested && RecordingToolbarWindow.RunLayoutSelfTest();
        }
        finally { window._starting = false; window._countdown = null; window.Close(); }
    }
    internal static async Task TestFloatingConfiguration()
    {
        var window = new RecordingWindow(new System.Drawing.Rectangle(40, 40, 320, 192), new SettingsService());
        try
        {
            window.ShowRecordingTools();
            if (window.IsVisible || window._regionIndicator?.IsVisible != true || window._toolbar?.IsVisible != true)
                throw new InvalidOperationException("录屏没有先显示区域和悬浮工具条。");
            var configuration = (System.Windows.Controls.Button)window._toolbar.FindName("ConfigurationButton");
            configuration.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (!window.IsVisible || window.WindowState != WindowState.Normal)
                throw new InvalidOperationException("工具条配置按钮没有打开配置窗口。");
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
