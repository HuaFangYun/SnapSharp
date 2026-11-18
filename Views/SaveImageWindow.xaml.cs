using L = SnapSharp.Localization.UiText;
using SnapSharp.Services;
using SnapSharp.Models;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TextBox = System.Windows.Controls.TextBox;

namespace SnapSharp.Views;

public partial class SaveImageWindow : Window
{
    private readonly BitmapSource _source;
    private readonly SkiaImageService _images;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly SemaphoreSlim _encoding = new(1);
    private bool _ready, _dimensionsChanging, _generatedName = true, _saving, _closed;
    private int _revision;
    private string? _savedPath;
    private readonly DateTime _nameTime = DateTime.Now;

    public SaveImageWindow(BitmapSource source, SkiaImageService images, SettingsService settings, string? defaultExtension = null)
    {
        _source = source.IsFrozen ? source : source.Clone();
        if (!_source.IsFrozen) _source.Freeze();
        _images = images;
        _settings = settings;
        InitializeComponent();
        DirectoryBox.Text = settings.Current.SaveDirectory;
        TemplateBox.Text = settings.Current.FileNameTemplate;
        FormatBox.ItemsSource = OutputFormat.All;
        FormatBox.MaxDropDownHeight = Math.Min(560, Math.Max(180, SystemParameters.WorkArea.Height - 120));
        FormatBox.SelectedItem = OutputFormat.Find(defaultExtension ?? (OutputFormat.IsSupported(settings.Current.OutputFormat) ? settings.Current.OutputFormat : ".png"));
        LosslessBox.IsChecked = settings.Current.WebpLossless;
        QualitySlider.Value = settings.Current.ImageQuality;
        WidthBox.Text = source.PixelWidth.ToString();
        HeightBox.Text = source.PixelHeight.ToString();
        SourceInfo.Text = L.Format("Original0N01N0Pixels", source.PixelWidth, source.PixelHeight);
        Preview.Source = _source;
        _timer.Tick += async (_, _) => { _timer.Stop(); await RefreshPreview(); };
        Closed += (_, _) => { _closed = true; _revision++; _timer.Stop(); };
        _ready = true;
        GenerateName();
        UpdateOptions();
    }

    public static void Open(BitmapSource source, SkiaImageService images, SettingsService settings, Window? owner = null, string? defaultExtension = null)
    {
        var window = new SaveImageWindow(source, images, settings, defaultExtension);
        WindowDialogService.ShowDialog(window, owner);
    }

    private string Extension => ((OutputFormat)FormatBox.SelectedItem).Extension;
    private (int Width, int Height) Dimensions()
    {
        if (!int.TryParse(WidthBox.Text, out var width) || !int.TryParse(HeightBox.Text, out var height) ||
            width < 1 || height < 1 || width > 16384 || height > 16384 || (long)width * height > 64_000_000)
            throw new ArgumentException(L.Get("DimensionsMustBe116384PixelsWith"));
        return (width, height);
    }

    private string TemplateName()
    {
        var size = Dimensions();
        return OutputFileService.ExpandTemplate(TemplateBox.Text, _nameTime, size.Width, size.Height, 1);
    }

    private void GenerateName()
    {
        try
        {
            var size = Dimensions();
            var name = OutputFileService.NewName(TemplateBox.Text, size.Width, size.Height);
            NameBox.Text = Path.GetFileNameWithoutExtension(OutputFileService.AvailablePath(DirectoryBox.Text, name, Extension));
            _generatedName = true;
            TemplatePreview.Text = L.Get("Example") + TemplateName() + Extension;
        }
        catch (Exception ex) { TemplatePreview.Text = ex.Message; }
    }

