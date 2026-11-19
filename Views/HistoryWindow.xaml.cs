using NimCap.Screenshot.Models;
using NimCap.Screenshot.Services;
using System.Windows;
using System.Windows.Media.Imaging;

namespace NimCap.Screenshot.Views;

public partial class HistoryWindow : Window
{
    private readonly HistoryService _history;
    private readonly SkiaImageService _images;
    private readonly SettingsService _settings;
    private readonly IClipboardService _clipboard;
    private readonly Action<BitmapSource> _pin;

    public HistoryWindow(HistoryService history, SkiaImageService images, SettingsService settings,
        IClipboardService clipboard, Action<BitmapSource> pin)
    {
        InitializeComponent();
        _history = history;
        _images = images;
        _settings = settings;
        _clipboard = clipboard;
        _pin = pin;
        Refresh();
    }

    private HistoryEntry? Selected => (HistoryList.SelectedItem as HistoryListItem)?.Entry;
    private void Refresh() => HistoryList.ItemsSource = _history.Entries.Select(x => new HistoryListItem(x)).ToArray();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } entry) _clipboard.SetImage(_history.LoadImage(entry));
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } entry) _pin(_history.LoadImage(entry));
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry) return;
        SaveImageWindow.Open(_history.LoadImage(entry), _images, _settings, this);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry) return;
        _history.Delete(entry);
        Refresh();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class HistoryListItem
    {
        public HistoryListItem(HistoryEntry entry)
        {
            Entry = entry;
            using var stream = File.OpenRead(entry.ThumbnailPath);
            var thumbnail = new BitmapImage();
            thumbnail.BeginInit();
            thumbnail.CacheOption = BitmapCacheOption.OnLoad;
            thumbnail.StreamSource = stream;
            thumbnail.EndInit();
            thumbnail.Freeze();
            Thumbnail = thumbnail;
        }

        public HistoryEntry Entry { get; }
        public BitmapSource Thumbnail { get; }
        public DateTimeOffset CreatedAt => Entry.CreatedAt;
        public int Width => Entry.Width;
        public int Height => Entry.Height;
    }
}
