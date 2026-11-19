using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using NimCap.Screenshot.Models;

namespace NimCap.Screenshot.Services;

public enum IdentityDocumentKind { Unknown, IdCardFront, IdCardBack, IdCardBoth, DrivingLicense, VehicleLicense, HouseholdRegister, Passport, BankCard, BusinessLicense }

public sealed class IdentityDocumentField
{
    private string _value = "";
    public string Name { get; init; } = "";
    public string Value { get => _value; set => _value = IdentityDocumentRecognition.CleanFieldValue(value); }
    public string OriginalValue { get; init; } = "";
    public Rect Bounds { get; init; }
    public bool NeedsReview { get; init; }
}

public sealed record IdentityDocumentResult(IdentityDocumentKind Kind, IReadOnlyList<IdentityDocumentField> Fields);

/// <summary>Local, conservative extraction from OCR blocks. No network or invented field values.</summary>
public static partial class IdentityDocumentRecognition
{
    private sealed record Field(string Name, string[] Labels);
    private sealed record Template(IdentityDocumentKind Kind, string[] Titles, string[] Signals, Field[] Fields);
    private sealed record Anchor(Field Field, RecognizedBlock Block, string Inline, Rect Bounds);
    private static Field F(string name, params string[] labels) => new(name, labels);
    private static readonly Field Name = F("姓名", "姓名", "姓 名");
    private static readonly Field Gender = F("性别", "性别");
    private static readonly Field Birth = F("出生日期", "出生日期", "出生", "Date of birth");
    private static readonly Field Address = F("地址", "住址", "住所", "地址");
    private static readonly Field Validity = F("有效期", "有效期限", "有效期至", "有效期", "Date of expiry");
    private static readonly Template[] Templates =
    [
        new(IdentityDocumentKind.IdCardFront, [], ["公民身份号码", "民族", "出生", "姓名"],
            [Name, Gender, F("民族", "民族"), Birth, Address, F("身份证号", "公民身份号码", "身份证号码", "身份证号")]),
        new(IdentityDocumentKind.IdCardBack, ["居民身份证"], ["签发机关", "有效期限"],
            [F("签发机关", "签发机关"), Validity]),
        new(IdentityDocumentKind.DrivingLicense, ["机动车驾驶证", "驾驶证副页"], ["准驾车型", "初次领证日期", "档案编号"],
            [Name, F("证号", "证号"), Gender, F("国籍", "国籍"), Address, Birth, F("初次领证日期", "初次领证日期"), F("准驾车型", "准驾车型"), Validity,
             F("核发机关", "核发机关", "发证机关"), F("档案编号", "档案编号"), F("记录", "记录")]),
        new(IdentityDocumentKind.VehicleLicense, ["机动车行驶证", "行驶证副页"], ["号牌号码", "车辆识别代号", "发动机号码", "品牌型号"],
            [F("号牌号码", "号牌号码", "号牌"), F("车辆类型", "车辆类型"), F("所有人", "所有人"), Address,
             F("品牌型号", "品牌型号"), F("VIN", "车辆识别代号", "VIN"), F("发动机号", "发动机号码", "发动机号"), F("注册日期", "注册日期", "注日期"), F("发证日期", "发证日期")]),
        new(IdentityDocumentKind.HouseholdRegister, ["居民户口簿", "常住人口登记卡"], ["户别", "户主姓名", "与户主关系", "户号"],
            [F("户别", "户别"), F("户主姓名", "户主姓名"), F("户号", "户号"), Address, Name, F("与户主关系", "与户主关系", "与户主或户主关系"), Gender, Birth,
             F("民族", "民族"), F("身份证号", "公民身份号码", "身份证号码"), F("文化程度", "文化程度")]),
        new(IdentityDocumentKind.Passport, ["护照", "PASSPORT"], ["护照号码", "Nationality", "Date of expiry"],
            [Name, F("姓", "Surname", "姓"), F("名", "Given names", "名"), F("护照号", "护照号码", "护照号", "Passport No."),
             F("国籍", "国籍", "Nationality"), Birth, F("签发地", "签发地点", "签发地", "Place of issue"), Validity]),
        new(IdentityDocumentKind.BankCard, ["储蓄卡", "借记卡", "信用卡"], ["卡号", "有效期", "银行"],
            [F("卡号", "卡号"), F("发卡行", "发卡行"), Validity]),
        new(IdentityDocumentKind.BusinessLicense, ["营业执照"], ["统一社会信用代码", "经营范围", "法定代表人"],
            [F("统一社会信用代码", "统一社会信用代码", "社会信用代码"), F("注册号", "注册号"), F("证照编号", "证照编号", "编号"),
             F("名称", "企业名称", "公司名称", "名称"), F("类型", "主体类型", "企业类型", "公司类型", "类型"),
             F("法定代表人", "法定代表人", "经营者"), F("负责人", "负责人"), F("注册资本", "注册资本", "注册资金"),
             F("成立日期", "成立日期", "成立时间"), F("营业期限", "营业期限", "经营期限"),
             F("经营范围", "经营范围"), F("地址", "主要经营场所", "经营场所", "营业场所", "住所", "住址", "地址"),
             F("登记机关", "登记机关"), F("发照日期", "发照日期", "核发日期")])
    ];

