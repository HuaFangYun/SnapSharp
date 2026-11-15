using SnapSharp.Services;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Size = System.Windows.Size;

namespace SnapSharp.Views;

public partial class RecordingSetupWindow : Window
{
    private CancellationTokenSource? _operation;
    public RecordingSetupWindow()
    {
        InitializeComponent(); DialogLayoutService.Attach(this); Refresh();
        Closed += (_, _) => _operation?.Cancel();
    }
    internal static void Open(Window owner)
    {
        var window = new RecordingSetupWindow { Owner = owner };
        window.ShowDialog();
    }
    private void Refresh()
    {
        try { EnvironmentStatus.Text = RecordingSetupService.Check().Description; }
        catch (Exception ex) { EnvironmentStatus.Text = "环境检测失败：" + ex.GetBaseException().Message; }
    }
    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();
    private async void Configure_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null) return;
        using var cancellation = new CancellationTokenSource(); _operation = cancellation;
        ConfigureButton.IsEnabled = RefreshButton.IsEnabled = false; CancelDownloadButton.IsEnabled = true;
        try
        {
            SetupStatus.Text = await new RecordingSetupService().Configure(new Progress<string>(message => SetupStatus.Text = message), cancellation.Token);
        }
        catch (OperationCanceledException) { SetupStatus.Text = "配置已取消，可重新一键配置。已经安装的组件不会回退；系统安装已启动时会等待完成。"; }
        catch (Exception ex) { SetupStatus.Text = "配置未完成：" + ex.GetBaseException().Message; }
        finally { _operation = null; ConfigureButton.IsEnabled = RefreshButton.IsEnabled = true; CancelDownloadButton.IsEnabled = false; Refresh(); }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _operation?.Cancel(); CancelDownloadButton.IsEnabled = false;
        SetupStatus.Text = "正在取消下载；若系统安装已启动，请等待安装结束，不会强制中断 Windows 安装。";
    }
    private void SystemSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { Tag: string uri } && uri is "ms-settings:privacy-microphone" or "ms-settings:privacy-webcam" or "ms-settings:optionalfeatures")
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex) { SetupStatus.Text = "无法打开系统设置：" + ex.GetBaseException().Message; }
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_operation is null) return;
        e.Cancel = true; SetupStatus.Text = "请先取消下载并等待；系统安装已启动时需等待安装完成。";
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    internal static bool RunLayoutSelfTest()
    {
        var window = new RecordingSetupWindow();
        try
        {
            var content = (FrameworkElement)window.Content;
            foreach (var size in new[] { new Size(660, 610), new Size(520, 420) })
            {
                content.Measure(size); content.Arrange(new Rect(size)); content.UpdateLayout();
                if (window.ConfigureButton.ActualWidth < 100 || window.SetupStatus.ActualWidth < 300) return false;
            }
            return true;
        }
        finally { window.Close(); }
    }
}
