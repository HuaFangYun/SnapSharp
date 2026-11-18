using L = SnapSharp.Localization.UiText;
using SnapSharp.Models;
using SnapSharp.Native;
using SnapSharp.Services;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;

namespace SnapSharp.Views;

public partial class ScrollCaptureWindow : Window
{
    private System.Drawing.Rectangle _region;
    private readonly ScrollingStitcher _stitcher = new();
    private readonly SkiaImageService _images = new();
    private ScrollCaptureSession _session;
    private ScrollStitchResult? _stitch;
    private ScrollTrim _frameTrim = new(), _crop = new();
    private BitmapSource _output;
    private CancellationTokenSource? _stop;
    private bool _ready, _working, _closed, _updating, _discard;
    private ManualScrollInput? _manualInput;
    private bool _manualRunning;
    private bool _selectingRows;
    private int _selectedRows;
    private readonly List<ScrollProject> _undo = [], _redo = [];
    public bool IsCapturing { get; private set; }
    public event Action<BitmapSource, CaptureAction>? OutputRequested;
    public event Action<BitmapSource>? EditRequested;

    public ScrollCaptureWindow(System.Drawing.Rectangle region, BitmapSource initial)
    {
        _region = region;
        _output = initial;
        _session = new ScrollCaptureSession([initial], [], [], ScrollDirection.Vertical, L.Get("ScrollingCapturePreviewCropTopBottomLeft"));
        InitializeComponent();
        DialogLayoutService.Attach(this);
        RegionInfo.Text = region.IsEmpty ? L.Get("StitchingProjectOpenedYouCanCorrectCrop") : L.Format("FixedPhysicalRegion0123", region.X, region.Y, region.Width, region.Height);
        _ready = true;
        SetPreview(new ScrollStitchResult(initial, [], [0]), new(), true);
        Closed += (_, _) => { _closed = true; _stop?.Cancel(); };
    }
    private static int Number(TextBox field, int min, int max, string label)
    {
        if (int.TryParse(field.Text, out var value) && value >= min && value <= max) return value;
        throw new ArgumentException(L.Format("0MustBeAnIntegerFrom1", label, min, max));
    }
    private ScrollTrim ReadFrameTrim() => new(Number(FrameLeft, 0, 16384, L.Get("LeftFrameMargin")), Number(FrameTop, 0, 16384, L.Get("TopFrameMargin")), Number(FrameRight, 0, 16384, L.Get("RightFrameMargin")), Number(FrameBottom, 0, 16384, L.Get("BottomFrameMargin")));
    private ScrollTrim ReadCrop() => new(Number(CropLeft, 0, 16384, L.Get("LeftResultCrop")), Number(CropTop, 0, 16384, L.Get("TopResultCrop")), Number(CropRight, 0, 16384, L.Get("RightResultCrop")), Number(CropBottom, 0, 16384, L.Get("BottomResultCrop")));
    private static void FillTrim(ScrollTrim trim, params TextBox[] fields)
    { var values = new[] { trim.Left, trim.Top, trim.Right, trim.Bottom }; for (var i = 0; i < 4; i++) fields[i].Text = values[i].ToString(); }

