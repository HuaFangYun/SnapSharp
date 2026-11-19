using System.Text.RegularExpressions;
using System.Windows;
using NimCap.Screenshot.Models;

namespace NimCap.Screenshot.Services;

public static partial class IdentityDocumentRecognition
{
    private static string[] BusinessLabels => Templates.Single(t => t.Kind == IdentityDocumentKind.BusinessLicense)
        .Fields.SelectMany(f => f.Labels).Select(Compact).Distinct().ToArray();

    // A large gap inside 名  称 / 类  型 / 住  所 is typography, not a column boundary.
    private static bool JoinsBusinessLabel(List<RecognizedBlock> segment, RecognizedBlock next)
    {
        var left = Compact(string.Concat(segment.Select(b => b.Text)));
        var right = Compact(next.Text);
        return BusinessLabels.Any(label => left.Length < label.Length && label.StartsWith(left, StringComparison.Ordinal)
            && right.StartsWith(label[left.Length..], StringComparison.Ordinal));
    }

    private static int BusinessValueQuality(string name, string text) => name switch
    {
        "统一社会信用代码" => ValueQuality(name, text),
        "成立日期" or "发照日期" => FooterDate(text) is not null ? 100 : 0,
        "登记机关" => Regex.IsMatch(Compact(text), @"^[\p{IsCJKUnifiedIdeographs}]{4,}(?:管理局|监督局|工商局)$", RegexOptions.CultureInvariant) ? 100
            : Compact(text).Contains("工商行政") || Compact(text).Contains("市场监督") ? 50 : 0,
        _ => 1
    };

    private static bool BusinessFooter(string text)
    {
        var value = Compact(text);
        return FooterDate(text) is not null || new[] { "登记机关", "发照日期", "国家企业信用", "企业信用信息", "市场主体应当", "年度报告", "监制", "工商行政管理", "市场监督管理", "扫码", "扫描二维码" }
            .Any(value.Contains);
    }

