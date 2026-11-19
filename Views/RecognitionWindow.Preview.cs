using System.Windows;
using System.Windows.Input;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace SnapSharp.Views;

public partial class RecognitionWindow
{
    private Point? _previewDragStart;
    private Point _previewDragOffset;

    private void FitImage()
    {
        if (PictureScroll.ViewportWidth <= 8 || PictureScroll.ViewportHeight <= 8) return;
        EndPreviewDrag();
        // Use the displayed image dimensions, including rotated and cutout previews.
        SetPreviewScale(Math.Min((PictureScroll.ViewportWidth - 8) / PictureCanvas.Width,
            (PictureScroll.ViewportHeight - 8) / PictureCanvas.Height));
        PictureScroll.UpdateLayout();
        PictureScroll.ScrollToHorizontalOffset(0);
        PictureScroll.ScrollToVerticalOffset(0);
    }

    private void SetPreviewScale(double scale)
    {
        // Very large images can still fit; wheel magnification is capped at 1600%.
        scale = Math.Clamp(scale, .0001, 16);
        PictureScale.ScaleX = PictureScale.ScaleY = scale;
        foreach (var highlight in PictureCanvas.Children.OfType<Rectangle>())
            highlight.StrokeThickness = 2 / scale;
    }

    private void ZoomPreview(double factor, Point anchor)
    {
        var previous = PictureScale.ScaleX;
        var imagePoint = new Point((PictureScroll.HorizontalOffset + anchor.X) / previous,
            (PictureScroll.VerticalOffset + anchor.Y) / previous);
        SetPreviewScale(previous * factor);
        PictureScroll.UpdateLayout();
        PictureScroll.ScrollToHorizontalOffset(imagePoint.X * PictureScale.ScaleX - anchor.X);
        PictureScroll.ScrollToVerticalOffset(imagePoint.Y * PictureScale.ScaleY - anchor.Y);
    }

    private void Picture_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        EndPreviewDrag();
        ZoomPreview(Math.Pow(1.2, e.Delta / 120.0), e.GetPosition(PictureScroll));
        e.Handled = true;
    }

    private void Picture_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left && e.ChangedButton != MouseButton.Middle) return;
        var point = e.GetPosition(PictureScroll);
        // Leave the scroll bars and their thumbs to the ScrollViewer.
        if (point.X < 0 || point.Y < 0 || point.X >= PictureScroll.ViewportWidth || point.Y >= PictureScroll.ViewportHeight) return;
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            FitImage();
            e.Handled = true;
            return;
        }
        if (!PictureScroll.CaptureMouse()) return;
        _previewDragStart = point;
        _previewDragOffset = new Point(PictureScroll.HorizontalOffset, PictureScroll.VerticalOffset);
        PictureScroll.Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void PanPreview(Point point)
    {
        if (_previewDragStart is not { } start) return;
        PictureScroll.ScrollToHorizontalOffset(_previewDragOffset.X + start.X - point.X);
        PictureScroll.ScrollToVerticalOffset(_previewDragOffset.Y + start.Y - point.Y);
    }

    private void Picture_MouseMove(object sender, MouseEventArgs e)
    {
        if (_previewDragStart is null) return;
        if (e.LeftButton != MouseButtonState.Pressed && e.MiddleButton != MouseButtonState.Pressed)
            EndPreviewDrag();
        else PanPreview(e.GetPosition(PictureScroll));
        e.Handled = true;
    }

    private void Picture_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_previewDragStart is null || (e.ChangedButton != MouseButton.Left && e.ChangedButton != MouseButton.Middle)) return;
        EndPreviewDrag();
        e.Handled = true;
    }

    private void Picture_LostMouseCapture(object sender, MouseEventArgs e) => EndPreviewDrag();

    private void EndPreviewDrag()
    {
        _previewDragStart = null;
        PictureScroll.Cursor = null;
        if (PictureScroll.IsMouseCaptured) PictureScroll.ReleaseMouseCapture();
    }
}
