using System.Windows;
using System.Windows.Media;
using SkiaSharp;

namespace NimCap.Screenshot.Models;

public enum AnnotationTool
{
    Select,
    Rectangle,
    Ellipse,
    Line,
    Arrow,
    Pen,
    Highlight,
    Polyline,
    Text,
    Watermark,
    Number,
    Mosaic,
    Blur,
    Spotlight,
    Bubble,
    Magnifier,
    Polygon,
    Eraser
}

public enum AnnotationStrokeStyle
{
    Solid,
    Dash,
    Dot,
    LongDash,
    DashDot,
    DashDotDot
}

public enum AnnotationArrowStyle
{
    EndOpen,
    BothOpen,
    StartOpen,
    EndFilled,
    BothFilled,
    EndOpenStartBar,
    StartOpenEndBar,
    Bars,
    TaperedOutline,
    TaperedFilled
}

public enum AnnotationNumberStyle
{
    Arabic,
    UpperRoman,
    LowerAlpha,
    UpperAlpha
}

public abstract record Annotation(Color Color, double Thickness)
{
    public double Rotation { get; init; }
    public AnnotationStrokeStyle StrokeStyle { get; init; }

    public abstract void Draw(DrawingContext context);
    public abstract void DrawSkia(SKCanvas canvas);

    protected static SKColor ToSkia(Color color, byte? alpha = null) =>
        new(color.R, color.G, color.B, alpha ?? color.A);

    protected Pen CreatePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
            DashStyle = StrokeStyle switch
            {
                AnnotationStrokeStyle.Dash => DashStyles.Dash,
                AnnotationStrokeStyle.Dot => DashStyles.Dot,
                AnnotationStrokeStyle.LongDash => new DashStyle([6, 2], 0),
                AnnotationStrokeStyle.DashDot => DashStyles.DashDot,
                AnnotationStrokeStyle.DashDotDot => DashStyles.DashDotDot,
                _ => DashStyles.Solid
            }
        };
        pen.Freeze();
        return pen;
    }

    protected SKPathEffect? CreatePathEffect() => StrokeStyle switch
    {
        AnnotationStrokeStyle.Dash => SKPathEffect.CreateDash([(float)Math.Max(6, Thickness * 2.5), (float)Math.Max(4, Thickness * 1.5)], 0),
        AnnotationStrokeStyle.Dot => SKPathEffect.CreateDash([(float)Math.Max(1, Thickness * 0.2), (float)Math.Max(3, Thickness * 1.5)], 0),
        AnnotationStrokeStyle.LongDash => SKPathEffect.CreateDash([(float)Math.Max(12, Thickness * 5), (float)Math.Max(4, Thickness * 1.8)], 0),
        AnnotationStrokeStyle.DashDot => SKPathEffect.CreateDash([(float)Math.Max(8, Thickness * 3), (float)Math.Max(3, Thickness),
            (float)Math.Max(1, Thickness * 0.25), (float)Math.Max(3, Thickness)], 0),
        AnnotationStrokeStyle.DashDotDot => SKPathEffect.CreateDash([(float)Math.Max(8, Thickness * 3), (float)Math.Max(3, Thickness),
            (float)Math.Max(1, Thickness * 0.25), (float)Math.Max(3, Thickness),
            (float)Math.Max(1, Thickness * 0.25), (float)Math.Max(3, Thickness)], 0),
        _ => null
    };
}

