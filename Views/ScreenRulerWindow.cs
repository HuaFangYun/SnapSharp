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
public sealed class ScreenRulerWindow : Window
{
    private readonly PixelGuides _guides = new() { ShowRulers = true, IsHitTestVisible = false };
    private readonly Grid _surface = new() { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
    private readonly TextBlock _readout = new() { Foreground = Brushes.White, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0), IsHitTestVisible = false };

    public ScreenRulerWindow()
    {
        Title = L.Get("ScreenRuler");
        Width = 640; Height = 240; MinWidth = 280; MinHeight = 120;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; Topmost = true;
        ThemeService.ApplyToWindow(this);
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        layout.RowDefinitions.Add(new RowDefinition());
        var header = new Grid { Background = new SolidColorBrush(Color.FromRgb(27, 30, 36)), ToolTip = L.Get("ScreenRulerHelp") };
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(_readout);
        var close = new Button { Content = "×", Width = 32, Margin = new Thickness(0), ToolTip = L.Get("Close") };
        close.Click += (_, _) => Close(); Grid.SetColumn(close, 1); header.Children.Add(close);
        header.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is not Button) { DragMove(); e.Handled = true; } };
        layout.Children.Add(header);
        _surface.Children.Add(_guides); Grid.SetRow(_surface, 1); layout.Children.Add(_surface);
        var border = new Border { BorderThickness = new Thickness(1), BorderBrush = Brushes.DodgerBlue, IsHitTestVisible = false };
        layout.Children.Add(border); Grid.SetRowSpan(border, 2);
        var grip = new Thumb { Width = 18, Height = 18, HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE, ToolTip = L.Get("ScreenRulerResize") };
        grip.DragDelta += (_, e) => { Width = Math.Max(MinWidth, ActualWidth + e.HorizontalChange); Height = Math.Max(MinHeight, ActualHeight + e.VerticalChange); };
        Grid.SetRow(grip, 1); layout.Children.Add(grip);
        Content = layout;
        Loaded += (_, _) => UpdateMeasurements();
        LocationChanged += (_, _) => UpdateMeasurements();
        SizeChanged += (_, _) => UpdateMeasurements();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(UpdateMeasurements, System.Windows.Threading.DispatcherPriority.Loaded);
        _surface.MouseMove += (_, e) => { _guides.Pointer = e.GetPosition(_surface); UpdateMeasurements(); };
        KeyDown += (_, e) =>
        {
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

    private void UpdateMeasurements()
    {
        if (!IsLoaded || _surface.ActualWidth <= 0 || _surface.ActualHeight <= 0) return;
        var origin = _surface.PointToScreen(new Point());
        var end = _surface.PointToScreen(new Point(_surface.ActualWidth, _surface.ActualHeight));
        _guides.PixelWidth = (int)Math.Round(end.X - origin.X);
        _guides.PixelHeight = (int)Math.Round(end.Y - origin.Y);
        // Measure from the ruler's own zero, independent of its desktop location.
        // Keep distances in physical pixels when the ruler crosses monitors with different DPI.
        _guides.OriginX = 0;
        _guides.OriginY = 0;
        var x = Math.Clamp((int)Math.Floor(_guides.Pointer.X * _guides.PixelWidth / _surface.ActualWidth + 1e-7), 0, _guides.PixelWidth);
        var y = Math.Clamp((int)Math.Floor(_guides.Pointer.Y * _guides.PixelHeight / _surface.ActualHeight + 1e-7), 0, _guides.PixelHeight);
        _readout.Text = L.Get("ScreenRuler") + "  " + L.Format("ScreenRulerReadout", _guides.PixelWidth, _guides.PixelHeight, x, y);
        _guides.InvalidateVisual();
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
}
