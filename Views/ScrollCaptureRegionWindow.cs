using L = SnapSharp.Localization.UiText;
using SnapSharp.Native;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Size = System.Windows.Size;

namespace SnapSharp.Views;

/// <summary>A live, non-activating region indicator with capture exclusion and an outside-only fallback.</summary>
internal sealed class ScrollCaptureRegionWindow : Window
{
    private readonly System.Drawing.Rectangle _region;
    private readonly RegionVisual _visual = new();

    public ScrollCaptureRegionWindow(System.Drawing.Rectangle region)
    {
        if (region.Width < 1 || region.Height < 1) throw new ArgumentException(L.Get("ScrollingCaptureRegionCannotBeEmpty"));
        _region = region;
        Style = (Style)Application.Current.FindResource("AppWindow");
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; IsHitTestVisible = false;
        Content = _visual;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
            // WS_EX_NOACTIVATE and WS_EX_TRANSPARENT: wheel/click input belongs to the target app.
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle,
                (nint)(style | NativeMethods.WsExTransparent | NativeMethods.WsExToolWindow | 0x08000000L));
            _visual.Excluded = ScreenRecorderLib.Recorder.SetExcludeFromCapture(hwnd, true);
            Position();
        };
        Loaded += (_, _) => Position();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Position);
    }

    private void Position()
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source) return;
        var scale = source.CompositionTarget.TransformFromDevice;
        var marginX = (int)Math.Ceiling(14 / scale.M11);
        var marginY = (int)Math.Ceiling(14 / scale.M22);
        var bounds = _region;
        bounds.Inflate(marginX, marginY);
        Width = bounds.Width * scale.M11; Height = bounds.Height * scale.M22;
        _visual.Capture = new Rect(marginX * scale.M11, marginY * scale.M22,
            _region.Width * scale.M11, _region.Height * scale.M22);
        _visual.InvalidateVisual();
        NativeMethods.SetWindowPos(source.Handle, NativeMethods.HwndTopMost, bounds.X, bounds.Y,
            bounds.Width, bounds.Height, NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }

    private sealed class RegionVisual : FrameworkElement
    {
        internal Rect Capture { get; set; }
        internal bool Excluded { get; set; }
        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            if (Capture.IsEmpty || Capture.Width <= 0 || Capture.Height <= 0) return;
            var blue = new SolidColorBrush(Color.FromRgb(0x39, 0x8B, 0xFF));
            var pen = new Pen(blue, 2);
            // A 1 DIP clear gap between the stroke and captured pixels also protects GDI fallback
            // when display-affinity exclusion is unavailable. No shade or screenshot covers the centre.
            var outline = Capture;
            if (!Excluded) outline.Inflate(2, 2);
            context.DrawRectangle(null, pen, outline);
            foreach (var point in HandleCenters(Capture, Excluded))
                context.DrawEllipse(Brushes.White, pen, point, 5, 5);
        }
    }

    private static Point[] HandleCenters(Rect capture, bool excluded) => excluded ?
    [
        capture.TopLeft, new(capture.Left + capture.Width / 2, capture.Top), capture.TopRight,
        new(capture.Left, capture.Top + capture.Height / 2), new(capture.Right, capture.Top + capture.Height / 2),
        capture.BottomLeft, new(capture.Left + capture.Width / 2, capture.Bottom), capture.BottomRight
    ] :
    [
        new(capture.Left - 7, capture.Top - 7), new(capture.Left + capture.Width / 2, capture.Top - 7), new(capture.Right + 7, capture.Top - 7),
        new(capture.Left - 7, capture.Top + capture.Height / 2), new(capture.Right + 7, capture.Top + capture.Height / 2),
        new(capture.Left - 7, capture.Bottom + 7), new(capture.Left + capture.Width / 2, capture.Bottom + 7), new(capture.Right + 7, capture.Bottom + 7)
    ];

    internal static bool RunVisualSelfTest()
    {
        // Render at mixed-DPI scales without showing overlays or capturing the user's desktop.
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2, 2.5, 3 })
        foreach (var excluded in new[] { false, true })
        {
            var capture = new Rect(14, 14, 240, 180);
            var visual = new RegionVisual { Capture = capture, Excluded = excluded };
            var size = new Size(268, 208);
            visual.Measure(size); visual.Arrange(new Rect(size)); visual.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(size.Width * scale),
                (int)(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var stride = bitmap.PixelWidth * 4;
            var pixels = new byte[stride * bitmap.PixelHeight]; bitmap.CopyPixels(pixels, stride, 0);
            var clear = capture; if (excluded) clear.Inflate(-7, -7);
            for (var y = (int)Math.Ceiling(clear.Top * scale); y < (int)Math.Floor(clear.Bottom * scale); y++)
            for (var x = (int)Math.Ceiling(clear.Left * scale); x < (int)Math.Floor(clear.Right * scale); x++)
                if (pixels[y * stride + x * 4 + 3] != 0) throw new InvalidOperationException("长截图边框进入采集区域。");
            foreach (var point in HandleCenters(capture, excluded))
            {
                var offset = (int)(point.Y * scale) * stride + (int)(point.X * scale) * 4;
                if (pixels[offset + 3] == 0) throw new InvalidOperationException("长截图定位锚点缺失。");
            }
            var offsetFromEdge = excluded ? 0 : 2;
            foreach (var point in new[] { new Point(capture.Left - offsetFromEdge, 100), new Point(capture.Right + offsetFromEdge, 100),
                new Point(100, capture.Top - offsetFromEdge), new Point(100, capture.Bottom + offsetFromEdge) })
                if (pixels[(int)(point.Y * scale) * stride + (int)(point.X * scale) * 4 + 3] == 0)
                    throw new InvalidOperationException("长截图区域边框缺失。");
        }
        return true;
    }
}
