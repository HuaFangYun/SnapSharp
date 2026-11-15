using System.Windows;
using System.Windows.Media;
using SnapSharp.Models;
using Brush = System.Windows.Media.Brush;

namespace SnapSharp.Views;

// Use the actual annotation renderer instead of font-dependent arrow glyphs.
public sealed class ArrowStylePreview : FrameworkElement
{
    public static readonly DependencyProperty ArrowStyleProperty = DependencyProperty.Register(
        nameof(ArrowStyle), typeof(AnnotationArrowStyle), typeof(ArrowStylePreview),
        new FrameworkPropertyMetadata(AnnotationArrowStyle.EndOpen, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(ArrowStylePreview),
        new FrameworkPropertyMetadata(Brushes.WhiteSmoke, FrameworkPropertyMetadataOptions.AffectsRender));

    public AnnotationArrowStyle ArrowStyle
    {
        get => (AnnotationArrowStyle)GetValue(ArrowStyleProperty);
        set => SetValue(ArrowStyleProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public ArrowStylePreview()
    {
        Width = 64;
        Height = 22;
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 8 || ActualHeight <= 0) return;
        var color = Foreground is SolidColorBrush brush ? brush.Color : Colors.WhiteSmoke;
        new ShapeAnnotation(AnnotationTool.Arrow, new Point(4, ActualHeight / 2),
            new Point(ActualWidth - 4, ActualHeight / 2), color, 2)
        { ArrowStyle = ArrowStyle }.Draw(drawingContext);
    }
}
