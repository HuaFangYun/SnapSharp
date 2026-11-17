using L = SnapSharp.Localization.UiText;
using SnapSharp.Models;
using SnapSharp.Native;
using System.Drawing;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapSharp.Views;

// Behavioral reference: ShareX region selection workflow, GPL-3.0.
// Independently implemented; no source code was copied.
public partial class SelectionWindow : Window
{
    private readonly CapturedScreen _screen;
    private readonly IReadOnlyList<WindowCandidate> _windows;
    private Point _start;
    private Rect _selection;
    private bool _dragging;

    public event EventHandler<CaptureResult>? CaptureAccepted;
    public event EventHandler? CaptureCanceled;

    public SelectionWindow(CapturedScreen screen, IReadOnlyList<WindowCandidate> windows)
    {
        InitializeComponent();
        _screen = screen;
        _windows = windows;
        Screenshot.Source = screen.Image;
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) =>
        {
            Focus();
            UpdateShade();
            UpdatePointer(new Point(ActualWidth / 2, ActualHeight / 2));
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var source = (HwndSource)PresentationSource.FromVisual(this);
        var transform = source.CompositionTarget.TransformFromDevice;
        Width = _screen.Bounds.Width * transform.M11;
        Height = _screen.Bounds.Height * transform.M22;
        NativeMethods.SetWindowPos(source.Handle, NativeMethods.HwndTopMost,
            _screen.Bounds.Left, _screen.Bounds.Top, _screen.Bounds.Width, _screen.Bounds.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(Root);
        _selection = new Rect(_start, _start);
        _dragging = true;
        Root.CaptureMouse();
        UpdateSelection();
    }

    private void Root_MouseMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(Root);
        if (_dragging)
        {
            _selection = Normalize(_start, Clamp(point));
            UpdateSelection();
        }
        else
        {
            _selection = SuggestedWindowAt(point);
            UpdateSelection();
        }
        UpdatePointer(point);
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Root.ReleaseMouseCapture();
        if (_selection.Width < 3 || _selection.Height < 3)
        {
            _selection = SuggestedWindowAt(e.GetPosition(Root));
        }
        AcceptSelection();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CaptureCanceled?.Invoke(this, EventArgs.Empty);
        }
    }

    private void AcceptSelection()
    {
        if (_selection.Width < 2 || _selection.Height < 2) return;
        var scaleX = _screen.Image.PixelWidth / ActualWidth;
        var scaleY = _screen.Image.PixelHeight / ActualHeight;
        var x = Math.Clamp((int)Math.Round(_selection.X * scaleX), 0, _screen.Image.PixelWidth - 1);
        var y = Math.Clamp((int)Math.Round(_selection.Y * scaleY), 0, _screen.Image.PixelHeight - 1);
        var width = Math.Clamp((int)Math.Round(_selection.Width * scaleX), 1, _screen.Image.PixelWidth - x);
        var height = Math.Clamp((int)Math.Round(_selection.Height * scaleY), 1, _screen.Image.PixelHeight - y);
        var pixels = new Int32Rect(x, y, width, height);
        var cropped = new CroppedBitmap(_screen.Image, pixels);
        cropped.Freeze();
        CaptureAccepted?.Invoke(this, new CaptureResult(cropped, _screen.Bounds,
            new Rectangle(x, y, width, height)));
    }

    private Rect SuggestedWindowAt(Point point)
    {
        var globalX = _screen.Bounds.Left + point.X * _screen.Bounds.Width / Math.Max(1, ActualWidth);
        var globalY = _screen.Bounds.Top + point.Y * _screen.Bounds.Height / Math.Max(1, ActualHeight);
        var candidate = _windows
            .Where(w => w.Bounds.Contains((int)globalX, (int)globalY))
            .OrderBy(w => w.Bounds.Width * w.Bounds.Height)
            .FirstOrDefault();
        if (candidate is null) return Rect.Empty;
        var clipped = Rectangle.Intersect(candidate.Bounds, _screen.Bounds);
        return new Rect(
            (clipped.Left - _screen.Bounds.Left) * ActualWidth / _screen.Bounds.Width,
            (clipped.Top - _screen.Bounds.Top) * ActualHeight / _screen.Bounds.Height,
            clipped.Width * ActualWidth / _screen.Bounds.Width,
            clipped.Height * ActualHeight / _screen.Bounds.Height);
    }

    private void UpdateSelection()
    {
        if (_selection.IsEmpty || _selection.Width <= 0 || _selection.Height <= 0)
        {
            SelectionBorder.Visibility = Visibility.Collapsed;
            InfoText.Text = L.Get("SelectRegion");
        }
        else
        {
            SelectionBorder.Visibility = Visibility.Visible;
            SelectionBorder.Width = _selection.Width;
            SelectionBorder.Height = _selection.Height;
            CanvasPosition(SelectionBorder, _selection.X, _selection.Y);
            var pixelWidth = (int)Math.Round(_selection.Width * _screen.Image.PixelWidth / ActualWidth);
            var pixelHeight = (int)Math.Round(_selection.Height * _screen.Image.PixelHeight / ActualHeight);
            InfoText.Text = $"{pixelWidth} x {pixelHeight}";
            CanvasPosition(InfoPanel, _selection.X, Math.Max(0, _selection.Y - 30));
        }
        UpdateShade();
    }

    private void UpdateShade()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var geometry = new PathGeometry { FillRule = FillRule.EvenOdd };
        geometry.AddGeometry(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        if (!_selection.IsEmpty) geometry.AddGeometry(new RectangleGeometry(_selection));
        Shade.Data = geometry;
    }

    private void UpdatePointer(Point point)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var scaleX = _screen.Image.PixelWidth / ActualWidth;
        var scaleY = _screen.Image.PixelHeight / ActualHeight;
        var px = Math.Clamp((int)(point.X * scaleX), 0, _screen.Image.PixelWidth - 1);
        var py = Math.Clamp((int)(point.Y * scaleY), 0, _screen.Image.PixelHeight - 1);
        var cropSize = 15;
        var left = Math.Clamp(px - cropSize / 2, 0, Math.Max(0, _screen.Image.PixelWidth - cropSize));
        var top = Math.Clamp(py - cropSize / 2, 0, Math.Max(0, _screen.Image.PixelHeight - cropSize));
        var crop = new Int32Rect(left, top, Math.Min(cropSize, _screen.Image.PixelWidth), Math.Min(cropSize, _screen.Image.PixelHeight));
        ZoomImage.Source = new CroppedBitmap(_screen.Image, crop);
        var pixel = new byte[4];
        _screen.Image.CopyPixels(new Int32Rect(px, py, 1, 1), pixel, 4, 0);
        ColorText.Text = $"#{pixel[2]:X2}{pixel[1]:X2}{pixel[0]:X2}  ({px + _screen.Bounds.Left}, {py + _screen.Bounds.Top})";
        var magnifierX = point.X + 22 + Magnifier.Width > ActualWidth ? point.X - Magnifier.Width - 22 : point.X + 22;
        var magnifierY = point.Y + 22 + Magnifier.Height > ActualHeight ? point.Y - Magnifier.Height - 22 : point.Y + 22;
        CanvasPosition(Magnifier, Math.Max(0, magnifierX), Math.Max(0, magnifierY));
    }

    private Point Clamp(Point point) => new(Math.Clamp(point.X, 0, ActualWidth), Math.Clamp(point.Y, 0, ActualHeight));
    private static Rect Normalize(Point a, Point b) => new(new Point(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), new Point(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
    private static void CanvasPosition(FrameworkElement element, double x, double y)
    {
        element.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        element.VerticalAlignment = System.Windows.VerticalAlignment.Top;
        element.Margin = new Thickness(x, y, 0, 0);
    }
}
