using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using NimCap.Screenshot.Models;

namespace NimCap.Screenshot.Services;

/// <summary>Maps minimal sensitive substrings back to OCR source coordinates.</summary>
public static class SensitiveTextDetection
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);
    private static Regex R(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Numbers = R(@"(?<![A-Za-z0-9])(?:\+?86-?)?1[3-9](?:-?[0-9]){9}(?![0-9])|(?<![A-Za-z0-9])[0-9]{17}[0-9Xx](?![A-Za-z0-9])|(?<![A-Za-z0-9])[0-9](?:-?[0-9]){12,18}(?![A-Za-z0-9])");
    private static readonly Regex Email = R(@"[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?\.[A-Za-z]{2,}");
    private static readonly Regex Plate = R(@"[京津沪渝冀豫云辽黑湘皖鲁新苏浙赣鄂桂甘晋蒙陕吉闽贵粤青藏川宁琼使领][A-HJ-NP-Z][·.・-]?(?:[A-HJ-NP-Z0-9]{5,6}|[A-HJ-NP-Z0-9]{4}[挂学警港澳领使])(?![A-Za-z0-9])");
    private static readonly Regex Address = R(@"[\p{IsCJKUnifiedIdeographs}]{2,16}(?:省|自治区|特别行政区|市|区|县|镇|乡|街道)[\p{IsCJKUnifiedIdeographs}0-9A-Za-z-]{1,40}(?:路|街|巷|弄|村|小区|大厦|广场)(?:[0-9一二三四五六七八九十百-]+(?:号|栋|幢|单元|室|楼))*|[\p{IsCJKUnifiedIdeographs}]{2,16}(?:路|街|巷|弄)[0-9一二三四五六七八九十百-]+号(?:[0-9A-Za-z-]+(?:栋|幢|单元|室|楼))*");
    private static readonly Regex EnglishAddress = R(@"(?<![A-Za-z0-9])[0-9]{1,6}\s+[A-Za-z][A-Za-z .'-]{1,60}\s(?:Street|St|Road|Rd|Avenue|Ave|Lane|Ln|Drive|Dr|Boulevard|Blvd)\b");
    private static readonly Regex Label = R(@"户主姓名|姓名|联系人|收件人|收货人|持卡人|所有人|法定代表人|负责人|报警人|反馈人|主办民警|警情地址|报警地址|发案地点|住址|住所|通讯地址|收货地址|联系地址|家庭地址|地址|号牌号码|车牌号码|车牌号|号牌|(?<![a-z])(?:fullname|name|address|licen[cs]eplate):");
    private static readonly Regex Field = R(@"(?:户主姓名|姓名|联系人|收件人|收货人|持卡人|所有人|法定代表人|负责人|报警人性别|报警人|反馈人|主办民警|警情地址|报警地址|发案地点|住址|住所|地址|性别|民族|出生日期|出生|国籍|有效期限|有效期|签发机关|证号|公民身份号码|身份证号|准驾车型|初次领证日期|注册日期|经营范围|类型|名称|统一社会信用代码|登记机关|电话|手机|邮箱|卡号|邮编|文化程度|婚姻|户别|户号|籍贯|报警类别|报警内容|案件名称|案件编号|受理单位|受理时间|承办单位|gender|sex|date|nationality)[:：]?");
    private static readonly Regex Narrative = R(@"驾驶|涉嫌|报警|民警|出警|检测|通知|办理|移交|依法|来到|赶到|发现|反馈|经查|现场");
    private static readonly Regex AddressFragment = R(@"^[\p{IsCJKUnifiedIdeographs}A-Za-z0-9-]{1,50}(?:省|市|区|县|镇|乡|路|街|巷|弄|村|小区|大厦|广场|号|栋|幢|单元|室|楼)[\p{IsCJKUnifiedIdeographs}A-Za-z0-9-]{0,12}$");

    private sealed record SourceCharacter(RecognizedBlock Block, int Start, int Length);
    private sealed class TextRun
    {
        public string Text { get; }
        public List<SourceCharacter> Map { get; } = [];
        public TextRun(IEnumerable<RecognizedBlock> blocks, bool compact = true)
        {
            var text = new StringBuilder();
            foreach (var block in blocks)
            {
                var elements = StringInfo.GetTextElementEnumerator(block.Text);
                while (elements.MoveNext())
                {
                    var original = elements.GetTextElement();
                    foreach (var c in original.Normalize(NormalizationForm.FormKC))
                    {
                        if (compact && char.IsWhiteSpace(c)) continue;
                        text.Append(c);
                        Map.Add(new(block, elements.ElementIndex, original.Length));
                    }
                }
            }
            Text = text.ToString();
        }
    }

    public static IReadOnlyList<Rect> FindRegions(RecognitionDocument document, int width, int height, IReadOnlyList<string>? entities = null)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var blocks = document.Blocks.Where(b => b.Kind == "text" && !string.IsNullOrWhiteSpace(b.Text) && Valid(b.Bounds))
            .OrderBy(b => b.Bounds.Top).ThenBy(b => b.Bounds.Left).ToArray();
        var regions = new List<Rect>();
        void Mark(TextRun run, int start, int length)
        {
            if (length <= 0) return;
            foreach (var group in run.Map.Skip(start).Take(length).GroupBy(c => c.Block))
            {
                var from = group.Min(c => c.Start); var to = group.Max(c => c.Start + c.Length);
                regions.AddRange(SubstringBounds(group.Key, from, to - from));
            }
        }
        void MatchRules(TextRun run)
        {
            foreach (var regex in new[] { Numbers, Email, Plate })
                foreach (Match match in regex.Matches(run.Text)) Mark(run, match.Index, match.Length);
            foreach (Match match in Address.Matches(run.Text))
            {
                var value = match.Value;
                var context = Regex.Matches(value, "位于|前往|住在|地址[:：]?|地点[:：]?|在|到|至", RegexOptions.None, Timeout).LastOrDefault();
                var offset = context is null ? 0 : context.Index + context.Length;
                if (!Narrative.IsMatch(value[offset..])) Mark(run, match.Index + offset, value.Length - offset);
            }
        }
        foreach (var block in blocks)
        {
            var run = new TextRun([block]);
            MatchRules(run);
            var spaced = new TextRun([block], false);
            foreach (Match match in EnglishAddress.Matches(spaced.Text)) Mark(spaced, match.Index, match.Length);
            foreach (Match label in Label.Matches(run.Text))
            {
                var start = label.Index + label.Length;
                while (start < run.Text.Length && run.Text[start] is ':' or '：') start++;
                var end = run.Text.IndexOfAny(['，', ',', '。', ';', '；', '(', '（', ')', '）'], start);
                if (end < 0) end = run.Text.Length;
                var nextField = Field.Match(run.Text, start);
                if (nextField.Success) end = Math.Min(end, nextField.Index);
                var address = label.Value.Contains('址') || label.Value.Contains("住所") || label.Value.Contains("地点") || label.Value.Contains("address", StringComparison.OrdinalIgnoreCase);
                var plate = label.Value.Contains("牌") || label.Value.Contains("plate", StringComparison.OrdinalIgnoreCase);
                var value = run.Text[start..Math.Max(start, end)];
                if (value.Length > 0)
                {
                    if (value is "无" or "未知" or "不详" or "暂无") continue;
                    if (!address && !plate)
                    {
                        var prose = Narrative.Match(value);
                        if (prose.Success) value = value[..prose.Index];
                        var name = Regex.Match(value, @"^[\p{IsCJKUnifiedIdeographs}·]{2,6}|^[A-Za-z][A-Za-z.'-]{1,39}", RegexOptions.None, Timeout);
                        if (name.Success) Mark(run, start, name.Length);
                    }
                    else if (!Narrative.IsMatch(value)) Mark(run, start, value.Length);
                }
                else if (start == run.Text.Length)
                {
                    var right = blocks.Where(b => b != block && Math.Abs(Center(b.Bounds) - Center(block.Bounds)) <= block.Bounds.Height * .5
                        && b.Bounds.Left >= block.Bounds.Right && b.Bounds.Left - block.Bounds.Right <= block.Bounds.Height * 12)
                        .OrderBy(b => b.Bounds.Left).FirstOrDefault() ?? NextLine(block, block.Bounds, blocks);
                    if (right is not null)
                    {
                        var next = new TextRun([right]);
                        if (!Field.IsMatch(next.Text) && !Narrative.IsMatch(next.Text)
                            && (address ? next.Text.Length <= 60 : next.Text.Length <= (plate ? 12 : 40))) Mark(next, 0, next.Text.Length);
                    }
                }
                // Continuation is limited to standalone address fragments, never prose rows.
                if (address && !Narrative.IsMatch(value))
                {
                    var previous = block;
                    for (var line = 0; line < 8; line++)
                    {
                        var next = NextLine(previous, block.Bounds, blocks);
                        if (next is null) break;
                        var part = new TextRun([next]);
                        if (!AddressFragment.IsMatch(part.Text) || Field.IsMatch(part.Text) || Narrative.IsMatch(part.Text)) break;
                        Mark(part, 0, part.Text.Length); previous = next;
                    }
                }
            }
        }
        // Joining a row never expands a match to unrelated blocks on that row.
        var rows = new List<List<RecognizedBlock>>();
        foreach (var block in blocks)
        {
            var row = rows.LastOrDefault(r => Math.Abs(Center(r[0].Bounds) - Center(block.Bounds)) <= Math.Min(r[0].Bounds.Height, block.Bounds.Height) * .5);
            if (row is null) rows.Add([block]); else row.Add(block);
        }
        foreach (var row in rows)
        {
            var segment = new List<RecognizedBlock>();
            foreach (var block in row.OrderBy(b => b.Bounds.Left))
            {
                if (segment.Count > 0 && block.Bounds.Left - segment[^1].Bounds.Right > Math.Max(block.Bounds.Height, segment[^1].Bounds.Height) * 2)
                { MatchRules(new TextRun(segment)); segment.Clear(); }
                segment.Add(block);
            }
            MatchRules(new TextRun(segment));
        }
        var source = new TextRun(blocks);
        foreach (var entity in entities?.Take(512) ?? [])
        {
            var value = Compact(entity);
            if (value.Length is < 2 or > 256) continue;
            for (var from = 0; from <= source.Text.Length - value.Length;)
            {
                var index = source.Text.IndexOf(value, from, StringComparison.OrdinalIgnoreCase);
                if (index < 0) break;
                Mark(source, index, value.Length); from = index + value.Length;
            }
        }
        var image = new Rect(0, 0, width, height);
        return regions.Select(r =>
        {
            r.Inflate(Math.Clamp(r.Height * .06, 1, 2), Math.Clamp(r.Height * .1, 1, 2));
            r.Intersect(image); return r;
        }).Where(r => !r.IsEmpty && r.Width > 0 && r.Height > 0).Distinct().ToArray();
    }

    private static RecognizedBlock? NextLine(RecognizedBlock previous, Rect column, RecognizedBlock[] blocks) =>
        blocks.Where(b => b != previous && b.Bounds.Top >= previous.Bounds.Bottom - 2 && b.Bounds.Top - previous.Bounds.Bottom <= previous.Bounds.Height * 1.5
            && b.Bounds.Left >= column.Left - column.Height * 2 && b.Bounds.Left <= column.Right + column.Height * 2)
            .OrderBy(b => b.Bounds.Top).ThenBy(b => b.Bounds.Left).FirstOrDefault();

    private static IEnumerable<Rect> SubstringBounds(RecognizedBlock block, int start, int length)
    {
        var end = start + length;
        var spans = block.TextSpans?.Where(s => s.Start >= 0 && s.Length > 0 && s.Start + s.Length <= block.Text.Length && Valid(s.Bounds)).ToArray();
        if (spans is { Length: > 0 })
        {
            var covered = new HashSet<int>();
            var matched = Rect.Empty;
            foreach (var span in spans.Where(s => s.Start < end && s.Start + s.Length > start))
            {
                var from = Math.Max(start, span.Start); var to = Math.Min(end, span.Start + span.Length);
                matched.Union(Estimate(block.Text.Substring(span.Start, span.Length), span.Bounds, from - span.Start, to - from));
                for (var i = from; i < to; i++) covered.Add(i);
            }
            if (Enumerable.Range(start, length).All(i => char.IsWhiteSpace(block.Text[i]) || covered.Contains(i)))
            { if (!matched.IsEmpty) yield return matched; yield break; }
        }
        var lines = block.Text.Split('\n');
        var offset = 0;
        for (var line = 0; line < lines.Length; line++)
        {
            var content = lines[line].TrimEnd('\r');
            var from = Math.Max(start, offset); var to = Math.Min(end, offset + content.Length);
            if (to > from)
                yield return Estimate(content, new Rect(block.Bounds.Left, block.Bounds.Top + block.Bounds.Height * line / lines.Length,
                    block.Bounds.Width, block.Bounds.Height / lines.Length), from - offset, to - from);
            offset += lines[line].Length + 1;
        }
    }

    private static Rect Estimate(string text, Rect bounds, int start, int length)
    {
        // Line-only/plugin results use relative glyph advances. Native OCR positions
        // keep interpolation within one word/character when they are available.
        static double Width(string value) => value.Sum(c => char.IsWhiteSpace(c) ? .35 : c > 255 ? 1 : "ilI.,:;'!|".Contains(c) ? .28 : "MWmw@".Contains(c) ? .85 : .55);
        var total = Width(text);
        if (total <= 0) return Rect.Empty;
        return new Rect(bounds.Left + bounds.Width * Width(text[..start]) / total, bounds.Top,
            bounds.Width * Width(text.Substring(start, length)) / total, bounds.Height);
    }

    private static string Compact(string text) => string.Concat(text.Normalize(NormalizationForm.FormKC).Where(c => !char.IsWhiteSpace(c)));
    private static double Center(Rect rect) => rect.Top + rect.Height / 2;
    private static bool Valid(Rect rect) => !rect.IsEmpty && rect.Width > 0 && rect.Height > 0
        && double.IsFinite(rect.Left) && double.IsFinite(rect.Top) && double.IsFinite(rect.Right) && double.IsFinite(rect.Bottom);
}
