using SnapSharp.Models;
using SnapSharp.Native;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace SnapSharp.Views;

public partial class RecordingToolbarWindow : Window
{
    public event Action? RecordRequested, PauseRequested, StopRequested, ConfigurationRequested, CloseRequested;
    public bool ExcludedFromCapture { get; private set; }
    private System.Drawing.Rectangle? _anchor;
    public RecordingToolbarWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ExcludedFromCapture = ScreenRecorderLib.Recorder.SetExcludeFromCapture(new WindowInteropHelper(this).Handle, true);
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => { if (_anchor is { } region && IsVisible) ShowForRegion(region); });
    }
    internal void Update(RecordingState state, bool starting, bool countdown, TimeSpan elapsed, double fps)
    {
        var active = starting || state is RecordingState.Starting or RecordingState.Recording or RecordingState.Paused or RecordingState.Stopping or RecordingState.Encoding;
        Clock.Text = elapsed.ToString(@"hh\:mm\:ss");
        StateText.Text = countdown ? "即将开始" : state switch
        {
            RecordingState.Recording => $"录制中 · {fps:F0} FPS",
            RecordingState.Paused => "已暂停",
            RecordingState.Starting => "正在启动…",
            RecordingState.Stopping or RecordingState.Encoding => "正在保存…",
            _ => starting ? "准备开始…" : "准备录制"
        };
        StateDot.Fill = state == RecordingState.Paused ? Brushes.Orange : active ? Brushes.Tomato : (Brush)FindResource("ToolbarActiveBrush");
        RecordButton.IsEnabled = !active;
        PauseButton.IsEnabled = state is RecordingState.Recording or RecordingState.Paused;
        PauseIcon.Kind = state == RecordingState.Paused ? MahApps.Metro.IconPacks.PackIconLucideKind.Play : MahApps.Metro.IconPacks.PackIconLucideKind.Pause;
        StopButton.IsEnabled = countdown || state is RecordingState.Starting or RecordingState.Recording or RecordingState.Paused;
        StopButton.ToolTip = countdown ? "取消开始" : "停止并保存";
        ConfigurationButton.IsEnabled = !starting;
        CloseButton.IsEnabled = !active;
    }
    internal void Countdown(int seconds) { Clock.Text = seconds + " 秒"; StateText.Text = "即将开始 · 点击停止可取消"; }
    public void ShowForRegion(System.Drawing.Rectangle region)
    {
        _anchor = region;
        Show(); UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this);
        var work = System.Windows.Forms.Screen.FromRectangle(region).WorkingArea;
        MaxWidth = Math.Max(1, work.Width / dpi.DpiScaleX - 8); UpdateLayout();
        var width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        var x = Math.Clamp(region.Left + (region.Width - width) / 2, work.Left, Math.Max(work.Left, work.Right - width));
        var y = region.Bottom + height + 10 <= work.Bottom ? region.Bottom + 8
            : region.Top - height - 10 >= work.Top ? region.Top - height - 8 : work.Bottom - height - 8;
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));
        if (!ExcludedFromCapture && region.IntersectsWith(new System.Drawing.Rectangle(x, y, width, height))) { Hide(); return; }
        NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, NativeMethods.HwndTopMost, x, y, width, height, NativeMethods.SwpNoActivate);
    }
    private void Drag_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    private void Record_Click(object sender, RoutedEventArgs e) => RecordRequested?.Invoke();
    private void Pause_Click(object sender, RoutedEventArgs e) => PauseRequested?.Invoke();
    private void Stop_Click(object sender, RoutedEventArgs e) => StopRequested?.Invoke();
    private void Configuration_Click(object sender, RoutedEventArgs e) => ConfigurationRequested?.Invoke();
    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();
    internal static bool RunLayoutSelfTest()
    {
        var toolbar = new RecordingToolbarWindow();
        try
        {
            var configured = 0; toolbar.ConfigurationRequested += () => configured++;
            foreach (var state in new[] { RecordingState.Ready, RecordingState.Recording, RecordingState.Paused })
            {
                toolbar.Update(state, false, false, TimeSpan.FromSeconds(12), 30);
                toolbar.ConfigurationButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if (!toolbar.ConfigurationButton.IsEnabled || toolbar.RecordButton.IsEnabled != (state == RecordingState.Ready) ||
                    toolbar.PauseButton.IsEnabled != (state != RecordingState.Ready) || toolbar.CloseButton.IsEnabled != (state == RecordingState.Ready)) return false;
            }
            toolbar.Update(RecordingState.Ready, true, true, TimeSpan.Zero, 0);
            if (!toolbar.StopButton.IsEnabled || toolbar.RecordButton.IsEnabled) return false;
            return configured == 3;
        }
        finally { toolbar.Close(); }
    }
}