    public static IdentityDocumentResult Recognize(RecognitionDocument document, IdentityDocumentKind? overrideKind = null)
    {
        var sourceBlocks = document.Blocks;
        var businessLayout = overrideKind == IdentityDocumentKind.BusinessLicense || IsBusinessLicense(document);
        document = document with { Blocks = AssembleRows(document.Blocks, businessLayout) };
        var text = Compact(document.Text);
        var ranked = Templates.Select(t => (Template: t, Score: Score(t, text))).OrderByDescending(x => x.Score).ToArray();
        var front = ranked.Single(x => x.Template.Kind == IdentityDocumentKind.IdCardFront);
        var back = ranked.Single(x => x.Template.Kind == IdentityDocumentKind.IdCardBack);
        var both = front.Score >= 4 && back.Score >= 4 && ranked.Where(x => x.Template.Kind is not (IdentityDocumentKind.IdCardFront or IdentityDocumentKind.IdCardBack)).All(x => x.Score < Math.Max(front.Score, back.Score));
        var kind = overrideKind ?? (both ? IdentityDocumentKind.IdCardBoth
            : ranked[0].Score >= 4 && ranked[0].Score - ranked[1].Score >= 2 ? ranked[0].Template.Kind : IdentityDocumentKind.Unknown);
        // An ID title alone cannot determine the side, or distinguish a document mentioning an ID card.
        var template = kind == IdentityDocumentKind.IdCardBoth
            ? new Template(kind, [], [], [.. front.Template.Fields, .. back.Template.Fields])
            : Templates.FirstOrDefault(t => t.Kind == kind);
        if (template is null) return new(IdentityDocumentKind.Unknown, []);
        if (kind == IdentityDocumentKind.BankCard) return RecognizeBankCard(new RecognitionDocument(sourceBlocks, document.Backend));
        if (kind == IdentityDocumentKind.Passport) return RecognizePassport(new RecognitionDocument(sourceBlocks, document.Backend));
        if (kind == IdentityDocumentKind.HouseholdRegister) return RecognizeHousehold(new RecognitionDocument(sourceBlocks, document.Backend));

        var anchors = new List<Anchor>();
        foreach (var block in document.Blocks)
        {
            var value = block.Text.Normalize(NormalizationForm.FormKC);
            var labelMap = template.Fields.SelectMany(f => f.Labels.Select(l => (Label: Compact(l), Field: f)))
                .DistinctBy(x => x.Label).OrderByDescending(x => x.Label.Length).ToArray();
            // Whitespace inside printed Chinese labels is common in OCR output.
            var pattern = string.Join("|", labelMap.Select(x => string.Join(@"\s*", x.Label.Select(c => Regex.Escape(c.ToString())))));
            var matches = Regex.Matches(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            for (var i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                var field = labelMap.First(x => Compact(x.Label).Equals(Compact(match.Value), StringComparison.OrdinalIgnoreCase)).Field;
                // Do not interpret 姓/名 in the middle of another word as passport labels.
                if (match.Length == 1 && match.Index > 0 && char.IsLetter(value[match.Index - 1])) continue;
                var end = i + 1 < matches.Count ? matches[i + 1].Index : value.Length;
                var bounds = block.Bounds;
                if (matches.Count > 1)
                {
                    static double Width(string s) => s.Sum(c => char.IsWhiteSpace(c) ? .4 : c > 127 ? 2d : 1d);
                    var total = Math.Max(1, Width(value));
                    bounds = new Rect(block.Bounds.Left + block.Bounds.Width * Width(value[..match.Index]) / total, block.Bounds.Top,
                        block.Bounds.Width * Width(value[match.Index..end]) / total, block.Bounds.Height);
                }
                anchors.Add(new(field, block, value[(match.Index + match.Length)..end].Trim(' ', ':', '：', '\r', '\n', '\t', '/'), bounds));
            }
        }

        var fields = new List<IdentityDocumentField>();
        foreach (var spec in template.Fields)
        {
            var found = anchors.Where(a => a.Field == spec).OrderBy(a => a.Block.Kind.StartsWith("document-", StringComparison.Ordinal)).ToArray();
            var candidates = found.Select(a => kind == IdentityDocumentKind.BusinessLicense
                ? ReadBusinessValue(a, anchors, document.Blocks, sourceBlocks)
                : ReadValue(a, anchors, document.Blocks, businessLayout))
                .Select(v => (Text: CleanLicenseValue(kind, spec.Name, v.Text), Raw: v.Text, v.Bounds, v.Confidence)).Where(v => v.Text.Length > 0).ToArray();
            if (candidates.Length == 0)
            {
                fields.Add(new() { Name = spec.Name, NeedsReview = true });
                continue;
            }
            // A longer retry can contain a neighboring seal or footer. Prefer primary
            // business OCR when it already provides a value; numbers still use validity.
            var first = candidates.OrderByDescending(c => kind == IdentityDocumentKind.BusinessLicense ? BusinessValueQuality(spec.Name, c.Text) : ValueQuality(spec.Name, c.Text))
                .ThenByDescending(c => kind == IdentityDocumentKind.BusinessLicense ? 0 : c.Confidence ?? 0).First();
            var original = first.Raw;
            var value = CleanFieldValue(first.Text);
            if (kind == IdentityDocumentKind.BusinessLicense && spec.Name == "注册号")
                value = Regex.Replace(value, @"\(\d+/\d+\)$", "", RegexOptions.CultureInvariant);
            if (spec.Name == "统一社会信用代码")
            {
                var code = Regex.Match(value, @"(?<![0-9A-Z])[0-9A-Z]{18}(?![0-9A-Z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (code.Success) value = code.Value.ToUpperInvariant();
                else if (!Regex.IsMatch(value, @"[0-9A-Z]{8,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) value = "";
            }
            if (spec.Name == "准驾车型")
            {
                var corrected = Compact(value).ToUpperInvariant().Replace('L', '1').Replace('I', '1');
                if (Regex.IsMatch(corrected, @"^(?:A[123]|B[12]|C[1-6]|[DEFMNP])+$", RegexOptions.CultureInvariant)) value = corrected;
            }
            var review = first.Confidence is null or < .85 || candidates.Any(c => Compact(c.Text) != Compact(first.Text));
            if (spec.Name == "身份证号") review |= !IsValidIdNumber(Compact(value));
            if (spec.Name == "出生日期") review |= kind == IdentityDocumentKind.DrivingLicense ? FooterDate(value) is null : ValueQuality(spec.Name, value) == 0;
            if (spec.Name == "性别") review |= value is not ("男" or "女");
            if (spec.Name == "卡号") review |= !IsValidBankNumber(Compact(value));
            if (spec.Name == "统一社会信用代码") review |= !Regex.IsMatch(value, @"^[0-9A-HJ-NPQRTUWXY]{18}$", RegexOptions.CultureInvariant);
            if (spec.Name == "注册资本") review |= value.Contains("方元");
            if (kind == IdentityDocumentKind.BusinessLicense && spec.Name is "成立日期" or "发照日期") review |= FooterDate(value) is null;
            if (kind is IdentityDocumentKind.DrivingLicense or IdentityDocumentKind.VehicleLicense)
            {
                if (spec.Name is "初次领证日期" or "注册日期" or "发证日期") review |= FooterDate(value) is null;
                if (spec.Name == "有效期") review |= Regex.Matches(value, @"\d{4}[-./]\d{1,2}[-./]\d{1,2}").Cast<Match>().Any(m => FooterDate(m.Value) is null);
            }
            fields.Add(new() { Name = spec.Name, Value = value, OriginalValue = original, Bounds = first.Bounds, NeedsReview = review || Compact(value) != Compact(original) });
        }
        if (kind is IdentityDocumentKind.IdCardFront or IdentityDocumentKind.IdCardBoth && fields.All(f => f.Name != "身份证号" || f.Value.Length == 0))
            AddNumber(fields, document, "身份证号", @"(?<![0-9A-Z])[1-9][0-9]{16}[0-9Xx](?![0-9A-Z])", IsValidIdNumber);
        if (kind == IdentityDocumentKind.BankCard && fields.All(f => f.Name != "卡号" || f.Value.Length == 0))
            AddNumber(fields, document, "卡号", @"(?<!\d)(?:\d[ -]?){15,18}\d(?!\d)", IsValidBankNumber);
        if (kind == IdentityDocumentKind.BusinessLicense && fields.Single(f => f.Name == "发照日期").Value.Length == 0)
        {
            var authority = anchors.Where(a => a.Field.Name == "登记机关").ToArray();
            // Date digits can have wide blank spaces under separately printed 年/月/日.
            // Reassemble these footer rows without applying the body column-gap rule.
            var dates = AssembleRows(sourceBlocks).Where(b => authority.Any(a => a.Block.Kind == b.Kind && b.Bounds.Top >= a.Bounds.Bottom - 2 && b.Bounds.Top - a.Bounds.Bottom <= a.Bounds.Height * 10
                && b.Bounds.Left >= a.Bounds.Left - a.Bounds.Height * 2)).Select(b => (Block: b, Date: FooterDate(b.Text))).Where(x => x.Date is not null).ToArray();
            if (dates.Select(x => x.Date).Distinct().Count() == 1)
            {
                var date = dates[0];
                fields.RemoveAll(f => f.Name == "发照日期");
                fields.Add(new() { Name = "发照日期", Value = date.Date!, OriginalValue = date.Block.Text, Bounds = date.Block.Bounds,
                    NeedsReview = date.Block.Confidence is null or < .85 });
            }
        }
        if (kind is IdentityDocumentKind.DrivingLicense or IdentityDocumentKind.VehicleLicense)
            RefineLicenseFields(kind, fields, sourceBlocks);
        if (kind is IdentityDocumentKind.IdCardFront or IdentityDocumentKind.IdCardBoth && fields.Single(f => f.Name == "姓名").Value.Length == 0)
            RecoverIdName(fields, sourceBlocks);
        return new(kind, fields);
    }

    private static string? FooterDate(string text)
    {
        var match = Regex.Match(text.Trim(), @"^(\d{4})\s*(?:年|[./-]|\s)\s*(\d{1,2})\s*(?:月|[./-]|\s)\s*(\d{1,2})\s*日?$", RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        if (!DateTime.TryParseExact($"{match.Groups[1].Value}-{int.Parse(match.Groups[2].Value):00}-{int.Parse(match.Groups[3].Value):00}", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date)) return null;
        return $"{date.Year}年{date.Month:00}月{date.Day:00}日";
    }

    // Detection boxes often overlap after DB unclip. Reassemble a row before matching
    // spaced labels (姓 / 名, 住 / 址) and values split over several OCR boxes.
    public static bool IsBusinessLicense(RecognitionDocument document)
    {
        var text = Compact(document.Text);
        return text.Contains("营业执照") || new[] { "统一社会信用代码", "注册资本", "经营范围", "法定代表人" }.Count(text.Contains) >= 2;
    }

    public static IReadOnlyList<RecognizedBlock> AssembleRows(IReadOnlyList<RecognizedBlock> blocks, bool separateColumns = false)
    {
        if (separateColumns)
            blocks = blocks.Where(b => !(b.Confidence < .5 && Regex.IsMatch(Compact(b.Text), @"^[0-9A-Za-z]{1,2}$", RegexOptions.CultureInvariant))).ToArray();
        if (blocks.Select(b => b.Kind).Distinct().Count() > 1)
            return blocks.GroupBy(b => b.Kind).SelectMany(g => AssembleRows(g.ToArray(), separateColumns)).ToArray();
        var rows = new List<List<RecognizedBlock>>();
        foreach (var block in blocks.OrderBy(b => b.Bounds.Top + b.Bounds.Height / 2))
        {
            var row = rows.LastOrDefault();
            var center = block.Bounds.Top + block.Bounds.Height / 2;
            if (row is null || Math.Abs(center - row.Average(b => b.Bounds.Top + b.Bounds.Height / 2)) > Math.Min(block.Bounds.Height, row.Min(b => b.Bounds.Height)) * .55
                || row.Any(b => b.Kind != block.Kind)) rows.Add([block]);
            else row.Add(block);
        }
        if (separateColumns)
        {
            var segments = new List<List<RecognizedBlock>>();
            foreach (var row in rows)
            {
                List<RecognizedBlock> segment = [];
                foreach (var block in row.OrderBy(b => b.Bounds.Left))
                {
                    if (segment.Count > 0 && (block.Bounds.Left - segment.Max(b => b.Bounds.Right) > Math.Max(block.Bounds.Height, segment.Max(b => b.Bounds.Height)) * 3
                        || Math.Max(block.Bounds.Height, segment.Max(b => b.Bounds.Height)) > Math.Min(block.Bounds.Height, segment.Min(b => b.Bounds.Height)) * 3)
                        && !JoinsBusinessLabel(segment, block))
                    { segments.Add(segment); segment = []; }
                    segment.Add(block);
                }
                if (segment.Count > 0) segments.Add(segment);
            }
            rows = segments;
        }
        return rows.Select(row =>
        {
            var sorted = row.OrderBy(b => b.Bounds.Left).ToArray();
            var confidence = sorted.Any(b => !b.Confidence.HasValue) ? null : sorted.Min(b => b.Confidence);
            return new RecognizedBlock(string.Join(" ", sorted.Select(b => b.Text)), sorted.Select(b => b.Bounds).Aggregate(Rect.Union), confidence, sorted[0].Kind);
        }).ToArray();
    }

    public static string CleanFieldValue(string value) => Regex.Replace(value.Normalize(NormalizationForm.FormKC), @"[\s\p{Cf}]+", "", RegexOptions.CultureInvariant);

    private static int ValueQuality(string name, string value)
    {
        var compact = Compact(value);
        if (name == "身份证号") return IsValidIdNumber(compact) ? 100 : Regex.IsMatch(compact, @"^[0-9]{17}[0-9Xx]$") ? 50 : 0;
        if (name == "统一社会信用代码") return Regex.IsMatch(compact, @"[0-9A-Z]{18}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ? 100 : 0;
        if (name == "出生日期") return Regex.IsMatch(compact, @"^\d{4}年\d{1,2}月\d{1,2}日$") ? 100 : 0;
        if (name == "性别") return compact is "男" or "女" ? 100 : 0;
        if (name == "姓名") return Regex.IsMatch(compact, @"^[\p{IsCJKUnifiedIdeographs}·]{2,20}$", RegexOptions.CultureInvariant) ? 100 : 1;
        if (name is "地址" or "经营范围" or "记录") return compact.Length;
        return compact.Length > 0 ? 1 : 0;
    }

    private static int Score(Template template, string text)
    {
        var signals = template.Signals.Count(s => text.Contains(Compact(s), StringComparison.OrdinalIgnoreCase));
        var title = template.Titles.Any(s => text.Contains(Compact(s), StringComparison.OrdinalIgnoreCase));
        if (template.Kind == IdentityDocumentKind.IdCardBack && signals < 2) return 0;
        if (template.Kind == IdentityDocumentKind.IdCardFront && !text.Contains("公民身份号码") && !(text.Contains("民族") && signals >= 3)) return 0;
        if (template.Kind == IdentityDocumentKind.BankCard)
            return title || text.Contains("银行") && (text.Contains("银联") || text.Contains("UnionPay", StringComparison.OrdinalIgnoreCase)) ? 8 : 0;
        if (template.Kind == IdentityDocumentKind.HouseholdRegister && title) return 14 + signals * 2;
        return (title ? 6 : 0) + signals * 2;
    }

    private static (string Text, Rect Bounds, double? Confidence) ReadValue(Anchor anchor, List<Anchor> anchors, IReadOnlyList<RecognizedBlock> blocks, bool separateColumns)
    {
        // Keep original and retry passes separate so alternate OCR boxes do not
        // duplicate address continuations or block one another's labels.
        blocks = blocks.Where(b => b.Kind == anchor.Block.Kind).ToArray();
        anchors = anchors.Where(a => a.Block.Kind == anchor.Block.Kind).ToList();
        var box = anchor.Bounds;
        var neighbors = blocks.Where(b => !ReferenceEquals(b, anchor.Block) && !anchors.Any(a => ReferenceEquals(a.Block, b)))
            .Where(b => b.Bounds.Left >= box.Right - 3 && Math.Abs(b.Bounds.Top + b.Bounds.Height / 2 - box.Top - box.Height / 2) <= Math.Max(box.Height, b.Bounds.Height) * .6)
            .Where(b => !separateColumns || b.Bounds.Left - box.Right <= box.Height * 3)
            .OrderBy(b => b.Bounds.Left).ToArray();
        // Stop before the next label in the same row (e.g. 性别 男 民族 汉).
        var nextLabel = anchors.Where(a => a.Bounds.Left > box.Right && Math.Abs(a.Bounds.Top - box.Top) < box.Height)
            .Select(a => a.Bounds.Left).DefaultIfEmpty(double.MaxValue).Min();
        var values = anchor.Inline.Length > 0 ? new[] { anchor.Block with { Text = anchor.Inline, Bounds = box } } : neighbors.TakeWhile(b => b.Bounds.Left < nextLabel).ToArray();
        bool SameColumn(Rect bounds) => !separateColumns || bounds.Left < box.Right + box.Height && bounds.Right > box.Left - box.Height;
        var nextRow = anchors.Where(a => a.Bounds.Top >= box.Bottom - 2 && !ReferenceEquals(a.Block, anchor.Block) && SameColumn(a.Bounds))
            .Select(a => a.Bounds.Top).DefaultIfEmpty(double.MaxValue).Min();
        if (values.Length == 0 && separateColumns && anchor.Field.Name is "统一社会信用代码" or "注册号" or "证照编号")
            values = blocks.Where(b => b.Bounds.Top >= box.Bottom - 2 && b.Bounds.Top < nextRow && b.Bounds.Top - box.Bottom < box.Height * 3 && SameColumn(b.Bounds) && !anchors.Any(a => ReferenceEquals(a.Block, b)))
                .OrderBy(b => b.Bounds.Top).ThenBy(b => b.Bounds.Left).Take(1).ToArray();
        if (values.Length == 0) return ("", box, null);
        if (anchor.Field.Name is "地址" or "经营范围" or "记录" || separateColumns && anchor.Field.Name is "名称" or "类型" or "登记机关")
        {
            var bottom = values.Max(b => b.Bounds.Bottom);
            var continuation = blocks.Where(b => b.Bounds.Top >= box.Bottom - 2 && b.Bounds.Top < nextRow && b.Bounds.Left >= box.Left - box.Height && b.Bounds.Left <= values.Max(v => v.Bounds.Right)
                && SameColumn(b.Bounds) && !values.Contains(b) && !anchors.Any(a => ReferenceEquals(a.Block, b))).OrderBy(b => b.Bounds.Top).ThenBy(b => b.Bounds.Left).ToArray();
            var extra = new List<RecognizedBlock>();
            foreach (var b in continuation)
            {
                if (separateColumns && (FooterDate(b.Text) is not null || Compact(b.Text).Contains("国家企业信用信息") || Compact(b.Text).Contains("监制"))) break;
                if (b.Bounds.Top - bottom > Math.Max(box.Height, b.Bounds.Height) * 2.5) break;
                extra.Add(b); bottom = Math.Max(bottom, b.Bounds.Bottom);
            }
            values = [.. values, .. extra];
        }
        var bounds = values.Aggregate(box, (r, b) => Rect.Union(r, b.Bounds));
        var confidence = values.Select(b => b.Confidence).Where(c => c.HasValue).DefaultIfEmpty(null).Min();
        return (string.Join(" ", values.Select(b => b.Text.Trim())), bounds, confidence);
    }

    private static void AddNumber(List<IdentityDocumentField> fields, RecognitionDocument document, string name, string pattern, Func<string, bool> validate)
    {
        var candidates = document.Blocks.SelectMany(b => Regex.Matches(Compact(b.Text), pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            .Select(m => (Block: b, Value: m.Value))).ToArray();
        if (candidates.Length != 1) return;
        var c = candidates[0];
        fields.RemoveAll(f => f.Name == name && f.Value.Length == 0);
        fields.Add(new() { Name = name, Value = c.Value, OriginalValue = c.Value, Bounds = c.Block.Bounds, NeedsReview = !validate(Compact(c.Value)) || c.Block.Confidence is null or < .85 });
    }

    private static string Compact(string value) => CleanFieldValue(value);

    public static bool IsValidIdNumber(string value)
    {
        if (!Regex.IsMatch(value, @"^[1-9][0-9]{16}[0-9Xx]$", RegexOptions.CultureInvariant)) return false;
        if (!DateTime.TryParseExact(value.Substring(6, 8), "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)) return false;
        int[] weights = [7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2];
        var sum = Enumerable.Range(0, 17).Sum(i => (value[i] - '0') * weights[i]);
        return char.ToUpperInvariant(value[17]) == "10X98765432"[sum % 11];
    }

    public static bool IsValidBankNumber(string value)
    {
        value = value.Replace(" ", "").Replace("-", "");
        if (!Regex.IsMatch(value, @"^[0-9]{16,19}$", RegexOptions.CultureInvariant)) return false;
        var sum = 0;
        for (var i = value.Length - 1; i >= 0; i--) { var digit = value[i] - '0'; if ((value.Length - 1 - i) % 2 == 1) { digit *= 2; if (digit > 9) digit -= 9; } sum += digit; }
        return sum % 10 == 0;
    }
}
