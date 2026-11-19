using NimCap.Screenshot.Models;
using NimCap.Screenshot.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using L = NimCap.Screenshot.Localization.UiText;
using Size = System.Windows.Size;
using Button = System.Windows.Controls.Button;

namespace NimCap.Screenshot.Views;

public partial class RecognitionWindow
{
    private WatermarkMask _watermarkMask = null!;
    private BitmapSource? _watermarkResult;
    private WriteableBitmap? _watermarkOverlay;
    private readonly List<byte[]> _watermarkUndo = [];
    private Point? _watermarkGestureStart, _watermarkLastPoint;
    private byte[]? _watermarkBeforeGesture;
    private Int32Rect _watermarkRegion;
    private bool _watermarkRectangleGesture, _watermarkEraseGesture, _watermarkReady;
    private string WatermarkMethodValue => (WatermarkMethod.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Brush";
    private bool WatermarkImageSupported => (long)_image.PixelWidth * _image.PixelHeight <= 16_000_000;
    private WatermarkModelDefinition WatermarkModel => WatermarkModelBox.SelectedItem as WatermarkModelDefinition ?? WatermarkModels.All[0];

    private void RefreshWatermarkChoices(string? id = null)
    {
        _modelControlsUpdating = true;
        try
        {
            id ??= (WatermarkModelBox.SelectedItem as WatermarkModelDefinition)?.Id ?? _settings.Current.WatermarkDefaultModelId;
            WatermarkModelBox.ItemsSource = WatermarkModels.All;
            WatermarkModelBox.SelectedItem = WatermarkModels.All.FirstOrDefault(x => x.Id == id) ?? WatermarkModels.All[0];
        }
        finally { _modelControlsUpdating = false; }
    }

    private void InitializeWatermark()
    {
        _watermarkMask = WatermarkImageSupported ? new(_image.PixelWidth, _image.PixelHeight) : new(1, 1);
        RefreshWatermarkChoices();
        WatermarkDeviceBox.SelectedValue = _settings.Current.WatermarkDevice;
        _watermarkReady = true;
        RefreshWatermarkModels();
        UpdateWatermarkMode();
    }
    private void UpdateWatermarkMode()
    {
        if (!_watermarkReady) return;
        FinishWatermarkGesture();
        var active = Mode == "Watermark";
        RunButton.IsEnabled = !_busy && (!active || WatermarkImageSupported);
        WatermarkMaskTools.IsEnabled = !_busy && WatermarkImageSupported;
        WatermarkMethod.Visibility = WatermarkResultTab.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        WatermarkPreviewActions.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        WatermarkHistoryActions.Visibility = active && ImageTools.ActualWidth >= 760 ? Visibility.Visible : Visibility.Collapsed;
        WatermarkPreviewActions.IsEnabled = !_busy && WatermarkImageSupported;
        WatermarkHistoryActions.IsEnabled = !_busy && WatermarkImageSupported;
        EngineSettingsButton.Visibility = Visibility.Visible;
        WatermarkMaskTools.Visibility = WatermarkMethodValue == "Brush" && ImageTools.ActualWidth >= 900 ? Visibility.Visible : Visibility.Collapsed;
        WatermarkMoreButton.Visibility = active && WatermarkMethodValue == "Brush" && ImageTools.ActualWidth < 900 ? Visibility.Visible : Visibility.Collapsed;
        if (!active || _busy || WatermarkMoreButton.Visibility != Visibility.Visible) WatermarkMorePopup.IsOpen = false;
        WatermarkBrushCursor.Visibility = Visibility.Collapsed;
        WatermarkOverlay.Visibility = active && WatermarkImageSupported && WatermarkShowMask.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        WatermarkRegionOutline.Visibility = active && WatermarkMethodValue == "Region" && !_watermarkRegion.IsEmpty && WatermarkShowMask.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PictureScroll.ToolTip = L.Get(active ? "WatermarkPreviewHint" : "RecognitionPreviewHint");
        if (active && !WatermarkImageSupported) Status.Text = L.Get("RecognitionBackgroundRemovalInputIsLimitedTo");
        RefreshWatermarkOverlay();
        RefreshComparison();
    }
    private void WatermarkMethod_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_watermarkReady) return;
        UpdateWatermarkMode();
    }
    private void WatermarkMore_Click(object sender, RoutedEventArgs e) => WatermarkMorePopup.IsOpen = true;
    private async void WatermarkModel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_watermarkReady || _modelControlsUpdating) return;
        RefreshWatermarkModels();
        if (IsLoaded) await EnsureSelectedModel();
    }
    private void RefreshWatermarkModels()
    {
        var configured = _settings.Current.WatermarkModels;
        WatermarkModelStatus.Text = L.Format(configured.TryGetValue(WatermarkModel.Id, out var path) && WatermarkModelDownloadService.IsInstalled(WatermarkModel.Id, path) ? "WatermarkModelReady" : "WatermarkModelNotReady", WatermarkModel.Size / 1048576d);
        WatermarkDetectorStatus.Text = L.Get(configured.TryGetValue("slbr", out var detector) && File.Exists(detector) ? "WatermarkSlbrReady" : "WatermarkSlbrMissing");
    }
    private AppSettings WatermarkSettings(string? id = null, string? path = null)
    {
        var candidate = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(_settings.Current))!;
        candidate.WatermarkDefaultModelId = id ?? WatermarkModel.Id;
        candidate.WatermarkDevice = WatermarkDeviceBox.SelectedValue as string ?? "CPU";
        candidate.WatermarkDetectionThreshold = _settings.Current.WatermarkDetectionThreshold;
        if (path is not null) candidate.WatermarkModels[id!] = path;
        return candidate;
    }
    private void SetWatermarkBusy(bool busy)
    {
        if (!_watermarkReady) return;
        if (busy) WatermarkMorePopup.IsOpen = false;
        WatermarkMethod.IsEnabled = WatermarkModelBox.IsEnabled = WatermarkDeviceBox.IsEnabled = WatermarkMaskTools.IsEnabled = !busy;
        WatermarkPreviewActions.IsEnabled = !busy && WatermarkImageSupported;
        WatermarkHistoryActions.IsEnabled = !busy && WatermarkImageSupported;
        WatermarkMaskTools.IsEnabled = !busy && WatermarkImageSupported;
        if (Mode == "Watermark") RunButton.IsEnabled = !busy && WatermarkImageSupported;
        if (busy) WatermarkBrushCursor.Visibility = Visibility.Collapsed;
    }
    private async Task RepairWatermark()
    {
        if (!WatermarkImageSupported) { Status.Text = L.Get("RecognitionBackgroundRemovalInputIsLimitedTo"); return; }
        FinishWatermarkGesture();
        if (_busy) return;
        if (WatermarkMethodValue == "Brush" && _watermarkMask.Bounds().IsEmpty) { Status.Text = L.Get("WatermarkEmptyMask"); return; }
        if (WatermarkMethodValue == "Region" && _watermarkRegion.IsEmpty) { Status.Text = L.Get("WatermarkSelectRegion"); return; }
        if (!await EnsureSelectedModel()) return;
        if (WatermarkMethodValue != "Brush" && (!_settings.Current.WatermarkModels.TryGetValue("slbr", out var detector) || !File.Exists(detector)))
        { Status.Text = L.Get("WatermarkSlbrMissing"); return; }
        if (!_settings.Current.WatermarkModels.TryGetValue(WatermarkModel.Id, out var path) || !File.Exists(path))
        { Status.Text = L.Get("WatermarkModelMissing"); return; }
        SetBusy(true); _operation = new();
        try
        {
            Status.Text = L.Get("ProcessingLocallyClickStopToCancel");
            var settings = WatermarkSettings();
            if (WatermarkMethodValue != "Brush")
            {
                byte[]? scope = null;
                if (WatermarkMethodValue == "Region")
                {
                    var region = new WatermarkMask(_image.PixelWidth, _image.PixelHeight);
                    region.Rectangle(new(_watermarkRegion.X, _watermarkRegion.Y), new(_watermarkRegion.X + _watermarkRegion.Width, _watermarkRegion.Y + _watermarkRegion.Height));
                    scope = region.Pixels;
                }
                var pixels = await OnnxImageRuntime.Shared.DetectWatermark(_image, scope, settings, _operation.Token);
                if (_closed) return;
                SaveWatermarkUndo(_watermarkMask.Pixels.ToArray());
                _watermarkMask = new(_image.PixelWidth, _image.PixelHeight, pixels);
                RefreshWatermarkOverlay();
                if (_watermarkMask.Bounds().IsEmpty) { Status.Text = L.Get("WatermarkNoDetection"); return; }
            }
            var result = await OnnxImageRuntime.Shared.Inpaint(_image, _watermarkMask.Pixels, settings, _operation.Token);
            if (_closed) return;
            SaveResultUndo(_watermarkRepairUndo, _watermarkResult); _watermarkResult = result;
            SetPicture(_image); UpdateWatermarkMode();
            Status.Text = L.Get("WatermarkRepairDone");
        }
        catch (OperationCanceledException) { if (!_closed) Status.Text = L.Get("ProcessingStoppedOrTimedOutExistingResults"); }
        catch (Exception ex) { if (!_closed) Status.Text = L.Get("ProcessingFailed") + ex.GetBaseException().Message; }
        finally { _operation?.Dispose(); _operation = null; SetBusy(false); }
    }
    private bool BeginWatermarkGesture(MouseButtonEventArgs e)
    {
        if (Mode != "Watermark" || e.ChangedButton != MouseButton.Left || Keyboard.IsKeyDown(Key.Space)) return false;
        if (WatermarkMethodValue == "Smart") return false;
        if (_busy || !WatermarkImageSupported) { e.Handled = true; return true; }
        var viewport = e.GetPosition(PictureScroll);
        if (viewport.X < 0 || viewport.Y < 0 || viewport.X >= PictureScroll.ViewportWidth || viewport.Y >= PictureScroll.ViewportHeight) return false;
        var point = e.GetPosition(PictureCanvas);
        if (point.X < 0 || point.Y < 0 || point.X >= _image.PixelWidth || point.Y >= _image.PixelHeight) return false;
        WatermarkShowMask.IsChecked = true;
        EndPreviewDrag();
        if (!PictureScroll.CaptureMouse()) return false;
        _watermarkGestureStart = _watermarkLastPoint = point;
        _watermarkRectangleGesture = WatermarkMethodValue == "Region";
        _watermarkEraseGesture = WatermarkErase.IsChecked == true;
        _watermarkBeforeGesture = _watermarkMask.Pixels.ToArray();
        // Keep the previous result visible and available to Undo until the next operation succeeds.
        if (!_watermarkRectangleGesture) DrawWatermarkStroke(point, point);
        else ShowWatermarkRegion(point, point);
        e.Handled = true; return true;
    }
    private bool MoveWatermarkGesture(MouseEventArgs e)
    {
        if (_watermarkGestureStart is not { } start) return false;
        if (e.LeftButton != MouseButtonState.Pressed) { FinishWatermarkGesture(); return true; }
        var point = e.GetPosition(PictureCanvas);
        if (_watermarkRectangleGesture) ShowWatermarkRegion(start, point);
        else DrawWatermarkStroke(_watermarkLastPoint ?? point, point);
        _watermarkLastPoint = point; e.Handled = true; return true;
    }
    private bool EndWatermarkGesture(MouseButtonEventArgs e)
    {
        if (_watermarkGestureStart is null || e.ChangedButton != MouseButton.Left) return false;
        var point = e.GetPosition(PictureCanvas);
        if (_watermarkRectangleGesture) ShowWatermarkRegion(_watermarkGestureStart.Value, point);
        else DrawWatermarkStroke(_watermarkLastPoint ?? point, point);
        FinishWatermarkGesture(); e.Handled = true; return true;
    }
    private void FinishWatermarkGesture()
    {
        if (_watermarkGestureStart is null) return;
        _watermarkGestureStart = _watermarkLastPoint = null;
        if (_watermarkRectangleGesture)
        {
            // The rectangle restricts detection; it is never silently treated as all watermark pixels.
            if (!_watermarkRegion.IsEmpty) { Array.Clear(_watermarkMask.Pixels); RefreshWatermarkOverlay(); }
        }
        if (_watermarkBeforeGesture is { } before && !before.AsSpan().SequenceEqual(_watermarkMask.Pixels)) SaveWatermarkUndo(before);
        _watermarkBeforeGesture = null;
        if (PictureScroll.IsMouseCaptured) PictureScroll.ReleaseMouseCapture();
    }
    private void ShowWatermarkRegion(Point start, Point end)
    {
        var left = (int)Math.Clamp(Math.Floor(Math.Min(start.X, end.X)), 0, _image.PixelWidth);
        var top = (int)Math.Clamp(Math.Floor(Math.Min(start.Y, end.Y)), 0, _image.PixelHeight);
        var right = (int)Math.Clamp(Math.Ceiling(Math.Max(start.X, end.X)), left, _image.PixelWidth);
        var bottom = (int)Math.Clamp(Math.Ceiling(Math.Max(start.Y, end.Y)), top, _image.PixelHeight);
        _watermarkRegion = right > left && bottom > top ? new(left, top, right - left, bottom - top) : Int32Rect.Empty;
        WatermarkRegionOutline.Margin = new(left, top, 0, 0); WatermarkRegionOutline.Width = right - left; WatermarkRegionOutline.Height = bottom - top;
        WatermarkRegionOutline.StrokeThickness = 2 / PictureScale.ScaleX;
        WatermarkRegionOutline.Visibility = _watermarkRegion.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
    }
    private void DrawWatermarkStroke(Point start, Point end)
    {
        var radius = WatermarkBrushSize.Value / 2;
        _watermarkMask.Stroke(start, end, radius, _watermarkEraseGesture);
        var left = (int)Math.Clamp(Math.Floor(Math.Min(start.X, end.X) - radius), 0, _image.PixelWidth);
        var top = (int)Math.Clamp(Math.Floor(Math.Min(start.Y, end.Y) - radius), 0, _image.PixelHeight);
        var right = (int)Math.Clamp(Math.Ceiling(Math.Max(start.X, end.X) + radius), 0, _image.PixelWidth);
        var bottom = (int)Math.Clamp(Math.Ceiling(Math.Max(start.Y, end.Y) + radius), 0, _image.PixelHeight);
        if (right > left && bottom > top) RefreshWatermarkOverlay(new(left, top, right - left, bottom - top));
    }
    private void RefreshWatermarkOverlay(Int32Rect? dirty = null)
    {
        if (!_watermarkReady || Mode != "Watermark" || !WatermarkImageSupported) return;
        if (_watermarkOverlay is null)
        {
            _watermarkOverlay = new(_image.PixelWidth, _image.PixelHeight, 96, 96, PixelFormats.Bgra32, null);
            WatermarkOverlay.Source = _watermarkOverlay; dirty = null;
        }
        var rect = dirty ?? new Int32Rect(0, 0, _image.PixelWidth, _image.PixelHeight);
        var pixels = new byte[rect.Width * rect.Height * 4];
        for (var y = 0; y < rect.Height; y++) for (var x = 0; x < rect.Width; x++)
            if (_watermarkMask.Pixels[(y + rect.Y) * _image.PixelWidth + x + rect.X] != 0)
            { var index = (y * rect.Width + x) * 4; pixels[index] = 67; pixels[index + 1] = 159; pixels[index + 2] = 255; pixels[index + 3] = 110; }
        _watermarkOverlay.WritePixels(rect, pixels, rect.Width * 4, 0);
        WatermarkUndoButton.IsEnabled = _watermarkUndo.Count > 0;
    }
    private void SaveWatermarkUndo(byte[] pixels)
    {
        _watermarkUndo.Add(pixels);
        while (_watermarkUndo.Count > 8 || (long)_watermarkUndo.Count * pixels.Length > 64_000_000) _watermarkUndo.RemoveAt(0);
        WatermarkUndoButton.IsEnabled = _watermarkUndo.Count > 0;
    }
    private void InvalidateWatermarkResult()
    {
        _watermarkResult = null;
        _watermarkRepairUndo.Clear();
        SetPicture(_image);
        WatermarkOverlay.Visibility = Mode == "Watermark" && WatermarkShowMask.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RefreshComparison();
    }
    private void WatermarkUndo_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _watermarkUndo.Count == 0) return;
        _watermarkMask = new(_image.PixelWidth, _image.PixelHeight, _watermarkUndo[^1]); _watermarkUndo.RemoveAt(_watermarkUndo.Count - 1);
        InvalidateWatermarkResult(); RefreshWatermarkOverlay();
    }
    private void WatermarkUndoRepair_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _watermarkResult is null) return;
        _watermarkResult = PopResultUndo(_watermarkRepairUndo);
        UpdateWatermarkMode();
    }
    private void WatermarkClear_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SaveWatermarkUndo(_watermarkMask.Pixels.ToArray()); Array.Clear(_watermarkMask.Pixels);
        _watermarkRegion = Int32Rect.Empty; WatermarkRegionOutline.Visibility = Visibility.Collapsed;
        InvalidateWatermarkResult(); RefreshWatermarkOverlay();
    }
    private void ResetWatermarkImage()
    {
        _watermarkResult = null; _watermarkMask = WatermarkImageSupported ? new(_image.PixelWidth, _image.PixelHeight) : new(1, 1); _watermarkUndo.Clear();
        _watermarkRepairUndo.Clear();
        _watermarkRegion = Int32Rect.Empty; _watermarkOverlay = null; WatermarkOverlay.Source = null;
        WatermarkRegionOutline.Visibility = Visibility.Collapsed;
        RefreshWatermarkOverlay();
    }
    private void WatermarkBrushSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (_watermarkReady) UpdateWatermarkBrushCursor(Mouse.GetPosition(PictureCanvas)); }

    private void UpdateWatermarkBrushCursor(Point point)
    {
        var visible = _watermarkReady && Mode == "Watermark" && WatermarkMethodValue == "Brush" && !_busy
            && PictureScroll.IsMouseOver && _previewDragStart is null && !Keyboard.IsKeyDown(Key.Space)
            && point.X >= 0 && point.Y >= 0 && point.X < _image.PixelWidth && point.Y < _image.PixelHeight;
        WatermarkBrushCursor.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;
        var size = WatermarkBrushSize.Value;
        WatermarkBrushCursor.Width = WatermarkBrushCursor.Height = size;
        WatermarkBrushCursor.Margin = new(point.X - size / 2, point.Y - size / 2, 0, 0);
        WatermarkBrushCursor.StrokeThickness = 1.5 / PictureScale.ScaleX;
    }
    private void WatermarkMaskVisibility_Changed(object sender, RoutedEventArgs e)
    { if (_watermarkReady) UpdateWatermarkMode(); }
    private void WatermarkOutput_Click(object sender, RoutedEventArgs e)
    {
        if (_watermarkResult is null || _busy) return;
        try
        {
            var action = Enum.Parse<CaptureAction>(((FrameworkElement)sender).Tag.ToString()!);
            if (action == CaptureAction.Save) SaveImageWindow.Open(_watermarkResult, new SkiaImageService(), _settings, this, ".png");
            else OutputRequested?.Invoke(_watermarkResult, action);
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    internal static void RunWatermarkLayoutSelfTest(BitmapSource image, SettingsService settings, string screenshot)
    {
        var window = new RecognitionWindow(image, settings, mode: "Watermark", autoRun: false);
        try
        {
            var root = (FrameworkElement)window.Content;
            string? requested = null;
            window.SettingsRequested += category => requested = category;
            window.Settings_Click(window, new());
            if (requested != "Eraser" || window.EngineSettingsButton.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Watermark settings entry failed.");
            root.Measure(new Size(1200, 700)); root.Arrange(new Rect(0, 0, 1200, 700)); root.UpdateLayout();
            if (window.WatermarkPreviewActions.Visibility != Visibility.Visible || window.WatermarkPreviewActions.ActualHeight < 20)
                throw new InvalidOperationException("Watermark preview actions are hidden.");
            if (window.WatermarkResultTab.Visibility != Visibility.Visible || window.ResultTabs.SelectedItem != window.WatermarkResultTab || window.TextResultTab.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Watermark mode layout failed.");
            window._watermarkMask.Stroke(new(200, 200), new(370, 200), 18); window.RefreshWatermarkOverlay();
            window.SaveWatermarkUndo(window._watermarkMask.Pixels.ToArray());
            window._watermarkMask.Stroke(new(270, 200), new(300, 200), 25, erase: true);
            window.WatermarkUndo_Click(window, new());
            if (window._watermarkMask.Pixels[200 * image.PixelWidth + 280] == 0) throw new InvalidOperationException("Mask undo failed.");
            window.WatermarkMethod.SelectedIndex = 1;
            if (window.WatermarkMaskTools.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Region must hide brush and threshold controls.");
            window.ShowWatermarkRegion(new(180, 160), new(390, 240));
            window.DocumentText_Selected(window, new());
            if (!window.PictureCanvas.Children.Contains(window.WatermarkRegionOutline)) throw new InvalidOperationException("Document selection removed the region overlay.");
            window.WatermarkMethod.SelectedIndex = 2;
            if (window.WatermarkRegionOutline.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Region leaked into smart mode.");
            var repaired = image.Clone(); repaired.Freeze();
            SaveResultUndo(window._watermarkRepairUndo, null);
            window._watermarkResult = repaired;
            window.SetWatermarkBusy(false);
            window.RefreshComparison();
            BitmapSource? output = null; window.OutputRequested += (result, _) => output = result;
            window.WatermarkOutput_Click(new Button { Tag = "Copy" }, new());
            if (!ReferenceEquals(output, repaired)) throw new InvalidOperationException("Watermark output failed.");
            window.WatermarkShowMask.IsChecked = false;
            if (!ReferenceEquals(window.Picture.Source, image) || window.WatermarkOverlay.Visibility != Visibility.Collapsed
                || !ReferenceEquals(window.ComparisonImage.Source, repaired)) throw new InvalidOperationException("Side-by-side comparison failed.");
            window.WatermarkShowMask.IsChecked = true;
            if (!ReferenceEquals(window.Picture.Source, image) || !ReferenceEquals(window.ComparisonImage.Source, repaired))
                throw new InvalidOperationException("Mask display replaced an original or result image.");
            var selectedPixels = window._watermarkMask.Pixels.ToArray();
            window.SetBusy(true);
            window.WatermarkUndoRepair_Click(window, new());
            if (!ReferenceEquals(window._watermarkResult, repaired) || window.WatermarkPreviewActions.IsEnabled)
                throw new InvalidOperationException("Undo repair must be blocked during processing.");
            window.SetBusy(false);
            window.WatermarkUndoRepair_Click(window, new());
            if (window._watermarkResult is not null || !ReferenceEquals(window.Picture.Source, image)
                || !selectedPixels.SequenceEqual(window._watermarkMask.Pixels) || window.CommonCopyButton.IsEnabled
                || window.CommonUndoButton.IsEnabled || window.ComparisonImage.Source is not null
                || window.ComparisonPlaceholder.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Undo repair must restore the source and keep the selection.");
            window.WatermarkMethod.SelectedIndex = 0;
            root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1200, 700, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
            File.WriteAllBytes(screenshot, OnnxWorker.Encode(bitmap));
            window.ModeBox.SelectedIndex = 0;
            if (window.WatermarkOverlay.Visibility != Visibility.Collapsed || window.WatermarkResultTab.Visibility != Visibility.Collapsed
                || window.WatermarkPreviewActions.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Watermark state leaked into OCR.");
        }
        finally { window.Close(); }
    }
    internal static void RunLargeWatermarkImageSelfTest(SettingsService settings)
    {
        var source = BitmapSource.Create(4001, 4000, 96, 96, PixelFormats.Gray8, null, new byte[4001 * 4000], 4001); source.Freeze();
        var window = new RecognitionWindow(source, settings, autoRun: false);
        try
        {
            if (window._watermarkMask.Pixels.Length != 1) throw new InvalidOperationException("Oversized image allocated a repair mask.");
            window.ModeBox.SelectedItem = window.ModeBox.Items.OfType<ListBoxItem>().First(x => x.Tag?.ToString() == "Watermark");
            if (window.RunButton.IsEnabled || window.WatermarkMaskTools.IsEnabled) throw new InvalidOperationException("Oversized repair was not disabled.");
            window.ModeBox.SelectedIndex = 0;
            if (!window.RunButton.IsEnabled) throw new InvalidOperationException("Oversized preview affected OCR mode.");
        }
        finally { window.Close(); }
    }
}
