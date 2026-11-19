using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using NimCap.Screenshot.Services;
using L = NimCap.Screenshot.Localization.UiText;

namespace NimCap.Screenshot.Views;

public partial class ModelStorageWindow : Window
{
    private readonly string _root;
    private readonly Func<IReadOnlyCollection<string>> _references;
    private bool _busy;

    internal ModelStorageWindow(string root, Func<IReadOnlyCollection<string>> references)
    {
        _root = root; _references = references;
        InitializeComponent();
        RootPath.Text = root;
        DialogLayoutService.Attach(this);
        Loaded += async (_, _) => await Refresh();
        Closing += (_, e) => e.Cancel = _busy;
    }

    private async Task Refresh()
    {
        SetBusy(true);
        Status.Text = L.Get("ModelStorageScanning");
        Entries.ItemsSource = null;
        try
        {
            var references = _references();
            var entries = await Task.Run(() => ModelStorageService.Scan(_root, references));
            Entries.ItemsSource = entries;
            Status.Text = L.Format("ModelStorageCount", entries.Count);
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Actions.IsEnabled = !busy;
        Selection_Changed(this, null!);
    }

    private void Selection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (DeleteButton is null || OpenButton is null) return;
        DeleteButton.IsEnabled = !_busy && Entries.SelectedItem is ModelStorageService.Entry { CanDelete: true };
        OpenButton.IsEnabled = !_busy && Entries.SelectedItem is ModelStorageService.Entry;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await Refresh();
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (Entries.SelectedItem is not ModelStorageService.Entry entry) return;
        try
        {
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            start.ArgumentList.Add("/select,"); start.ArgumentList.Add(entry.Path);
            Process.Start(start);
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || Entries.SelectedItem is not ModelStorageService.Entry { CanDelete: true } entry) return;
        if (MessageBox.Show(this, L.Format("ModelStorageConfirm", entry.Path, entry.SizeLabel), L.Get("ModelStorageTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        SetBusy(true);
        string? error = null;
        try
        {
            // Keep shell recycle dialogs on the owning STA thread.
            ModelStorageService.Delete(_root, entry.Path, _references());
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { error = ex.Message; }
        finally { SetBusy(false); }
        await Refresh();
        if (error is not null) Status.Text = error;
    }
}