    private void Name_Changed(object sender, TextChangedEventArgs e) { if (_ready) _generatedName = false; }
    private void Template_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        try { TemplatePreview.Text = L.Get("Example") + TemplateName() + Extension; }
        catch (Exception ex) { TemplatePreview.Text = ex.Message; }
    }
    private void ApplyTemplate_Click(object sender, RoutedEventArgs e) => GenerateName();
    private void Options_Changed(object sender, RoutedEventArgs e) { if (_ready) UpdateOptions(); }
    private void UpdateOptions()
    {
        var webp = Extension == ".webp";
        LosslessBox.Visibility = webp ? Visibility.Visible : Visibility.Collapsed;
        var lossy = Extension == ".jpg" || (webp && LosslessBox.IsChecked != true);
        QualityPanel.Visibility = lossy ? Visibility.Visible : Visibility.Collapsed;
        PreviewHint.Text = Extension == ".pdf" ? L.Get("ThePreviewShowsTheFullImageNot")
            : L.Get("PreviewShowsTheEncodedImageTheCheckerboard");
        FormatHint.Text = Extension switch
        {
            ".png" => L.Get("PNGIsLosslessAndSupportsTransparencyLossy"),
            ".jpg" => L.Get("JPEGUsesLossyCompressionWithoutTransparencyTransparent"),
            ".webp" => lossy ? L.Get("LossyWebPPreservesAlphaQuality100Does") : L.Get("LosslessWebPPreservesAlphaQualitySettingsDo"),
            ".tiff" => L.Get("TIFFUsesLosslessCompressionAndWrites300"),
            ".bmp" => L.Get("BMPIsALargeLosslessBitmapTransparent"),
            ".pdf" => L.Get("PDFFitsImagesProportionallyOnA4Pages"),
            _ => L.Get("ThisVersionDoesNotSupportEncodingThis")
        };
        Template_Changed(this, null!);
        SchedulePreview();
    }

    private void Dimensions_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _dimensionsChanging) return;
        _dimensionsChanging = true;
        try
        {
            if (LockRatio.IsChecked == true && sender is TextBox box && int.TryParse(box.Text, out var value) && value is > 0 and <= 16384)
            {
                if (box == WidthBox) HeightBox.Text = Math.Max(1, (int)Math.Round(value * (double)_source.PixelHeight / _source.PixelWidth)).ToString();
                else WidthBox.Text = Math.Max(1, (int)Math.Round(value * (double)_source.PixelWidth / _source.PixelHeight)).ToString();
            }
            if (_generatedName) GenerateName();
            Template_Changed(this, null!);
            SchedulePreview();
        }
        finally { _dimensionsChanging = false; }
    }
    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _dimensionsChanging = true;
        WidthBox.Text = _source.PixelWidth.ToString(); HeightBox.Text = _source.PixelHeight.ToString();
        _dimensionsChanging = false;
        if (_generatedName) GenerateName();
        SchedulePreview();
    }
    private void SchedulePreview() { _revision++; _timer.Stop(); _timer.Start(); SizeInfo.Text = L.Get("CalculatingEncodedSize"); }

    private async Task<byte[]> EncodeCurrent(int? previewRevision = null)
    {
        var size = Dimensions(); var extension = Extension; var quality = (int)QualitySlider.Value; var lossless = LosslessBox.IsChecked == true;
        await _encoding.WaitAsync();
        try
        {
            if (_closed || (previewRevision.HasValue && previewRevision.Value != _revision)) throw new OperationCanceledException();
            return await Task.Run(() => _images.Encode(_source, extension, quality, lossless, size.Width, size.Height));
        }
        finally { _encoding.Release(); }
    }
    private async Task RefreshPreview()
    {
        var revision = _revision;
        try
        {
            var extension = Extension;
            var size = Dimensions();
            var bytes = await EncodeCurrent(revision);
            if (_closed || revision != _revision) return;
            var preview = await Task.Run(() => extension == ".pdf"
                ? _images.Decode(_images.Encode(_source, ".bmp", width: size.Width, height: size.Height)) : _images.Decode(bytes));
            if (_closed || revision != _revision) return;
            Preview.Source = preview;
            SizeInfo.Text = $"{size.Width:N0} × {size.Height:N0}  ·  {bytes.Length / 1024d:N1} KB";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed && revision == _revision) SizeInfo.Text = ex.Message; }
    }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = DirectoryBox.Text, Title = L.Get("ChooseOutputFolder") };
        if (WindowDialogService.ShowDialog(dialog, this) == true) { DirectoryBox.Text = dialog.FolderName; if (_generatedName) GenerateName(); }
    }
    private async void Save_Click(object sender, RoutedEventArgs e) => await Save(false);
    private async void SystemSave_Click(object sender, RoutedEventArgs e) => await Save(true);

    private async Task Save(bool systemDialog)
    {
        if (_saving) return;
        _saving = true;
        SettingsPanel.IsEnabled = false;
        try
        {
            _ = TemplateName(); // Reject invalid templates even when the filename has been edited.
            var name = NameBox.Text.Trim();
            if (name.Length == 0 || name.Length > 180 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') ||
                OutputFileService.ExpandTemplate(name, _nameTime, 1, 1, 1) != name)
                throw new ArgumentException(L.Get("InvalidFileNameDoNotIncludePaths"));
            if (!Path.IsPathFullyQualified(DirectoryBox.Text)) throw new ArgumentException(L.Get("ChooseAnAbsoluteSaveFolderPath"));
            var extension = Extension;
            var path = Path.Combine(DirectoryBox.Text, name + extension);
            if (_generatedName) path = OutputFileService.AvailablePath(DirectoryBox.Text, name, extension);
            if (systemDialog)
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = OutputFormat.SaveDialogFilter,
                    FilterIndex = OutputFormat.FilterIndex(extension),
                    InitialDirectory = DirectoryBox.Text, FileName = Path.GetFileName(path), AddExtension = true, OverwritePrompt = false
                };
                if (WindowDialogService.ShowDialog(dialog, this) != true) return;
                path = dialog.FileName;
                extension = Path.GetExtension(path).ToLowerInvariant();
                if (!OutputFormat.IsSupported(extension)) throw new ArgumentException(L.Get("ChooseAnAvailableOutputFormatExtensionFrom"));
            }
            var overwrite = File.Exists(path);
            if (overwrite && MessageBox.Show(this, L.Format("TheFileAlreadyExistsReplaceIt0", path), L.Get("ConfirmOverwrite"), MessageBoxButton.YesNo, MessageBoxImage.Warning,
                    MessageBoxResult.No) != MessageBoxResult.Yes) return;
            var size = Dimensions(); var quality = (int)QualitySlider.Value; var lossless = LosslessBox.IsChecked == true;
            await _encoding.WaitAsync();
            try
            {
                await Task.Run(() => OutputFileService.Write(path, _images.Encode(_source, extension, quality, lossless, size.Width, size.Height), overwrite));
            }
            finally { _encoding.Release(); }
            _savedPath = Path.GetFullPath(path);
            DirectoryBox.Text = Path.GetDirectoryName(_savedPath)!;
            _settings.Current.SaveDirectory = DirectoryBox.Text;
            _settings.Current.ImageQuality = quality;
            _settings.Current.WebpLossless = lossless;
            _settings.Current.OutputFormat = OutputFormat.NormalizeExtension(extension);
            _settings.Current.FileNameTemplate = TemplateBox.Text;
            SavedPathText.Text = L.Get("Saved") + _savedPath;
            SavedActions.Visibility = Visibility.Visible;
            StatusText.Text = L.Get("SavedYouCanCopyTheFileOr");
            try { _settings.Save(); } catch (Exception ex) { StatusText.Text = L.Get("ImageSavedButSettingsCouldNotBe") + ex.Message; }
            if (_generatedName) GenerateName();
        }
        catch (Exception ex) { StatusText.Text = L.Get("SaveFailed") + ex.Message; }
        finally { _saving = false; if (!_closed) SettingsPanel.IsEnabled = true; }
    }

    private void SavedAction(Action<string> action)
    {
        try
        {
            if (_savedPath is null || !File.Exists(_savedPath)) throw new FileNotFoundException(L.Get("SaveTheImageFirstAPreviouslySaved"));
            action(_savedPath);
            StatusText.Text = L.Get("OperationComplete");
        }
        catch (Exception ex) { StatusText.Text = L.Get("OperationFailed") + ex.Message; }
    }
    private void CopyFile_Click(object sender, RoutedEventArgs e) => SavedAction(OutputActions.CopyFile);
    private void CopyPath_Click(object sender, RoutedEventArgs e) => SavedAction(OutputActions.CopyPath);
    private void Reveal_Click(object sender, RoutedEventArgs e) => SavedAction(OutputActions.Reveal);
    private async void Print_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var extension = Extension; var size = Dimensions();
            var bytes = await EncodeCurrent();
            var image = extension == ".pdf" ? _images.Decode(await Task.Run(() => _images.Encode(_source, ".bmp", width: size.Width, height: size.Height))) : _images.Decode(bytes);
            if (!_closed) OutputActions.Print(image, this);
        }
        catch (Exception ex) { StatusText.Text = L.Get("PrintFailed") + ex.Message; }
    }

    internal static bool RunLayoutSelfTest(BitmapSource source)
    {
        var window = new SaveImageWindow(source, new SkiaImageService(), new SettingsService());
        try
        {
            if (window.QualityPanel.Visibility != Visibility.Collapsed || window.LosslessBox.Visibility != Visibility.Collapsed) return false;
            window.FormatBox.SelectedItem = OutputFormat.Find(".jpg");
            if (window.QualityPanel.Visibility != Visibility.Visible) return false;
            window.FormatBox.SelectedItem = OutputFormat.Find(".webp");
            window.LosslessBox.IsChecked = true;
            if (window.QualityPanel.Visibility != Visibility.Collapsed || window.LosslessBox.Visibility != Visibility.Visible) return false;
            window.LosslessBox.IsChecked = false;
            if (window.QualityPanel.Visibility != Visibility.Visible) return false;
            foreach (var extension in new[] { ".tiff", ".bmp", ".pdf" })
            {
                window.FormatBox.SelectedItem = OutputFormat.Find(extension);
                if (window.QualityPanel.Visibility != Visibility.Collapsed || window.LosslessBox.Visibility != Visibility.Collapsed || window.Extension != extension) return false;
            }
            var disabledItem = new ComboBoxItem { DataContext = OutputFormat.Find(".avif"), Style = (Style)window.FindResource("OutputFormatItem") };
            if (disabledItem.IsEnabled) return false;
            var selectedItem = new ComboBoxItem { DataContext = OutputFormat.Find(".png"), Style = (Style)window.FindResource("OutputFormatItem"), IsSelected = true };
            selectedItem.ApplyTemplate();
            if (((FrameworkElement)selectedItem.Template.FindName("SelectedMark", selectedItem)).Visibility != Visibility.Visible) return false;
            window.WidthBox.Text = "48";
            if (window.HeightBox.Text != "32") return false;
            window.LockRatio.IsChecked = false;
            window.HeightBox.Text = "40";
            if (window.WidthBox.Text != "48") return false;
            window.TemplateBox.Text = "bad-{unknown}";
            try { window.TemplateName(); return false; } catch (ArgumentException) { }
            window.TemplateBox.Text = OutputFileService.DefaultTemplate;
            window.GenerateName();
            if (!window._generatedName || string.IsNullOrWhiteSpace(window.NameBox.Text)) return false;
            window.NameBox.Text = "自定义文件名";
            window.WidthBox.Text = "96";
            if (window.NameBox.Text != "自定义文件名" || window._generatedName) return false;
            window.SavedActions.Visibility = Visibility.Visible;
            window.SavedPathText.Text = @"已保存：C:\带空格的目录 output\截图.png";
            foreach (var size in new[] { new System.Windows.Size(780, 480), new System.Windows.Size(1000, 680), new System.Windows.Size(1240, 780) })
            {
                var content = (FrameworkElement)window.Content;
                content.Measure(size); content.Arrange(new Rect(size)); content.UpdateLayout();
                if (((System.Windows.Controls.TextBlock)window.FormatBox.Template.FindName("SelectedFormatName", window.FormatBox)).Text != "PDF") return false;
                if (window.SaveButton.ActualWidth < 200 || window.NameBox.ActualWidth < 130 || window.Preview.ActualWidth < 150) return false;
                var render = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                render.Render(content);
            }
            return true;
        }
        finally { window.Close(); }
    }
}