    private async Task<bool> RenderSession(ScrollCaptureSession session, ScrollTrim trim, ScrollTrim crop, bool fit = false, bool recordHistory = true)
    {
        if (_working || _closed) return false;
        SetBusy(true);
        try
        {
            var result = await Task.Run(() => _stitcher.Stitch(session, trim));
            var width = result.Image.PixelWidth - crop.Left - crop.Right; var height = result.Image.PixelHeight - crop.Top - crop.Bottom;
            if (crop.Left < 0 || crop.Top < 0 || crop.Right < 0 || crop.Bottom < 0 || width < 1 || height < 1) throw new ArgumentException(L.Get("AtLeast11PixelMustRemain"));
            var output = _images.Crop(result.Image, new Int32Rect(crop.Left, crop.Top, width, height));
            if (_closed) return false;
            if (recordHistory)
            {
                // Keep only parameter snapshots; never retain another capture's full frame set.
                if (!ReferenceEquals(session.Frames, _session.Frames)) { _undo.Clear(); _redo.Clear(); }
                else if (!session.Offsets.SequenceEqual(_session.Offsets) || !session.Matches.SequenceEqual(_session.Matches) ||
                         trim != _frameTrim || crop != _crop)
                {
                    _undo.Add(new(_session, _frameTrim, _crop));
                    if (_undo.Count > 20) _undo.RemoveAt(0);
                    _redo.Clear();
                }
            }
            _session = session; _frameTrim = trim; _crop = crop; _output = output;
            FillTrim(trim, FrameLeft, FrameTop, FrameRight, FrameBottom);
            FillTrim(crop, CropLeft, CropTop, CropRight, CropBottom);
            SetPreview(result, crop, fit);
            return true;
        }
        catch (Exception ex) { if (!_closed) Status.Text = L.Get("StitchingNotApplied") + ex.Message; return false; }
        finally { SetBusy(false); }
    }
    private void SetBusy(bool busy)
    {
        _working = busy;
        if (_closed) return;
        ControlsPanel.IsEnabled = !busy; OutputButtons.IsEnabled = !busy;
        UpdateHistoryButtons();
    }
    private void UpdateHistoryButtons()
    {
        UndoButton.IsEnabled = !_working && _undo.Count > 0;
        RedoButton.IsEnabled = !_working && _redo.Count > 0;
    }
    private async void Undo_Click(object sender, RoutedEventArgs e) => await RestoreEdit(false);
    private async void Redo_Click(object sender, RoutedEventArgs e) => await RestoreEdit(true);
    private async Task RestoreEdit(bool redo)
    {
        if (_working || IsCapturing) return;
        var source = redo ? _redo : _undo; var destination = redo ? _undo : _redo;
        if (source.Count == 0) return;
        var target = source[^1]; var current = new ScrollProject(_session, _frameTrim, _crop);
        if (await RenderSession(target.Session, target.FrameTrim, target.ResultCrop, recordHistory: false))
        {
            source.RemoveAt(source.Count - 1); destination.Add(current); UpdateHistoryButtons();
        }
    }
    private void SetPreview(ScrollStitchResult result, ScrollTrim crop, bool fit)
    {
        ClearRowSelection();
        _stitch = result;
        PreviewImage.Source = _output;
        CanvasHost.Width = _output.PixelWidth; CanvasHost.Height = _output.PixelHeight;
        _updating = true;
        var selected = Math.Max(0, SeamBox.SelectedIndex);
        SeamBox.ItemsSource = _session.Offsets.Select((offset, index) => L.Format("Frame01Offset2Px", index + 1, index + 2, offset)).ToArray();
        SeamBox.SelectedIndex = _session.Offsets.Count == 0 ? -1 : Math.Min(selected, _session.Offsets.Count - 1);
        _updating = false;
        MatchInfo.Text = L.Get("NoSeams");
        UpdateSelectedSeam(false); DrawSeams();
        Status.Text = L.Format("0Frames12N03N0", _session.Frames.Count, (_session.Direction == ScrollDirection.Horizontal ? L.Get("Horizontal") : L.Get("Vertical")), _output.PixelWidth, _output.PixelHeight, _session.StopReason);
        var uncertain = Enumerable.Range(0, _session.Matches.Count).FirstOrDefault(i => !_session.Matches[i].Reliable && !_session.Matches[i].ManuallyAdjusted, -1);
        if (uncertain >= 0)
        {
            SeamSettings.IsExpanded = true;
            SeamBox.SelectedIndex = uncertain;
            UpdateSelectedSeam(false);
            Status.Text += " · " + L.Get("ReviewMatch");
        }
        if (fit) Dispatcher.BeginInvoke(() => { if (!_closed) FitPreview(); });
    }
    private void DrawSeams()
    {
        if (!_ready) return;
        SeamLines.Children.Clear();
        if (ShowSeams.IsChecked != true || _stitch is null) return;
        foreach (var seam in _stitch.Seams)
        {
            var vertical = _session.Direction == ScrollDirection.Vertical;
            var position = seam - (vertical ? _crop.Top : _crop.Left);
            if (position < 0 || position > (vertical ? _output.PixelHeight : _output.PixelWidth)) continue;
            var line = new System.Windows.Shapes.Line
            {
                X1 = vertical ? 0 : position, Y1 = vertical ? position : 0,
                X2 = vertical ? _output.PixelWidth : position, Y2 = vertical ? position : _output.PixelHeight,
                Stroke = Brushes.DeepSkyBlue, StrokeThickness = 1.5 / Zoom.Value, StrokeDashArray = [4, 3]
            };
            SeamLines.Children.Add(line);
        }
    }
    private void Seams_Changed(object sender, RoutedEventArgs e) => DrawSeams();
    private void Zoom_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_ready) DrawSeams(); }
    private void FitPreview() => Zoom.Value = Math.Clamp(Math.Min(Math.Max(1, Scroller.ViewportWidth - 12) / _output.PixelWidth, Math.Max(1, Scroller.ViewportHeight - 12) / _output.PixelHeight), .01, 4);
    private void Fit_Click(object sender, RoutedEventArgs e) => FitPreview();
    private void FitWidth_Click(object sender, RoutedEventArgs e) =>
        Zoom.Value = Math.Clamp(Math.Max(1, Scroller.ViewportWidth - 12) / _output.PixelWidth, .01, 4);
    private void ActualSize_Click(object sender, RoutedEventArgs e) => Zoom.Value = 1;
    private void Preview_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        var position = e.GetPosition(Scroller);
        var oldZoom = Zoom.Value;
        var x = (Scroller.HorizontalOffset + position.X) / oldZoom;
        var y = (Scroller.VerticalOffset + position.Y) / oldZoom;
        Zoom.Value = Math.Clamp(oldZoom * (e.Delta > 0 ? 1.15 : 1 / 1.15), .01, 4);
        Scroller.UpdateLayout();
        Scroller.ScrollToHorizontalOffset(x * Zoom.Value - position.X);
        Scroller.ScrollToVerticalOffset(y * Zoom.Value - position.Y);
        e.Handled = true;
    }
    private void ClearRowSelection()
    {
        _selectedRows = 0; CropSelection.Children.Clear(); DeleteRowsButton.IsEnabled = false; CropSelectionInfo.Text = L.Get("NoCropAreaSelected");
    }
    private void CropSide_Changed(object sender, RoutedEventArgs e) { if (_ready) ClearRowSelection(); }
    private void Crop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_working || IsCapturing) return; _selectingRows = true; CanvasHost.CaptureMouse(); SelectEdge(e.GetPosition(CanvasHost)); e.Handled = true;
    }
    private void Crop_MouseMove(object sender, MouseEventArgs e) { if (_selectingRows) SelectEdge(e.GetPosition(CanvasHost)); }
    private void Crop_MouseUp(object sender, MouseButtonEventArgs e) { if (!_selectingRows) return; SelectEdge(e.GetPosition(CanvasHost)); _selectingRows = false; CanvasHost.ReleaseMouseCapture(); e.Handled = true; }
    private void Crop_LostCapture(object sender, MouseEventArgs e) => _selectingRows = false;
    internal enum CropEdge { Top, Bottom, Left, Right }
    private CropEdge SelectedEdge => RemoveLeft.IsChecked == true ? CropEdge.Left : RemoveRight.IsChecked == true ? CropEdge.Right : RemoveBottom.IsChecked == true ? CropEdge.Bottom : CropEdge.Top;
    private void SelectEdge(Point position)
    {
        var edge = SelectedEdge;
        var horizontal = edge is CropEdge.Left or CropEdge.Right;
        var length = horizontal ? _output.PixelWidth : _output.PixelHeight;
        var boundary = Math.Clamp((int)Math.Round(horizontal ? position.X : position.Y), 0, length);
        _selectedRows = edge is CropEdge.Top or CropEdge.Left ? boundary : length - boundary;
        CropSelection.Children.Clear();
        var band = new System.Windows.Shapes.Rectangle { Width = horizontal ? _selectedRows : _output.PixelWidth, Height = horizontal ? _output.PixelHeight : _selectedRows, Fill = new SolidColorBrush(Color.FromArgb(110, 244, 67, 54)), Stroke = Brushes.OrangeRed, StrokeThickness = 1 / Zoom.Value };
        Canvas.SetLeft(band, edge == CropEdge.Right ? boundary : 0);
        Canvas.SetTop(band, edge == CropEdge.Bottom ? boundary : 0);
        CropSelection.Children.Add(band);
        var side = edge switch { CropEdge.Top => L.Get("Top2"), CropEdge.Bottom => L.Get("Bottom2"), CropEdge.Left => L.Get("Left2"), _ => L.Get("Right3") };
        CropSelectionInfo.Text = L.Format("Remove1N02From0", side, _selectedRows, (horizontal ? L.Get("FullHeightColumns") : L.Get("FullWidthRows")));
        DeleteRowsButton.IsEnabled = _selectedRows > 0 && _selectedRows < length;
    }
    internal static ScrollTrim DeleteEdgePixels(ScrollTrim current, int width, int height, int pixels, CropEdge edge)
    {
        if (!Enum.IsDefined(edge)) throw new ArgumentException(L.Get("ChooseTheTopBottomLeftOrRight"));
        var length = edge is CropEdge.Left or CropEdge.Right ? width : height;
        if (width < 1 || height < 1 || pixels <= 0 || pixels >= length) throw new ArgumentException(L.Get("AtLeast11PixelMustRemain2"));
        return edge switch
        {
            CropEdge.Top => current with { Top = checked(current.Top + pixels) },
            CropEdge.Bottom => current with { Bottom = checked(current.Bottom + pixels) },
            CropEdge.Left => current with { Left = checked(current.Left + pixels) },
            _ => current with { Right = checked(current.Right + pixels) }
        };
    }
    private async void DeleteRows_Click(object sender, RoutedEventArgs e)
    {
        if (_working) return;
        try
        {
            var crop = DeleteEdgePixels(_crop, _output.PixelWidth, _output.PixelHeight, _selectedRows, SelectedEdge);
            await RenderSession(_session, _frameTrim, crop);
            FillTrim(_crop, CropLeft, CropTop, CropRight, CropBottom);
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    private void Seam_Changed(object sender, SelectionChangedEventArgs e) { if (_ready && !_updating) UpdateSelectedSeam(true); }
    private void UpdateSelectedSeam(bool scroll)
    {
        var index = SeamBox.SelectedIndex;
        if (index < 0 || index >= _session.Offsets.Count) { OffsetBox.Text = "0"; return; }
        OffsetBox.Text = _session.Offsets[index].ToString();
        var match = index < _session.Matches.Count ? _session.Matches[index] : null;
        MatchInfo.Text = match is null || match.ManuallyAdjusted ? L.Get("ManualSeam") : L.Format("AutomaticMatchError0F21Adjust", match.Error, (match.Reliable ? L.Get("ReliableMatch") : L.Get("ReviewMatch")));
        if (!scroll || _stitch is null) return;
        var seam = _stitch.Seams[index];
        if (_session.Direction == ScrollDirection.Vertical) Scroller.ScrollToVerticalOffset((seam - _crop.Top) * Zoom.Value - Scroller.ViewportHeight / 2);
        else Scroller.ScrollToHorizontalOffset((seam - _crop.Left) * Zoom.Value - Scroller.ViewportWidth / 2);
    }
    private async void ApplyOffset_Click(object sender, RoutedEventArgs e) => await ApplyOffset(0);
    private async void Nudge_Click(object sender, RoutedEventArgs e) => await ApplyOffset(int.Parse(((FrameworkElement)sender).Tag.ToString()!));
    private async Task ApplyOffset(int nudge)
    {
        try
        {
            var index = SeamBox.SelectedIndex;
            if (index < 0) throw new ArgumentException(L.Get("CaptureAtLeastTwoFramesAndSelect"));
            var offsets = _session.Offsets.ToArray(); offsets[index] = Number(OffsetBox, -16384, 16384, L.Get("FrameOffset")) + nudge;
            var matches = _session.Matches.ToArray();
            if (index < matches.Length) matches[index] = matches[index] with { Offset = offsets[index], ManuallyAdjusted = true };
            await RenderSession(_session with { Offsets = offsets, Matches = matches }, ReadFrameTrim(), ReadCrop());
            UpdateSelectedSeam(true);
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    private async void ApplyCrop_Click(object sender, RoutedEventArgs e)
    { try { await RenderSession(_session, ReadFrameTrim(), ReadCrop(), true); } catch (Exception ex) { Status.Text = ex.Message; } }
    private async void ResetCrop_Click(object sender, RoutedEventArgs e)
    {
        await RenderSession(_session, new(), new(), true);
    }
    private async void Rematch_Click(object sender, RoutedEventArgs e)
    {
        if (_working) return;
        try
        {
            var trim = ReadFrameTrim(); var crop = ReadCrop();
            SetBusy(true);
            var session = _session;
            var matches = await Task.Run(() =>
            {
                BitmapSource Frame(BitmapSource image)
                {
                    var width = image.PixelWidth - trim.Left - trim.Right; var height = image.PixelHeight - trim.Top - trim.Bottom;
                    if (width < 32 || height < 32) throw new ArgumentException(L.Get("AtLeast3232PixelsPerFrame"));
                    return _images.Crop(image, new Int32Rect(trim.Left, trim.Top, width, height));
                }
                return Enumerable.Range(1, session.Frames.Count - 1).Select(i => _stitcher.Match(Frame(session.Frames[i - 1]), Frame(session.Frames[i]), session.Direction)).ToArray();
            });
            SetBusy(false);
            matches = matches.Select(m => m.Reliable ? m : m with { Offset = 0 }).ToArray();
            await RenderSession(session with { Matches = matches, Offsets = matches.Select(x => x.Offset).ToArray() }, trim, crop);
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { SetBusy(false); }
    }
    private async Task<bool> SelectCaptureRegion()
    {
        if (_working || IsCapturing || _closed) return false;
        try
        {
            var selection = await CaptureCoordinator.SelectScrollingRegionAsync();
            if (selection is null || _closed) return false;
            _region = new System.Drawing.Rectangle(selection.ScreenBounds.Left + selection.PixelRegion.Left,
                selection.ScreenBounds.Top + selection.PixelRegion.Top, selection.PixelRegion.Width, selection.PixelRegion.Height);
            RegionInfo.Text = L.Format("FixedPhysicalRegion0123", _region.X, _region.Y, _region.Width, _region.Height);
            return true;
        }
        catch (Exception ex) { if (!_closed) Status.Text = L.Get("RegionSelectionFailed") + ex.Message; return false; }
    }
    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_working || IsCapturing) return;
        if (!await SelectCaptureRegion()) return;
        var hidden = Array.Empty<Window>(); Window? progressWindow = null;
        ScrollCaptureRegionWindow? regionFrame = null;
        try
        {
            var trim = ReadFrameTrim();
            var options = new ScrollCaptureOptions(Enum.Parse<ScrollDirection>(((ComboBoxItem)DirectionBox.SelectedItem).Tag.ToString()!),
                Number(DelayBox, 100, 5000, L.Get("FrameDelay")), Number(WheelBox, 1, 6, L.Get("WheelSteps")), Number(FrameLimitBox, 2, 80, L.Get("MaximumFrames")), trim);
            _ = _stitcher.Stitch(new ScrollCaptureSession([_session.Frames[0]], [], [], ScrollDirection.Vertical, ""), trim);
            _stop = new CancellationTokenSource(); _discard = false; IsCapturing = true; SetBusy(true);
            hidden = Application.Current.Windows.Cast<Window>().Where(x => x.IsVisible && x != this).ToArray();
            foreach (var window in hidden) window.Hide();
            Hide();
            regionFrame = new ScrollCaptureRegionWindow(_region); regionFrame.Show();
            progressWindow = ShowProgressOutsideRegion();
            await Task.Delay(350, _stop.Token);
            var progress = new Progress<string>(message => { if (!_closed) Status.Text = message; });
            var session = await new ScrollingCaptureService().CaptureSessionAsync(_region, options, progress, _stop.Token);
            SetBusy(false);
            if (!_discard && !_closed)
            {
                await RenderSession(session, trim, new(), true);
            }
            if (_discard && !_closed) Status.Text = L.Get("CaptureCanceledOriginalPreviewPreserved");
        }
        catch (OperationCanceledException) { if (!_closed) Status.Text = L.Get("CaptureCanceled"); }
        catch (Exception ex) { if (!_closed) Status.Text = L.Get("CaptureFailed") + ex.Message; }
        finally
        {
            progressWindow?.Close(); regionFrame?.Close(); IsCapturing = false; _stop?.Dispose(); _stop = null; SetBusy(false);
            foreach (var window in hidden.Where(x => x.IsLoaded)) window.Show();
            if (!_closed) { Show(); Activate(); }
        }
    }
    public void StopCapture()
    {
        if (_manualRunning) _manualInput?.Preview(); else _stop?.Cancel();
    }
    private async void ManualStart_Click(object sender, RoutedEventArgs e)
    {
        if (await SelectCaptureRegion()) BeginManualCapture(true);
    }
    public async void BeginManualCapture(bool preservePreviewOnCancel = false)
    {
        if (IsCapturing || _working || _region.IsEmpty) return;
        ScrollCaptureOptions options;
        try
        {
            options = new ScrollCaptureOptions(Enum.Parse<ScrollDirection>(((ComboBoxItem)DirectionBox.SelectedItem).Tag.ToString()!),
                120,
                Number(WheelBox, 1, 6, L.Get("WheelSteps")), preservePreviewOnCancel ? Number(FrameLimitBox, 2, 80, L.Get("MaximumFrames")) : 80, ReadFrameTrim());
            _ = _stitcher.Stitch(new ScrollCaptureSession([_session.Frames[0]], [], [], ScrollDirection.Vertical, ""), options.MatchingTrim);
        }
        catch (Exception ex) { Status.Text = L.Get("InvalidCaptureOptions") + ex.Message; return; }
        var hidden = Application.Current.Windows.Cast<Window>().Where(x => x.IsVisible && x != this && x is not SelectionWindow).ToArray();
        Window? controls = null;
        ScrollCaptureRegionWindow? regionFrame = null;
        _discard = false; _manualRunning = IsCapturing = true; _stop = new CancellationTokenSource(); SetBusy(true);
        try
        {
            foreach (var window in hidden) window.Hide(); Hide();
            regionFrame = new ScrollCaptureRegionWindow(_region); regionFrame.Show();
            _manualInput = new ManualScrollInput(_region, () => { _discard = true; _stop?.Cancel(); });
            controls = ShowManualControls();
            await Task.Delay(350, _stop.Token);
            var progress = new Progress<string>(message => { if (!_closed) { Status.Text = message; if (controls.Content is Border { Child: DockPanel panel } && panel.Children[0] is TextBlock label) label.Text = message; } });
            var captureTrace = new List<string>();
            // Capture only the selected region. One-shot WGC reconstructs a full
            // desktop session for every sample, losing intermediate scroll positions.
            var session = await new ScrollingCaptureService(new ScreenCaptureService(false)).CaptureSessionAsync(_region,
                options, progress, _stop.Token, _manualInput.Wait,
                () => ScrollDirection.Vertical,
                message => { if (captureTrace.Count < 1000) captureTrace.Add(message); },
                _manualInput.ObserveFrame);
            try
            {
                Directory.CreateDirectory(AppPaths.DiagnosticsRoot);
                File.WriteAllLines(Path.Combine(AppPaths.DiagnosticsRoot, "last-scrolling-capture.txt"), captureTrace);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            SetBusy(false);
            if (!_discard && !_closed)
            {
                await RenderSession(session, options.MatchingTrim ?? new(), new(), true);
            }
        }
        catch (OperationCanceledException) { _discard = true; }
        catch (Exception ex) { Status.Text = L.Get("ScrollingCaptureIncomplete") + ex.Message; }
        finally
        {
            _manualInput?.Dispose(); _manualInput = null; controls?.Close(); regionFrame?.Close(); _manualRunning = IsCapturing = false;
            _stop?.Dispose(); _stop = null; SetBusy(false);
            foreach (var window in hidden.Where(x => x.IsLoaded)) window.Show();
            if (!_closed)
            {
                if (_discard && !preservePreviewOnCancel) Close();
                else { if (_discard) Status.Text = L.Get("RecaptureCanceledOriginalPreviewAndEditHistory"); Show(); Activate(); }
            }
        }
    }
    private Window ShowManualControls()
    {
        var label = new TextBlock { Text = L.Get("ScrollSlowlyDownOrRightInsideThe"), FontSize = 13, Width = 250, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var cancel = new Button { ToolTip = L.Get("ExitScrollingCaptureEsc"), Width = 36, Height = 36, Style = (Style)FindResource("ToolbarButton"), Content = new MahApps.Metro.IconPacks.PackIconLucide { Kind = MahApps.Metro.IconPacks.PackIconLucideKind.X } };
        var preview = new Button { ToolTip = L.Get("FinishCaptureAndPreview"), Width = 36, Height = 36, Style = (Style)FindResource("ToolbarButton"), Content = new MahApps.Metro.IconPacks.PackIconLucide { Kind = MahApps.Metro.IconPacks.PackIconLucideKind.Check } };
        cancel.Click += (_, _) => { _discard = true; _stop?.Cancel(); }; preview.Click += (_, _) => _manualInput?.Preview();
        var panel = new DockPanel(); panel.Children.Add(label); panel.Children.Add(cancel); panel.Children.Add(preview);
        var window = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Topmost = true, SizeToContent = SizeToContent.WidthAndHeight,
            UseLayoutRounding = true, Content = new Border { BorderThickness = new Thickness(1), BorderBrush = (Brush)FindResource("BorderBrush"), Background = (Brush)FindResource("SurfaceBrush"), Padding = new Thickness(10), Child = panel } };
        var excluded = false;
        window.SourceInitialized += (_, _) => excluded = ScreenRecorderLib.Recorder.SetExcludeFromCapture(new WindowInteropHelper(window).Handle, true);
        window.Closed += (_, _) => { if (IsCapturing && !_discard) _manualInput?.Preview(); };
        window.Show();
        var dpi = VisualTreeHelper.GetDpi(window); var width = (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX); var height = (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY);
        var bounds = System.Windows.Forms.Screen.FromRectangle(_region).Bounds;
        var left = Math.Clamp(_region.Right - width, bounds.Left, Math.Max(bounds.Left, bounds.Right - width));
        var top = _region.Top - height - 8 >= bounds.Top ? _region.Top - height - 8 : _region.Top + 8;
        top = Math.Clamp(top, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - height));
        if (!excluded && _region.IntersectsWith(new System.Drawing.Rectangle(left, top, width, height))) { window.Close(); throw new InvalidOperationException(L.Get("ThisSystemCannotExcludeFloatingControlsFrom")); }
        NativeMethods.SetWindowPos(new WindowInteropHelper(window).Handle, NativeMethods.HwndTopMost, left, top, width, height, NativeMethods.SwpNoActivate);
        return window;
    }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Leave text-field undo to the editor.
        if (e.OriginalSource is not TextBox && Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Z or Key.Y)
        {
            if (e.Key == Key.Z) Undo_Click(sender, e); else Redo_Click(sender, e);
            e.Handled = true; return;
        }
        if (e.Key != Key.Escape) return;
        if (!IsCapturing && _selectedRows > 0) { ClearRowSelection(); e.Handled = true; return; }
        if (IsCapturing) StopCapture(); else if (!_working) Close();
        e.Handled = true;
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private Window? ShowProgressOutsideRegion()
    {
        var work = System.Windows.Forms.Screen.FromRectangle(_region).WorkingArea;
        var candidates = new[] { new System.Drawing.Rectangle(work.Left + 10, _region.Top - 150, 400, 140), new System.Drawing.Rectangle(work.Left + 10, _region.Bottom + 10, 400, 140),
            new System.Drawing.Rectangle(_region.Left - 410, work.Top + 10, 400, 140), new System.Drawing.Rectangle(_region.Right + 10, work.Top + 10, 400, 140) };
        var place = candidates.FirstOrDefault(x => work.Contains(x) && !x.IntersectsWith(_region));
        if (place.IsEmpty) return null; // Never place controls inside the captured rectangle. Esc/tray remain available.
        var stop = new Button { Content = L.Get("StopAndPreview"), Style = (Style)FindResource("DialogButton"), Width = 116 };
        var cancel = new Button { Content = L.Get("CancelCapture"), Style = (Style)FindResource("DialogButton"), Width = 100 };
        stop.Click += (_, _) => StopCapture(); cancel.Click += (_, _) => { _discard = true; StopCapture(); };
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal }; buttons.Children.Add(stop); buttons.Children.Add(cancel);
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = L.Get("ScrollingCaptureEscToStop"), FontSize = 16, Margin = new Thickness(0, 0, 0, 12) }); panel.Children.Add(buttons);
        var window = new Window { Width = 400, Height = 140, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Topmost = true,
            Content = new Border { Padding = new Thickness(16), Child = panel } };
        window.Closed += (_, _) => StopCapture();
        window.Show(); NativeMethods.SetWindowPos(new WindowInteropHelper(window).Handle, NativeMethods.HwndTopMost, place.X, place.Y, place.Width, place.Height, NativeMethods.SwpNoActivate);
        return window;
    }
    private void Output_Click(object sender, RoutedEventArgs e)
    {
        try { if (!_working) OutputRequested?.Invoke(_output, Enum.Parse<CaptureAction>(((FrameworkElement)sender).Tag.ToString()!)); }
        catch (Exception ex) { Status.Text = L.Get("OutputFailed") + ex.Message; }
    }
    private void Edit_Click(object sender, RoutedEventArgs e) { if (!_working) EditRequested?.Invoke(_output); }

    private async void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = L.Get("SnapSharpScrollingCaptureProjectSsscroll"), FileName = $"SnapSharp-scroll-{DateTime.Now:yyyyMMdd-HHmmss-fff}.ssscroll", OverwritePrompt = false };
        if (WindowDialogService.ShowDialog(dialog, this) != true) return;
        var overwrite = File.Exists(dialog.FileName);
        if (overwrite && MessageBox.Show(this, L.Get("TheProjectAlreadyExistsReplaceIt"), L.Get("ConfirmOverwrite"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        SetBusy(true);
        try { var project = new ScrollProject(_session, _frameTrim, _crop); await Task.Run(() => new ScrollProjectService().Save(dialog.FileName, project, overwrite)); Status.Text = L.Get("SavedOriginalFramesOffsetsAndCrops") + dialog.FileName; }
        catch (Exception ex) { Status.Text = L.Get("CouldNotSaveProject") + ex.Message; }
        finally { SetBusy(false); }
    }
    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = L.Get("SnapSharpScrollingCaptureProjectSsscroll") };
        if (WindowDialogService.ShowDialog(dialog, this) == true) await LoadProject(dialog.FileName);
    }
    public async Task LoadProject(string path)
    {
        SetBusy(true);
        try
        {
            var project = await Task.Run(() => new ScrollProjectService().Load(path));
            if (_closed) return;
            SetBusy(false); await RenderSession(project.Session, project.FrameTrim, project.ResultCrop, true);
        }
        catch (Exception ex) { Status.Text = L.Get("CouldNotOpenProject") + ex.Message; }
        finally { SetBusy(false); }
    }
    private void RawFrames_Click(object sender, RoutedEventArgs e)
    {
        var index = SeamBox.SelectedIndex; if (index < 0) { Status.Text = L.Get("SelectASeamFirst"); return; }
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var column = 0; column < 2; column++)
        {
            var image = _session.Frames[index + column];
            var panel = new DockPanel { Margin = new Thickness(10) }; Grid.SetColumn(panel, column);
            var label = new TextBlock { Text = L.Format("OriginalFrame0100PixelsScrollIndependently", index + column + 1), Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(label, Dock.Top); panel.Children.Add(label);
            panel.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new System.Windows.Controls.Image { Source = image, Width = image.PixelWidth, Height = image.PixelHeight, Stretch = Stretch.Fill } });
            grid.Children.Add(panel);
        }
        new Window { Owner = this, Title = L.Get("OriginalFrameComparisonNoMarginsApplied"), Width = 1000, Height = 700, Content = grid, WindowStartupLocation = WindowStartupLocation.CenterOwner }.Show();
    }

    internal static bool RunLayoutSelfTest(ScrollCaptureSession session)
    {
        var window = new ScrollCaptureWindow(new System.Drawing.Rectangle(-1600, 40, 240, 180), session.Frames[0]);
        try
        {
            window._session = session;
            var result = new ScrollingStitcher().Stitch(session);
            window._output = result.Image; window.SetPreview(result, new(), false);
            if (window.SeamBox.Items.Count != 4 || window.SeamLines.Children.Count != 4) return false;
            window.SeamBox.SelectedIndex = 1;
            if (window.OffsetBox.Text != "53") return false;
            window.ShowSeams.IsChecked = false;
            if (window.SeamLines.Children.Count != 0) return false;
            window.ShowSeams.IsChecked = true; window.Zoom.Value = 1;
            var copied = false; var edited = false;
            window.OutputRequested += (image, action) => copied = action == CaptureAction.Copy && image.PixelHeight == 392;
            window.EditRequested += image => edited = image.PixelHeight == 392;
            window.Output_Click(new Button { Tag = "Copy" }, new RoutedEventArgs()); window.Edit_Click(window, new RoutedEventArgs());
            if (!copied || !edited) return false;
            var content = (FrameworkElement)window.Content;
            foreach (var size in new[] { new System.Windows.Size(860, 540), new System.Windows.Size(1140, 760), new System.Windows.Size(1400, 900) })
            {
                content.Measure(size); content.Arrange(new Rect(size)); content.UpdateLayout();
                if (window.Scroller.ActualWidth < 400 || window.ControlsPanel.ActualWidth < 250 || window.DeleteRowsButton.ActualWidth < 200) return false;
                var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
            }
            var width = result.Image.PixelWidth;
            var topCrop = DeleteEdgePixels(new(), width, 392, 40, CropEdge.Top);
            var bothCrop = DeleteEdgePixels(topCrop, width, 352, 30, CropEdge.Bottom);
            if (bothCrop != new ScrollTrim(0, 40, 0, 30)) return false;
            var cropped = window._images.Crop(result.Image, new Int32Rect(0, bothCrop.Top, result.Image.PixelWidth, result.Image.PixelHeight - bothCrop.Top - bothCrop.Bottom));
            if (cropped.PixelWidth != result.Image.PixelWidth || cropped.PixelHeight != 322) return false;
            window.SelectEdge(new Point(100, 50));
            if (!window.DeleteRowsButton.IsEnabled || window._selectedRows != 50 || window.CropSelection.Children.Count != 1) return false;
            window.RemoveBottom.IsChecked = true; window.SelectEdge(new Point(100, 300));
            if (window._selectedRows != 92) return false;
            window.RemoveLeft.IsChecked = true;
            if (window._selectedRows != 0 || window.DeleteRowsButton.IsEnabled) return false;
            window.SelectEdge(new Point(25, 100));
            if (window._selectedRows != 25 || !window.DeleteRowsButton.IsEnabled || window.CropSelection.Children[0] is not System.Windows.Shapes.Rectangle { Width: 25, Height: 392 }) return false;
            window.RemoveRight.IsChecked = true; window.SelectEdge(new Point(width - 35, 100));
            if (window._selectedRows != 35 || window.CropSelection.Children[0] is not System.Windows.Shapes.Rectangle rightBand || rightBand.Width != 35 || Canvas.GetLeft(rightBand) != width - 35) return false;
            var leftCrop = DeleteEdgePixels(bothCrop, width, 322, 25, CropEdge.Left);
            var fourCrop = DeleteEdgePixels(leftCrop, width - 25, 322, 35, CropEdge.Right);
            if (fourCrop != new ScrollTrim(25, 40, 35, 30)) return false;
            var fourOutput = window._images.Crop(result.Image, new Int32Rect(fourCrop.Left, fourCrop.Top, width - 60, 322));
            var expectedPixels = new byte[fourOutput.PixelWidth * fourOutput.PixelHeight * 4];
            var actualPixels = new byte[expectedPixels.Length];
            result.Image.CopyPixels(new Int32Rect(25, 40, width - 60, 322), expectedPixels, fourOutput.PixelWidth * 4, 0);
            fourOutput.CopyPixels(actualPixels, fourOutput.PixelWidth * 4, 0);
            if (!expectedPixels.SequenceEqual(actualPixels)) return false;
            // A click in the middle always selects a band connected to an edge, never a floating hole.
            window.SelectEdge(new Point(width / 2, 200));
            if (window.CropSelection.Children[0] is not System.Windows.Shapes.Rectangle middleBand || middleBand.Height != 392 || Canvas.GetLeft(middleBand) + middleBand.Width != width) return false;
            foreach (var edge in Enum.GetValues<CropEdge>())
            {
                var length = edge is CropEdge.Left or CropEdge.Right ? width : 392;
                foreach (var invalid in new[] { -1, 0, length, length + 1 })
                { try { DeleteEdgePixels(new(), width, 392, invalid, edge); return false; } catch (ArgumentException) { } }
                _ = DeleteEdgePixels(new(), width, 392, length - 1, edge);
            }
            window.SelectEdge(new Point(width + 100, 0));
            if (window.DeleteRowsButton.IsEnabled) return false;
            window.ClearRowSelection();
            if (window.CropSelection.Children.Count != 0 || window.DeleteRowsButton.IsEnabled) return false;
            // Exercise actual async edits and verify failed edits never change history/output.
            static void Complete(Task task)
            {
                var frame = new System.Windows.Threading.DispatcherFrame();
                var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                task.GetAwaiter().GetResult();
            }
            Complete(window.RenderSession(session, new(), new(0, 40, 0, 30)));
            if (window._output.PixelHeight != 322 || window._undo.Count != 1 || !window.UndoButton.IsEnabled) return false;
            Complete(window.RestoreEdit(false));
            if (window._output.PixelHeight != 392 || window._undo.Count != 0 || !window.RedoButton.IsEnabled || window.CropTop.Text != "0") return false;
            Complete(window.RestoreEdit(true));
            if (window._output.PixelHeight != 322 || window.CropTop.Text != "40" || window._redo.Count != 0) return false;
            var previous = window._output;
            Complete(window.RenderSession(session, new(), new(0, 392)));
            if (!ReferenceEquals(window._output, previous) || window._undo.Count != 1 || window.CropTop.Text != "40") return false;
            Complete(window.RestoreEdit(false));
            Complete(window.RenderSession(session with { Offsets = new[] { 54, 53, 53, 53 } }, new(), new()));
            if (window._output.PixelHeight != 393 || window._redo.Count != 0) return false;
            Complete(window.RestoreEdit(false));
            if (window._output.PixelHeight != 392 || !window._session.Offsets.SequenceEqual(session.Offsets)) return false;
            Complete(window.RenderSession(session with { Frames = session.Frames.ToArray() }, new(), new()));
            if (window._undo.Count != 0 || window._redo.Count != 0) return false;
            if (window.CaptureSettings.Visibility != Visibility.Visible || !window.CaptureSettings.IsEnabled) return false;
            // The production handoff must expose both originals and the unresolved seam.
            var unresolved = new ScrollCaptureSession(session.Frames.Take(2).ToArray(), [0],
                [session.Matches[0] with { Offset = 0, Reliable = false }], ScrollDirection.Vertical, "review");
            Complete(window.RenderSession(unresolved, new(), new()));
            if (!window.SeamSettings.IsExpanded || window.SeamBox.SelectedIndex != 0 || window._output.PixelHeight != 180 ||
                window._session.Frames.Count != 2) return false;
            window.OffsetBox.Text = "53"; Complete(window.ApplyOffset(0));
            if (window._output.PixelHeight != 233 || !window._session.Matches[0].ManuallyAdjusted ||
                window.MatchInfo.Text != L.Get("ManualSeam")) return false;
            Complete(window.RestoreEdit(false));
            if (window._session.Offsets[0] != 0 || window._session.Matches[0].ManuallyAdjusted || window._output.PixelHeight != 180) return false;
            Complete(window.RestoreEdit(true));
            if (window._session.Offsets[0] != 53 || !window._session.Matches[0].ManuallyAdjusted || window._output.PixelHeight != 233) return false;
            return true;
        }
        finally { window.Close(); }
    }
}
