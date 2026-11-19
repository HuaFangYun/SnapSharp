using System.Windows;

namespace NimCap.Screenshot.Models;

public sealed record RecognizedTextSpan(int Start, int Length, Rect Bounds);
public sealed record RecognizedBlock(string Text, Rect Bounds, double? Confidence = null, string Kind = "text")
{
    public IReadOnlyList<RecognizedTextSpan>? TextSpans { get; init; }
}
public sealed record RecognitionDocument(IReadOnlyList<RecognizedBlock> Blocks, string Backend)
{
    public string Text => string.Join(Environment.NewLine, Blocks.Select(x => x.Text));
}
public sealed class RecognizedCell
{
    public int Row { get; set; }
    public int Column { get; set; }
    public int RowSpan { get; set; } = 1;
    public int ColumnSpan { get; set; } = 1;
    public string Text { get; set; } = "";
}
