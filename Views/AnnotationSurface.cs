using SnapSharp.Models;
using SnapSharp.Native;
using SnapSharp.Services;
using SnapSharp.ViewModels;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace SnapSharp.Views;

public sealed class AnnotationSurface : Canvas
{
    private enum ResizeHandle { None, TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left, StartPoint, EndPoint }

    private EditorViewModel? _viewModel;
    private Point _start;
    private Point _current;
    private List<Point>? _points;
    private Annotation? _dragOriginal;
    private Rect _dragBounds;
    private int _vertexIndex = -1;
    private bool _drawing;
    private ResizeHandle _resizeHandle;
    private bool _rotating;
    private bool _movingBubbleTip;
    private BitmapSource? _pixelPreview;
    private readonly SkiaImageService _previewRenderer = new();
    private double _rotationStartAngle;
    private double? _snapX;
    private double? _snapY;
    private DrawingGroup? _annotationCache;
    private WpfTextBox? _inlineEditor;
    private Annotation? _inlineEditTarget;
    private Point _inlineTextPosition;
    private BubbleAnnotation? _pendingBubble;
    private bool _finishingInlineEdit;
    private bool _imeComposing;

    public PixelRect ScreenPixelBounds { get; set; }

    public static double BenchmarkCachedPreview(int frames = 600)
    {
        var cached = new DrawingGroup();
        using (var context = cached.Open())
        {
            for (var row = 0; row < 12; row++)
            for (var column = 0; column < 20; column++)
            {
                var x = 24 + column * 185;
                var y = 24 + row * 170;
                context.DrawRectangle(null, new Pen(Brushes.DodgerBlue, 3), new Rect(x, y, 140, 96));
            }
        }
        cached.Freeze();
        var visual = new DrawingVisual();
        var watch = Stopwatch.StartNew();
        for (var frame = 0; frame < frames; frame++)
        {
            using var context = visual.RenderOpen();
            context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 3840, 2160));
            context.DrawDrawing(cached);
            var x = frame % 3600;
            context.DrawLine(new Pen(Brushes.Red, 4), new Point(x, 1800), new Point(x + 180, 1980));
        }
        watch.Stop();
        return frames / Math.Max(0.001, watch.Elapsed.TotalSeconds);
    }

    public AnnotationSurface()
    {
        Focusable = true;
        Background = Brushes.Transparent;
        ClipToBounds = false;
        Cursor = Cursors.Cross;
        DataContextChanged += OnDataContextChanged;
        KeyDown += OnSurfaceKeyDown;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        CancelInlineEdit();
        if (_viewModel is not null)
        {
            _viewModel.Annotations.CollectionChanged -= OnAnnotationsChanged;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
        _viewModel = e.NewValue as EditorViewModel;
        _annotationCache = null;
        _pixelPreview = null;
        if (_viewModel is not null)
        {
            _viewModel.Annotations.CollectionChanged += OnAnnotationsChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
        UpdateCursor();
        ResetInteraction();
        InvalidateVisual();
    }

    private void OnAnnotationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _annotationCache = null;
        _pixelPreview = null;
        InvalidateVisual();
    }
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.Image)) { _pixelPreview = null; InvalidateVisual(); }
        if (e.PropertyName == nameof(EditorViewModel.SelectedTool)) UpdateCursor();
        if (_inlineEditor?.Visibility == Visibility.Visible)
        {
            if (_inlineEditTarget is not null && e.PropertyName == nameof(EditorViewModel.SelectedAnnotation) &&
                _viewModel?.SelectedAnnotation is TextAnnotation or BubbleAnnotation)
            {
                _inlineEditTarget = _viewModel.SelectedAnnotation;
                SyncInlineEditorStyle();
            }
            if (e.PropertyName is nameof(EditorViewModel.SelectedColor) or nameof(EditorViewModel.FontSize) or
                nameof(EditorViewModel.FontFamilyName) or nameof(EditorViewModel.FillColor) or
                nameof(EditorViewModel.StrokeThickness) or nameof(EditorViewModel.StrokeStyle))
                SyncInlineEditorStyle();
            if (e.PropertyName is nameof(EditorViewModel.TextOutlineColor) or nameof(EditorViewModel.TextOutlineThickness))
                SyncInlineEditorStyle();
        }
        if (e.PropertyName is nameof(EditorViewModel.SelectedAnnotation) or nameof(EditorViewModel.SelectedTool)) InvalidateVisual();
    }

    private void UpdateCursor() => Cursor = _viewModel?.SelectedTool == AnnotationTool.Select
        ? Cursors.Arrow : Cursors.Cross;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_viewModel is null) return;
        if (_inlineEditor?.Visibility == Visibility.Visible && e.OriginalSource is DependencyObject source &&
            IsVisualDescendant(_inlineEditor, source)) return;
        if (_inlineEditor?.Visibility == Visibility.Visible)
        {
            CommitInlineEdit();
            e.Handled = true;
            return;
        }
        Focus();
        _start = _current = GetImagePoint(e);
        var tool = _viewModel.SelectedTool;

        if (tool == AnnotationTool.Select)
        {
            BeginSelectionInteraction(e);
            return;
        }
        if (tool == AnnotationTool.Eraser)
        {
            var hit = HitAnnotation(_start);
            if (hit is not null) _viewModel.Remove(hit);
            e.Handled = true;
            return;
        }
        if (tool == AnnotationTool.Text)
        {
            BeginInlineTextEdit(null, _start);
            e.Handled = true;
            return;
        }
        if (tool == AnnotationTool.Watermark)
        {
            var dialog = new WatermarkDialog { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
                _viewModel.CompleteAnnotation(new WatermarkAnnotation(_start, dialog.Watermark, _viewModel.SelectedColor,
                    dialog.WatermarkFontSize, dialog.WatermarkOpacity, dialog.WatermarkAngle,
                    dialog.IsTiled, ActualWidth, ActualHeight));
            e.Handled = true;
            return;
        }
        if (tool == AnnotationTool.Number)
        {
            var next = Math.Max(1, _viewModel.NextNumber);
            _viewModel.Add(new NumberAnnotation(_start, next, _viewModel.SelectedColor,
                Math.Max(12, _viewModel.StrokeThickness * 3), _viewModel.NumberStyle));
            _viewModel.NextNumber = next + 1;
            if (_viewModel.ContinuousNumbering)
                _viewModel.SelectedAnnotation = null;
            else
                _viewModel.SelectedTool = AnnotationTool.Select;
            e.Handled = true;
            return;
        }
        if (tool == AnnotationTool.Polygon)
        {
            _points ??= [];
            _points.Add(_start);
            if (e.ClickCount >= 2 && _points.Count >= 3) FinishPolygon();
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        _drawing = true;
        _points = tool is AnnotationTool.Pen or AnnotationTool.Highlight or AnnotationTool.Polyline ? [_start] : null;
        CaptureMouse();
        InvalidateVisual();
        e.Handled = true;
    }

    private void BeginSelectionInteraction(MouseButtonEventArgs e)
    {
        if (_viewModel is null) return;
        var selected = _viewModel.SelectedAnnotation;
        var editTarget = e.ClickCount >= 2 ? HitAnnotation(_start) : null;
        if (editTarget is TextAnnotation text)
        {
            SelectAnnotation(text);
            BeginInlineTextEdit(text, text.Position);
            e.Handled = true;
            return;
        }
        if (editTarget is BubbleAnnotation bubble)
        {
            SelectAnnotation(bubble);
            BeginInlineBubbleEdit(bubble);
            e.Handled = true;
            return;
        }
        if (selected is not null)
        {
            if (selected is BubbleAnnotation selectedBubble && (VisualPoint(selectedBubble, selectedBubble.Tip) - _start).Length <= 12)
            {
                _dragOriginal = selected;
                _movingBubbleTip = true;
                _drawing = true;
                CaptureMouse();
                e.Handled = true;
                return;
            }
            var selectedBounds = AnnotationEditing.Bounds(selected);
            var rotationHandle = new Point(selectedBounds.Left + selectedBounds.Width / 2, selectedBounds.Top - 24);
            if ((rotationHandle - _start).Length <= 10)
            {
                _dragOriginal = selected;
                _dragBounds = selectedBounds;
                _rotating = true;
                Cursor = RotationCursor.Value;
                var center = new Point(selectedBounds.Left + selectedBounds.Width / 2, selectedBounds.Top + selectedBounds.Height / 2);
                _rotationStartAngle = Math.Atan2(_start.Y - center.Y, _start.X - center.X) * 180 / Math.PI;
                _drawing = true;
                CaptureMouse();
                e.Handled = true;
                return;
            }
            _resizeHandle = HitResizeHandle(selected, _start);
            if (_resizeHandle != ResizeHandle.None)
            {
                _dragOriginal = selected;
                _dragBounds = selectedBounds;
                _drawing = true;
                CaptureMouse();
                e.Handled = true;
                return;
            }
        }
        selected = HitAnnotation(_start);
        if (selected is null)
        {
            _viewModel.SelectedAnnotation = null;
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        SelectAnnotation(selected);
        if (selected is PolygonAnnotation polygon)
        {
            _vertexIndex = Enumerable.Range(0, polygon.Points.Count)
                .Where(i => (polygon.Points[i] - _start).Length <= 10)
                .DefaultIfEmpty(-1).First();
        }
        _dragOriginal = selected;
        _dragBounds = AnnotationEditing.Bounds(selected);
        _drawing = true;
        CaptureMouse();
        InvalidateVisual();
        e.Handled = true;
    }

    private void SelectAnnotation(Annotation selected)
    {
        if (_viewModel is null) return;
        _viewModel.SelectedColor = selected.Color;
        _viewModel.StrokeThickness = selected is NumberAnnotation numberStyle
            ? Math.Clamp(numberStyle.Radius / 3, 1, 24)
            : selected.Thickness;
        _viewModel.StrokeStyle = selected.StrokeStyle;
        if (selected is ShapeAnnotation { Tool: AnnotationTool.Arrow } selectedArrow)
            _viewModel.ArrowStyle = selectedArrow.ArrowStyle;
        if (selected is ShapeAnnotation shape) _viewModel.FillColor = shape.FillColor;
        else if (selected is PolygonAnnotation polygonStyle) _viewModel.FillColor = polygonStyle.FillColor;
        else if (selected is BubbleAnnotation bubbleStyle)
            _viewModel.FillColor = bubbleStyle.FillColor.A == 0 ? null : bubbleStyle.FillColor;
        if (selected is TextAnnotation textStyle) _viewModel.FontSize = textStyle.FontSize;
        else if (selected is WatermarkAnnotation watermarkStyle) _viewModel.FontSize = watermarkStyle.FontSize;
        else if (selected is BubbleAnnotation bubbleTextStyle) _viewModel.FontSize = bubbleTextStyle.FontSize;
        if (selected is TextAnnotation fontText) _viewModel.FontFamilyName = fontText.FontFamilyName;
        else if (selected is BubbleAnnotation fontBubble) _viewModel.FontFamilyName = fontBubble.FontFamilyName;
        if (selected is TextAnnotation outlinedText)
        {
            _viewModel.TextOutlineColor = outlinedText.TextOutlineColor;
            if (outlinedText.TextOutlineThickness > 0)
                _viewModel.TextOutlineThickness = outlinedText.TextOutlineThickness;
        }
        else if (selected is BubbleAnnotation outlinedBubble)
        {
            _viewModel.TextOutlineColor = outlinedBubble.TextOutlineColor;
            if (outlinedBubble.TextOutlineThickness > 0)
                _viewModel.TextOutlineThickness = outlinedBubble.TextOutlineThickness;
        }
        if (selected is RegionEffectAnnotation effectStyle) _viewModel.EffectStrength = effectStyle.Strength;
        else if (selected is SpotlightAnnotation spotlightStyle) _viewModel.SpotlightDarkness = spotlightStyle.Darkness;
        else if (selected is MagnifierAnnotation magnifierStyle) _viewModel.MagnifierZoom = magnifierStyle.Zoom;
        _viewModel.SelectedAnnotation = selected;
    }

    private WpfTextBox EnsureInlineEditor()
    {
        if (_inlineEditor is not null) return _inlineEditor;
        var editor = new WpfTextBox
        {
            Visibility = Visibility.Collapsed,
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.Wrap,
            Background = new SolidColorBrush(Color.FromArgb(224, 22, 24, 28)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(57, 139, 255)),
            BorderThickness = new Thickness(1.5),
            CaretBrush = Brushes.White,
            SelectionBrush = new SolidColorBrush(Color.FromArgb(150, 57, 139, 255)),
            Padding = new Thickness(5, 2, 5, 2),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden
        };
        SpellCheck.SetIsEnabled(editor, false);
        editor.PreviewKeyDown += InlineEditor_PreviewKeyDown;
        editor.TextChanged += InlineEditor_TextChanged;
        TextCompositionManager.AddPreviewTextInputStartHandler(editor, (_, _) => _imeComposing = true);
        TextCompositionManager.AddPreviewTextInputUpdateHandler(editor, (_, _) => _imeComposing = true);
        TextCompositionManager.AddPreviewTextInputHandler(editor, (_, _) => _imeComposing = false);
        Children.Add(editor);
        System.Windows.Controls.Panel.SetZIndex(editor, 1000);
        _inlineEditor = editor;
        return editor;
    }

    private void BeginInlineTextEdit(TextAnnotation? annotation, Point position)
    {
        var editor = EnsureInlineEditor();
        _inlineEditTarget = annotation;
        _pendingBubble = null;
        _inlineTextPosition = position;
        editor.Text = annotation?.Text ?? string.Empty;
        editor.Background = new SolidColorBrush(Color.FromArgb(224, 22, 24, 28));
        editor.RenderTransformOrigin = new Point(0.5, 0.5);
        editor.RenderTransform = new RotateTransform(annotation?.Rotation ?? 0);
        SetLeft(editor, position.X);
        SetTop(editor, position.Y);
        SyncInlineEditorStyle();
        ResizeInlineTextEditor();
        ShowInlineEditor(selectAll: annotation is not null);
    }

    private void BeginInlineBubbleEdit(BubbleAnnotation bubble, bool isNew = false)
    {
        var editor = EnsureInlineEditor();
        _inlineEditTarget = isNew ? null : bubble;
        if (isNew) _pendingBubble = bubble;
        else _pendingBubble = null;
        _inlineTextPosition = new Point(bubble.Region.Left + 8, bubble.Region.Top + 6);
        editor.Text = bubble.Text;
        editor.Background = Brushes.Transparent;
        editor.RenderTransformOrigin = new Point(0.5, 0.5);
        editor.RenderTransform = new RotateTransform(bubble.Rotation);
        editor.Width = Math.Max(24, bubble.Region.Width - 16);
        editor.Height = Math.Max(22, bubble.Region.Height - 12);
        SetLeft(editor, _inlineTextPosition.X);
        SetTop(editor, _inlineTextPosition.Y);
        SyncInlineEditorStyle();
        ShowInlineEditor(selectAll: !isNew);
    }

    private void ShowInlineEditor(bool selectAll)
    {
        if (_inlineEditor is null) return;
        _inlineEditor.Visibility = Visibility.Visible;
        InvalidateVisual();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_inlineEditor?.Visibility != Visibility.Visible) return;
            _inlineEditor.Focus();
            if (selectAll) _inlineEditor.SelectAll();
            else _inlineEditor.CaretIndex = _inlineEditor.Text.Length;
        });
    }

    private void SyncInlineEditorStyle()
    {
        if (_inlineEditor is null || _viewModel is null) return;
        var color = _inlineEditTarget?.Color ?? _viewModel.SelectedColor;
        var fontSize = _inlineEditTarget switch
        {
            TextAnnotation text => text.FontSize,
            BubbleAnnotation bubble => bubble.FontSize,
            _ => _viewModel.FontSize
        };
        var fontFamilyName = _inlineEditTarget switch
        {
            TextAnnotation text => text.FontFamilyName,
            BubbleAnnotation bubble => bubble.FontFamilyName,
            _ => _viewModel.FontFamilyName
        };
        _inlineEditor.Foreground = new SolidColorBrush(color);
        _inlineEditor.FontFamily = new System.Windows.Media.FontFamily(fontFamilyName);
        _inlineEditor.FontSize = Math.Max(4, fontSize);
        var outlineColor = _inlineEditTarget switch
        {
            TextAnnotation text => text.TextOutlineColor,
            BubbleAnnotation bubble => bubble.TextOutlineColor,
            _ => _viewModel.TextOutlineColor
        };
        var outlineThickness = _inlineEditTarget switch
        {
            TextAnnotation text => text.TextOutlineThickness,
            BubbleAnnotation bubble => bubble.TextOutlineThickness,
            _ => _viewModel.TextOutlineThickness
        };
        _inlineEditor.Effect = outlineColor is Color activeOutline && outlineThickness > 0
            ? new DropShadowEffect
            {
                Color = activeOutline, ShadowDepth = 0,
                BlurRadius = Math.Max(1, outlineThickness * 1.6), Opacity = 1
            }
            : null;

        if (_pendingBubble is not null)
        {
            var pending = _pendingBubble;
            _pendingBubble = new BubbleAnnotation(pending.Region, pending.Tip, pending.Text,
                _viewModel.SelectedColor, _viewModel.StrokeThickness,
                _viewModel.FillColor ?? Colors.Transparent, _viewModel.FontSize,
                _viewModel.TextOutlineColor, _viewModel.TextOutlineThickness)
            {
                Rotation = pending.Rotation,
                StrokeStyle = _viewModel.StrokeStyle,
                FontFamilyName = _viewModel.FontFamilyName
            };
        }
        if (_inlineEditTarget is TextAnnotation || _inlineEditTarget is null && _pendingBubble is null)
        {
            ResizeInlineTextEditor();
            _ = Dispatcher.InvokeAsync(ResizeInlineTextEditor, DispatcherPriority.Render);
        }
        InvalidateVisual();
    }

    private void InlineEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_inlineEditTarget is BubbleAnnotation || _pendingBubble is not null) return;
        ResizeInlineTextEditor();
    }

    private void ResizeInlineTextEditor()
    {
        if (_inlineEditor is null) return;
#pragma warning disable CS0618
        var formatted = new FormattedText(_inlineEditor.Text.Length == 0 ? " " : _inlineEditor.Text,
            System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(_inlineEditor.FontFamily, _inlineEditor.FontStyle, _inlineEditor.FontWeight,
                _inlineEditor.FontStretch), _inlineEditor.FontSize, Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
#pragma warning restore CS0618
        var maxWidth = Math.Max(80, ActualWidth - _inlineTextPosition.X - 8);
        var extraWidth = _inlineEditor.Padding.Left + _inlineEditor.Padding.Right +
            _inlineEditor.BorderThickness.Left + _inlineEditor.BorderThickness.Right + 6;
        var extraHeight = _inlineEditor.Padding.Top + _inlineEditor.Padding.Bottom +
            _inlineEditor.BorderThickness.Top + _inlineEditor.BorderThickness.Bottom + 6;
        _inlineEditor.Width = Math.Clamp(formatted.WidthIncludingTrailingWhitespace + extraWidth, 80, maxWidth);
        _inlineEditor.Height = Math.Clamp(formatted.Height + extraHeight,
            _inlineEditor.FontSize + extraHeight, Math.Max(_inlineEditor.FontSize + extraHeight,
                ActualHeight - _inlineTextPosition.Y - 8));
        _inlineEditor.InvalidateMeasure();
        _inlineEditor.InvalidateArrange();
    }

    private void InlineEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelInlineEdit();
            Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !_imeComposing && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            CommitInlineEdit();
            Focus();
            e.Handled = true;
        }
    }

    public bool CommitInlineEdit()
    {
        if (_finishingInlineEdit || _inlineEditor?.Visibility != Visibility.Visible || _viewModel is null) return false;
        _finishingInlineEdit = true;
        try
        {
            var text = _inlineEditor.Text.Trim();
            var target = _inlineEditTarget;
            var pendingBubble = _pendingBubble;
            var textPosition = _inlineTextPosition;
            CloseInlineEditor();
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (target is TextAnnotation existingText)
                _viewModel.Replace(existingText, existingText with { Text = text });
            else if (target is BubbleAnnotation existingBubble)
                _viewModel.Replace(existingBubble, existingBubble with { Text = text });
            else if (pendingBubble is not null)
                _viewModel.CompleteAnnotation(pendingBubble with { Text = text });
            else
                _viewModel.CompleteAnnotation(new TextAnnotation(textPosition, text,
                    _viewModel.SelectedColor, _viewModel.FontSize,
                    _viewModel.TextOutlineColor, _viewModel.TextOutlineThickness)
                    { FontFamilyName = _viewModel.FontFamilyName });
            return true;
        }
        finally
        {
            _finishingInlineEdit = false;
            InvalidateVisual();
        }
    }

    public void CancelInlineEdit()
    {
        if (_inlineEditor?.Visibility != Visibility.Visible) return;
        CloseInlineEditor();
        InvalidateVisual();
    }

    private void CloseInlineEditor()
    {
        if (_inlineEditor is not null)
        {
            _inlineEditor.Visibility = Visibility.Collapsed;
            _inlineEditor.Text = string.Empty;
            _inlineEditor.RenderTransform = Transform.Identity;
        }
        _inlineEditTarget = null;
        _pendingBubble = null;
        _imeComposing = false;
    }

    private static bool IsVisualDescendant(DependencyObject ancestor, DependencyObject element)
    {
        for (var current = element; current is not null;
             current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }

    internal bool AllowsWindowDoubleClickCopy(Point point)
    {
        if (_viewModel is null || _inlineEditor?.Visibility == Visibility.Visible) return false;
        return _viewModel.SelectedTool != AnnotationTool.Select ||
               HitAnnotation(point) is not (TextAnnotation or BubbleAnnotation);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        _current = GetImagePoint(e);
        if (_viewModel?.SelectedTool == AnnotationTool.Polygon && _points is { Count: > 0 } && !_drawing)
        {
            InvalidateVisual();
            return;
        }
        if (!_drawing)
        {
            UpdateSelectionCursor();
            return;
        }
        if (_points is { } points && (points.Count == 0 || (points[^1] - _current).Length >= 1.5)) points.Add(_current);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_drawing || _viewModel is null) return;
        // Keep the last rendered pointer for object transforms. Mouse-up often
        // carries a final 1-2 px hand jitter that was never shown to the user.
        if (_viewModel.SelectedTool != AnnotationTool.Select)
            _current = GetImagePoint(e);
        ReleaseMouseCapture();
        _drawing = false;
        if (_viewModel.SelectedTool == AnnotationTool.Select)
        {
            FinishSelectionInteraction();
            e.Handled = true;
            return;
        }
        if (_viewModel.SelectedTool == AnnotationTool.Bubble)
        {
            var bubbleRegion = CreateBubbleRegion(_start, _current);
            _pendingBubble = new BubbleAnnotation(bubbleRegion, CreateBubbleTip(bubbleRegion), string.Empty,
                _viewModel.SelectedColor, _viewModel.StrokeThickness,
                _viewModel.FillColor ?? Colors.Transparent, _viewModel.FontSize,
                _viewModel.TextOutlineColor, _viewModel.TextOutlineThickness)
            { StrokeStyle = _viewModel.StrokeStyle, FontFamilyName = _viewModel.FontFamilyName };
            BeginInlineBubbleEdit(_pendingBubble, isNew: true);
            _points = null;
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        var region = Normalize(_start, _current);
        Annotation? annotation = _viewModel.SelectedTool switch
        {
            AnnotationTool.Pen or AnnotationTool.Polyline when _points?.Count > 1 =>
                new PenAnnotation(_points.ToArray(), _viewModel.SelectedColor, _viewModel.StrokeThickness) { StrokeStyle = _viewModel.StrokeStyle },
            AnnotationTool.Highlight when _points?.Count > 1 =>
                new PenAnnotation(_points.ToArray(), _viewModel.SelectedColor, Math.Max(10, _viewModel.StrokeThickness * 3), true) { StrokeStyle = _viewModel.StrokeStyle },
            AnnotationTool.Rectangle or AnnotationTool.Ellipse or AnnotationTool.Line or AnnotationTool.Arrow =>
                new ShapeAnnotation(_viewModel.SelectedTool, _start, _current, _viewModel.SelectedColor,
                    _viewModel.StrokeThickness, _viewModel.FillColor)
                { StrokeStyle = _viewModel.StrokeStyle, ArrowStyle = _viewModel.ArrowStyle },
            AnnotationTool.Mosaic or AnnotationTool.Blur =>
                new RegionEffectAnnotation(_viewModel.SelectedTool, region, _viewModel.EffectStrength),
            AnnotationTool.Spotlight => new SpotlightAnnotation(region, _viewModel.SpotlightDarkness, ActualWidth, ActualHeight),
            AnnotationTool.Magnifier => CreateMagnifier(_start, _current),
            _ => null
        };
        if (annotation is not null && IsValidAnnotation(annotation))
            _viewModel.CompleteAnnotation(annotation);
        _points = null;
        InvalidateVisual();
        e.Handled = true;
    }

    private void FinishSelectionInteraction()
    {
        if (_viewModel is null || _dragOriginal is null) return;
        Annotation updated;
        if (_movingBubbleTip && _dragOriginal is BubbleAnnotation bubble) updated = MoveBubbleTip(bubble, _current);
        else if (_vertexIndex >= 0 && _dragOriginal is PolygonAnnotation polygon)
        {
            var points = polygon.Points.ToArray();
            points[_vertexIndex] = _current;
            updated = polygon with { Points = points };
        }
        else if (_resizeHandle != ResizeHandle.None)
            updated = ResizeSelectedAnnotation();
        else if (_rotating) updated = RotationPreview(_dragOriginal);
        else updated = AnnotationEditing.Move(_dragOriginal, SnappedMoveDelta(_dragOriginal, _current - _start));
        _viewModel.Replace(_dragOriginal, updated);
        ResetDrag();
    }

    private void FinishPolygon()
    {
        if (_viewModel is null || _points is not { Count: >= 3 }) return;
        var points = _points.ToArray();
        if (points.Length > 3 && (points[^1] - points[^2]).Length < 2) points = points[..^1];
        _viewModel.CompleteAnnotation(new PolygonAnnotation(points, _viewModel.SelectedColor,
            _viewModel.StrokeThickness, _viewModel.FillColor) { StrokeStyle = _viewModel.StrokeStyle });
        _points = null;
        InvalidateVisual();
    }

    private void OnSurfaceKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel is null) return;
        if (_viewModel.SelectedTool == AnnotationTool.Select &&
            (e.Key is Key.F2 or Key.Enter) && _viewModel.SelectedAnnotation is TextAnnotation selectedText)
        {
            BeginInlineTextEdit(selectedText, selectedText.Position);
            e.Handled = true;
            return;
        }
        if (_viewModel.SelectedTool == AnnotationTool.Select &&
            (e.Key is Key.F2 or Key.Enter) && _viewModel.SelectedAnnotation is BubbleAnnotation selectedBubble)
        {
            BeginInlineBubbleEdit(selectedBubble);
            e.Handled = true;
            return;
        }
        if (_viewModel.SelectedTool == AnnotationTool.Polygon && e.Key == Key.Enter)
        {
            FinishPolygon(); e.Handled = true; return;
        }
        if (_viewModel.SelectedTool == AnnotationTool.Polygon && e.Key == Key.Escape && _points is { Count: > 0 })
        {
            _points.RemoveAt(_points.Count - 1); InvalidateVisual(); e.Handled = true; return;
        }
        if (_viewModel.SelectedTool == AnnotationTool.Select && e.Key == Key.Delete && _viewModel.SelectedAnnotation is { } selected)
        {
            _viewModel.Remove(selected); e.Handled = true;
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (_viewModel is null) return;
        var draft = _drawing && _dragOriginal is null ? CreatePreviewAnnotation() : null;
        if (_viewModel.Annotations.Any(IsPixelEffect) || draft is not null && IsPixelEffect(draft))
        {
            var annotations = _viewModel.Annotations.ToList();
            if (_inlineEditor?.Visibility == Visibility.Visible && _inlineEditTarget is not null)
            {
                annotations.Remove(_inlineEditTarget);
                if (_inlineEditTarget is BubbleAnnotation editingBubble) annotations.Add(editingBubble with { Text = string.Empty });
            }
            if (_pendingBubble is not null) annotations.Add(_pendingBubble);
            if (_drawing && _dragOriginal is not null)
            {
                var index = annotations.IndexOf(_dragOriginal);
                if (index >= 0) annotations[index] = SelectedTransformPreview();
            }
            if (draft is not null) annotations.Add(draft);
            var image = !_drawing && _inlineEditor?.Visibility != Visibility.Visible
                ? _pixelPreview ??= _previewRenderer.Render(_viewModel.Image, annotations)
                : _previewRenderer.Render(_viewModel.Image, annotations);
            drawingContext.DrawImage(image, new Rect(0, 0, _viewModel.Image.PixelWidth, _viewModel.Image.PixelHeight));
            if (_drawing && _dragOriginal is not null) DrawSelection(drawingContext, SelectedTransformPreview());
            else if (_viewModel.SelectedAnnotation is { } selectedEffect) DrawSelection(drawingContext, selectedEffect);
            foreach (var lens in annotations.OfType<MagnifierAnnotation>())
            {
                drawingContext.DrawLine(new Pen(Brushes.White, 1), lens.Source - new Vector(5, 0), lens.Source + new Vector(5, 0));
                drawingContext.DrawLine(new Pen(Brushes.White, 1), lens.Source - new Vector(0, 5), lens.Source + new Vector(0, 5));
            }
            if (_viewModel.SelectedTool == AnnotationTool.Polygon && _points is { Count: > 0 })
                new PenAnnotation(_points.Append(_current).ToArray(), _viewModel.SelectedColor, _viewModel.StrokeThickness).Draw(drawingContext);
            DrawSnapGuides(drawingContext);
            return;
        }

        if (_inlineEditor?.Visibility == Visibility.Visible && _inlineEditTarget is not null)
        {
            foreach (var annotation in _viewModel.Annotations)
            {
                if (!Equals(annotation, _inlineEditTarget)) DrawAnnotation(drawingContext, annotation);
                else if (annotation is BubbleAnnotation bubble) DrawAnnotation(drawingContext, bubble with { Text = string.Empty });
            }
        }
        else if (_drawing && _dragOriginal is not null)
        {
            foreach (var annotation in _viewModel.Annotations)
            {
                if (Equals(annotation, _dragOriginal)) continue;
                DrawAnnotation(drawingContext, annotation);
            }
        }
        else drawingContext.DrawDrawing(GetAnnotationCache());
        if (_pendingBubble is not null) DrawAnnotation(drawingContext, _pendingBubble);
        if (_drawing && _dragOriginal is not null) PreviewSelectedTransform(drawingContext);
        else if (_viewModel.SelectedAnnotation is { } selected) DrawSelection(drawingContext, selected);
        if (_viewModel.SelectedTool == AnnotationTool.Polygon && _points is { Count: > 0 })
        {
            var preview = _points.Append(_current).ToArray();
            new PenAnnotation(preview, _viewModel.SelectedColor, _viewModel.StrokeThickness) { StrokeStyle = _viewModel.StrokeStyle }.Draw(drawingContext);
            foreach (var point in _points) drawingContext.DrawEllipse(Brushes.White,
                new Pen(new SolidColorBrush(_viewModel.SelectedColor), 1), point, 4, 4);
        }
        DrawSnapGuides(drawingContext);
        if (!_drawing || _dragOriginal is not null) return;
        if (draft is not null) DrawAnnotation(drawingContext, draft);
    }

    private static bool IsPixelEffect(Annotation annotation) => annotation is RegionEffectAnnotation or MagnifierAnnotation;

    internal static bool RunEffectInteractionSelfTest()
    {
        void Stage(string name) => File.WriteAllText(Path.Combine(AppPaths.DiagnosticsRoot, "self-test.txt"), "RUNNING: " + name);
        Stage("effect-preview-start");
        const int width = 128, height = 96;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var index = (y * width + x) * 4;
            pixels[index] = (byte)((x * 37 + y * 17) % 256);
            pixels[index + 1] = (byte)((x * 13 + y * 47) % 256);
            pixels[index + 2] = (byte)((x * 7 + y * 29) % 256);
            pixels[index + 3] = 255;
        }
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        var renderer = new SkiaImageService();
        foreach (var annotation in new Annotation[]
        {
            new RegionEffectAnnotation(AnnotationTool.Mosaic, new Rect(16, 16, 64, 48), 8),
            new RegionEffectAnnotation(AnnotationTool.Blur, new Rect(16, 16, 64, 48), 8),
            new MagnifierAnnotation(new Point(20, 20), new Rect(60, 30, 60, 50), 3)
        })
        {
            Stage("effect-preview-" + annotation.GetType().Name + "-" + (annotation is RegionEffectAnnotation effect ? effect.Tool : AnnotationTool.Magnifier));
            var model = new EditorViewModel(source);
            model.Annotations.Add(annotation);
            var surface = new AnnotationSurface { DataContext = model, Width = width, Height = height };
            surface.Measure(new System.Windows.Size(width, height));
            surface.Arrange(new Rect(0, 0, width, height));
            var actual = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            actual.Render(surface);
            var actualPixels = new byte[pixels.Length]; actual.CopyPixels(actualPixels, width * 4, 0);
            var expectedPixels = new byte[pixels.Length];
            var expected = new FormatConvertedBitmap(renderer.Render(source, [annotation]), PixelFormats.Pbgra32, null, 0);
            expected.CopyPixels(expectedPixels, width * 4, 0);
            var changed = false;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if (annotation is MagnifierAnnotation lens && Math.Abs(x - lens.Source.X) < 7 && Math.Abs(y - lens.Source.Y) < 7) continue;
                var index = (y * width + x) * 4;
                for (var channel = 0; channel < 4; channel++)
                {
                    if (Math.Abs(actualPixels[index + channel] - expectedPixels[index + channel]) > 2) return false;
                    changed |= expectedPixels[index + channel] != pixels[index + channel];
                }
            }
            if (!changed) return false;
            model.Annotations.Clear();
            surface.DataContext = null;
        }
        foreach (var angle in new[] { 0d, 37d, 120d })
        foreach (var target in new[] { new Point(10, 40), new Point(100, 40), new Point(50, 5), new Point(50, 90) })
        {
            Stage("bubble-tail");
            var original = new BubbleAnnotation(new Rect(25, 20, 50, 40), new Point(50, 80), "test", Colors.Red, 2, Colors.White, 12) { Rotation = angle };
            var changed = MoveBubbleTip(original, target);
            if ((VisualPoint(changed, changed.Tip) - target).Length > 0.01 ||
                (VisualPoint(changed, changed.Region.TopLeft) - VisualPoint(original, original.Region.TopLeft)).Length > 0.01) return false;
        }
        Stage("rotation-cursor");
        _ = RotationCursor.Value;
        foreach (var style in Enum.GetValues<AnnotationNumberStyle>())
        foreach (var number in new[] { 1, 12, 9999 })
        {
            var annotation = new NumberAnnotation(new Point(40, 40), number, Colors.Red, 24, style);
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen()) annotation.Draw(context);
            var image = new RenderTargetBitmap(80, 80, 96, 96, PixelFormats.Pbgra32); image.Render(visual);
            var glyphs = new byte[80 * 80 * 4]; image.CopyPixels(glyphs, 80 * 4, 0);
            if (!Centered(glyphs)) return false;
            using var bitmap = new SkiaSharp.SKBitmap(80, 80);
            using var canvas = new SkiaSharp.SKCanvas(bitmap); canvas.Clear(SkiaSharp.SKColors.Transparent);
            annotation.DrawSkia(canvas);
            renderer.ToBitmapSource(bitmap).CopyPixels(glyphs, 80 * 4, 0);
            if (!Centered(glyphs)) return false;
        }
        Stage("effect-preview-complete");
        return true;

        static bool Centered(byte[] glyphs)
        {
            var left = 80; var top = 80; var right = -1; var bottom = -1;
            for (var y = 0; y < 80; y++) for (var x = 0; x < 80; x++)
            {
                var i = (y * 80 + x) * 4;
                if (glyphs[i] < 230 || glyphs[i + 1] < 230 || glyphs[i + 2] < 230 || glyphs[i + 3] < 230) continue;
                left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
            }
            return right >= left && Math.Abs((left + right + 1) / 2d - 40) <= 1.1 &&
                Math.Abs((top + bottom + 1) / 2d - 40) <= 1.1 && Math.Max(right - left, bottom - top) <= 38;
        }
    }

    private Annotation? CreatePreviewAnnotation()
    {
        if (_viewModel is null) return null;
        var region = Normalize(_start, _current);
        return _viewModel.SelectedTool switch
        {
            AnnotationTool.Pen or AnnotationTool.Polyline when _points?.Count > 1 => new PenAnnotation(_points, _viewModel.SelectedColor, _viewModel.StrokeThickness) { StrokeStyle = _viewModel.StrokeStyle },
            AnnotationTool.Highlight when _points?.Count > 1 => new PenAnnotation(_points, _viewModel.SelectedColor, Math.Max(10, _viewModel.StrokeThickness * 3), true) { StrokeStyle = _viewModel.StrokeStyle },
            AnnotationTool.Mosaic or AnnotationTool.Blur => new RegionEffectAnnotation(_viewModel.SelectedTool, region, _viewModel.EffectStrength),
            AnnotationTool.Spotlight => new SpotlightAnnotation(region, _viewModel.SpotlightDarkness, ActualWidth, ActualHeight),
            AnnotationTool.Bubble => new BubbleAnnotation(CreateBubbleRegion(_start, _current),
                CreateBubbleTip(CreateBubbleRegion(_start, _current)), string.Empty, _viewModel.SelectedColor,
                _viewModel.StrokeThickness, _viewModel.FillColor ?? Colors.Transparent, _viewModel.FontSize,
                _viewModel.TextOutlineColor, _viewModel.TextOutlineThickness)
                { StrokeStyle = _viewModel.StrokeStyle, FontFamilyName = _viewModel.FontFamilyName },
            AnnotationTool.Magnifier => CreateMagnifier(_start, _current),
            _ => new ShapeAnnotation(_viewModel.SelectedTool, _start, _current, _viewModel.SelectedColor,
                _viewModel.StrokeThickness, _viewModel.FillColor)
            { StrokeStyle = _viewModel.StrokeStyle, ArrowStyle = _viewModel.ArrowStyle }
        };
    }

    private void PreviewSelectedTransform(DrawingContext context)
    {
        if (_dragOriginal is null) return;
        var preview = SelectedTransformPreview();
        DrawAnnotation(context, preview);
        DrawSelection(context, preview);
    }

    private Annotation SelectedTransformPreview()
    {
        if (_dragOriginal is null) throw new InvalidOperationException();
        Annotation preview;
        if (_movingBubbleTip && _dragOriginal is BubbleAnnotation bubble) preview = MoveBubbleTip(bubble, _current);
        else if (_vertexIndex >= 0 && _dragOriginal is PolygonAnnotation polygon)
        {
            var points = polygon.Points.ToArray(); points[_vertexIndex] = _current; preview = polygon with { Points = points };
        }
        else if (_resizeHandle != ResizeHandle.None) preview = ResizeSelectedAnnotation();
        else if (_rotating) preview = RotationPreview(_dragOriginal);
        else preview = AnnotationEditing.Move(_dragOriginal, SnappedMoveDelta(_dragOriginal, _current - _start));
        return preview;
    }

    private static void DrawSelection(DrawingContext context, Annotation annotation)
    {
        var bounds = AnnotationEditing.Bounds(annotation);
        bounds.Inflate(4, 4);
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(57, 139, 255)), 1) { DashStyle = DashStyles.Dash };
        context.DrawRectangle(null, pen, bounds);
        var handlePen = new Pen(new SolidColorBrush(Color.FromRgb(57, 139, 255)), 1);
        if (annotation is BubbleAnnotation bubble)
            context.DrawEllipse(Brushes.White, handlePen, VisualPoint(bubble, bubble.Tip), 6, 6);
        if (annotation is ShapeAnnotation { Tool: AnnotationTool.Line or AnnotationTool.Arrow } line)
        {
            foreach (var point in new[] { VisualPoint(line, line.Start), VisualPoint(line, line.End) })
                context.DrawEllipse(Brushes.White, handlePen, point, 5, 5);
        }
        else
        {
            foreach (var (_, point) in VisibleResizeHandles(bounds))
                context.DrawRectangle(Brushes.White, handlePen, new Rect(point.X - 4, point.Y - 4, 8, 8));
        }
        var rotationPoint = new Point(bounds.Left + bounds.Width / 2, bounds.Top - 20);
        context.DrawLine(handlePen, new Point(rotationPoint.X, bounds.Top), rotationPoint);
        context.DrawEllipse(Brushes.White, handlePen, rotationPoint, 5, 5);
