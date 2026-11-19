using L = NimCap.Screenshot.Localization.UiText;
using NimCap.Screenshot.Services;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Size = System.Windows.Size;

namespace NimCap.Screenshot.Views;

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
        var window = new RecordingSetupWindow();
        WindowDialogService.ShowDialog(window, owner);
    }
    private void Refresh()
    {
        try { EnvironmentStatus.Text = RecordingSetupService.Check().Description; }
        catch (Exception ex) { EnvironmentStatus.Text = L.Get("EnvironmentCheckFailed") + ex.GetBaseException().Message; }
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
        catch (OperationCanceledException) { SetupStatus.Text = L.Get("SetupCanceledYouCanRunSetupAgain"); }
        catch (Exception ex) { SetupStatus.Text = L.Get("SetupIncomplete") + ex.GetBaseException().Message; }
        finally { _operation = null; ConfigureButton.IsEnabled = RefreshButton.IsEnabled = true; CancelDownloadButton.IsEnabled = false; Refresh(); }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _operation?.Cancel(); CancelDownloadButton.IsEnabled = false;
        SetupStatus.Text = L.Get("CancelingDownloadIfSystemInstallationHasStarted");
    }
    private void SystemSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { Tag: string uri } && uri is "ms-settings:privacy-microphone" or "ms-settings:privacy-webcam" or "ms-settings:optionalfeatures")
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex) { SetupStatus.Text = L.Get("CouldNotOpenSystemSettings") + ex.GetBaseException().Message; }
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_operation is null) return;
        e.Cancel = true; SetupStatus.Text = L.Get("CancelTheDownloadAndWaitFirstAny");
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
