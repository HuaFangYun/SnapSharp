using SnapSharp.Models;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace SnapSharp.Services;

public sealed class AnnotationTemplateService
{
    private sealed class TemplateDocument
    {
        public int Version { get; set; } = 1;
        public List<TemplateItem> Items { get; set; } = [];
    }

    private sealed class TemplateItem
    {
        public string Type { get; set; } = "";
        public string? Tool { get; set; }
        public double[][]? Points { get; set; }
        public double[]? Start { get; set; }
        public double[]? End { get; set; }
        public double[]? Position { get; set; }
        public double[]? Region { get; set; }
        public double[]? Tip { get; set; }
        public double[]? Source { get; set; }
        public double[]? Destination { get; set; }
        public string? Text { get; set; }
        public string Color { get; set; } = "#FF000000";
        public string? Fill { get; set; }
        public double Thickness { get; set; }
        public double FontSize { get; set; }
        public double Radius { get; set; }
        public int Number { get; set; }
        public string? NumberStyle { get; set; }
        public double Opacity { get; set; }
        public double Angle { get; set; }
        public bool Tiled { get; set; }
        public double Strength { get; set; }
        public double Darkness { get; set; }
        public double Zoom { get; set; }
        public bool IsHighlighter { get; set; }
        public double Rotation { get; set; }
        public string? StrokeStyle { get; set; }
        public string? ArrowStyle { get; set; }
        public string? TextOutline { get; set; }
        public double TextOutlineThickness { get; set; }
        public string? FontFamilyName { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public void Save(string path, IEnumerable<Annotation> annotations, double canvasWidth, double canvasHeight)
    {
        canvasWidth = Math.Max(1, canvasWidth);
        canvasHeight = Math.Max(1, canvasHeight);
        var document = new TemplateDocument
        {
            Items = annotations.Select(annotation => AnnotationEditing.Transform(annotation,
                    point => new Point(point.X / canvasWidth, point.Y / canvasHeight),
                    1 / canvasWidth, 1 / canvasHeight))
                .Select(ToItem).ToList()
        };
        File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions));
    }

    public IReadOnlyList<Annotation> Load(string path, double canvasWidth, double canvasHeight)
    {
        var document = JsonSerializer.Deserialize<TemplateDocument>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("标注模板无效。");
        if (document.Version != 1) throw new InvalidDataException($"不支持标注模板版本 {document.Version}。");
        return document.Items.Select(FromItem)
            .Select(annotation => AnnotationEditing.Transform(annotation,
                point => new Point(point.X * canvasWidth, point.Y * canvasHeight), canvasWidth, canvasHeight))
            .ToArray();
    }

    private static TemplateItem ToItem(Annotation annotation)
    {
        var item = new TemplateItem
        {
            Type = annotation.GetType().Name,
            Color = ColorText(annotation.Color),
            Thickness = annotation.Thickness,
            Rotation = annotation.Rotation,
            StrokeStyle = annotation.StrokeStyle.ToString()
        };
        switch (annotation)
        {
            case ShapeAnnotation x:
                item.Tool = x.Tool.ToString(); item.Start = PointData(x.Start); item.End = PointData(x.End);
                item.Fill = ColorText(x.FillColor); item.ArrowStyle = x.ArrowStyle.ToString(); break;
            case PenAnnotation x:
                item.Points = x.Points.Select(PointData).ToArray(); item.IsHighlighter = x.IsHighlighter; break;
            case PolygonAnnotation x:
                item.Points = x.Points.Select(PointData).ToArray(); item.Fill = ColorText(x.FillColor); break;
            case TextAnnotation x:
                item.Position = PointData(x.Position); item.Text = x.Text; item.FontSize = x.FontSize;
                item.TextOutline = ColorText(x.TextOutlineColor); item.TextOutlineThickness = x.TextOutlineThickness;
                item.FontFamilyName = x.FontFamilyName; break;
            case WatermarkAnnotation x:
                item.Position = PointData(x.Position); item.Text = x.Text; item.FontSize = x.FontSize; item.Opacity = x.Opacity; item.Angle = x.Angle; item.Tiled = x.Tiled; break;
            case NumberAnnotation x:
                item.Position = PointData(x.Center); item.Number = x.Number; item.Radius = x.Radius; item.NumberStyle = x.NumberStyle.ToString(); break;
            case RegionEffectAnnotation x:
                item.Tool = x.Tool.ToString(); item.Region = RectData(x.Region); item.Strength = x.Strength; break;
            case SpotlightAnnotation x:
                item.Region = RectData(x.Region); item.Darkness = x.Darkness; break;
            case BubbleAnnotation x:
                item.Region = RectData(x.Region); item.Tip = PointData(x.Tip); item.Text = x.Text; item.Fill = ColorText(x.FillColor);
                item.FontSize = x.FontSize; item.TextOutline = ColorText(x.TextOutlineColor);
                item.TextOutlineThickness = x.TextOutlineThickness; item.FontFamilyName = x.FontFamilyName; break;
            case MagnifierAnnotation x:
                item.Source = PointData(x.Source); item.Destination = RectData(x.Destination); item.Zoom = x.Zoom; break;
            default: throw new NotSupportedException($"不支持模板对象 {annotation.GetType().Name}。");
        }
        return item;
    }

