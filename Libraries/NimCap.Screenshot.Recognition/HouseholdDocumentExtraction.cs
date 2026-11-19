using System.Text.RegularExpressions;
using System.Windows;
using NimCap.Screenshot.Models;

namespace NimCap.Screenshot.Services;

public static partial class IdentityDocumentRecognition
{
    private static IdentityDocumentResult RecognizeHousehold(RecognitionDocument document)
    {
        var specs = new[] { F("户别", "户别"), F("户主姓名", "户主姓名"), F("户号", "户号"),
            F("地址", "本市(县)其他住址", "本市（县）其他住址", "住址", "住所", "地址"), Name,
            F("与户主关系", "与户主或户主关系", "与户主关系", "户主或与", "户主关系"), Gender, F("出生日期", "出生日期"), F("民族", "民族"),
            F("身份证号", "公民身份号码", "公民身份证件编号", "公民身份", "证件编号"), F("文化程度", "文化程度"),
            F("_曾用名", "曾用名"), F("_出生地", "出生地", "出生生地"), F("_籍贯", "籍贯"), F("_宗教", "宗教信仰"),
            F("_身高", "身高"), F("_血型", "血型"), F("_婚姻", "婚姻状况"), F("_兵役", "兵役状况"), F("_服务", "服务处所"),
            F("_职业", "职业"), F("_迁入", "何时由何地"), F("_登记", "登记日期"), F("_签章", "承办人签章") };
        var map = specs.SelectMany(f => f.Labels.Select(l => (Field: f, Label: Compact(l))))
            .DistinctBy(x => x.Label).OrderByDescending(x => x.Label.Length).ToArray();
        var pattern = string.Join("|", map.Select(x => Regex.Escape(x.Label)));
        var evidence = new Dictionary<string, List<(string Value, RecognizedBlock Block)>>();
        // Restore each table row first, then stop every value at the next label
        // (including cells we don't export). No continuation can cross a table row.
        foreach (var row in AssembleRows(document.Blocks))
        {
            var text = Compact(row.Text);
            var matches = Regex.Matches(text, pattern, RegexOptions.CultureInvariant);
            for (var i = 0; i < matches.Count; i++)
            {
                var match = matches[i]; var spec = map.First(x => x.Label == match.Value).Field;
                if (spec.Name.StartsWith('_')) continue;
                var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
                var value = text[(match.Index + match.Length)..end].Trim(':', '：', '「', '」', '丨', '|');
                if (value.Length == 0) continue;
                if (!evidence.TryGetValue(spec.Name, out var list)) evidence[spec.Name] = list = [];
                list.Add((value, row));
            }
        }
        var fields = new List<IdentityDocumentField>();
        foreach (var spec in specs.Where(s => !s.Name.StartsWith('_')))
        {
            var candidates = evidence.GetValueOrDefault(spec.Name) ?? [];
            var selected = candidates.OrderByDescending(c => spec.Name is "出生日期" or "性别" or "身份证号" ? ValueQuality(spec.Name, c.Value) : 1)
                .ThenBy(c => c.Block.Kind.StartsWith("document-", StringComparison.Ordinal)).FirstOrDefault();
            fields.Add(Extracted(spec.Name, selected.Value ?? "", selected.Block is null ? [] : [selected.Block],
                candidates.Select(c => c.Value).Distinct().Count() > 1));
        }
        if (fields.Single(f => f.Name == "身份证号").Value.Length == 0)
            AddNumber(fields, document with { Blocks = document.Blocks.Where(b => !b.Kind.StartsWith("document-", StringComparison.Ordinal)).ToArray() },
                "身份证号", @"(?<![0-9A-Z])[1-9][0-9]{16}[0-9Xx](?![0-9A-Z])", IsValidIdNumber);
        return new(IdentityDocumentKind.HouseholdRegister, fields);
    }
}
