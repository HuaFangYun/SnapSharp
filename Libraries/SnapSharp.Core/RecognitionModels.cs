using System.Windows;

namespace SnapSharp.Models;

public sealed record RecognizedBlock(string Text, Rect Bounds, double? Confidence = null, string Kind = "text");
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
