using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MahApps.Metro.IconPacks;
using SnapSharp.Services;
using L = SnapSharp.Localization.UiText;
using Button = System.Windows.Controls.Button;
using Image = System.Windows.Controls.Image;
using TextBlock = System.Windows.Controls.TextBlock;
using FontFamily = System.Windows.Media.FontFamily;
using Size = System.Windows.Size;

namespace SnapSharp.Views;

public sealed partial class ScreenRulerWindow
{
    private readonly RulerMeasurements _measurements = new();
    private RulerMeasurementOverlay _measurementOverlay = null!;
    private readonly Dictionary<RulerTool, ToggleButton> _toolButtons = [];
    private ToggleButton _guideButton = null!, _zoomButton = null!;
    private readonly Dictionary<RulerTool, ToggleButton> _visibilityButtons = [];
    private Border _startPanel = null!;
    private readonly TextBlock _startTitle = new() { Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _startHint = new() { Foreground = Brushes.LightGray, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _measurementText = new() { FontFamily = new FontFamily("Consolas"), FontSize = 11, Foreground = Brushes.White,
        TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 3, 10, 0) };
    private readonly TextBlock _interactionText = new() { FontSize = 12, Foreground = Brushes.LightGray,
        TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 7, 10, 12) };
    private Border _zoomPanel = null!;
    private readonly Image _zoomImage = new() { Width = 119, Height = 119, Stretch = Stretch.Fill };
    private readonly TextBlock _zoomText = new() { Foreground = Brushes.White, FontFamily = new FontFamily("Consolas"), FontSize = 10, TextAlignment = TextAlignment.Center };
    private readonly MagnifierReticle _zoomReticle = new() { IsHitTestVisible = false };
    private BitmapSource? _sample;
    private Exception? _sampleError;
    private System.Drawing.Rectangle _sampleBounds;
    private long _lastSample;
    private bool _captureExcluded;
    private int? _draggedGuide;
    private int? _selectedGuide;
    private bool _pointerDown;

