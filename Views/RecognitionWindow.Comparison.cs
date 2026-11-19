using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace SnapSharp.Views;

public partial class RecognitionWindow
{
    private readonly List<BitmapSource?> _cutoutUndo = [];
    private readonly List<BitmapSource?> _watermarkRepairUndo = [];
    private bool _syncingComparison;
    private Point? _comparisonDragStart;
    private Point _comparisonDragOffset;
    private bool IsImageComparison => Mode is "Cutout" or "Watermark";

    private static void SaveResultUndo(List<BitmapSource?> history, BitmapSource? result)
    {
        history.Add(result);
        while (history.Count > 4 || history.Sum(image => image is null ? 0L : (long)image.PixelWidth * image.PixelHeight * 4) > 128_000_000)
            history.RemoveAt(0);
    }

    private void RefreshComparison()
    {
        if (ComparisonImage is null) return;
        var result = Mode == "Watermark" ? _watermarkResult : Mode == "Cutout" ? _cutout : null;
        ComparisonImage.Source = result;
        ComparisonCanvas.Width = _image.PixelWidth;
        ComparisonCanvas.Height = _image.PixelHeight;
        ComparisonPlaceholder.Visibility = result is null ? Visibility.Visible : Visibility.Collapsed;
        RefreshCommonActions();
    }

    private void CutoutUndo_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _cutout is null) return;
        _cutout = PopResultUndo(_cutoutUndo);
        SetPicture(_image);
        RefreshComparison();
    }

    private static BitmapSource? PopResultUndo(List<BitmapSource?> history)
    {
        if (history.Count == 0) return null;
        var result = history[^1]; history.RemoveAt(history.Count - 1);
        return result;
    }

    private void Comparison_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_syncingComparison || !IsImageComparison || ComparisonScroll is null || !ReferenceEquals(e.OriginalSource, sender)) return;
        _syncingComparison = true;
        try
        {
            var source = (ScrollViewer)sender;
            var target = ReferenceEquals(source, PictureScroll) ? ComparisonScroll : PictureScroll;
            target.ScrollToHorizontalOffset(source.HorizontalOffset);
            target.ScrollToVerticalOffset(source.VerticalOffset);
        }
        finally { _syncingComparison = false; }
    }

    private void Comparison_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        EndComparisonDrag(); EndPreviewDrag();
        ZoomPreview(Math.Pow(1.2, e.Delta / 120d), e.GetPosition(ComparisonScroll));
        e.Handled = true;
    }

    private void Comparison_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Middle)) return;
        var point = e.GetPosition(ComparisonScroll);
        if (point.X < 0 || point.Y < 0 || point.X >= ComparisonScroll.ViewportWidth || point.Y >= ComparisonScroll.ViewportHeight) return;
        if (e.ClickCount == 2) { FitImage(); e.Handled = true; return; }
        if (!ComparisonScroll.CaptureMouse()) return;
        _comparisonDragStart = point;
        _comparisonDragOffset = new(ComparisonScroll.HorizontalOffset, ComparisonScroll.VerticalOffset);
        ComparisonScroll.Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void Comparison_MouseMove(object sender, MouseEventArgs e)
    {
        if (_comparisonDragStart is not { } start) return;
        if (e.LeftButton != MouseButtonState.Pressed && e.MiddleButton != MouseButtonState.Pressed) EndComparisonDrag();
        else
        {
            var point = e.GetPosition(ComparisonScroll);
            ComparisonScroll.ScrollToHorizontalOffset(_comparisonDragOffset.X + start.X - point.X);
            ComparisonScroll.ScrollToVerticalOffset(_comparisonDragOffset.Y + start.Y - point.Y);
        }
        e.Handled = true;
    }

    private void Comparison_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_comparisonDragStart is null || e.ChangedButton is not (MouseButton.Left or MouseButton.Middle)) return;
        EndComparisonDrag(); e.Handled = true;
    }
    private void Comparison_LostMouseCapture(object sender, MouseEventArgs e) => EndComparisonDrag();
    private void EndComparisonDrag()
    {
        _comparisonDragStart = null;
        ComparisonScroll.Cursor = null;
        if (ComparisonScroll.IsMouseCaptured) ComparisonScroll.ReleaseMouseCapture();
    }
}
