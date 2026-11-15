using SnapSharp.Native;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace SnapSharp.Views;

internal sealed class RecordingRegionWindow : Window
{
    private readonly System.Drawing.Rectangle _region;
    private readonly Border _frame;
    private bool _excluded;
    public RecordingRegionWindow(System.Drawing.Rectangle region)
    {
        _region = region;
        Style = (Style)Application.Current.FindResource("AppWindow");
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; IsHitTestVisible = false;
        _frame = new Border { BorderBrush = (Brush)FindResource("ToolbarActiveBrush"), Background = Brushes.Transparent };
        Content = _frame;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, (nint)(style | NativeMethods.WsExTransparent | NativeMethods.WsExToolWindow | 0x08000000L));
            _excluded = ScreenRecorderLib.Recorder.SetExcludeFromCapture(hwnd, true);
            Position();
        };
        Loaded += (_, _) => Position();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Position);
    }
    private void Position()
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source) return;
        // If exclusion is unavailable, keep every border pixel outside the encoded region.
        var bounds = _region; if (!_excluded) bounds.Inflate(3, 3);
        var fromDevice = source.CompositionTarget.TransformFromDevice;
        Width = bounds.Width * fromDevice.M11; Height = bounds.Height * fromDevice.M22;
        _frame.BorderThickness = new Thickness(3 * fromDevice.M11, 3 * fromDevice.M22, 3 * fromDevice.M11, 3 * fromDevice.M22);
        NativeMethods.SetWindowPos(source.Handle, NativeMethods.HwndTopMost, bounds.X, bounds.Y, bounds.Width, bounds.Height, NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }
    public void SetPaused(bool paused) => _frame.BorderBrush = paused ? Brushes.Orange : (Brush)FindResource("ToolbarActiveBrush");
}