    private void InitializeMeasurementTools(Grid layout)
    {
        var bar = new WrapPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(8, 8, 6, 8) };
        var toolbarContent = new DockPanel();
        var close = new Button { Content = "×", Width = 32, Height = 30, Margin = new Thickness(0, 8, 8, 0),
            VerticalAlignment = VerticalAlignment.Top, ToolTip = L.Get("Close"), Foreground = Brushes.LightGray,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Right); toolbarContent.Children.Add(close);
        toolbarContent.Children.Add(bar);
        var toolbar = new Border { Background = new SolidColorBrush(Color.FromRgb(27, 36, 50)), Child = toolbarContent };
        toolbar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.Handled) return;
            DragMove(); e.Handled = true;
        };
        var title = new TextBlock { Text = L.Get("ScreenRuler"), Foreground = Brushes.LightGray, FontSize = 12,
            Margin = new Thickness(3, 8, 10, 0), ToolTip = L.Get("ScreenRulerHelp") };
        title.SetBinding(ToolTipProperty, new System.Windows.Data.Binding(nameof(TextBlock.Text)) { Source = _readout });
        bar.Children.Add(title);
        Grid.SetRow(toolbar, 1); layout.Children.Add(toolbar);
        foreach (var (tool, resource, icon, key) in new[] {
            (RulerTool.Line, "DesktopRulerLine", PackIconLucideKind.Ruler, "1"),
            (RulerTool.Rectangle, "DesktopRulerBox", PackIconLucideKind.Square, "2"),
            (RulerTool.Distance, "DesktopRulerDistance", PackIconLucideKind.ArrowUpRight, "3"),
            (RulerTool.Angle, "DesktopRulerAngle", PackIconLucideKind.Ruler, "A") })
        {
            var button = MakeToggle(resource, icon, key, true);
            button.Click += (_, _) => SelectMeasurementTool(tool);
            _toolButtons.Add(tool, button);
            var group = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            group.Children.Add(button);
            var visibility = MakeToggle(resource, PackIconLucideKind.Eye, "", false);
            visibility.ToolTip = L.Get("DesktopRulerLayers") + ": " + L.Get(resource);
            visibility.Click += (_, _) =>
            {
                if (visibility.IsChecked == true) _measurements.VisibleTools.Add(tool);
                else if (_measurements.ActiveTool == tool) _measurements.SelectTool(tool);
                else _measurements.VisibleTools.Remove(tool);
                RefreshToolButtons(); UpdateMeasurements();
            };
            visibility.Width = 26; visibility.Height = 28; visibility.Margin = new Thickness(0);
            button.Margin = new Thickness(0); button.Height = 28;
            group.Children.Add(new Border { Width = 1, Margin = new Thickness(0, 6, 0, 6), Background = new SolidColorBrush(Color.FromRgb(67, 85, 111)) });
            _visibilityButtons.Add(tool, visibility); group.Children.Add(visibility);
            bar.Children.Add(new Border { Child = group, CornerRadius = new CornerRadius(5), Padding = new Thickness(1),
                BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(61, 79, 104)),
                Margin = new Thickness(2, 0, 4, 4) });
        }
        _guideButton = MakeToggle("DesktopRulerGuides", PackIconLucideKind.Plus, "G", true);
        _guideButton.IsChecked = _measurements.ShowGuides;
        _guideButton.Click += (_, _) => ToggleReferenceGuides();
        _guideButton.ToolTip = L.Get("DesktopRulerGuidesHint") + "\nG";
        bar.Children.Add(_guideButton);
        _zoomButton = MakeToggle("DesktopRulerMagnifier", PackIconLucideKind.ZoomIn, "M", true);
        _zoomButton.Click += (_, _) => { _zoomPanel.Visibility = _zoomButton.IsChecked == true && (_sample is not null || _sampleError is not null) ? Visibility.Visible : Visibility.Collapsed; UpdateMeasurements(); };
        bar.Children.Add(_zoomButton);
        InitializeClipboardTool(bar);
        var clear = new Button { Content = new PackIconLucide { Kind = PackIconLucideKind.Trash2, Width = 16, Height = 16 },
            Height = 30, Width = 30, Margin = new Thickness(3, 0, 0, 4), Foreground = Brushes.LightGray, BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, ToolTip = L.Get("DesktopRulerClear") };
        clear.Click += (_, _) => { _measurements.Clear(); _selectedGuide = null; UpdateMeasurements(); };
        bar.Children.Add(clear);
        var status = new Grid { Background = new SolidColorBrush(Color.FromRgb(27, 36, 50)) };
        status.ColumnDefinitions.Add(new ColumnDefinition()); status.ColumnDefinitions.Add(new ColumnDefinition());
        var keyHint = new TextBlock { Text = L.Get("DesktopRulerMoveKeys"), FontSize = 11, Foreground = Brushes.LightSteelBlue,
            ToolTip = L.Get("DesktopRulerMoveKeys"),
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 7, 12, 12), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(keyHint, 1); status.Children.Add(keyHint); status.Children.Add(_interactionText);
        status.Children.Add(_copyNotice);
        Grid.SetRow(status, 3); layout.Children.Add(status);
        _measurementOverlay = new RulerMeasurementOverlay(_measurements) { IsHitTestVisible = false };
        _surface.Children.Add(_measurementOverlay);
        var startContent = new StackPanel(); startContent.Children.Add(_startTitle); startContent.Children.Add(_startHint);
        _startPanel = new Border { Child = startContent, Padding = new Thickness(18), CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(235, 27, 30, 36)), MaxWidth = 360,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(44, 30, 175, 8), IsHitTestVisible = false };
        _surface.Children.Add(_startPanel);
        InitializeMagnifier();
        _surface.MouseLeftButtonDown += MeasurementDown;
        _surface.MouseMove += MeasurementMove;
        _surface.MouseLeave += (_, _) => { if (!_pointerDown) { _measurementOverlay.HoveredGuide = null; _measurementOverlay.InvalidateVisual(); } };
        _surface.MouseLeftButtonUp += MeasurementUp;
        _surface.MouseRightButtonDown += (_, e) =>
        {
            var index = GuideAt(ToMeasurement(e.GetPosition(_guides)));
            if (index is { } i) { _measurements.Guides.RemoveAt(i); _selectedGuide = null; }
            else _measurements.Cancel();
            _draggedGuide = null; _pointerDown = false; _surface.ReleaseMouseCapture(); UpdateMeasurements(); e.Handled = true;
        };
        _surface.LostMouseCapture += (_, _) => { _pointerDown = false; _draggedGuide = null; };
        SourceInitialized += (_, _) => _captureExcluded = ScreenRecorderLib.Recorder.SetExcludeFromCapture(new WindowInteropHelper(this).Handle, true);
        LocationChanged += (_, _) => { _sample = null; _zoomImage.Source = null; _zoomPanel.Visibility = Visibility.Collapsed; };
        RefreshToolButtons();
    }

    private static ToggleButton MakeToggle(string resource, PackIconLucideKind icon, string key, bool caption)
    {
        var content = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(caption ? 6 : 3, 0, caption ? 6 : 3, 0) };
        if (resource == "DesktopRulerAngle" && caption)
            content.Children.Add(new TextBlock { Text = "∠", FontSize = 19, VerticalAlignment = VerticalAlignment.Center });
        else content.Children.Add(new PackIconLucide { Kind = icon, Width = caption ? 14 : 12, Height = caption ? 14 : 12, VerticalAlignment = VerticalAlignment.Center });
        if (caption) content.Children.Add(new TextBlock { Text = L.Get(resource) + " " + key, FontSize = 12, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var template = new ControlTemplate(typeof(ToggleButton));
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "ToggleBackground";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter); template.VisualTree = border;
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        var selected = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, caption ? new SolidColorBrush(Color.FromRgb(44, 72, 110)) : Brushes.Transparent, "ToggleBackground"));
        template.Triggers.Add(selected);
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(63, 88, 123)), "ToggleBackground")); template.Triggers.Add(hover);
        return new ToggleButton { Content = content, Height = 30, Margin = new Thickness(2, 0, 2, 4), Foreground = Brushes.White,
            Template = template, Background = Brushes.Transparent, HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
            ToolTip = L.Format("CaptureToolShortcutHint", L.Get(resource), key), IsChecked = true };
    }

    private void SelectMeasurementTool(RulerTool tool)
    {
        if (_measurements.ActiveTool != tool) _measurements.SelectTool(tool);
        else _measurements.VisibleTools.Add(tool);
        _selectedGuide = null;
        RefreshToolButtons(); UpdateMeasurements(); Focus();
    }

    private void RefreshToolButtons()
    {
        foreach (var (tool, button) in _toolButtons)
        {
            button.IsChecked = tool == _measurements.ActiveTool;
            button.Background = tool == _measurements.ActiveTool ? new SolidColorBrush(Color.FromRgb(35, 78, 125)) : Brushes.Transparent;
        }
        foreach (var (tool, button) in _visibilityButtons)
        {
            button.IsChecked = _measurements.VisibleTools.Contains(tool);
            button.Opacity = button.IsChecked == true ? 1 : .4;
        }
    }

    private void InitializeMagnifier()
    {
        RenderOptions.SetBitmapScalingMode(_zoomImage, BitmapScalingMode.NearestNeighbor);
        var imageGrid = new Grid { Width = 119, Height = 119, ClipToBounds = true, Background = Brushes.Black };
        imageGrid.Children.Add(_zoomImage);
        imageGrid.Children.Add(_zoomReticle);
        var zoomContent = new StackPanel(); zoomContent.Children.Add(imageGrid); zoomContent.Children.Add(_zoomText);
        _zoomPanel = new Border { Child = zoomContent, Background = new SolidColorBrush(Color.FromArgb(245, 20, 24, 30)),
            BorderBrush = Brushes.SlateGray, BorderThickness = new Thickness(1), Padding = new Thickness(6), CornerRadius = new CornerRadius(6),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8, 32, 8, 8), IsHitTestVisible = false };
        _zoomPanel.Visibility = Visibility.Collapsed;
        _surface.Children.Add(_zoomPanel);
    }

    private Point ToMeasurement(Point point)
    {
        var bounds = _guides.MeasurementBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return new Point();
        return _measurements.Clamp(new Point((point.X - bounds.Left) * _guides.PixelWidth / bounds.Width,
            (point.Y - bounds.Top) * _guides.PixelHeight / bounds.Height));
    }

    private Point ResolveMeasurementPoint(Point local, bool forceSample = false)
    {
        var point = ToMeasurement(local);
        var snap = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        SampleScreen(local, forceSample || snap);
        if (snap)
        {
            if (_sample is not null)
            {
                var screen = _guides.PointToScreen(local);
                var center = new Point(Math.Floor(screen.X) - _sampleBounds.Left, Math.Floor(screen.Y) - _sampleBounds.Top);
                var edge = RulerEdgeSnap.Find(_sample, center);
                point += edge - center;
            }
            point = _measurements.SnapToGuides(point);
        }
        return _measurements.Update(point, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
    }

    private void MeasurementDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        var local = e.GetPosition(_guides); var bounds = _guides.MeasurementBounds;
        _guides.Pointer = local;
        if (local.X < bounds.Left && local.Y < bounds.Top) return;
        var point = ResolveMeasurementPoint(local, true);
        _selectedGuide = null;
        if (local.X < bounds.Left || local.Y < bounds.Top)
        {
            _measurements.Cancel();
            _measurements.ShowGuides = true; _guideButton.IsChecked = true;
            var vertical = local.X < bounds.Left;
            _measurements.Guides.Add(new RulerGuide(vertical, vertical ? point.X : point.Y));
            _draggedGuide = _selectedGuide = _measurements.Guides.Count - 1;
        }
        else if (GuideAt(point) is { } guide && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _measurements.Cancel();
            _draggedGuide = _selectedGuide = guide;
        }
        else _measurements.Press(point);
        _pointerDown = true; _surface.CaptureMouse();
        UpdateMeasurements(); e.Handled = true;
    }

    private void MeasurementMove(object sender, MouseEventArgs e)
    {
        var local = e.GetPosition(_guides);
        _guides.Pointer = local;
        var point = ResolveMeasurementPoint(local);
        if (_pointerDown && _draggedGuide is { } index)
        {
            var guide = _measurements.Guides[index];
            _measurements.Guides[index] = guide with { Position = guide.Vertical ? point.X : point.Y };
        }
        var hovered = _draggedGuide ?? GuideAt(point);
        _measurementOverlay.HoveredGuide = hovered;
        _surface.Cursor = hovered is { } guideIndex && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            ? (_measurements.Guides[guideIndex].Vertical ? Cursors.SizeWE : Cursors.SizeNS) : PrecisionCursor.Cross;
        UpdateMeasurements();
    }

    private void MeasurementUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pointerDown) return;
        var local = e.GetPosition(_guides); _guides.Pointer = local;
        var point = ResolveMeasurementPoint(local, true);
        if (_draggedGuide is null) _measurements.Release(point, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        _pointerDown = false; _draggedGuide = null; _surface.ReleaseMouseCapture();
        UpdateMeasurements(); e.Handled = true;
    }

    private void ToggleReferenceGuides()
    {
        // An empty layer must produce visible feedback on the first click (or after Clear).
        if (_measurements.Guides.Count == 0)
        {
            _measurements.Guides.Add(new RulerGuide(true, Math.Round(_measurements.Extent.Width / 2)));
            _measurements.Guides.Add(new RulerGuide(false, Math.Round(_measurements.Extent.Height / 2)));
            _measurements.ShowGuides = true;
        }
        else _measurements.ShowGuides = !_measurements.ShowGuides;
        _guideButton.IsChecked = _measurements.ShowGuides;
        UpdateMeasurements();
    }

    private int? GuideAt(Point point)
    {
        if (!_measurements.ShowGuides) return null;
        for (var i = _measurements.Guides.Count - 1; i >= 0; i--)
        {
            var guide = _measurements.Guides[i];
            if (Math.Abs((guide.Vertical ? point.X : point.Y) - guide.Position) <= 4) return i;
        }
        return null;
    }

    private bool HandleMeasurementKey(KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
        {
            if (!e.IsRepeat) CopyCurrentMeasurement();
            e.Handled = true; return true;
        }
        if (Keyboard.Modifiers is not (ModifierKeys.None or ModifierKeys.Alt)) return false;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var tool = key switch { Key.D1 or Key.NumPad1 => RulerTool.Line, Key.D2 or Key.NumPad2 => RulerTool.Rectangle,
            Key.D3 or Key.NumPad3 => RulerTool.Distance, Key.A => RulerTool.Angle, _ => RulerTool.None };
        if (tool != RulerTool.None) { if (!e.IsRepeat) SelectMeasurementTool(tool); }
        else if (key == Key.G) { if (!e.IsRepeat) ToggleReferenceGuides(); }
        else if (key == Key.M) { if (!e.IsRepeat) { _zoomButton.IsChecked = _zoomButton.IsChecked != true; _zoomPanel.Visibility = _zoomButton.IsChecked == true && (_sample is not null || _sampleError is not null) ? Visibility.Visible : Visibility.Collapsed; } }
        else if (key == Key.Delete)
        {
            if (_selectedGuide is { } i && i < _measurements.Guides.Count) { _measurements.Guides.RemoveAt(i); _selectedGuide = null; }
            else _measurements.DeleteActive();
        }
        else if (key == Key.Escape && _measurements.HasPending) _measurements.Cancel();
        else return false;
        UpdateMeasurements(); e.Handled = true; return true;
    }

    private void RefreshMeasurements(Rect bounds)
    {
        if (_measurementOverlay is null) return;
        _measurements.Resize(new Size(_guides.PixelWidth, _guides.PixelHeight));
        _measurementOverlay.Bounds = bounds;
        _measurementOverlay.Scale = new Vector(bounds.Width / _guides.PixelWidth, bounds.Height / _guides.PixelHeight);
        _measurementOverlay.InvalidateVisual();
        var drawing = _measurements.Drawings().LastOrDefault(x => x.Tool == _measurements.ActiveTool);
        var points = drawing.Points;
        _measurementText.Text = "";
        if (points is { Length: >= 2 })
        {
            var a = points[0]; var b = points[1];
            _measurementText.Text = drawing.Tool switch
            {
                RulerTool.Rectangle => FormattableString.Invariant($"W: {Math.Abs(b.X - a.X):0} × H: {Math.Abs(b.Y - a.Y):0} px   A: ({a.X:0}, {a.Y:0})   B: ({b.X:0}, {b.Y:0})"),
                RulerTool.Angle when points.Length == 3 => FormattableString.Invariant($"θ: {RulerMeasurements.Angle(a, b, points[2]):0.##}°   O: ({a.X:0}, {a.Y:0})"),
                _ => FormattableString.Invariant($"Δx: {b.X - a.X:0}   Δy: {b.Y - a.Y:0}   D: {RulerMeasurements.Distance(a, b):0.##} px   A: ({a.X:0}, {a.Y:0})   B: ({b.X:0}, {b.Y:0})")
            };
        }
        var modeKey = _measurements.ActiveTool switch { RulerTool.Line => "DesktopRulerLine", RulerTool.Distance => "DesktopRulerDistance",
            RulerTool.Angle => "DesktopRulerAngle", _ => "DesktopRulerBox" };
        var instructionKey = _measurements.ActiveTool switch { RulerTool.Line => "DesktopRulerLineSteps", RulerTool.Distance => "DesktopRulerDistanceSteps",
            RulerTool.Angle => _measurements.Pending.Count switch { 0 => "DesktopRulerAngleStep1", 1 => "DesktopRulerAngleStep2", _ => "DesktopRulerAngleStep3" },
            _ => "DesktopRulerBoxSteps" };
        _interactionText.Text = L.Get(modeKey) + " · " + L.Get(instructionKey);
        _startTitle.Text = L.Get(modeKey);
        _startHint.Text = L.Get(instructionKey);
        _startPanel.Visibility = _measurements.ActiveTool != RulerTool.None && !_measurements.HasPending && !_measurements.Results.ContainsKey(_measurements.ActiveTool) ? Visibility.Visible : Visibility.Collapsed;
        _interactionText.ToolTip = _interactionText.Text + "\n" + L.Get("DesktopRulerQuickHelp") + "\n" + L.Get("DesktopRulerGuidesHint") + "\n" + _measurementText.Text;
        _measurementText.ToolTip = _measurementText.Text;
        if (_zoomPanel.Visibility == Visibility.Visible) PositionMagnifier(_guides.Pointer);
        else _measurementOverlay.ExcludedBounds = null;
    }

    private void SampleScreen(Point local, bool force)
    {
        if (!_captureExcluded || (_zoomButton.IsChecked != true && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)))
        {
            if (!_captureExcluded) _zoomText.Text = L.Get("DesktopRulerCaptureUnavailable");
            return;
        }
        if (!force && Environment.TickCount64 - _lastSample < 50) return;
        _lastSample = Environment.TickCount64;
        try
        {
            _sampleError = null;
            var screen = _guides.PointToScreen(local);
            var desktop = System.Windows.Forms.SystemInformation.VirtualScreen;
            var width = Math.Min(33, desktop.Width); var height = Math.Min(33, desktop.Height);
            var left = Math.Clamp((int)Math.Floor(screen.X) - width / 2, desktop.Left, desktop.Right - width);
            var top = Math.Clamp((int)Math.Floor(screen.Y) - height / 2, desktop.Top, desktop.Bottom - height);
            _sampleBounds = new System.Drawing.Rectangle(left, top, width, height);
            _sample = new ScreenCaptureService().CaptureRegion(_sampleBounds);
            var cx = Math.Clamp((int)Math.Floor(screen.X) - left, 0, width - 1);
            var cy = Math.Clamp((int)Math.Floor(screen.Y) - top, 0, height - 1);
            var crop = new Int32Rect(Math.Clamp(cx - 8, 0, width - 17), Math.Clamp(cy - 8, 0, height - 17), 17, 17);
            _zoomImage.Source = new CroppedBitmap(_sample, crop);
            _zoomReticle.SetPixel(cx - crop.X, cy - crop.Y);
            var color = new byte[4];
            new FormatConvertedBitmap(_sample, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(cx, cy, 1, 1), color, 4, 0);
            var point = _measurements.ReadoutPoint(ToMeasurement(local));
            _zoomText.Text = FormattableString.Invariant($"({point.X:0}, {point.Y:0})\n#{color[2]:X2}{color[1]:X2}{color[0]:X2}");
            _zoomPanel.Visibility = _zoomButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PositionMagnifier(local);
        }
        catch (Exception ex) when (ex is Win32Exception or ExternalException or ArgumentException)
        { _sampleError = ex; _sample = null; _zoomImage.Source = null; _zoomText.Text = L.Get("DesktopRulerCaptureUnavailable"); }
    }

    private void PositionMagnifier(Point pointer)
    {
        const double width = 133, height = 165, gap = 12;
        var bounds = _guides.MeasurementBounds;
        if (bounds.Width < width || bounds.Height < height) return;
        var endpoints = _measurements.Drawings().SelectMany(d => d.Points).Select(_measurementOverlay.ToLocal).ToArray();
        var candidates = new[] {
            new Point(bounds.Right - width - gap, bounds.Bottom - height - gap),
            new Point(bounds.Left + gap, bounds.Bottom - height - gap),
            new Point(bounds.Right - width - gap, bounds.Top + gap),
            new Point(bounds.Left + gap, bounds.Top + gap) };
        double Score(Point p)
        {
            var exclusion = new Rect(p - new Vector(gap, gap), new Size(width + gap * 2, height + gap * 2));
            return (exclusion.Contains(pointer) ? 1000 : 0) + endpoints.Count(exclusion.Contains) * 100;
        }
        var best = candidates.OrderBy(Score).First();
        _zoomPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        _zoomPanel.VerticalAlignment = VerticalAlignment.Top;
        _zoomPanel.Margin = new Thickness(best.X, best.Y, 0, 0);
        _measurementOverlay.ExcludedBounds = new Rect(best, new Size(width, height));
        _measurementOverlay.InvalidateVisual();
    }
}