    // Business licenses have two columns, long wrapped values and a separate seal/footer.
    // Keep these rules out of the extraction path for ID cards and other documents.
    private static (string Text, Rect Bounds, double? Confidence) ReadBusinessValue(
        Anchor anchor, List<Anchor> allAnchors, IReadOnlyList<RecognizedBlock> allBlocks, IReadOnlyList<RecognizedBlock> sourceBlocks)
    {
        var blocks = allBlocks.Where(b => b.Kind == anchor.Block.Kind).ToArray();
        var anchors = allAnchors.Where(a => a.Block.Kind == anchor.Block.Kind).ToArray();
        var box = anchor.Bounds;
        var height = Math.Max(1, box.Height);
        bool Labeled(RecognizedBlock b) => anchors.Any(a => ReferenceEquals(a.Block, b));
        bool SameRow(Rect r) => Math.Abs(r.Top + r.Height / 2 - box.Top - box.Height / 2) <= Math.Min(height, r.Height) * .6;
        var columnRight = anchors.Where(a => a.Bounds.Left > box.Right + 1 && SameRow(a.Bounds))
            .Select(a => a.Bounds.Left).DefaultIfEmpty(double.MaxValue).Min();
        bool InColumn(Rect r) => r.Left >= box.Left - height && r.Left < columnRight - height * .25;
        var nextRow = anchors.Where(a => !ReferenceEquals(a.Block, anchor.Block) && a.Bounds.Top >= box.Bottom - 2 && InColumn(a.Bounds)
            // A right-hand column below a short left-hand field is not its next row.
            && a.Bounds.Left <= box.Right + height)
            .Select(a => a.Bounds.Top).DefaultIfEmpty(double.MaxValue).Min();

        var values = new List<RecognizedBlock>();
        if (anchor.Inline.Length > 0)
        {
            // Use detector geometry for the value, not the bounding box of label + value.
            // This matters when label characters are separated by a wide printed gap.
            var inline = Compact(anchor.Inline);
            var pieces = sourceBlocks.Where(b => b.Kind == anchor.Block.Kind && SameRow(b.Bounds)
                && b.Bounds.Left >= box.Left - 1 && b.Bounds.Right <= box.Right + 1)
                .OrderBy(b => b.Bounds.Left).ToArray();
            var firstValue = pieces.FirstOrDefault(b => Compact(b.Text).Length > 0 && inline.StartsWith(Compact(b.Text), StringComparison.Ordinal));
            var valueBounds = firstValue?.Bounds ?? EstimateBusinessValueBounds(anchor);
            if (firstValue is not null)
                valueBounds = pieces.Where(b => b.Bounds.Left >= firstValue.Bounds.Left).Select(b => b.Bounds).Aggregate(Rect.Union);
            values.Add(anchor.Block with { Text = anchor.Inline, Bounds = valueBounds });
        }
        else
        {
            values.AddRange(blocks.Where(b => !ReferenceEquals(b, anchor.Block) && !Labeled(b) && SameRow(b.Bounds)
                && b.Bounds.Left >= box.Right - 2 && b.Bounds.Left - box.Right <= height * 3 && InColumn(b.Bounds)
                && !BusinessFooter(b.Text)).OrderBy(b => b.Bounds.Left));
        }

        if (values.Count == 0 && anchor.Field.Name is "统一社会信用代码" or "注册号" or "证照编号")
        {
            var below = blocks.Where(b => !Labeled(b) && b.Bounds.Top >= box.Bottom - 2 && b.Bounds.Top < nextRow
                && b.Bounds.Top - box.Bottom < height * 3 && InColumn(b.Bounds)
                && b.Bounds.Left <= box.Right && Regex.IsMatch(Compact(b.Text), @"^[0-9A-Z()/-]{8,}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                .OrderBy(b => b.Bounds.Top).ThenBy(b => b.Bounds.Left).FirstOrDefault();
            if (below is not null) values.Add(below);
        }
        if (anchor.Field.Name == "登记机关")
        {
            // On older licenses the printed authority begins slightly above the
            // label and wraps beside it. Recover that printed prefix without
            // treating arbitrary curved seal fragments as a complete authority.
            var authorityLeft = values.Count > 0 ? values[0].Bounds.Left : box.Right;
            var above = blocks.Where(b => !Labeled(b) && b.Bounds.Left >= authorityLeft - height
                && b.Bounds.Top < box.Top && box.Top - b.Bounds.Top <= height
                && Math.Abs(b.Bounds.Height - height) <= height * .5
                && Regex.IsMatch(Compact(b.Text), @"(?:工商行政|市场监督)$", RegexOptions.CultureInvariant))
                .OrderByDescending(b => b.Bounds.Top).FirstOrDefault();
            if (above is not null) values.Insert(0, above);
        }
        if (values.Count == 0) return ("", box, null);

        if (anchor.Field.Name is "地址" or "经营范围" or "名称" or "类型" or "登记机关")
        {
            var valueLeft = values[0].Bounds.Left;
            // A single detector box has no character coordinates; labels can span
            // several widely spaced glyphs. Allow that uncertainty only for the
            // first wrapped line, then require subsequent lines to align with it.
            var leftTolerance = height * (anchor.Inline.Length > 0 && !sourceBlocks.Any(b => b.Kind == anchor.Block.Kind && Compact(b.Text) == Compact(anchor.Inline)) ? 8 : 2);
            var bottom = values.Max(b => b.Bounds.Bottom);
            var lineHeight = values[0].Bounds.Height;
            // DB-expanded boxes on adjacent tight lines overlap vertically. A
            // following line need not start below the first box's bottom edge.
            foreach (var b in blocks.Where(b => b.Bounds.Top >= values[0].Bounds.Top + values[0].Bounds.Height * .4 && b.Bounds.Top < nextRow && !Labeled(b)
                && !values.Contains(b) && InColumn(b.Bounds)).OrderBy(b => b.Bounds.Top).ThenBy(b => b.Bounds.Left))
            {
                // Printed continuation lines share a left edge and type size. Seal arcs
                // and distant footer fragments must not extend an address/scope diagonally.
                if (b.Bounds.Top - bottom > Math.Max(lineHeight, b.Bounds.Height) * 1.25) break;
                if (BusinessFooter(b.Text)) break;
                if (b.Confidence < .6) continue;
                if (Math.Abs(b.Bounds.Left - valueLeft) > leftTolerance || b.Bounds.Height < lineHeight * .6 || b.Bounds.Height > lineHeight * 1.5) continue;
                values.Add(b);
                valueLeft = b.Bounds.Left;
                leftTolerance = height * 2;
                bottom = Math.Max(bottom, b.Bounds.Bottom);
            }
        }
        return (string.Join(" ", values.Select(b => b.Text.Trim())), values.Aggregate(box, (r, b) => Rect.Union(r, b.Bounds)),
            values.Select(b => b.Confidence).Where(c => c.HasValue).DefaultIfEmpty(null).Min());
    }

    private static Rect EstimateBusinessValueBounds(Anchor anchor)
    {
        static double Width(string text) => text.Sum(c => char.IsWhiteSpace(c) ? .4 : c > 127 ? 2d : 1d);
        var fraction = Math.Clamp(Width(anchor.Inline) / Math.Max(1, Width(anchor.Block.Text)), 0, 1);
        var left = Math.Min(anchor.Bounds.Right - anchor.Bounds.Width * fraction,
            anchor.Bounds.Left + (Width(anchor.Block.Text) - Width(anchor.Inline)) * anchor.Bounds.Height / 2);
        return new Rect(left, anchor.Bounds.Top, anchor.Bounds.Right - left, anchor.Bounds.Height);
    }
}
