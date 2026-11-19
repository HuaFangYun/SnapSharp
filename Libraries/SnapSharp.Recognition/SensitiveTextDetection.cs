using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using SnapSharp.Models;

namespace SnapSharp.Services;

/// <summary>Finds sensitive text regions in local OCR results, in source pixel coordinates.</summary>
public static class SensitiveTextDetection
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);
    private static readonly Regex Numbers = new(@"(?<![A-Za-z0-9])(?:\+?86-?)?1[3-9](?:-?[0-9]){9}(?![0-9])|(?<![A-Za-z0-9])[0-9]{17}[0-9Xx](?![A-Za-z0-9])|(?<![A-Za-z0-9])[0-9](?:-?[0-9]){12,18}(?![A-Za-z0-9])",
        RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Email = new(@"[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?\.[A-Za-z]{2,}",
        RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Plate = new(@"[京津沪渝冀豫云辽黑湘皖鲁新苏浙赣鄂桂甘晋蒙陕吉闽贵粤青藏川宁琼使领][A-HJ-NP-Z][·.・-]?(?:[A-HJ-NP-Z0-9]{5,6}|[A-HJ-NP-Z0-9]{4}[挂学警港澳领使])(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Address = new(@"[\p{IsCJKUnifiedIdeographs}]{2,20}(?:省|自治区|特别行政区|市|区|县|镇|乡|街道)[\p{IsCJKUnifiedIdeographs}0-9A-Za-z-]{1,50}(?:路|街|巷|弄|村|小区|大厦|广场|号|栋|幢|单元|室)|[\p{IsCJKUnifiedIdeographs}]{2,20}(?:路|街|巷|弄)[0-9一二三四五六七八九十百-]+号|(?<![A-Za-z0-9])[0-9]{1,6}\s+[A-Za-z][A-Za-z .'-]{1,60}\s(?:Street|St|Road|Rd|Avenue|Ave|Lane|Ln|Drive|Dr|Boulevard|Blvd)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex PrivateLabel = new(@"(?:户主姓名|姓名|联系人|收件人|收货人|持卡人|所有人|法定代表人|负责人|住址|住所|通讯地址|收货地址|联系地址|家庭地址|地址|号牌号码|车牌号码|车牌号|号牌|(?<![a-z])(?:full\s*name|name|address|licen[cs]e\s*plate)\s*:)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex OtherLabel = new(@"^(?:性别|民族|出生|国籍|有效|签发|证号|公民|身份|准驾|初次|注册|经营|类型|名称|统一|登记|电话|手机|邮箱|卡号|邮编|文化程度|婚姻|宗教|身高|血型|服务|兵役|迁入|与户主|户别|户号|曾用名|籍贯|gender|sex|date|nationality)[:：]?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex AddressTail = new(@"(?:省|市|区|县|镇|乡|路|街|巷|弄|村|小区|大厦|广场|号|栋|幢|单元|室|楼)|\b(?:apt|suite|unit|floor)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);

    public static IReadOnlyList<Rect> FindRegions(RecognitionDocument document, int width, int height, IReadOnlyList<string>? entities = null)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var blocks = document.Blocks.Where(b => b.Kind == "text" && !string.IsNullOrWhiteSpace(b.Text)
            && Valid(b.Bounds)).OrderBy(b => b.Bounds.Top).ThenBy(b => b.Bounds.Left).ToArray();
        var marked = new HashSet<RecognizedBlock>();
        MarkEntities(blocks, entities, marked);
        foreach (var block in blocks)
            if (Sensitive(block.Text)) marked.Add(block);

        // OCR engines may split a number or email into adjacent blocks. Join only
        // nearby blocks on the same baseline; retain each block's real bounds.
        var rows = new List<List<RecognizedBlock>>();
        foreach (var block in blocks)
        {
            var row = rows.LastOrDefault(r => Math.Abs(Center(r[0].Bounds) - Center(block.Bounds))
                <= Math.Min(r[0].Bounds.Height, block.Bounds.Height) * .5);
            if (row is null) rows.Add([block]); else row.Add(block);
        }
        foreach (var row in rows)
        {
            var segment = new List<RecognizedBlock>();
            foreach (var block in row.OrderBy(b => b.Bounds.Left))
            {
                if (segment.Count > 0 && block.Bounds.Left - segment[^1].Bounds.Right > Math.Max(block.Bounds.Height, segment[^1].Bounds.Height) * 2)
                {
                    MarkSegment(segment, marked); segment.Clear();
                }
                segment.Add(block);
            }
            MarkSegment(segment, marked);
        }

        foreach (var block in blocks)
        {
            var text = Compact(block.Text);
            var label = PrivateLabel.Match(text);
            var detectedAddress = Address.IsMatch(block.Text.Normalize(NormalizationForm.FormKC)) || Address.IsMatch(text);
            if (!label.Success && !detectedAddress) continue;
            marked.Add(block);
            var address = detectedAddress || label.Value.Contains("址") || label.Value.Contains("住所") || label.Value.Contains("address", StringComparison.OrdinalIgnoreCase);
            var labelOnly = label.Success && text[(label.Index + label.Length)..].Trim(':', '：').Length == 0;
            var fieldBounds = block.Bounds;
            // A separate label can have its value to the right or immediately below.
            if (labelOnly)
            {
                var right = blocks.Where(b => b != block && Math.Abs(Center(b.Bounds) - Center(block.Bounds)) <= block.Bounds.Height * .5
                    && b.Bounds.Left >= block.Bounds.Right && b.Bounds.Left - block.Bounds.Right <= block.Bounds.Height * 12)
                    .OrderBy(b => b.Bounds.Left).FirstOrDefault();
                if (right is not null && !OtherLabel.IsMatch(Compact(right.Text)) && !PrivateLabel.IsMatch(Compact(right.Text)))
                {
                    marked.Add(right);
                    fieldBounds.Union(right.Bounds);
                    if (!address) continue;
                }
            }
            if (!address && !labelOnly) continue;
            var previous = fieldBounds;
            for (var line = 0; line < (address ? 8 : 1); line++)
            {
                var next = blocks.Where(b => b.Bounds.Top >= previous.Bottom - 2 && b.Bounds.Top - previous.Bottom <= previous.Height * 1.5
                    && b.Bounds.Left >= fieldBounds.Left - block.Bounds.Height * 2
                    && b.Bounds.Left <= fieldBounds.Right + block.Bounds.Height * 2)
                    .OrderBy(b => b.Bounds.Top).ThenBy(b => b.Bounds.Left).FirstOrDefault();
                if (next is null || OtherLabel.IsMatch(Compact(next.Text)) || PrivateLabel.IsMatch(Compact(next.Text))) break;
                if (address && !AddressTail.IsMatch(next.Text)) break;
                marked.Add(next); previous = next.Bounds;
            }
        }
        var image = new Rect(0, 0, width, height);
        return marked.Select(b =>
        {
            var rect = b.Bounds;
            rect.Inflate(Math.Max(2, rect.Height * .12), Math.Max(2, rect.Height * .12));
            rect.Intersect(image);
            return rect;
        }).Where(r => !r.IsEmpty && r.Width > 0 && r.Height > 0).Distinct().ToArray();
    }

    private static void MarkSegment(List<RecognizedBlock> segment, HashSet<RecognizedBlock> marked)
    {
        var text = string.Concat(segment.Select(b => Compact(b.Text)));
        if (!Sensitive(text)) return;
        foreach (var block in segment) marked.Add(block);
    }

    private static bool Sensitive(string text)
    {
        var address = Address.IsMatch(text.Normalize(NormalizationForm.FormKC));
        text = Compact(text);
        return address || Numbers.IsMatch(text) || Email.IsMatch(text) || Plate.IsMatch(text) || Address.IsMatch(text) || PrivateLabel.IsMatch(text);
    }

    private static void MarkEntities(RecognizedBlock[] blocks, IReadOnlyList<string>? entities, HashSet<RecognizedBlock> marked)
    {
        if (entities is null || entities.Count == 0) return;
        var spans = new List<(RecognizedBlock Block, int Start, int End)>();
        var text = new StringBuilder();
        foreach (var block in blocks)
        {
            var start = text.Length;
            text.Append(Compact(block.Text));
            spans.Add((block, start, text.Length));
        }
        var source = text.ToString();
        foreach (var entity in entities.Take(512))
        {
            var value = Compact(entity);
            if (value.Length is < 2 or > 256) continue;
            for (var start = 0; start <= source.Length - value.Length;)
            {
                var index = source.IndexOf(value, start, StringComparison.OrdinalIgnoreCase);
                if (index < 0) break;
                foreach (var span in spans.Where(s => s.Start < index + value.Length && s.End > index)) marked.Add(span.Block);
                start = index + value.Length;
            }
        }
    }

    private static string Compact(string text) => string.Concat(text.Normalize(NormalizationForm.FormKC).Where(c => !char.IsWhiteSpace(c)));
    private static double Center(Rect rect) => rect.Top + rect.Height / 2;
    private static bool Valid(Rect rect) => !rect.IsEmpty && rect.Width > 0 && rect.Height > 0
        && double.IsFinite(rect.Left) && double.IsFinite(rect.Top) && double.IsFinite(rect.Right) && double.IsFinite(rect.Bottom);
}