public sealed record ShapeAnnotation(
    AnnotationTool Tool,
    Point Start,
    Point End,
    Color StrokeColor,
    double StrokeThickness,
    Color? FillColor = null) : Annotation(StrokeColor, StrokeThickness)
{
    public AnnotationArrowStyle ArrowStyle { get; init; }

    public override void Draw(DrawingContext context)
    {
        if (IsTaperedArrow)
        {
            var points = TaperedArrowPoints();
            if (points.Length == 0) return;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(points[0], true, true);
                g.PolyLineTo(points.Skip(1).ToArray(), true, false);
            }
            geometry.Freeze();
            var brush = new SolidColorBrush(Color);
            context.DrawGeometry(ArrowStyle == AnnotationArrowStyle.TaperedFilled ? brush : null,
                ArrowStyle == AnnotationArrowStyle.TaperedOutline
                    ? new Pen(brush, TaperedOutlineThickness) { LineJoin = PenLineJoin.Round } : null, geometry);
            return;
        }
        var pen = CreatePen(new SolidColorBrush(Color), Thickness);
        var rect = new Rect(Start, End);
        var fill = FillColor is Color fillColor ? new SolidColorBrush(fillColor) : Brushes.Transparent;
        if (Tool == AnnotationTool.Rectangle)
        {
            context.DrawRectangle(fill, pen, rect);
        }
        else if (Tool == AnnotationTool.Ellipse)
        {
            context.DrawEllipse(fill, pen,
                new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2),
                rect.Width / 2, rect.Height / 2);
        }
        else if (Tool is AnnotationTool.Line or AnnotationTool.Arrow)
        {
            context.DrawLine(pen, Start, End);
            if (Tool == AnnotationTool.Line) return;
            var brush = new SolidColorBrush(Color);
            var headPen = new Pen(brush, Thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            DrawArrowDecorations(context, brush, headPen);
        }
    }

    public override void DrawSkia(SKCanvas canvas)
    {
        if (IsTaperedArrow)
        {
            var points = TaperedArrowPoints();
            if (points.Length == 0) return;
            using var path = new SKPath();
            path.MoveTo((float)points[0].X, (float)points[0].Y);
            foreach (var point in points.Skip(1)) path.LineTo((float)point.X, (float)point.Y);
            path.Close();
            using var taperedPaint = new SKPaint
            {
                Color = ToSkia(Color), IsAntialias = true,
                Style = ArrowStyle == AnnotationArrowStyle.TaperedFilled ? SKPaintStyle.Fill : SKPaintStyle.Stroke,
                StrokeWidth = (float)TaperedOutlineThickness, StrokeJoin = SKStrokeJoin.Round
            };
            canvas.DrawPath(path, taperedPaint);
            return;
        }
        using var paint = new SKPaint
        {
            Color = ToSkia(Color),
            StrokeWidth = (float)Thickness,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true
        };
        using var pathEffect = CreatePathEffect();
        paint.PathEffect = pathEffect;
        var rect = SKRect.Create((float)Math.Min(Start.X, End.X), (float)Math.Min(Start.Y, End.Y),
            (float)Math.Abs(End.X - Start.X), (float)Math.Abs(End.Y - Start.Y));
        if (FillColor is Color fillColor && Tool is AnnotationTool.Rectangle or AnnotationTool.Ellipse)
        {
            using var fill = new SKPaint { Color = ToSkia(fillColor), Style = SKPaintStyle.Fill, IsAntialias = true };
            if (Tool == AnnotationTool.Rectangle) canvas.DrawRect(rect, fill); else canvas.DrawOval(rect, fill);
        }
        if (Tool == AnnotationTool.Rectangle) canvas.DrawRect(rect, paint);
        else if (Tool == AnnotationTool.Ellipse) canvas.DrawOval(rect, paint);
        else if (Tool is AnnotationTool.Line or AnnotationTool.Arrow)
        {
            canvas.DrawLine((float)Start.X, (float)Start.Y, (float)End.X, (float)End.Y, paint);
            if (Tool == AnnotationTool.Line) return;
            using var headPaint = new SKPaint
            {
                Color = ToSkia(Color),
                StrokeWidth = (float)Thickness,
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true
            };
            DrawArrowDecorations(canvas, headPaint);
        }
    }

    private bool IsTaperedArrow => Tool == AnnotationTool.Arrow &&
        ArrowStyle is AnnotationArrowStyle.TaperedOutline or AnnotationArrowStyle.TaperedFilled;

    private double TaperedOutlineThickness => Math.Max(1, Thickness * 0.35);

    // Both renderers share one silhouette: pointed tail, widening shaft and notched head.
    private Point[] TaperedArrowPoints()
    {
        var direction = End - Start;
        var distance = direction.Length;
        if (distance <= 0.1) return [];
        direction.Normalize();
        var normal = new Vector(-direction.Y, direction.X);
        var headLength = Math.Min(distance * 0.45, Math.Max(12, Thickness * 4));
        var shoulder = End - direction * headLength;
        var neck = End - direction * (headLength * 0.72);
        var headHalfWidth = headLength * 0.48;
        var shaftHalfWidth = headLength * 0.18;
        return [Start, neck + normal * shaftHalfWidth, shoulder + normal * headHalfWidth,
            End, shoulder - normal * headHalfWidth, neck - normal * shaftHalfWidth];
    }

    private void DrawArrowDecorations(DrawingContext context, Brush brush, Pen pen)
    {
        switch (ArrowStyle)
        {
            case AnnotationArrowStyle.BothOpen:
                DrawOpenHead(context, pen, Start, End);
                DrawOpenHead(context, pen, End, Start);
                break;
            case AnnotationArrowStyle.StartOpen:
                DrawOpenHead(context, pen, End, Start);
                break;
            case AnnotationArrowStyle.EndFilled:
                DrawFilledHead(context, brush, Start, End);
                break;
            case AnnotationArrowStyle.BothFilled:
                DrawFilledHead(context, brush, Start, End);
                DrawFilledHead(context, brush, End, Start);
                break;
            case AnnotationArrowStyle.EndOpenStartBar:
                DrawBar(context, pen, Start, End);
                DrawOpenHead(context, pen, Start, End);
                break;
            case AnnotationArrowStyle.StartOpenEndBar:
                DrawOpenHead(context, pen, End, Start);
                DrawBar(context, pen, End, Start);
                break;
            case AnnotationArrowStyle.Bars:
                DrawBar(context, pen, Start, End);
                DrawBar(context, pen, End, Start);
                break;
            default:
                DrawOpenHead(context, pen, Start, End);
                break;
        }
    }

    private void DrawOpenHead(DrawingContext context, Pen pen, Point from, Point tip)
    {
        if (!TryArrowVectors(from, tip, out var direction, out var normal, out var length)) return;
        var baseCenter = tip - direction * length;
        var halfWidth = length * 0.5;
        context.DrawLine(pen, tip, baseCenter + normal * halfWidth);
        context.DrawLine(pen, tip, baseCenter - normal * halfWidth);
    }

    private void DrawFilledHead(DrawingContext context, Brush brush, Point from, Point tip)
    {
        if (!TryArrowVectors(from, tip, out var direction, out var normal, out var length)) return;
        var baseCenter = tip - direction * length;
        var halfWidth = length * 0.55;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(tip, true, true);
            g.LineTo(baseCenter + normal * halfWidth, true, false);
            g.LineTo(baseCenter - normal * halfWidth, true, false);
        }
        geometry.Freeze();
        context.DrawGeometry(brush, null, geometry);
    }

    private void DrawBar(DrawingContext context, Pen pen, Point point, Point toward)
    {
        if (!TryArrowVectors(point, toward, out _, out var normal, out var length)) return;
        var halfWidth = length * 0.48;
        context.DrawLine(pen, point + normal * halfWidth, point - normal * halfWidth);
    }

    private void DrawArrowDecorations(SKCanvas canvas, SKPaint paint)
    {
        switch (ArrowStyle)
        {
            case AnnotationArrowStyle.BothOpen:
                DrawOpenHead(canvas, paint, Start, End);
                DrawOpenHead(canvas, paint, End, Start);
                break;
            case AnnotationArrowStyle.StartOpen:
                DrawOpenHead(canvas, paint, End, Start);
                break;
            case AnnotationArrowStyle.EndFilled:
                DrawFilledHead(canvas, paint, Start, End);
                break;
            case AnnotationArrowStyle.BothFilled:
                DrawFilledHead(canvas, paint, Start, End);
                DrawFilledHead(canvas, paint, End, Start);
                break;
            case AnnotationArrowStyle.EndOpenStartBar:
                DrawBar(canvas, paint, Start, End);
                DrawOpenHead(canvas, paint, Start, End);
                break;
            case AnnotationArrowStyle.StartOpenEndBar:
                DrawOpenHead(canvas, paint, End, Start);
                DrawBar(canvas, paint, End, Start);
                break;
            case AnnotationArrowStyle.Bars:
                DrawBar(canvas, paint, Start, End);
                DrawBar(canvas, paint, End, Start);
                break;
            default:
                DrawOpenHead(canvas, paint, Start, End);
                break;
        }
    }

    private void DrawOpenHead(SKCanvas canvas, SKPaint paint, Point from, Point tip)
    {
        if (!TryArrowVectors(from, tip, out var direction, out var normal, out var length)) return;
        var baseCenter = tip - direction * length;
        var halfWidth = length * 0.5;
        canvas.DrawLine((float)tip.X, (float)tip.Y,
            (float)(baseCenter.X + normal.X * halfWidth), (float)(baseCenter.Y + normal.Y * halfWidth), paint);
        canvas.DrawLine((float)tip.X, (float)tip.Y,
            (float)(baseCenter.X - normal.X * halfWidth), (float)(baseCenter.Y - normal.Y * halfWidth), paint);
    }

    private void DrawFilledHead(SKCanvas canvas, SKPaint paint, Point from, Point tip)
    {
        if (!TryArrowVectors(from, tip, out var direction, out var normal, out var length)) return;
        var baseCenter = tip - direction * length;
        var halfWidth = length * 0.55;
        using var path = new SKPath();
        path.MoveTo((float)tip.X, (float)tip.Y);
        path.LineTo((float)(baseCenter.X + normal.X * halfWidth), (float)(baseCenter.Y + normal.Y * halfWidth));
        path.LineTo((float)(baseCenter.X - normal.X * halfWidth), (float)(baseCenter.Y - normal.Y * halfWidth));
        path.Close();
        var previousStyle = paint.Style;
        paint.Style = SKPaintStyle.Fill;
        canvas.DrawPath(path, paint);
        paint.Style = previousStyle;
    }

    private void DrawBar(SKCanvas canvas, SKPaint paint, Point point, Point toward)
    {
        if (!TryArrowVectors(point, toward, out _, out var normal, out var length)) return;
        var halfWidth = length * 0.48;
        canvas.DrawLine((float)(point.X + normal.X * halfWidth), (float)(point.Y + normal.Y * halfWidth),
            (float)(point.X - normal.X * halfWidth), (float)(point.Y - normal.Y * halfWidth), paint);
    }

    private bool TryArrowVectors(Point from, Point tip, out Vector direction, out Vector normal, out double length)
    {
        direction = tip - from;
        normal = default;
        length = Math.Max(10, Thickness * 3.5);
        if (direction.Length <= 0.1) return false;
        direction.Normalize();
        normal = new Vector(-direction.Y, direction.X);
        return true;
    }
}

