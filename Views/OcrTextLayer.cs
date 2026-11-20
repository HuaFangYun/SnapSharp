using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NimCap.Screenshot.Models;
using NimCap.Screenshot.Services;
using Brush = System.Windows.Media.Brush;
using Size = System.Windows.Size;

namespace NimCap.Screenshot.Views;

// Coordinates stay in source image pixels. The selection window applies exactly
// the same transform and shape clip as its image and annotation surfaces.
internal sealed class OcrTextSelection
{
    internal sealed record Character(int Start, int Length, Rect Bounds);
    private readonly List<Character> _characters = [];
    private readonly List<Rect> _lines = [];
    internal IReadOnlyList<Character> Characters => _characters;
    internal string Text { get; private set; } = "";
    internal int Anchor { get; private set; }
    internal int Caret { get; private set; }
    internal string SelectedText => Text[Math.Min(Anchor, Caret)..Math.Max(Anchor, Caret)];
    internal IEnumerable<Rect> SelectedBounds => _characters
        .Where(c => c.Start < Math.Max(Anchor, Caret) && c.Start + c.Length > Math.Min(Anchor, Caret))
        .Select(c => c.Bounds);

    internal OcrTextSelection(RecognitionDocument document, Size imageSize)
    {
        var text = new StringBuilder();
        var imageBounds = new Rect(imageSize);
        var widths = new Dictionary<string, double>();
        foreach (var block in LocalRecognitionService.ReadingOrder(document.Blocks))
        {
            if (string.IsNullOrWhiteSpace(block.Text)) continue;
            if (text.Length > 0) text.Append(Environment.NewLine);
            var offset = text.Length;
            text.Append(block.Text);
            // Bounds control selection hit testing, never whether recognized text
            // survives copy-all (some engines return rounded/invalid edge boxes).
            if (!Valid(block.Bounds)) continue;
            var bounds = Rect.Intersect(block.Bounds, imageBounds);
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) continue;
            _lines.Add(bounds);
            var starts = StringInfo.ParseCombiningCharacters(block.Text);
            var positions = new Rect[starts.Length];
            FillBounds(0, block.Text.Length, block.Bounds);
            foreach (var span in block.TextSpans ?? [])
                if (span.Start >= 0 && span.Length > 0 && (long)span.Start + span.Length <= block.Text.Length && Valid(span.Bounds))
                    FillBounds(span.Start, span.Length, span.Bounds);
            for (var i = 0; i < starts.Length; i++)
            {
                var end = i + 1 < starts.Length ? starts[i + 1] : block.Text.Length;
                var rect = Rect.Intersect(positions[i], imageBounds);
                if (!rect.IsEmpty && rect.Width > 0 && rect.Height > 0)
                    _characters.Add(new(offset + starts[i], end - starts[i], rect));
            }

            void FillBounds(int start, int length, Rect region)
            {
                var first = Array.BinarySearch(starts, start);
                var last = start + length == block.Text.Length ? starts.Length : Array.BinarySearch(starts, start + length);
                // Never split a surrogate pair or a combining-character sequence.
                if (first < 0 || last <= first) return;
                var weights = new double[last - first];
                for (var i = first; i < last; i++)
                {
                    var end = i + 1 < starts.Length ? starts[i + 1] : block.Text.Length;
                    var element = block.Text[starts[i]..end];
                    if (!widths.TryGetValue(element, out var width))
                    {
                        width = Math.Max(1, new FormattedText(element, CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 16, Brushes.Black, 1).WidthIncludingTrailingWhitespace);
                        widths[element] = width;
                    }
                    weights[i - first] = width;
                }
                var total = weights.Sum();
                var x = region.Left;
                for (var i = first; i < last; i++)
                {
                    var width = region.Width * weights[i - first] / total;
                    positions[i] = new Rect(x, region.Top, width, region.Height);
                    x += width;
                }
            }
        }
        Text = text.ToString();
    }

    private static bool Valid(Rect rect) => !rect.IsEmpty && rect.Width > 0 && rect.Height > 0 &&
        double.IsFinite(rect.X) && double.IsFinite(rect.Y) && double.IsFinite(rect.Right) && double.IsFinite(rect.Bottom);

    internal bool Contains(Point point) => _lines.Any(line => line.Contains(point));

    internal bool Begin(Point point)
    {
        Anchor = Caret = 0;
        if (!Contains(point)) return false;
        Anchor = Caret = Hit(point);
        return true;
    }

    internal void Extend(Point point) => Caret = Hit(point);
    internal void SelectAll() { Anchor = 0; Caret = Text.Length; }

    private int Hit(Point point)
    {
        if (_characters.Count == 0) return 0;
        var nearest = _characters[0];
        var bestY = double.PositiveInfinity;
        var bestX = double.PositiveInfinity;
        foreach (var character in _characters)
        {
            var dy = Math.Max(0, Math.Max(character.Bounds.Top - point.Y, point.Y - character.Bounds.Bottom));
            var dx = Math.Max(0, Math.Max(character.Bounds.Left - point.X, point.X - character.Bounds.Right));
            if (dy < bestY || dy == bestY && dx < bestX)
            {
                nearest = character;
                bestY = dy;
                bestX = dx;
            }
        }
        return point.X < nearest.Bounds.Left + nearest.Bounds.Width / 2 ? nearest.Start : nearest.Start + nearest.Length;
    }
}

public sealed class OcrTextLayer : FrameworkElement
{
    private static readonly Brush SelectionBrush = new SolidColorBrush(Color.FromArgb(105, 40, 125, 245));
    private bool _dragging;
    internal OcrTextSelection? Selection { get; private set; }
    public event Action<string>? CopyRequested;

    public OcrTextLayer()
    {
        Focusable = true;
        Cursor = Cursors.IBeam;
        ClipToBounds = true;
    }

    internal void SetDocument(RecognitionDocument? document, Size imageSize)
    {
        _dragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        Selection = document is null ? null : new OcrTextSelection(document, imageSize);
        InvalidateVisual();
    }

    internal void SelectAll() { Selection?.SelectAll(); InvalidateVisual(); }
    internal void CopySelection()
    {
        if (Selection?.SelectedText is { Length: > 0 } text) CopyRequested?.Invoke(text);
    }
    internal void CopyAll()
    {
        SelectAll();
        CopySelection();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        // Keep the screenshot's own glyphs visible; only selection adds pixels.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (Selection is null) return;
        foreach (var rect in Selection.SelectedBounds) drawingContext.DrawRectangle(SelectionBrush, null, rect);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        _dragging = BeginSelection(e.GetPosition(this)) && CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            ExtendSelection(e.GetPosition(this));
            e.Handled = true;
        }
        Cursor = _dragging || Selection?.Contains(e.GetPosition(this)) == true ? Cursors.IBeam : Cursors.Arrow;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        EndSelection(e.GetPosition(this));
        e.Handled = true;
    }

    internal bool BeginSelection(Point point)
    {
        _dragging = Selection?.Begin(point) == true;
        InvalidateVisual();
        return _dragging;
    }

    internal void ExtendSelection(Point point)
    {
        if (!_dragging) return;
        Selection?.Extend(point);
        InvalidateVisual();
    }

    internal void EndSelection(Point point)
    {
        if (!_dragging) return;
        Selection?.Extend(point);
        _dragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        InvalidateVisual();
        CopySelection();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _dragging = false;
        base.OnLostMouseCapture(e);
    }
}
