using L = SnapSharp.Localization.UiText;
using SnapSharp.Models;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace SnapSharp.Services;

public static class RecognitionExport
{
    public static RecognizedCell[] Draft(RecognitionDocument document)
    {
        var cells = new List<RecognizedCell>(); var row = -1; var top = double.NegativeInfinity; var column = 0;
        foreach (var block in LocalRecognitionService.ReadingOrder(document.Blocks))
        {
            if (Math.Abs(block.Bounds.Top - top) > block.Bounds.Height * .6) { row++; column = 0; top = block.Bounds.Top; }
            cells.Add(new RecognizedCell { Row = row + 1, Column = ++column, Text = block.Text });
        }
        return cells.ToArray();
    }
    public static (int Rows, int Columns) Validate(IReadOnlyList<RecognizedCell> cells)
    {
        if (cells.Count == 0 || cells.Count > 10000) throw new ArgumentException(L.Get("TableMustContain110000Cells"));
        var occupied = new HashSet<(int, int)>(); var rows = 0; var columns = 0;
        foreach (var cell in cells)
        {
            if (cell.Row < 1 || cell.Column < 1 || cell.RowSpan < 1 || cell.ColumnSpan < 1 || (long)cell.Row + cell.RowSpan > 2001 || (long)cell.Column + cell.ColumnSpan > 2001)
                throw new ArgumentException(L.Get("RowsColumnsStartAt1AndSpans"));
            for (var r = cell.Row; r < cell.Row + cell.RowSpan; r++) for (var c = cell.Column; c < cell.Column + cell.ColumnSpan; c++)
            { if (!occupied.Add((r, c))) throw new ArgumentException(L.Get("CellOrMergedRangesOverlapCorrectThem")); if (occupied.Count > 100000) throw new ArgumentException(L.Get("TableRangeExceeds100000Cells")); }
            rows = Math.Max(rows, cell.Row + cell.RowSpan - 1); columns = Math.Max(columns, cell.Column + cell.ColumnSpan - 1);
        }
        if ((long)rows * columns > 100000) throw new ArgumentException(L.Get("TableRangeExceeds100000Cells"));
        return (rows, columns);
    }
    public static byte[] Csv(IReadOnlyList<RecognizedCell> cells)
    {
        var size = Validate(cells); var values = cells.ToDictionary(x => (x.Row, x.Column), x => x.Text ?? ""); var output = new StringBuilder();
        for (var row = 1; row <= size.Rows; row++)
        {
            var fields = Enumerable.Range(1, size.Columns).Select(column =>
            {
                var value = values.GetValueOrDefault((row, column), "");
                if (value.TrimStart().StartsWithAny('=', '+', '-', '@', '\t', '\r')) value = "'" + value;
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            });
            output.AppendLine(string.Join(',', fields));
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(output.ToString())).ToArray();
    }
    private static bool StartsWithAny(this string value, params char[] starts) => value.Length > 0 && starts.Contains(value[0]);
    public static byte[] Xlsx(IReadOnlyList<RecognizedCell> cells)
    {
        Validate(cells); XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        string Column(int value) { var text = ""; while (value > 0) { value--; text = (char)('A' + value % 26) + text; value /= 26; } return text; }
        var sheet = new XElement(ns + "worksheet", new XElement(ns + "sheetData", cells.GroupBy(x => x.Row).OrderBy(x => x.Key).Select(row =>
            new XElement(ns + "row", new XAttribute("r", row.Key), row.OrderBy(x => x.Column).Select(cell => new XElement(ns + "c", new XAttribute("r", Column(cell.Column) + cell.Row), new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), cell.Text ?? ""))))))));
        var merges = cells.Where(x => x.RowSpan > 1 || x.ColumnSpan > 1).ToArray();
        if (merges.Length > 0) sheet.Add(new XElement(ns + "mergeCells", new XAttribute("count", merges.Length), merges.Select(cell => new XElement(ns + "mergeCell", new XAttribute("ref", $"{Column(cell.Column)}{cell.Row}:{Column(cell.Column + cell.ColumnSpan - 1)}{cell.Row + cell.RowSpan - 1}")))));
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Write(string path, XDocument document) { using var stream = zip.CreateEntry(path).Open(); document.Save(stream); }
            XNamespace package = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace content = "http://schemas.openxmlformats.org/package/2006/content-types";
            Write("[Content_Types].xml", new XDocument(new XElement(content + "Types",
                new XElement(content + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(content + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                new XElement(content + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                new XElement(content + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));
            Write("_rels/.rels", new XDocument(new XElement(package + "Relationships", new XElement(package + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", rel.NamespaceName + "/officeDocument"), new XAttribute("Target", "xl/workbook.xml")))));
            Write("xl/workbook.xml", new XDocument(new XElement(ns + "workbook", new XElement(ns + "sheets", new XElement(ns + "sheet", new XAttribute("name", L.Get("RecognitionResults")), new XAttribute("sheetId", "1"), new XAttribute(rel + "id", "rId1"))))));
            Write("xl/_rels/workbook.xml.rels", new XDocument(new XElement(package + "Relationships", new XElement(package + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", rel.NamespaceName + "/worksheet"), new XAttribute("Target", "worksheets/sheet1.xml")))));
            Write("xl/worksheets/sheet1.xml", new XDocument(sheet));
        }
        return output.ToArray();
    }
}
