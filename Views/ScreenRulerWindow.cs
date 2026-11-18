using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SnapSharp.Services;
using L = SnapSharp.Localization.UiText;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace SnapSharp.Views;

/// <summary>A movable, resizable desktop ruler. All measurements are physical screen pixels.</summary>
public sealed partial class ScreenRulerWindow : Window
{
    private readonly PixelGuides _guides = new() { ShowRulers = true, InsetOrigin = true, IsHitTestVisible = false };
    private readonly Grid _surface = new() { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
    private readonly TextBlock _readout = new() { Foreground = Brushes.White, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0), IsHitTestVisible = false };

    public ScreenRulerWindow(SettingsService? settings = null)
    {
        _rulerSettings = settings;
        Title = L.Get("ScreenRuler");
        Width = 1000; Height = 540; MinWidth = 520; MinHeight = 400;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; Topmost = true;
        ThemeService.ApplyToWindow(this);
        var layout = new Grid { Background = Brushes.Transparent };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _surface.Children.Add(_guides); Grid.SetRow(_surface, 2); layout.Children.Add(_surface);
        InitializeMeasurementTools(layout);
        var border = new Border { BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(76, 99, 130)), IsHitTestVisible = false };
        layout.Children.Add(border); Grid.SetRowSpan(border, 4);
        foreach (var edge in new[] { "Left", "Right", "Top", "Bottom" })
        {
            var vertical = edge is "Left" or "Right";
            var hitArea = CreateResizeGrip(vertical ? 5 : double.NaN, vertical ? double.NaN : 5, 0, 0, vertical ? Cursors.SizeWE : Cursors.SizeNS);
            hitArea.Name = edge + "ResizeEdge";
            var handle = CreateResizeGrip(vertical ? 10 : 44, vertical ? 44 : 10, vertical ? 2 : 22, vertical ? 22 : 2,
                vertical ? Cursors.SizeWE : Cursors.SizeNS);
            handle.Name = edge + "ResizeGrip";
            foreach (var gripPart in new[] { hitArea, handle })
            {
                gripPart.HorizontalAlignment = edge == "Left" ? System.Windows.HorizontalAlignment.Left : edge == "Right" ? System.Windows.HorizontalAlignment.Right :
                    gripPart == hitArea ? System.Windows.HorizontalAlignment.Stretch : System.Windows.HorizontalAlignment.Center;
                gripPart.VerticalAlignment = edge == "Top" ? VerticalAlignment.Top : edge == "Bottom" ? VerticalAlignment.Bottom :
                    gripPart == hitArea ? VerticalAlignment.Stretch : VerticalAlignment.Center;
                gripPart.DragDelta += (_, e) => ResizeFromEdge(edge, e.HorizontalChange, e.VerticalChange);
                Grid.SetRowSpan(gripPart, 4); layout.Children.Add(gripPart);
            }
        }
        var grip = CreateResizeGrip(18, 18, 8, 8, Cursors.SizeNWSE);
        grip.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        grip.VerticalAlignment = VerticalAlignment.Bottom;
        grip.DragDelta += (_, e) => { Width = Math.Max(MinWidth, ActualWidth + e.HorizontalChange); Height = Math.Max(MinHeight, ActualHeight + e.VerticalChange); };
        Grid.SetRowSpan(grip, 4); layout.Children.Add(grip);
        Content = layout;
        Loaded += (_, _) => UpdateMeasurements();
        LocationChanged += (_, _) => UpdateMeasurements();
        SizeChanged += (_, _) => UpdateMeasurements();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(UpdateMeasurements, System.Windows.Threading.DispatcherPriority.Loaded);
        KeyDown += (_, e) =>
        {
            if (HandleMeasurementKey(e)) return;
            if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
            if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;
            var hwnd = new WindowInteropHelper(this).Handle;
            if (GetWindowRect(hwnd, out var rect))
                SetWindowPos(hwnd, 0, rect.Left + (e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0),
                    rect.Top + (e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0), 0, 0, 0x0015);
            e.Handled = true;
        };
        SourceInitialized += (_, _) =>
        {
            var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
            SetWindowPos(new WindowInteropHelper(this).Handle, 0, screen.Left + 40, screen.Top + 80, 0, 0, 0x0015);
        };
    }

    private void ResizeFromEdge(string edge, double dx, double dy)
    {
        if (edge == "Left")
        {
            var right = Left + ActualWidth;
            var width = Math.Max(MinWidth, ActualWidth - dx);
            Left = right - width; Width = width;
        }
        else if (edge == "Top")
        {
            var bottom = Top + ActualHeight;
            var height = Math.Max(MinHeight, ActualHeight - dy);
            Top = bottom - height; Height = height;
        }
        else if (edge == "Right") Width = Math.Max(MinWidth, ActualWidth + dx);
        else Height = Math.Max(MinHeight, ActualHeight + dy);
    }

    private static Thumb CreateResizeGrip(double width, double height, double markerWidth, double markerHeight, System.Windows.Input.Cursor cursor)
    {
        var background = new FrameworkElementFactory(typeof(Border));
        background.Name = "GripBackground";
        background.SetValue(Border.BackgroundProperty, markerWidth == 0 ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(48, 70, 100)));
        background.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        var marker = new FrameworkElementFactory(typeof(Border));
        marker.SetValue(Border.WidthProperty, markerWidth);
        marker.SetValue(Border.HeightProperty, markerHeight);
        marker.SetValue(Border.BackgroundProperty, Brushes.DeepSkyBlue);
        marker.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        marker.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        marker.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        background.AppendChild(marker);
        var template = new ControlTemplate(typeof(Thumb)) { VisualTree = background };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(75, 123, 188)), "GripBackground"));
        template.Triggers.Add(hover);
        return new Thumb { Width = width, Height = height, Cursor = cursor, Focusable = false,
            ToolTip = L.Get("ScreenRulerResize"), Template = template };
    }

    private void UpdateMeasurements()
    {
        if (!IsLoaded || _surface.ActualWidth <= 0 || _surface.ActualHeight <= 0) return;
        var bounds = _guides.MeasurementBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var origin = _guides.PointToScreen(bounds.TopLeft);
        var end = _guides.PointToScreen(bounds.BottomRight);
        _guides.PixelWidth = (int)Math.Round(end.X - origin.X);
        _guides.PixelHeight = (int)Math.Round(end.Y - origin.Y);
        // Measure from the inset zero; the title bar and scale bands are not measuring space.
        // Keep distances in physical pixels when the ruler crosses monitors with different DPI.
        _guides.OriginX = 0;
        _guides.OriginY = 0;
        var x = Math.Clamp((int)Math.Floor((_guides.Pointer.X - bounds.Left) * _guides.PixelWidth / bounds.Width + 1e-7), 0, _guides.PixelWidth);
        var y = Math.Clamp((int)Math.Floor((_guides.Pointer.Y - bounds.Top) * _guides.PixelHeight / bounds.Height + 1e-7), 0, _guides.PixelHeight);
        _readout.Text = L.Get("ScreenRuler") + "  " + L.Format("ScreenRulerReadout", _guides.PixelWidth, _guides.PixelHeight, x, y);
        _guides.InvalidateVisual();
        RefreshMeasurements(bounds);
        if (_measurements.ActiveTool == RulerTool.Rectangle && _measurements.BoxBounds is { } box)
        {
            var point = _measurements.ReadoutPoint(new Point(x, y));
            _readout.Text = L.Get("DesktopRulerBox") + "  " + L.Format("ScreenRulerReadout", box.Width, box.Height, point.X, point.Y);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
}