public sealed record PenAnnotation(
    IReadOnlyList<Point> Points,
    Color StrokeColor,
    double StrokeThickness,
    bool IsHighlighter = false) : Annotation(StrokeColor, StrokeThickness)
{
    public override void Draw(DrawingContext context)
    {
        if (Points.Count < 2) return;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(Points[0], false, false);
            g.PolyLineTo(Points.Skip(1).ToArray(), true, false);
        }
        geometry.Freeze();
        var brush = new SolidColorBrush(Color) { Opacity = IsHighlighter ? 0.38 : 1 };
        var pen = CreatePen(brush, Thickness);
        context.DrawGeometry(null, pen, geometry);
    }

    public override void DrawSkia(SKCanvas canvas)
    {
        if (Points.Count < 2) return;
        using var path = new SKPath();
        path.MoveTo((float)Points[0].X, (float)Points[0].Y);
        foreach (var point in Points.Skip(1)) path.LineTo((float)point.X, (float)point.Y);
        using var paint = new SKPaint
        {
            Color = ToSkia(Color, IsHighlighter ? (byte)100 : Color.A),
            StrokeWidth = (float)Thickness,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true
        };
        using var pathEffect = CreatePathEffect();
        paint.PathEffect = pathEffect;
        canvas.DrawPath(path, paint);
    }
}

public sealed record TextAnnotation(
    Point Position,
    string Text,
    Color StrokeColor,
    double FontSize,
    Color? TextOutlineColor = null,
    double TextOutlineThickness = 0) : Annotation(StrokeColor, 1)
{
    public string FontFamilyName { get; init; } = "Segoe UI";

    public override void Draw(DrawingContext context)
    {
#pragma warning disable CS0618
        var text = new FormattedText(Text, System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, new Typeface(FontFamilyName), FontSize,
            new SolidColorBrush(Color));
#pragma warning restore CS0618
        if (TextOutlineColor is Color outlineColor && TextOutlineThickness > 0)
        {
            var geometry = text.BuildGeometry(Position);
            var outlinePen = new Pen(new SolidColorBrush(outlineColor), TextOutlineThickness * 2)
            {
                LineJoin = PenLineJoin.Round
            };
            context.DrawGeometry(null, outlinePen, geometry);
            context.DrawGeometry(new SolidColorBrush(Color), null, geometry);
        }
        else context.DrawText(text, Position);
    }

    public override void DrawSkia(SKCanvas canvas)
    {
        using var font = new SKFont(SKTypeface.FromFamilyName(FontFamilyName), (float)FontSize);
        using var paint = new SKPaint { Color = ToSkia(Color), IsAntialias = true };
        using var outline = TextOutlineColor is Color outlineColor && TextOutlineThickness > 0
            ? new SKPaint
            {
                Color = ToSkia(outlineColor), IsAntialias = true, Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)(TextOutlineThickness * 2), StrokeJoin = SKStrokeJoin.Round
            }
            : null;
        var y = Position.Y + FontSize;
        foreach (var line in Text.Replace("\r\n", "\n").Split('\n'))
        {
            if (outline is not null)
                canvas.DrawText(line, (float)Position.X, (float)y, SKTextAlign.Left, font, outline);
            canvas.DrawText(line, (float)Position.X, (float)y, SKTextAlign.Left, font, paint);
            y += FontSize * 1.35;
        }
    }
}

public sealed record WatermarkAnnotation(
    Point Position,
    string Text,
    Color StrokeColor,
    double FontSize,
    double Opacity,
    double Angle,
    bool Tiled,
    double CanvasWidth,
    double CanvasHeight) : Annotation(StrokeColor, 1)
{
    public override void Draw(DrawingContext context)
    {
        var brush = new SolidColorBrush(Color) { Opacity = Math.Clamp(Opacity, 0.05, 1) };
#pragma warning disable CS0618
        var text = new FormattedText(Text, System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), FontSize, brush);
#pragma warning restore CS0618
        if (!Tiled)
        {
            context.PushTransform(new RotateTransform(Angle, Position.X, Position.Y));
            context.DrawText(text, Position);
            context.Pop();
            return;
        }
        for (var y = 20d; y < CanvasHeight; y += Math.Max(80, FontSize * 4))
        for (var x = 20d; x < CanvasWidth; x += Math.Max(180, text.Width + 90))
        {
            context.PushTransform(new RotateTransform(Angle, x, y));
            context.DrawText(text, new Point(x, y));
            context.Pop();
        }
    }

    public override void DrawSkia(SKCanvas canvas)
    {
        using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI"), (float)FontSize);
        using var paint = new SKPaint { Color = ToSkia(Color, (byte)(255 * Math.Clamp(Opacity, 0.05, 1))), IsAntialias = true };
        void DrawAt(float x, float y)
        {
            canvas.Save();
            canvas.RotateDegrees((float)Angle, x, y);
            canvas.DrawText(Text, x, y + (float)FontSize, SKTextAlign.Left, font, paint);
            canvas.Restore();
        }
        if (!Tiled) DrawAt((float)Position.X, (float)Position.Y);
        else
        {
            var width = font.MeasureText(Text);
            for (var y = 20f; y < CanvasHeight; y += (float)Math.Max(80, FontSize * 4))
            for (var x = 20f; x < CanvasWidth; x += Math.Max(180, width + 90)) DrawAt(x, y);
        }
    }
}

