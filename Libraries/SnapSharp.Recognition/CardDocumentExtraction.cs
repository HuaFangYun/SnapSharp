using System.Text.RegularExpressions;
using System.Windows;
using SnapSharp.Models;

namespace SnapSharp.Services;

public static partial class IdentityDocumentRecognition
{
    private static IdentityDocumentField Extracted(string name, string value, IEnumerable<RecognizedBlock> evidence, bool review = false)
    {
        var blocks = evidence.ToArray();
        return new() { Name = name, Value = value, OriginalValue = string.Join(" ", blocks.Select(b => b.Text)),
            Bounds = blocks.Length == 0 ? Rect.Empty : blocks.Select(b => b.Bounds).Aggregate(Rect.Union),
            NeedsReview = review || value.Length == 0 || blocks.Any(b => b.Confidence is null or < .85) };
    }

    private static IdentityDocumentResult RecognizeBankCard(RecognitionDocument document)
    {
        var rows = AssembleRows(document.Blocks);
        var numbers = rows.SelectMany(b => Regex.Matches(Compact(b.Text), @"(?<!\d)\d{12,19}(?!\d)")
            .Select(m => (Block: b, Value: m.Value))).ToArray();
        // Luhn is a warning, not evidence of what is printed. A different digit
        // sequence can pass its checksum by chance (and public demo cards may
        // intentionally use fictional numbers). Rank actual OCR evidence instead.
        var selected = numbers.OrderByDescending(n => n.Value.Length >= 16)
            .ThenByDescending(n => n.Block.Confidence ?? 0)
            .ThenBy(n => n.Block.Kind.StartsWith("document-", StringComparison.Ordinal)).FirstOrDefault();
        var issuer = document.Blocks.Where(b => b.Kind == "text").Select(b => (Block: b, Match: Regex.Match(Compact(b.Text), @"[\p{IsCJKUnifiedIdeographs}]{2,}银行")))
            .FirstOrDefault(x => x.Match.Success);
        var expiry = document.Blocks.Where(b => b.Kind == "text").Select(b => (Block: b, Match: Regex.Match(Compact(b.Text), @"(?<!\d)(?:0[1-9]|1[0-2])/\d{2}(?!\d)")))
            .FirstOrDefault(x => x.Match.Success);
        return new(IdentityDocumentKind.BankCard,
        [Extracted("卡号", selected.Value ?? "", selected.Block is null ? [] : [selected.Block], selected.Value is null || !IsValidBankNumber(selected.Value) || numbers.Select(n => n.Value).Distinct().Count() > 1),
         Extracted("发卡行", issuer.Match?.Value ?? "", issuer.Block is null ? [] : [issuer.Block]),
         Extracted("有效期", expiry.Match?.Value ?? "", expiry.Block is null ? [] : [expiry.Block])]);
    }

