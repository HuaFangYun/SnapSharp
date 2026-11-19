using NimCap.Screenshot.Models;

namespace NimCap.Screenshot.ViewModels;

public partial class EditorViewModel
{
    public AnnotationTool EffectiveTool
    {
        get
        {
            var selectedTool = SelectedAnnotation switch
            {
                ShapeAnnotation shape => shape.Tool,
                PenAnnotation { IsHighlighter: true } => AnnotationTool.Highlight,
                PenAnnotation => AnnotationTool.Pen,
                PolygonAnnotation => AnnotationTool.Polygon,
                TextAnnotation => AnnotationTool.Text,
                WatermarkAnnotation => AnnotationTool.Watermark,
                NumberAnnotation => AnnotationTool.Number,
                RegionEffectAnnotation effect => effect.Tool,
                SpotlightAnnotation => AnnotationTool.Spotlight,
                BubbleAnnotation => AnnotationTool.Bubble,
                MagnifierAnnotation => AnnotationTool.Magnifier,
                _ => AnnotationTool.Select
            };
            return selectedTool != AnnotationTool.Select ? selectedTool : SelectedTool;
        }
    }

    // Selecting an object adopts its style for the next annotation without changing history.
    public void UseSelectedStyle(double minimumThickness, double maximumThickness)
    {
        if (SelectedAnnotation is not { } selected) return;
        SelectedColor = selected.Color;
        StrokeThickness = Math.Clamp(selected is NumberAnnotation number ? number.Radius / 3 : selected.Thickness,
            minimumThickness, maximumThickness);
        FillColor = selected switch
        {
            ShapeAnnotation shape => shape.FillColor,
            PolygonAnnotation polygon => polygon.FillColor,
            BubbleAnnotation bubble => bubble.FillColor.A == 0 ? null : bubble.FillColor,
            _ => FillColor
        };
        FontSize = selected switch
        {
            TextAnnotation text => text.FontSize,
            WatermarkAnnotation watermark => watermark.FontSize,
            BubbleAnnotation bubble => bubble.FontSize,
            _ => FontSize
        };
        FontFamilyName = selected switch
        {
            TextAnnotation text => text.FontFamilyName,
            BubbleAnnotation bubble => bubble.FontFamilyName,
            _ => FontFamilyName
        };
        TextOutlineColor = selected switch
        {
            TextAnnotation text => text.TextOutlineColor,
            BubbleAnnotation bubble => bubble.TextOutlineColor,
            _ => TextOutlineColor
        };
        TextOutlineThickness = Math.Max(0.5, selected switch
        {
            TextAnnotation { TextOutlineThickness: > 0 } text => text.TextOutlineThickness,
            BubbleAnnotation { TextOutlineThickness: > 0 } bubble => bubble.TextOutlineThickness,
            _ => TextOutlineThickness
        });
        StrokeStyle = selected.StrokeStyle;
        if (selected is ShapeAnnotation { Tool: AnnotationTool.Arrow } arrow) ArrowStyle = arrow.ArrowStyle;
    }
}