public sealed record NumberAnnotation(Point Center, int Number, Color StrokeColor, double Radius,
    AnnotationNumberStyle NumberStyle = AnnotationNumberStyle.Arabic)
    : Annotation(StrokeColor, 2)
{
    public string DisplayText => FormatNumber(Number, NumberStyle);

    public override void Draw(DrawingContext context)
    {
        var fill = new SolidColorBrush(Color);
        context.DrawEllipse(fill, null, Center, Radius, Radius);
#pragma warning disable CS0618
        var text = new FormattedText(DisplayText, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), Radius * 1.15, Brushes.White);
#pragma warning restore CS0618
        var glyphs = text.BuildGeometry(new Point());
        var bounds = glyphs.Bounds;
        var scale = Math.Min(1, Radius * 1.5 / Math.Max(bounds.Width, bounds.Height));
        context.PushTransform(new TranslateTransform(Center.X, Center.Y));
        context.PushTransform(new ScaleTransform(scale, scale));
        context.PushTransform(new TranslateTransform(-bounds.Left - bounds.Width / 2, -bounds.Top - bounds.Height / 2));
        context.DrawGeometry(Brushes.White, null, glyphs);
        context.Pop(); context.Pop(); context.Pop();
    }

    public override void DrawSkia(SKCanvas canvas)
    {
        using var fill = new SKPaint { Color = ToSkia(Color), IsAntialias = true };
        canvas.DrawCircle((float)Center.X, (float)Center.Y, (float)Radius, fill);
        using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), (float)(Radius * 1.15));
        using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        var text = DisplayText;
        font.MeasureText(text, out var bounds);
        var scale = Math.Min(1, Radius * 1.5 / Math.Max(bounds.Width, bounds.Height));
        canvas.Save(); canvas.Translate((float)Center.X, (float)Center.Y); canvas.Scale((float)scale);
        canvas.DrawText(text, -(bounds.Left + bounds.Right) / 2, -(bounds.Top + bounds.Bottom) / 2, SKTextAlign.Left, font, textPaint);
        canvas.Restore();
    }

    private static string FormatNumber(int number, AnnotationNumberStyle style)
    {
        number = Math.Max(1, number);
        return style switch
        {
            AnnotationNumberStyle.UpperRoman => ToRoman(number),
            AnnotationNumberStyle.LowerAlpha => ToAlpha(number, false),
            AnnotationNumberStyle.UpperAlpha => ToAlpha(number, true),
            _ => number.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private static string ToAlpha(int number, bool upper)
    {
        var result = new System.Text.StringBuilder();
        while (number > 0)
        {
            number--;
            result.Insert(0, (char)((upper ? 'A' : 'a') + number % 26));
            number /= 26;
        }
        return result.ToString();
    }

    private static string ToRoman(int number)
    {
        if (number > 3999) return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        (int Value, string Symbol)[] values =
        [
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"),
            (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
        ];
        var result = new System.Text.StringBuilder();
        foreach (var (value, symbol) in values)
            while (number >= value) { result.Append(symbol); number -= value; }
        return result.ToString();
    }
}

public sealed record PolygonAnnotation(
    IReadOnlyList<Point> Points,
    Color StrokeColor,
    double StrokeThickness,
    Color? FillColor = null) : Annotation(StrokeColor, StrokeThickness)
{
    public override void Draw(DrawingContext context)
    {
        if (Points.Count < 3) return;
        var geometry = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (var g = geometry.Open())
        {
            g.BeginFigure(Points[0], FillColor is not null, true);
            g.PolyLineTo(Points.Skip(1).ToArray(), true, false);
        }
        geometry.Freeze();
        var fill = FillColor is Color color ? new SolidColorBrush(color) : null;
        context.DrawGeometry(fill, CreatePen(new SolidColorBrush(Color), Thickness), geometry);
    }

    public override void DrawSkia(SKCanvas canvas)
    {
        if (Points.Count < 3) return;
        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        path.MoveTo((float)Points[0].X, (float)Points[0].Y);
        foreach (var point in Points.Skip(1)) path.LineTo((float)point.X, (float)point.Y);
        path.Close();
        if (FillColor is Color fillColor)
        {
            using var fill = new SKPaint { Color = ToSkia(fillColor), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawPath(path, fill);
        }
        using var stroke = new SKPaint { Color = ToSkia(Color), StrokeWidth = (float)Thickness,
            Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round, IsAntialias = true };
        using var pathEffect = CreatePathEffect();
        stroke.PathEffect = pathEffect;
        canvas.DrawPath(path, stroke);
    }
}

public sealed record BubbleAnnotation(
    Rect Region,
    Point Tip,
    string Text,
    Color StrokeColor,
    double StrokeThickness,
    Color FillColor,
    double FontSize,
    Color? TextOutlineColor = null,
    double TextOutlineThickness = 0) : Annotation(StrokeColor, StrokeThickness)
{
    public string FontFamilyName { get; init; } = "Segoe UI";

    public override void Draw(DrawingContext context)
    {
        var pen = CreatePen(new SolidColorBrush(Color), Thickness);
        var fill = new SolidColorBrush(FillColor);
        context.DrawRoundedRectangle(fill, pen, Region, 8, 8);
        var (left, right) = TailBase();
        var triangle = new StreamGeometry();
        using (var g = triangle.Open())
        {
            g.BeginFigure(left, true, true);
            g.LineTo(Tip, true, false);
            g.LineTo(right, true, false);
        }
        context.DrawGeometry(fill, null, triangle);
        var seamPen = new Pen(fill, Math.Max(Thickness + 2, 3)) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
        context.DrawLine(seamPen, left, right);
        context.DrawLine(pen, left, Tip);
        context.DrawLine(pen, Tip, right);
#pragma warning disable CS0618
        var text = new FormattedText(Text, System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, new Typeface(FontFamilyName), FontSize, new SolidColorBrush(Color), 1.0)
        { MaxTextWidth = Math.Max(1, Region.Width - 16), MaxTextHeight = Math.Max(1, Region.Height - 12) };
#pragma warning restore CS0618
        var textPosition = new Point(Region.Left + 8, Region.Top + 6);
        if (TextOutlineColor is Color outlineColor && TextOutlineThickness > 0)
        {
            var geometry = text.BuildGeometry(textPosition);
            var outlinePen = new Pen(new SolidColorBrush(outlineColor), TextOutlineThickness * 2)
            {
                LineJoin = PenLineJoin.Round
            };
            context.DrawGeometry(null, outlinePen, geometry);
            context.DrawGeometry(new SolidColorBrush(Color), null, geometry);
        }
        else context.DrawText(text, textPosition);
    }

    public override void DrawSkia(SKCanvas canvas)
    {
        using var fill = new SKPaint { Color = ToSkia(FillColor), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var stroke = new SKPaint { Color = ToSkia(Color), StrokeWidth = (float)Thickness,
            Style = SKPaintStyle.Stroke, IsAntialias = true };
        using var pathEffect = CreatePathEffect();
        stroke.PathEffect = pathEffect;
        var rect = SKRect.Create((float)Region.X, (float)Region.Y, (float)Region.Width, (float)Region.Height);
        canvas.DrawRoundRect(rect, 8, 8, fill); canvas.DrawRoundRect(rect, 8, 8, stroke);
        var (left, right) = TailBase();
        using var tail = new SKPath();
        tail.MoveTo((float)left.X, (float)left.Y); tail.LineTo((float)Tip.X, (float)Tip.Y); tail.LineTo((float)right.X, (float)right.Y); tail.Close();
        canvas.DrawPath(tail, fill);
        using var seam = new SKPaint
        {
            Color = fill.Color,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max((float)Thickness + 2, 3),
            StrokeCap = SKStrokeCap.Butt
        };
        canvas.DrawLine((float)left.X, (float)left.Y, (float)right.X, (float)right.Y, seam);
        canvas.DrawLine((float)left.X, (float)left.Y, (float)Tip.X, (float)Tip.Y, stroke);
        canvas.DrawLine((float)Tip.X, (float)Tip.Y, (float)right.X, (float)right.Y, stroke);
        using var font = new SKFont(SKTypeface.FromFamilyName(FontFamilyName), (float)Math.Max(1, FontSize));
        using var textPaint = new SKPaint { Color = ToSkia(Color), IsAntialias = true };
        using var outlinePaint = TextOutlineColor is Color outlineColor && TextOutlineThickness > 0
            ? new SKPaint
            {
                Color = ToSkia(outlineColor), IsAntialias = true, Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)(TextOutlineThickness * 2), StrokeJoin = SKStrokeJoin.Round
            }
            : null;
        var horizontalInset = Math.Min(8, Math.Max(0, rect.Width / 2 - 0.5f));
        var verticalInset = Math.Min(6, Math.Max(0, rect.Height / 2 - 0.5f));
        canvas.Save(); canvas.ClipRect(new SKRect(rect.Left + horizontalInset, rect.Top + verticalInset,
            rect.Right - horizontalInset, rect.Bottom - verticalInset));
        var textWidth = Math.Max(1, rect.Width - horizontalInset * 2);
        var lineHeight = (float)Math.Max(2, FontSize * 1.25);
        var y = rect.Top + verticalInset + (float)FontSize;
        foreach (var line in WrapText(Text, font, textWidth))
        {
            if (y > rect.Bottom - verticalInset) break;
            if (outlinePaint is not null)
                canvas.DrawText(line, rect.Left + horizontalInset, y, SKTextAlign.Left, font, outlinePaint);
            canvas.DrawText(line, rect.Left + horizontalInset, y, SKTextAlign.Left, font, textPaint);
            y += lineHeight;
        }
        canvas.Restore();
    }

    private (Point Left, Point Right) TailBase()
    {
        var center = new Point(Region.Left + Region.Width / 2, Region.Top + Region.Height / 2);
        var delta = Tip - center;
        if (Math.Abs(delta.X) / Math.Max(1, Region.Width) > Math.Abs(delta.Y) / Math.Max(1, Region.Height))
        {
            var half = Math.Min(9, Region.Height / 4);
            var y = Math.Clamp(Tip.Y, Region.Top + half, Region.Bottom - half);
            var x = delta.X < 0 ? Region.Left : Region.Right;
            return (new Point(x, y - half), new Point(x, y + half));
        }
        else
        {
            var half = Math.Min(9, Region.Width / 4);
            var x = Math.Clamp(Tip.X, Region.Left + half, Region.Right - half);
            var y = delta.Y < 0 ? Region.Top : Region.Bottom;
            return (new Point(x - half, y), new Point(x + half, y));
        }
    }

    private static IEnumerable<string> WrapText(string text, SKFont font, float maxWidth)
    {
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (paragraph.Length == 0) { yield return string.Empty; continue; }
            var line = new System.Text.StringBuilder();
            foreach (var character in paragraph)
            {
                line.Append(character);
                if (font.MeasureText(line.ToString()) <= maxWidth || line.Length == 1) continue;
                line.Length--;
                yield return line.ToString();
                line.Clear();
                line.Append(character);
            }
            if (line.Length > 0) yield return line.ToString();
        }
    }
}

public sealed record SpotlightAnnotation(Rect Region, double Darkness = 0.58, double CanvasWidth = 0, double CanvasHeight = 0)
    : Annotation(Colors.Transparent, Darkness)
{
    public override void Draw(DrawingContext context)
    {
        if (CanvasWidth > 0 && CanvasHeight > 0)
        {
            var geometry = new PathGeometry { FillRule = FillRule.EvenOdd };
            geometry.AddGeometry(new RectangleGeometry(new Rect(0, 0, CanvasWidth, CanvasHeight)));
            geometry.AddGeometry(new RectangleGeometry(Region));
            context.DrawGeometry(new SolidColorBrush(Color.FromArgb(
                (byte)(255 * Math.Clamp(Darkness, 0.1, 0.9)), 0, 0, 0)), null, geometry);
        }
        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)), 1), Region);
    }
    public override void DrawSkia(SKCanvas canvas) { }
    public void ApplySkia(SKSurface surface)
    {
        using var paint = new SKPaint { Color = new SKColor(0, 0, 0, (byte)(255 * Math.Clamp(Darkness, 0.1, 0.9))) };
        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        path.AddRect(SKRect.Create(surface.Canvas.DeviceClipBounds.Width, surface.Canvas.DeviceClipBounds.Height));
        path.AddRect(SKRect.Create((float)Region.X, (float)Region.Y, (float)Region.Width, (float)Region.Height));
        surface.Canvas.DrawPath(path, paint);
    }
}