    // Label blocks and value blocks are separate on passports. Read below each
    // label within its column rather than returning its English translation.
    private sealed record SpatialLabel(string Name, RecognizedBlock Block, string Inline);
    private static IdentityDocumentResult RecognizePassport(RecognitionDocument document)
    {
        var specs = new[] { F("姓名", "姓名", "Name"), F("姓", "姓", "Surname"), F("名", "名", "Givennames", "Givenname"),
            F("护照号", "护照号码", "护照号", "PassportNo.", "PassportNo", "PassportNumber"), F("国籍", "国籍", "Nationality"), F("国家码", "国家码", "CountryCode"),
            F("性别", "性别", "Sex"), F("出生日期", "出生日期", "Dateofbirth"), F("出生地点", "出生地点", "Placeofbirth"),
            F("签发地", "签发地点", "签发地", "Placeofissue"), F("签发日期", "签发日期", "Dateofissue"),
            F("有效期", "有效期至", "有效期限", "有效期", "Dateofexpiry"), F("签发机关", "签发机关", "Authority"),
            F("_类型", "类型", "Type"), F("_签名", "持照人签名", "Bearer") };
        var source = document.Blocks.Where(b => !b.Kind.StartsWith("document-", StringComparison.Ordinal)).ToArray();
        var labels = new List<SpatialLabel>();
        foreach (var block in source)
        {
            var text = Compact(block.Text);
            var match = specs.SelectMany(s => s.Labels.Select(l => (Spec: s, Label: Compact(l))))
                .OrderByDescending(x => x.Label.Length).FirstOrDefault(x => text.StartsWith(x.Label, StringComparison.OrdinalIgnoreCase)
                    || x.Label == "Nationality" && text.Contains("/Nationality", StringComparison.OrdinalIgnoreCase));
            if (match.Spec is null) continue;
            var tail = text.StartsWith(match.Label, StringComparison.OrdinalIgnoreCase) ? text[match.Label.Length..] : "";
            // A slash introduces the printed translation, including OCR misspellings.
            if (tail.StartsWith('/')) tail = "";
            labels.Add(new(match.Spec.Name, block, tail.Trim(':', '：')));
        }
        var fields = new List<IdentityDocumentField>();
        foreach (var spec in specs.Where(s => !s.Name.StartsWith('_')))
        {
            var found = labels.Where(l => l.Name == spec.Name).ToArray();
            var values = new List<RecognizedBlock>();
            foreach (var label in found)
            {
                if (label.Inline.Length > 0) { values.Add(label.Block with { Text = label.Inline }); break; }
                var r = label.Block.Bounds;
                var right = labels.Where(l => l.Block.Bounds.Left > r.Left + r.Height && Math.Abs(l.Block.Bounds.Top - r.Top) < r.Height)
                    .Select(l => l.Block.Bounds.Left).DefaultIfEmpty(double.MaxValue).Min();
                var next = labels.Where(l => l.Block.Bounds.Top > r.Bottom && Math.Abs(l.Block.Bounds.Left - r.Left) < r.Height * 2)
                    .Select(l => l.Block.Bounds.Top).DefaultIfEmpty(r.Bottom + r.Height * 4).Min();
                var below = source.Where(b => !labels.Any(l => ReferenceEquals(l.Block, b)) && b.Bounds.Top >= r.Bottom - r.Height * .25
                    && b.Bounds.Top < next && b.Bounds.Top - r.Bottom <= r.Height * 2
                    && b.Bounds.Left >= r.Left - r.Height && b.Bounds.Left < Math.Min(right - r.Height * .25, r.Left + r.Height * 2)
                    && !Compact(b.Text).Equals("PASSPORT", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(b => b.Bounds.Top).ToArray();
                if (below.Length > 0) { values.Add(below[0]); break; }
            }
            fields.Add(Extracted(spec.Name, string.Join("", values.Select(b => b.Text)), values));
        }
        if (fields.Single(f => f.Name == "姓名").Value.Length == 0)
        {
            var surname = fields.Single(f => f.Name == "姓"); var given = fields.Single(f => f.Name == "名");
            if (surname.Value.Length > 0 && given.Value.Length > 0)
            {
                var full = surname.Value.Split('/')[0] + given.Value.Split('/')[0];
                fields[fields.FindIndex(f => f.Name == "姓名")] = new() { Name = "姓名", Value = full, OriginalValue = surname.OriginalValue + " " + given.OriginalValue,
                    Bounds = Rect.Union(surname.Bounds, given.Bounds), NeedsReview = surname.NeedsReview || given.NeedsReview };
            }
        }
        return new(IdentityDocumentKind.Passport, fields);
    }

    private static string CleanLicenseValue(IdentityDocumentKind kind, string name, string value)
    {
        if (kind is not (IdentityDocumentKind.DrivingLicense or IdentityDocumentKind.VehicleLicense)) return value;
        value = Regex.Replace(value, @"^(?:Address?|Name|Class|Date\s*of\s*first\s*issue|Date\s*of\s*birth)[\s.:]*", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (name == "准驾车型") value = Regex.Replace(value, @"^Class[\s.:]*", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (name == "性别")
        {
            var genders = Regex.Matches(value, "男|女").Select(m => m.Value).Distinct().ToArray();
            if (genders.Length == 1) return genders[0];
        }
        return value;
    }

    private static void RefineLicenseFields(IdentityDocumentKind kind, List<IdentityDocumentField> fields, IReadOnlyList<RecognizedBlock> blocks)
    {
        var original = blocks.Where(b => !b.Kind.StartsWith("document-", StringComparison.Ordinal)).ToArray();
        if (kind == IdentityDocumentKind.DrivingLicense)
        {
            var label = original.FirstOrDefault(b => Compact(b.Text) == "姓名");
            if (label is not null)
            {
                var r = label.Bounds;
                var name = original.Where(b => b.Bounds.Left >= r.Right - 2 && b.Bounds.Left - r.Right < r.Height * 6
                    && Math.Abs(b.Bounds.Top + b.Bounds.Height / 2 - r.Top - r.Height / 2) < r.Height
                    && Regex.IsMatch(Compact(b.Text), @"^[\p{IsCJKUnifiedIdeographs}·]{2,20}$") && !new[] { "国籍", "性别" }.Contains(Compact(b.Text)))
                    .OrderBy(b => b.Bounds.Left).FirstOrDefault();
                if (name is not null) fields[fields.FindIndex(f => f.Name == "姓名")] = Extracted("姓名", name.Text, [label, name]);
            }
            var gender = fields.Single(f => f.Name == "性别");
            if (gender.Value is not ("男" or "女"))
            {
                var found = original.Select(b => (Block: b, Match: Regex.Match(Compact(b.Text), @"(?:性别|别)(男|女)")))
                    .FirstOrDefault(x => x.Match.Success);
                if (found.Block is not null) fields[fields.FindIndex(f => f.Name == "性别")] = Extracted("性别", found.Match.Groups[1].Value, [found.Block], true);
            }
            foreach (var fieldName in new[] { "性别", "国籍" })
            {
                if (fields.Single(f => f.Name == fieldName).Value.Length > 0) continue;
                var printedLabel = original.FirstOrDefault(b => Compact(b.Text) == fieldName);
                if (printedLabel is null) continue;
                var r = printedLabel.Bounds;
                var matches = original.Where(b => !ReferenceEquals(b, printedLabel) && Math.Abs(b.Bounds.Top - r.Top) <= r.Height
                    && b.Bounds.Left >= r.Left - r.Height * 6 && b.Bounds.Left <= r.Right + r.Height * 3)
                    .Select(b => (Block: b, Match: Regex.Match(Compact(b.Text), fieldName == "性别" ? @"^(男|女)" : @"(?:[a-zA-Z]+|别[男女])([\p{IsCJKUnifiedIdeographs}]{2,})$")))
                    .Where(x => x.Match.Success).ToArray();
                if (matches.Select(x => x.Match.Groups[1].Value).Distinct().Count() == 1)
                    fields[fields.FindIndex(f => f.Name == fieldName)] = Extracted(fieldName, matches[0].Match.Groups[1].Value, [printedLabel, matches[0].Block], true);
            }
            var birthLabel = original.FirstOrDefault(b => Compact(b.Text) == "出生日期");
            if (birthLabel is not null && fields.Single(f => f.Name == "核发机关").Value.Length == 0)
            {
                var r = birthLabel.Bounds;
                var stamp = original.Where(b => b.Bounds.Right <= r.Left + 2 && b.Bounds.Left >= r.Left - r.Height * 8
                    && b.Bounds.Top >= r.Top - r.Height && b.Bounds.Top <= r.Top + r.Height * 5
                    && Regex.IsMatch(Compact(b.Text), @"^[\p{IsCJKUnifiedIdeographs}]{2,}$"))
                    .OrderBy(b => b.Bounds.Top).ToArray();
                var text = string.Concat(stamp.Select(b => Compact(b.Text)));
                if (Regex.IsMatch(text, @"公安局.*(?:总队|支队)$"))
                    fields[fields.FindIndex(f => f.Name == "核发机关")] = Extracted("核发机关", text, stamp);
            }
        }
        // A missing label glyph is an OCR failure. A visible number in another
        // field must not be borrowed to fill it.
    }

    private static void RecoverIdName(List<IdentityDocumentField> fields, IReadOnlyList<RecognizedBlock> blocks)
    {
        var source = blocks.Where(b => !b.Kind.StartsWith("document-", StringComparison.Ordinal)).ToArray();
        var gender = source.FirstOrDefault(b => Compact(b.Text).Contains("性别"));
        if (gender is null) return;
        var r = gender.Bounds;
        var names = source.Where(b => b.Bounds.Top < r.Top && r.Top - b.Bounds.Bottom < r.Height * 2
            && Math.Abs(b.Bounds.Left - r.Left) < r.Height * 2
            && Regex.IsMatch(Compact(b.Text), @"^[\p{IsCJKUnifiedIdeographs}·]{2,20}$")
            && !new[] { "居民身份证", "中华人民共和国" }.Contains(Compact(b.Text))).ToArray();
        if (names.Length == 1) fields[fields.FindIndex(f => f.Name == "姓名")] = Extracted("姓名", names[0].Text, names, true);
    }
}
