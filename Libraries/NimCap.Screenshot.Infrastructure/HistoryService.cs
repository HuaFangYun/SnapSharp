using NimCap.Screenshot.Models;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace NimCap.Screenshot.Services;

public sealed class HistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SkiaImageService _images;
    private readonly SettingsService _settings;
    private readonly List<HistoryEntry> _entries = [];

    public HistoryService(SkiaImageService images, SettingsService settings)
    {
        _images = images;
        _settings = settings;
        LoadIndex();
    }

    public IReadOnlyList<HistoryEntry> Entries => _entries.OrderByDescending(x => x.CreatedAt).ToArray();

    public HistoryEntry? Add(BitmapSource image)
    {
        if (!_settings.Current.SaveHistory || _settings.Current.HistoryLimit == 0) return null;
        Directory.CreateDirectory(AppPaths.HistoryRoot);
        var id = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}"[..28];
        var imagePath = Path.Combine(AppPaths.HistoryRoot, id + ".png");
        var thumbnailPath = Path.Combine(AppPaths.HistoryRoot, id + ".thumb.png");
        _images.Save(image, imagePath, 100);
        _images.Save(_images.CreateThumbnail(image), thumbnailPath, 90);
        var entry = new HistoryEntry(id, DateTimeOffset.Now, imagePath, thumbnailPath, image.PixelWidth, image.PixelHeight);
        _entries.Add(entry);
        Trim();
        SaveIndex();
        return entry;
    }

    public BitmapSource LoadImage(HistoryEntry entry)
    {
        using var stream = File.OpenRead(entry.ImagePath);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public void Delete(HistoryEntry entry)
    {
        _entries.RemoveAll(x => x.Id == entry.Id);
        TryDelete(entry.ImagePath);
        TryDelete(entry.ThumbnailPath);
        SaveIndex();
    }

    private void LoadIndex()
    {
        Directory.CreateDirectory(AppPaths.HistoryRoot);
        if (!File.Exists(AppPaths.HistoryIndex)) return;
        try
        {
            var entries = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(AppPaths.HistoryIndex), JsonOptions) ?? [];
            _entries.AddRange(entries.Where(x => File.Exists(x.ImagePath) && File.Exists(x.ThumbnailPath)));
        }
        catch { }
    }

    private void Trim()
    {
        var overflow = _entries.OrderByDescending(x => x.CreatedAt).Skip(_settings.Current.HistoryLimit).ToArray();
        foreach (var entry in overflow)
        {
            _entries.Remove(entry);
            TryDelete(entry.ImagePath);
            TryDelete(entry.ThumbnailPath);
        }
    }

    private void SaveIndex()
    {
        Directory.CreateDirectory(AppPaths.HistoryRoot);
        File.WriteAllText(AppPaths.HistoryIndex, JsonSerializer.Serialize(_entries, JsonOptions));
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