public sealed record MagnifierAnnotation(Point Source, Rect Destination, double Zoom = 3)
    : Annotation(Colors.White, 3)
{
    public override void Draw(DrawingContext context)
    {
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
            new Pen(Brushes.White, Thickness), Destination);
        PixelReticle.DrawCrosshair(context, new Rect(Source - new Vector(6, 6), new System.Windows.Size(12, 12)), Source);
    }
    public override void DrawSkia(SKCanvas canvas) { }
    public void ApplySkia(SKSurface surface, SKImage sourceImage)
    {
        var sourceWidth = Math.Min(sourceImage.Width, Math.Max(1, Destination.Width / Math.Max(1, Zoom)));
        var sourceHeight = Math.Min(sourceImage.Height, Math.Max(1, Destination.Height / Math.Max(1, Zoom)));
        var source = SKRect.Create((float)Math.Clamp(Source.X - sourceWidth / 2, 0, sourceImage.Width - sourceWidth),
            (float)Math.Clamp(Source.Y - sourceHeight / 2, 0, sourceImage.Height - sourceHeight),
            (float)sourceWidth, (float)sourceHeight);
        var target = SKRect.Create((float)Destination.X, (float)Destination.Y, (float)Destination.Width, (float)Destination.Height);
        surface.Canvas.DrawImage(sourceImage, source, target, new SKSamplingOptions(SKFilterMode.Nearest));
        using var border = new SKPaint { Color = SKColors.White, StrokeWidth = (float)Thickness,
            Style = SKPaintStyle.Stroke, IsAntialias = true };
        surface.Canvas.DrawRect(target, border);
    }
}