#pragma warning disable CS0618
        var angleText = new FormattedText($"{annotation.Rotation:0.#}°",
            System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 11, new SolidColorBrush(Color.FromRgb(57, 139, 255)));
#pragma warning restore CS0618
        context.DrawText(angleText, new Point(rotationPoint.X + 8, rotationPoint.Y - angleText.Height / 2));
        if (annotation is PolygonAnnotation polygon)
            foreach (var point in polygon.Points) context.DrawEllipse(Brushes.White,
                new Pen(new SolidColorBrush(Color.FromRgb(57, 139, 255)), 1), point, 4, 4);
    }

    private Annotation? HitAnnotation(Point point) => _viewModel?.Annotations.Reverse()
        .FirstOrDefault(annotation => AnnotationEditing.HitTest(annotation, point));

    private MagnifierAnnotation CreateMagnifier(Point source, Point destinationCenter)
    {
        var distance = Math.Max(60, Math.Min(220, (destinationCenter - source).Length));
        var width = Math.Min(distance, Math.Max(1, ActualWidth));
        var height = Math.Min(distance * 0.72, Math.Max(1, ActualHeight));
        var x = Math.Clamp(destinationCenter.X - width / 2, 0, Math.Max(0, ActualWidth - width));
        var y = Math.Clamp(destinationCenter.Y - height / 2, 0, Math.Max(0, ActualHeight - height));
        var zoom = _viewModel?.MagnifierZoom ?? 3;
        var halfWidth = width / zoom / 2;
        var halfHeight = height / zoom / 2;
        source = new Point(Math.Clamp(source.X, halfWidth, ActualWidth - halfWidth),
            Math.Clamp(source.Y, halfHeight, ActualHeight - halfHeight));
        return new MagnifierAnnotation(source, new Rect(x, y, width, height), zoom);
    }

    private Rect CreateBubbleRegion(Point start, Point end)
    {
        var region = Normalize(start, end);
        const double minimumWidth = 96;
        const double minimumHeight = 52;
        if (region.Width >= minimumWidth && region.Height >= minimumHeight) return region;

        var width = Math.Max(minimumWidth, region.Width);
        var height = Math.Max(minimumHeight, region.Height);
        var x = Math.Clamp(start.X, 0, Math.Max(0, ActualWidth - width));
        var y = Math.Clamp(start.Y, 0, Math.Max(0, ActualHeight - height));
        return new Rect(x, y, Math.Min(width, ActualWidth), Math.Min(height, ActualHeight));
    }

    private Point CreateBubbleTip(Rect region)
    {
        const double tailLength = 24;
        var centerX = region.Left + region.Width / 2;
        if (region.Bottom + tailLength <= ActualHeight)
            return new Point(centerX, region.Bottom + tailLength);
        return new Point(centerX, Math.Max(0, region.Top - tailLength));
    }

    private static bool IsValidAnnotation(Annotation annotation)
    {
        var bounds = AnnotationEditing.Bounds(annotation);
        return annotation is ShapeAnnotation { Tool: AnnotationTool.Line or AnnotationTool.Arrow } or PenAnnotation
            ? bounds.Width >= 2 || bounds.Height >= 2
            : bounds.Width >= 2 && bounds.Height >= 2;
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        var point = hitTestParameters.HitPoint;
        return point.X >= 0 && point.Y >= 0 && point.X <= RenderSize.Width && point.Y <= RenderSize.Height
            ? new PointHitTestResult(this, point) : null;
    }

    private Point GetImagePoint(MouseEventArgs e)
    {
        if (_viewModel is not null && ScreenPixelBounds.Width > 0 && ScreenPixelBounds.Height > 0 && NativeMethods.GetCursorPos(out var cursor))
            return ScreenCoordinateMapper.ScreenPixelsToImagePoint(cursor.X, cursor.Y, ScreenPixelBounds,
                _viewModel.Image.PixelWidth, _viewModel.Image.PixelHeight);
        return e.GetPosition(this);
    }

    private void ResetInteraction() { _points = null; _drawing = false; ResetDrag(); }
    private static BubbleAnnotation MoveBubbleTip(BubbleAnnotation bubble, Point pointer)
    {
        var bounds = AnnotationEditing.UnrotatedBounds(bubble);
        var inverse = new RotateTransform(-bubble.Rotation, bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        var tip = inverse.Transform(pointer);
        // Changing the tail changes the rotation pivot; compensate to keep the body stationary.
        var changed = bubble with { Tip = tip };
        var next = AnnotationEditing.UnrotatedBounds(changed);
        var oldCenter = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        var newCenter = new Point(next.Left + next.Width / 2, next.Top + next.Height / 2);
        var rotation = new RotateTransform(bubble.Rotation);
        var shift = rotation.Transform(newCenter) - rotation.Transform(oldCenter) - (newCenter - oldCenter);
        return (BubbleAnnotation)AnnotationEditing.Move(changed, shift);
    }

    private static readonly Lazy<System.Windows.Input.Cursor> RotationCursor = new(() =>
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var geometry = Geometry.Parse("M 24,12 A 9,9 0 1 0 23,23 M 24,5 L 24,12 L 17,12");
            context.DrawGeometry(null, new Pen(Brushes.Black, 5) { LineJoin = PenLineJoin.Round }, geometry);
            context.DrawGeometry(null, new Pen(Brushes.White, 2.5) { LineJoin = PenLineJoin.Round }, geometry);
        }
        var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var png = new MemoryStream(); encoder.Save(png);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write((ushort)0); writer.Write((ushort)2); writer.Write((ushort)1);
            writer.Write((byte)32); writer.Write((byte)32); writer.Write((ushort)0);
            writer.Write((ushort)16); writer.Write((ushort)16);
            writer.Write((uint)png.Length); writer.Write((uint)22); writer.Write(png.ToArray());
        }
        stream.Position = 0;
        return new System.Windows.Input.Cursor(stream);
    });
    private void ResetDrag()
    {
        _dragOriginal = null;
        _vertexIndex = -1;
        _resizeHandle = ResizeHandle.None;
        _rotating = false;
        _movingBubbleTip = false;
        _snapX = null;
        _snapY = null;
    }

    private Annotation RotationPreview(Annotation annotation)
    {
        var center = new Point(_dragBounds.Left + _dragBounds.Width / 2, _dragBounds.Top + _dragBounds.Height / 2);
        var currentAngle = Math.Atan2(_current.Y - center.Y, _current.X - center.X) * 180 / Math.PI;
        var delta = currentAngle - _rotationStartAngle;
        var angle = annotation.Rotation + delta;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) angle = Math.Round(angle / 15) * 15;
        return annotation with { Rotation = AnnotationEditing.NormalizeAngle(angle) };
    }

    private Vector SnappedMoveDelta(Annotation annotation, Vector delta)
    {
        _snapX = null;
        _snapY = null;
        if (_viewModel is null) return delta;
        var moved = AnnotationEditing.Bounds(AnnotationEditing.Move(annotation, delta));
        var xTargets = new List<double> { 0, ActualWidth / 2, ActualWidth };
        var yTargets = new List<double> { 0, ActualHeight / 2, ActualHeight };
        foreach (var other in _viewModel.Annotations.Where(item => !Equals(item, annotation)))
        {
            var bounds = AnnotationEditing.Bounds(other);
            xTargets.AddRange([bounds.Left, bounds.Left + bounds.Width / 2, bounds.Right]);
            yTargets.AddRange([bounds.Top, bounds.Top + bounds.Height / 2, bounds.Bottom]);
        }
        var xEdges = new[] { moved.Left, moved.Left + moved.Width / 2, moved.Right };
        var yEdges = new[] { moved.Top, moved.Top + moved.Height / 2, moved.Bottom };
        var xMatch = (from edge in xEdges from target in xTargets let distance = target - edge where Math.Abs(distance) <= 6 orderby Math.Abs(distance) select (target, distance)).FirstOrDefault();
        var yMatch = (from edge in yEdges from target in yTargets let distance = target - edge where Math.Abs(distance) <= 6 orderby Math.Abs(distance) select (target, distance)).FirstOrDefault();
        if (xMatch != default) { delta.X += xMatch.distance; _snapX = xMatch.target; }
        if (yMatch != default) { delta.Y += yMatch.distance; _snapY = yMatch.target; }
        return delta;
    }

    private void DrawSnapGuides(DrawingContext context)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(210, 255, 64, 160)), 1) { DashStyle = DashStyles.Dash };
        if (_snapX is double x) context.DrawLine(pen, new Point(x, 0), new Point(x, ActualHeight));
        if (_snapY is double y) context.DrawLine(pen, new Point(0, y), new Point(ActualWidth, y));
    }

    private static void DrawAnnotation(DrawingContext context, Annotation annotation)
    {
        if (Math.Abs(annotation.Rotation) < 0.001) { annotation.Draw(context); return; }
        var bounds = AnnotationEditing.UnrotatedBounds(annotation);
        context.PushTransform(new RotateTransform(annotation.Rotation,
            bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2));
        annotation.Draw(context);
        context.Pop();
    }

    private DrawingGroup GetAnnotationCache()
    {
        if (_annotationCache is not null) return _annotationCache;
        var group = new DrawingGroup();
        using (var context = group.Open())
            if (_viewModel is not null)
                foreach (var annotation in _viewModel.Annotations) DrawAnnotation(context, annotation);
        _annotationCache = group;
        return group;
    }

    private static ResizeHandle HitResizeHandle(Annotation annotation, Point point)
    {
        if (annotation is ShapeAnnotation { Tool: AnnotationTool.Line or AnnotationTool.Arrow } line)
        {
            if ((VisualPoint(line, line.Start) - point).Length <= 12) return ResizeHandle.StartPoint;
            if ((VisualPoint(line, line.End) - point).Length <= 12) return ResizeHandle.EndPoint;
            return ResizeHandle.None;
        }
        var bounds = AnnotationEditing.Bounds(annotation);
        bounds.Inflate(4, 4);
        foreach (var (handle, location) in VisibleResizeHandles(bounds))
            if ((location - point).Length <= 11) return handle;
        return ResizeHandle.None;
    }

    private void UpdateSelectionCursor()
    {
        if (_viewModel?.SelectedTool != AnnotationTool.Select || _viewModel.SelectedAnnotation is not { } selected)
        {
            UpdateCursor();
            return;
        }
        var bounds = AnnotationEditing.Bounds(selected);
        var rotationHandle = new Point(bounds.Left + bounds.Width / 2, bounds.Top - 24);
        if ((rotationHandle - _current).Length <= 10)
        {
            Cursor = RotationCursor.Value;
            return;
        }
        if (selected is BubbleAnnotation bubble && (VisualPoint(bubble, bubble.Tip) - _current).Length <= 12)
        {
            Cursor = Cursors.Cross;
            return;
        }
        Cursor = HitResizeHandle(selected, _current) switch
        {
            ResizeHandle.Top or ResizeHandle.Bottom => Cursors.SizeNS,
            ResizeHandle.Left or ResizeHandle.Right => Cursors.SizeWE,
            ResizeHandle.TopLeft or ResizeHandle.BottomRight => Cursors.SizeNWSE,
            ResizeHandle.TopRight or ResizeHandle.BottomLeft => Cursors.SizeNESW,
            ResizeHandle.StartPoint or ResizeHandle.EndPoint => Cursors.Cross,
            _ when AnnotationEditing.HitTest(selected, _current) => Cursors.SizeAll,
            _ => Cursors.Arrow
        };
    }

    private Annotation ResizeSelectedAnnotation()
    {
        if (_dragOriginal is ShapeAnnotation { Tool: AnnotationTool.Line or AnnotationTool.Arrow } line &&
            _resizeHandle is ResizeHandle.StartPoint or ResizeHandle.EndPoint)
        {
            var visualStart = VisualPoint(line, line.Start);
            var visualEnd = VisualPoint(line, line.End);
            return _resizeHandle == ResizeHandle.StartPoint
                ? line with { Start = _current, End = visualEnd, Rotation = 0 }
                : line with { Start = visualStart, End = _current, Rotation = 0 };
        }
        return AnnotationEditing.Resize(_dragOriginal!, _dragBounds, ResizeBounds(_dragBounds, _current, _resizeHandle));
    }

    private static Point VisualPoint(Annotation annotation, Point point)
    {
        if (Math.Abs(annotation.Rotation) < 0.001) return point;
        var bounds = AnnotationEditing.UnrotatedBounds(annotation);
        var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        return AnnotationEditing.RotatePoint(point, center, annotation.Rotation);
    }

    private static Point[] ResizeHandlePoints(Rect bounds) =>
    [
        bounds.TopLeft, new Point(bounds.Left + bounds.Width / 2, bounds.Top), bounds.TopRight,
        new Point(bounds.Right, bounds.Top + bounds.Height / 2), bounds.BottomRight,
        new Point(bounds.Left + bounds.Width / 2, bounds.Bottom), bounds.BottomLeft,
        new Point(bounds.Left, bounds.Top + bounds.Height / 2)
    ];

    private static IEnumerable<(ResizeHandle Handle, Point Point)> VisibleResizeHandles(Rect bounds)
    {
        var points = ResizeHandlePoints(bounds);
        yield return (ResizeHandle.TopLeft, points[0]);
        if (bounds.Width >= 48) yield return (ResizeHandle.Top, points[1]);
        yield return (ResizeHandle.TopRight, points[2]);
        if (bounds.Height >= 48) yield return (ResizeHandle.Right, points[3]);
        yield return (ResizeHandle.BottomRight, points[4]);
        if (bounds.Width >= 48) yield return (ResizeHandle.Bottom, points[5]);
        yield return (ResizeHandle.BottomLeft, points[6]);
        if (bounds.Height >= 48) yield return (ResizeHandle.Left, points[7]);
    }

    private static Rect ResizeBounds(Rect original, Point point, ResizeHandle handle)
    {
        var left = original.Left;
        var top = original.Top;
        var right = original.Right;
        var bottom = original.Bottom;
        var movesLeft = handle is ResizeHandle.TopLeft or ResizeHandle.Left or ResizeHandle.BottomLeft;
        var movesRight = handle is ResizeHandle.TopRight or ResizeHandle.Right or ResizeHandle.BottomRight;
        var movesTop = handle is ResizeHandle.TopLeft or ResizeHandle.Top or ResizeHandle.TopRight;
        var movesBottom = handle is ResizeHandle.BottomLeft or ResizeHandle.Bottom or ResizeHandle.BottomRight;
        if (movesLeft) left = point.X;
        if (movesRight) right = point.X;
        if (movesTop) top = point.Y;
        if (movesBottom) bottom = point.Y;
        if (Math.Abs(right - left) < 2)
        {
            if (movesLeft) left = right + (point.X >= right ? 2 : -2);
            else if (movesRight) right = left + (point.X >= left ? 2 : -2);
        }
        if (Math.Abs(bottom - top) < 2)
        {
            if (movesTop) top = bottom + (point.Y >= bottom ? 2 : -2);
            else if (movesBottom) bottom = top + (point.Y >= top ? 2 : -2);
        }
        return new Rect(new Point(Math.Min(left, right), Math.Min(top, bottom)),
            new Point(Math.Max(left, right), Math.Max(top, bottom)));
    }
    private static Rect Normalize(Point a, Point b) => new(new Point(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)),
        new Point(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
}
