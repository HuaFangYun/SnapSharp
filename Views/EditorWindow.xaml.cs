using L = SnapSharp.Localization.UiText;
using SnapSharp.Models;
using SnapSharp.Services;
using SnapSharp.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapSharp.Views;

public partial class EditorWindow : Window
{
    private readonly EditorViewModel _viewModel;
    private readonly Action<BitmapSource> _pinImage;
    private readonly CaptureResult _capture;
    private readonly SkiaImageService _skia = new();
    private readonly IClipboardService _clipboard;
    private bool _syncingProperties;

    public EditorWindow(CaptureResult capture, Action<BitmapSource> pinImage,
        IClipboardService? clipboard = null)
    {
        InitializeComponent();
        _capture = capture;
        _pinImage = pinImage;
        _clipboard = clipboard ?? new ClipboardService();
        _viewModel = new EditorViewModel(capture.Image);
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        DataContext = _viewModel;
        ImageView.Source = capture.Image;
        ImageView.Width = capture.Image.PixelWidth;
        ImageView.Height = capture.Image.PixelHeight;
        AnnotationView.Width = capture.Image.PixelWidth;
        AnnotationView.Height = capture.Image.PixelHeight;
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && Enum.TryParse<AnnotationTool>(element.Tag?.ToString(), out var tool))
        {
            AnnotationView.CommitInlineEdit();
            _viewModel.ActivateTool(tool);
        }
    }

    private void Toolbar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => AnnotationView.CommitInlineEdit();

    private void Thickness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_viewModel is null || _syncingProperties) return;
        _viewModel.StrokeThickness = e.NewValue;
        if (_viewModel.SelectedTool == AnnotationTool.Select) _viewModel.ApplySelectedStyle();
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.ColorDialog { Color = System.Drawing.Color.FromArgb(_viewModel.SelectedColor.R, _viewModel.SelectedColor.G, _viewModel.SelectedColor.B) };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _viewModel.SelectedColor = Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
            if (FillToggle.IsChecked == true)
                _viewModel.FillColor = Color.FromArgb(96, dialog.Color.R, dialog.Color.G, dialog.Color.B);
            if (_viewModel.SelectedTool == AnnotationTool.Select) _viewModel.ApplySelectedStyle();
        }
    }

    private void Fill_Changed(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || _syncingProperties) return;
        _viewModel.FillColor = FillToggle.IsChecked == true
            ? Color.FromArgb(96, _viewModel.SelectedColor.R, _viewModel.SelectedColor.G, _viewModel.SelectedColor.B)
            : null;
        if (_viewModel.SelectedTool == AnnotationTool.Select) _viewModel.ApplySelectedStyle();
    }

    private void FontSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_viewModel is null || _syncingProperties) return;
        _viewModel.FontSize = e.NewValue;
        if (_viewModel.SelectedTool == AnnotationTool.Select) _viewModel.ApplySelectedStyle();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.Image))
        {
            UpdateImageSize(_viewModel.Image);
            return;
        }
        if (e.PropertyName != nameof(EditorViewModel.SelectedAnnotation) || _viewModel.SelectedAnnotation is not { } selected) return;
        _syncingProperties = true;
        try
        {
            ThicknessSlider.Value = selected.Thickness;
            FontSizeSlider.Value = selected switch
            {
                TextAnnotation text => text.FontSize,
                WatermarkAnnotation watermark => watermark.FontSize,
                BubbleAnnotation bubble => bubble.FontSize,
                _ => _viewModel.FontSize
            };
            FillToggle.IsChecked = selected switch
            {
                ShapeAnnotation shape => shape.FillColor is not null,
                PolygonAnnotation polygon => polygon.FillColor is not null,
                BubbleAnnotation => true,
                _ => false
            };
        }
        finally { _syncingProperties = false; }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        _clipboard.SetImage(RenderImage());
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsService();
        settings.Load();
        SaveImageWindow.Open(RenderImage(), _skia, settings, this);
    }

    private void Pin_Click(object sender, RoutedEventArgs e) => _pinImage(RenderImage());
    private void Recognition_Click(object sender, RoutedEventArgs e) => RecognitionUi.Open(RenderImage(), this, ((System.Windows.FrameworkElement)sender).Tag.ToString()!);
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z) _viewModel.UndoCommand.Execute(null);
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y) _viewModel.RedoCommand.Execute(null);
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        ZoomSlider.Value = Math.Clamp(ZoomSlider.Value * (e.Delta > 0 ? 1.1 : 0.9), ZoomSlider.Minimum, ZoomSlider.Maximum);
        e.Handled = true;
    }

    private void Fit_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Image.PixelWidth == 0 || _viewModel.Image.PixelHeight == 0) return;
        var x = Math.Max(0.1, (Scroller.ViewportWidth - 24) / _viewModel.Image.PixelWidth);
        var y = Math.Max(0.1, (Scroller.ViewportHeight - 24) / _viewModel.Image.PixelHeight);
        ZoomSlider.Value = Math.Clamp(Math.Min(x, y), ZoomSlider.Minimum, ZoomSlider.Maximum);
    }

    private async void ScrollCapture_Click(object sender, RoutedEventArgs e)
    {
        CaptureResult? selection;
        try { selection = await CaptureCoordinator.SelectScrollingRegionAsync(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, L.Get("ScrollingCapture")); return; }
        if (selection is null || !IsLoaded) return;
        var region = new System.Drawing.Rectangle(selection.ScreenBounds.Left + selection.PixelRegion.Left,
            selection.ScreenBounds.Top + selection.PixelRegion.Top, selection.PixelRegion.Width, selection.PixelRegion.Height);
        var window = new ScrollCaptureWindow(region, selection.Image);
        window.EditRequested += image => { _viewModel.ReplaceImage(image); UpdateImageSize(image); Activate(); };
        window.OutputRequested += (image, action) =>
        {
            if (action == CaptureAction.Copy) _clipboard.SetImage(image);
            else if (action == CaptureAction.Pin) _pinImage(image);
            else
            {
                var settings = new SettingsService(); settings.Load();
                SaveImageWindow.Open(image, _skia, settings, window);
            }
        };
        window.BeginManualCapture();
    }

    private BitmapSource RenderImage()
    {
        return _skia.Render(_viewModel.Image, _viewModel.Annotations);
    }

    private void UpdateImageSize(BitmapSource image)
    {
        ImageView.Source = image;
        ImageView.Width = image.PixelWidth;
        ImageView.Height = image.PixelHeight;
        AnnotationView.Width = image.PixelWidth;
        AnnotationView.Height = image.PixelHeight;
    }
}