public sealed record RegionEffectAnnotation(AnnotationTool Tool, Rect Region, double Strength)
    : Annotation(Colors.Transparent, Strength)
{
    public override void Draw(DrawingContext context)
    {
        var brush = Tool == AnnotationTool.Mosaic ? new SolidColorBrush(Color.FromArgb(70, 80, 80, 80)) : new SolidColorBrush(Color.FromArgb(55, 80, 150, 220));
        context.DrawRectangle(brush, new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1), Region);
    }

    public override void DrawSkia(SKCanvas canvas)
    {
        // Pixel effects require an SKSurface snapshot and are applied by SkiaImageService.
    }

    public void ApplySkia(SKSurface surface)
    {
        var canvas = surface.Canvas;
        var rect = SKRect.Create((float)Region.X, (float)Region.Y, (float)Region.Width, (float)Region.Height);
        if (rect.Width < 1 || rect.Height < 1) return;
        using var snapshot = surface.Snapshot();
        canvas.Save();
        canvas.ClipRect(rect);
        if (Tool == AnnotationTool.Blur)
        {
            using var filter = SKImageFilter.CreateBlur((float)Math.Max(2, Strength), (float)Math.Max(2, Strength));
            using var paint = new SKPaint { ImageFilter = filter };
            canvas.DrawImage(snapshot, 0, 0, paint);
        }
        else
        {
            var block = Math.Clamp((int)Strength, 4, 48);
            var smallWidth = Math.Max(1, (int)rect.Width / block);
            var smallHeight = Math.Max(1, (int)rect.Height / block);
            var source = new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
            var info = new SKImageInfo(smallWidth, smallHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var small = SKSurface.Create(info);
            small.Canvas.DrawImage(snapshot, source, SKRect.Create(smallWidth, smallHeight), new SKSamplingOptions(SKFilterMode.Linear));
            using var pixelated = small.Snapshot();
            canvas.DrawImage(pixelated, SKRect.Create(smallWidth, smallHeight), rect, new SKSamplingOptions(SKFilterMode.Nearest));
        }
        canvas.Restore();
    }
}

public static class AnnotationEditing
{
    public static Rect Bounds(Annotation annotation)
    {
        var bounds = UnrotatedBounds(annotation);
        if (bounds.IsEmpty || Math.Abs(annotation.Rotation) < 0.001) return bounds;
        var center = Center(bounds);
        var corners = new[] { bounds.TopLeft, bounds.TopRight, bounds.BottomRight, bounds.BottomLeft }
            .Select(point => RotatePoint(point, center, annotation.Rotation));
        return FromPoints(corners);
    }

    public static Rect UnrotatedBounds(Annotation annotation) => annotation switch
    {
        ShapeAnnotation x => new Rect(x.Start, x.End),
        PenAnnotation x when x.Points.Count > 0 => FromPoints(x.Points),
        PolygonAnnotation x when x.Points.Count > 0 => FromPoints(x.Points),
        TextAnnotation x => TextBounds(x),
        WatermarkAnnotation x => new Rect(x.Position, new System.Windows.Size(Math.Max(40, x.Text.Length * x.FontSize * 0.6), x.FontSize * 1.4)),
        NumberAnnotation x => new Rect(x.Center.X - x.Radius, x.Center.Y - x.Radius, x.Radius * 2, x.Radius * 2),
        RegionEffectAnnotation x => x.Region,
        SpotlightAnnotation x => x.Region,
        BubbleAnnotation x => Rect.Union(x.Region, new Rect(x.Tip, new System.Windows.Size(1, 1))),
        MagnifierAnnotation x => Rect.Union(x.Destination, new Rect(x.Source, new System.Windows.Size(1, 1))),
        _ => Rect.Empty
    };

    public static bool HitTest(Annotation annotation, Point point, double tolerance = 7)
    {
        var rawBounds = UnrotatedBounds(annotation);
        if (Math.Abs(annotation.Rotation) >= 0.001 && !rawBounds.IsEmpty)
            point = RotatePoint(point, Center(rawBounds), -annotation.Rotation);
        if (annotation is MagnifierAnnotation magnifier)
            return Inflate(magnifier.Destination, tolerance).Contains(point) || (magnifier.Source - point).Length <= tolerance;
        if (annotation is ShapeAnnotation { Tool: AnnotationTool.Line or AnnotationTool.Arrow } line)
            return DistanceToSegment(point, line.Start, line.End) <= tolerance + line.Thickness / 2;
        if (annotation is PenAnnotation pen)
            return SegmentsHit(pen.Points, point, tolerance + pen.Thickness / 2);
        if (annotation is PolygonAnnotation polygon)
            return UnrotatedBounds(polygon).Contains(point) || SegmentsHit(polygon.Points.Append(polygon.Points[0]).ToArray(), point, tolerance);
        return Inflate(rawBounds, tolerance).Contains(point);
    }

