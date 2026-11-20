using System.Text;
using NimCap.Screenshot.Models;

namespace NimCap.Screenshot.Services;

public static class OcrTextCleanup
{
    // Windows OCR inserts word separators even between individual Chinese glyphs.
    // Remap offsets together with the text so selecting on the image stays accurate.
    public static RecognitionDocument RemoveCjkSpacing(RecognitionDocument document) => document with
    {
        Blocks = document.Blocks.Select(Clean).ToArray()
    };

    private static RecognizedBlock Clean(RecognizedBlock block)
    {
        var source = block.Text;
        var text = new StringBuilder(source.Length);
        var offsets = new int[source.Length + 1];
        for (var i = 0; i < source.Length;)
        {
            if (source[i] is '\r' or '\n' || !char.IsWhiteSpace(source[i]))
            {
                offsets[i] = text.Length;
                text.Append(source[i++]);
                continue;
            }
            var start = i;
            while (i < source.Length && source[i] is not ('\r' or '\n') && char.IsWhiteSpace(source[i])) i++;
            var remove = start > 0 && i < source.Length &&
                (IsCjkAt(source, char.IsLowSurrogate(source[start - 1]) && start > 1 ? start - 2 : start - 1) ||
                 IsCjkAt(source, i));
            for (var j = start; j < i; j++)
            {
                offsets[j] = text.Length;
                if (!remove) text.Append(source[j]);
            }
        }
        offsets[source.Length] = text.Length;
        return block with
        {
            Text = text.ToString(),
            TextSpans = block.TextSpans?.Where(s => s.Start >= 0 && s.Length > 0 && (long)s.Start + s.Length <= source.Length)
                .Select(s => s with { Start = offsets[s.Start], Length = offsets[s.Start + s.Length] - offsets[s.Start] })
                .Where(s => s.Length > 0).ToArray()
        };
    }

    private static bool IsCjkAt(string text, int offset) => Rune.TryGetRuneAt(text, offset, out var rune) &&
        rune.Value is (>= 0x3400 and <= 0x9fff) or (>= 0xf900 and <= 0xfaff)
        or (>= 0x20000 and <= 0x323af) or (>= 0x3000 and <= 0x303f) or (>= 0xff01 and <= 0xff65);
}