    private static Annotation FromItem(TemplateItem item)
    {
        var color = ParseColor(item.Color);
        Annotation annotation = item.Type switch
        {
            nameof(ShapeAnnotation) => new ShapeAnnotation(ParseTool(item.Tool), PointValue(item.Start), PointValue(item.End), color, item.Thickness, ParseOptionalColor(item.Fill)),
            nameof(PenAnnotation) => new PenAnnotation(PointValues(item.Points), color, item.Thickness, item.IsHighlighter),
            nameof(PolygonAnnotation) => new PolygonAnnotation(PointValues(item.Points), color, item.Thickness, ParseOptionalColor(item.Fill)),
            nameof(TextAnnotation) => new TextAnnotation(PointValue(item.Position), item.Text ?? "", color, item.FontSize,
                ParseOptionalColor(item.TextOutline), item.TextOutlineThickness)
                { FontFamilyName = string.IsNullOrWhiteSpace(item.FontFamilyName) ? "Segoe UI" : item.FontFamilyName },
            nameof(WatermarkAnnotation) => new WatermarkAnnotation(PointValue(item.Position), item.Text ?? "", color, item.FontSize, item.Opacity, item.Angle, item.Tiled, 1, 1),
            nameof(NumberAnnotation) => new NumberAnnotation(PointValue(item.Position), item.Number, color, item.Radius,
                Enum.TryParse<AnnotationNumberStyle>(item.NumberStyle, out var numberStyle) ? numberStyle : AnnotationNumberStyle.Arabic),
            nameof(RegionEffectAnnotation) => new RegionEffectAnnotation(ParseTool(item.Tool), RectValue(item.Region), item.Strength),
            nameof(SpotlightAnnotation) => new SpotlightAnnotation(RectValue(item.Region), item.Darkness, 1, 1),
            nameof(BubbleAnnotation) => new BubbleAnnotation(RectValue(item.Region), PointValue(item.Tip), item.Text ?? "", color,
                item.Thickness, ParseOptionalColor(item.Fill) ?? Colors.White, item.FontSize,
                ParseOptionalColor(item.TextOutline), item.TextOutlineThickness)
                { FontFamilyName = string.IsNullOrWhiteSpace(item.FontFamilyName) ? "Segoe UI" : item.FontFamilyName },
            nameof(MagnifierAnnotation) => new MagnifierAnnotation(PointValue(item.Source), RectValue(item.Destination), item.Zoom),
            _ => throw new InvalidDataException($"未知模板对象 {item.Type}。")
        };
        var strokeStyle = Enum.TryParse<AnnotationStrokeStyle>(item.StrokeStyle, out var parsed)
            ? parsed
            : AnnotationStrokeStyle.Solid;
        var arrowStyle = Enum.TryParse<AnnotationArrowStyle>(item.ArrowStyle, out var parsedArrow)
            ? parsedArrow
            : AnnotationArrowStyle.EndOpen;
        if (annotation is ShapeAnnotation shape)
            return shape with { Rotation = item.Rotation, StrokeStyle = strokeStyle, ArrowStyle = arrowStyle };
        return annotation with { Rotation = item.Rotation, StrokeStyle = strokeStyle };
    }

    private static AnnotationTool ParseTool(string? value) =>
        Enum.TryParse<AnnotationTool>(value, out var tool) ? tool : throw new InvalidDataException($"未知标注工具 {value}。");
    private static double[] PointData(Point point) => [point.X, point.Y];
    private static double[] RectData(Rect rect) => [rect.X, rect.Y, rect.Width, rect.Height];
    private static Point PointValue(double[]? values) => values is { Length: >= 2 } ? new Point(values[0], values[1]) : throw new InvalidDataException("模板点坐标无效。");
    private static Point[] PointValues(double[][]? values) => values?.Select(PointValue).ToArray() ?? throw new InvalidDataException("模板点列表无效。");
    private static Rect RectValue(double[]? values) => values is { Length: >= 4 } ? new Rect(values[0], values[1], values[2], values[3]) : throw new InvalidDataException("模板矩形无效。");
    private static string ColorText(Color? color) => color is Color value ? $"#{value.A:X2}{value.R:X2}{value.G:X2}{value.B:X2}" : "";
    private static Color ParseColor(string value) => (Color)System.Windows.Media.ColorConverter.ConvertFromString(value);
    private static Color? ParseOptionalColor(string? value) => string.IsNullOrWhiteSpace(value) ? null : ParseColor(value);
}