    public static Annotation Move(Annotation annotation, Vector delta) => annotation switch
    {
        ShapeAnnotation x => x with { Start = x.Start + delta, End = x.End + delta },
        PenAnnotation x => x with { Points = x.Points.Select(p => p + delta).ToArray() },
        PolygonAnnotation x => x with { Points = x.Points.Select(p => p + delta).ToArray() },
        TextAnnotation x => x with { Position = x.Position + delta },
        WatermarkAnnotation x => x with { Position = x.Position + delta },
        NumberAnnotation x => x with { Center = x.Center + delta },
        RegionEffectAnnotation x => x with { Region = Offset(x.Region, delta) },
        SpotlightAnnotation x => x with { Region = Offset(x.Region, delta) },
        BubbleAnnotation x => x with { Region = Offset(x.Region, delta), Tip = x.Tip + delta },
        MagnifierAnnotation x => x with { Source = x.Source + delta, Destination = Offset(x.Destination, delta) },
        _ => annotation
    };

    public static Annotation WithStrokeThickness(Annotation annotation, double thickness) => annotation switch
    {
        ShapeAnnotation x => Preserve(x, new ShapeAnnotation(x.Tool, x.Start, x.End,
            x.StrokeColor, thickness, x.FillColor) { ArrowStyle = x.ArrowStyle }),
        PenAnnotation x => Preserve(x, new PenAnnotation(x.Points,
            x.StrokeColor, thickness, x.IsHighlighter)),
        PolygonAnnotation x => Preserve(x, new PolygonAnnotation(x.Points,
            x.StrokeColor, thickness, x.FillColor)),
        NumberAnnotation x => Preserve(x, new NumberAnnotation(x.Center, x.Number,
            x.StrokeColor, Math.Max(12, thickness * 3), x.NumberStyle)),
        BubbleAnnotation x => Preserve(x, new BubbleAnnotation(x.Region, x.Tip, x.Text,
            x.StrokeColor, thickness, x.FillColor, x.FontSize, x.TextOutlineColor, x.TextOutlineThickness)
            { FontFamilyName = x.FontFamilyName }),
        _ => annotation
    };

    public static Annotation WithColor(Annotation annotation, Color color, bool recolorFill = true)
    {
        static Color Recolor(Color source, Color target) =>
            Color.FromArgb(source.A, target.R, target.G, target.B);
        return annotation switch
        {
            ShapeAnnotation x => Preserve(x, new ShapeAnnotation(x.Tool, x.Start, x.End, color,
                x.StrokeThickness, recolorFill && x.FillColor is Color fill ? Recolor(fill, color) : x.FillColor)
                { ArrowStyle = x.ArrowStyle }),
            PenAnnotation x => Preserve(x, new PenAnnotation(x.Points, color,
                x.StrokeThickness, x.IsHighlighter)),
            PolygonAnnotation x => Preserve(x, new PolygonAnnotation(x.Points, color,
                x.StrokeThickness, recolorFill && x.FillColor is Color fill ? Recolor(fill, color) : x.FillColor)),
            TextAnnotation x => Preserve(x, new TextAnnotation(x.Position, x.Text, color, x.FontSize,
                x.TextOutlineColor, x.TextOutlineThickness) { FontFamilyName = x.FontFamilyName }),
            WatermarkAnnotation x => Preserve(x, new WatermarkAnnotation(x.Position, x.Text, color,
                x.FontSize, x.Opacity, x.Angle, x.Tiled, x.CanvasWidth, x.CanvasHeight)),
            NumberAnnotation x => Preserve(x, new NumberAnnotation(x.Center, x.Number, color,
                x.Radius, x.NumberStyle)),
            BubbleAnnotation x => Preserve(x, new BubbleAnnotation(x.Region, x.Tip, x.Text, color,
                x.StrokeThickness, recolorFill ? Recolor(x.FillColor, color) : x.FillColor, x.FontSize,
                x.TextOutlineColor, x.TextOutlineThickness) { FontFamilyName = x.FontFamilyName }),
            _ => annotation
        };
    }

    public static Annotation Restyle(Annotation annotation, Color color, double thickness, Color? fill, double fontSize,
        AnnotationStrokeStyle strokeStyle)
    {
        var updated = annotation switch
        {
            ShapeAnnotation x => new ShapeAnnotation(x.Tool, x.Start, x.End, color, thickness, fill)
                { ArrowStyle = x.ArrowStyle },
            PenAnnotation x => new PenAnnotation(x.Points, color, thickness, x.IsHighlighter),
            PolygonAnnotation x => new PolygonAnnotation(x.Points, color, thickness, fill),
            TextAnnotation x => new TextAnnotation(x.Position, x.Text, color, fontSize,
                x.TextOutlineColor, x.TextOutlineThickness) { FontFamilyName = x.FontFamilyName },
            WatermarkAnnotation x => new WatermarkAnnotation(x.Position, x.Text, color, fontSize,
                x.Opacity, x.Angle, x.Tiled, x.CanvasWidth, x.CanvasHeight),
            NumberAnnotation x => new NumberAnnotation(x.Center, x.Number, color,
                Math.Max(12, thickness * 3), x.NumberStyle),
            BubbleAnnotation x => new BubbleAnnotation(x.Region, x.Tip, x.Text, color, thickness,
                fill ?? Colors.Transparent, fontSize, x.TextOutlineColor, x.TextOutlineThickness)
                { FontFamilyName = x.FontFamilyName },
            _ => annotation
        };
        return Preserve(annotation, updated, strokeStyle);
    }

    private static Annotation Preserve(Annotation source, Annotation target,
        AnnotationStrokeStyle? strokeStyle = null) => target with
    {
        Rotation = source.Rotation,
        StrokeStyle = strokeStyle ?? source.StrokeStyle
    };

    public static Annotation Resize(Annotation annotation, Rect from, Rect to)
    {
        if (to.Width < 1 || to.Height < 1) return annotation;
        Point Map(Point p) => new(
            from.Width < 1 ? to.Left + to.Width / 2 : to.Left + (p.X - from.Left) * to.Width / from.Width,
            from.Height < 1 ? to.Top + to.Height / 2 : to.Top + (p.Y - from.Top) * to.Height / from.Height);
        Rect MapRect(Rect r) => new(Map(r.TopLeft), Map(r.BottomRight));
        return annotation switch
        {
            ShapeAnnotation x => x with { Start = Map(x.Start), End = Map(x.End) },
            PenAnnotation x => x with { Points = x.Points.Select(Map).ToArray() },
            PolygonAnnotation x => x with { Points = x.Points.Select(Map).ToArray() },
            TextAnnotation x => x with { Position = Map(x.Position), FontSize = x.FontSize * to.Height / from.Height },
            WatermarkAnnotation x => x with { Position = Map(x.Position), FontSize = x.FontSize * to.Height / from.Height },
            NumberAnnotation x => x with { Center = Map(x.Center), Radius = x.Radius * Math.Min(to.Width / from.Width, to.Height / from.Height) },
            RegionEffectAnnotation x => x with { Region = MapRect(x.Region) },
            SpotlightAnnotation x => x with { Region = MapRect(x.Region) },
            BubbleAnnotation x => x with { Region = MapRect(x.Region), Tip = Map(x.Tip), FontSize = x.FontSize * to.Height / from.Height },
            MagnifierAnnotation x => x with { Source = Map(x.Source), Destination = MapRect(x.Destination) },
            _ => annotation
        };
    }

    public static Annotation Scale(Annotation annotation, double factor)
    {
        factor = Math.Clamp(factor, 0.1, 10);
        var bounds = UnrotatedBounds(annotation);
        if (bounds.IsEmpty) return annotation;
        var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        Point Map(Point point) => center + (point - center) * factor;
        Rect MapRect(Rect rect) => new(Map(rect.TopLeft), Map(rect.BottomRight));
        return annotation switch
        {
            ShapeAnnotation x => x with { Start = Map(x.Start), End = Map(x.End) },
            PenAnnotation x => x with { Points = x.Points.Select(Map).ToArray() },
            PolygonAnnotation x => x with { Points = x.Points.Select(Map).ToArray() },
            TextAnnotation x => x with { Position = Map(x.Position), FontSize = Math.Max(4, x.FontSize * factor) },
            WatermarkAnnotation x => x with { Position = Map(x.Position), FontSize = Math.Max(4, x.FontSize * factor) },
            NumberAnnotation x => x with { Center = Map(x.Center), Radius = Math.Max(4, x.Radius * factor) },
            RegionEffectAnnotation x => x with { Region = MapRect(x.Region) },
            SpotlightAnnotation x => x with { Region = MapRect(x.Region) },
            BubbleAnnotation x => x with
            {
                Region = MapRect(x.Region), Tip = Map(x.Tip), FontSize = Math.Max(4, x.FontSize * factor)
            },
            MagnifierAnnotation x => x with { Source = Map(x.Source), Destination = MapRect(x.Destination) },
            _ => annotation
        };
    }

    public static Annotation Rotate(Annotation annotation, double angle) =>
        annotation with { Rotation = NormalizeAngle(annotation.Rotation + angle) };

    public static Annotation Transform(Annotation annotation, Func<Point, Point> map,
        double scaleX = 1, double scaleY = 1, double rotationDelta = 0)
    {
        Rect MapRect(Rect rect)
        {
            var points = new[] { map(rect.TopLeft), map(rect.TopRight), map(rect.BottomRight), map(rect.BottomLeft) };
            return FromPoints(points);
        }

        var transformed = annotation switch
        {
            ShapeAnnotation x => x with { Start = map(x.Start), End = map(x.End) },
            PenAnnotation x => x with { Points = x.Points.Select(map).ToArray() },
            PolygonAnnotation x => x with { Points = x.Points.Select(map).ToArray() },
            TextAnnotation x => x with { Position = map(x.Position), FontSize = x.FontSize * Math.Abs(scaleY) },
            WatermarkAnnotation x => x with
            {
                Position = map(x.Position), FontSize = x.FontSize * Math.Abs(scaleY),
                CanvasWidth = x.CanvasWidth * Math.Abs(scaleX), CanvasHeight = x.CanvasHeight * Math.Abs(scaleY)
            },
            NumberAnnotation x => x with { Center = map(x.Center), Radius = x.Radius * Math.Min(Math.Abs(scaleX), Math.Abs(scaleY)) },
            RegionEffectAnnotation x => x with { Region = MapRect(x.Region) },
            SpotlightAnnotation x => x with
            {
                Region = MapRect(x.Region), CanvasWidth = x.CanvasWidth * Math.Abs(scaleX), CanvasHeight = x.CanvasHeight * Math.Abs(scaleY)
            },
            BubbleAnnotation x => x with
            {
                Region = MapRect(x.Region), Tip = map(x.Tip), FontSize = x.FontSize * Math.Abs(scaleY)
            },
            MagnifierAnnotation x => x with { Source = map(x.Source), Destination = MapRect(x.Destination) },
            _ => annotation
        };
        return transformed with { Rotation = NormalizeAngle(annotation.Rotation + rotationDelta) };
    }

    public static Annotation Translate(Annotation annotation, double x, double y) =>
        Move(annotation, new Vector(x, y));

    public static Point RotatePoint(Point point, Point center, double angle)
    {
        if (Math.Abs(angle) < 0.001) return point;
        var radians = angle * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;
        return new Point(center.X + dx * cos - dy * sin, center.Y + dx * sin + dy * cos);
    }

    public static double NormalizeAngle(double angle)
    {
        angle %= 360;
        if (angle > 180) angle -= 360;
        if (angle <= -180) angle += 360;
        return angle;
    }

    private static Rect FromPoints(IEnumerable<Point> points)
    {
        var array = points.ToArray();
        return new Rect(new Point(array.Min(p => p.X), array.Min(p => p.Y)), new Point(array.Max(p => p.X), array.Max(p => p.Y)));
    }
    private static Point Center(Rect rect) => new(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
    private static Rect TextBounds(TextAnnotation text)
    {
#pragma warning disable CS0618
        var formatted = new FormattedText(text.Text, System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, new Typeface(text.FontFamilyName), text.FontSize, Brushes.Black);
#pragma warning restore CS0618
        var bounds = new Rect(text.Position, new System.Windows.Size(
            Math.Max(24, formatted.WidthIncludingTrailingWhitespace), Math.Max(text.FontSize, formatted.Height)));
        if (text.TextOutlineColor is not null && text.TextOutlineThickness > 0)
            bounds.Inflate(text.TextOutlineThickness, text.TextOutlineThickness);
        return bounds;
    }
    private static Rect Inflate(Rect rect, double value) { rect.Inflate(value, value); return rect; }
    private static Rect Offset(Rect rect, Vector delta) { rect.Offset(delta); return rect; }
    private static bool SegmentsHit(IReadOnlyList<Point> points, Point p, double tolerance)
    {
        for (var i = 1; i < points.Count; i++) if (DistanceToSegment(p, points[i - 1], points[i]) <= tolerance) return true;
        return false;
    }
    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var length = (b - a).LengthSquared;
        if (length == 0) return (p - a).Length;
        var t = Math.Clamp(Vector.Multiply(p - a, b - a) / length, 0, 1);
        return (p - (a + (b - a) * t)).Length;
    }
}
