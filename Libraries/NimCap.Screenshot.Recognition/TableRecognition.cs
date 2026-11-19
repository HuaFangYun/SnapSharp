using L = NimCap.Screenshot.Localization.UiText;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NimCap.Screenshot.Models;
using SkiaSharp;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;

namespace NimCap.Screenshot.Services;

public sealed partial class LocalRecognitionService
{
    public sealed record TableResult(RecognitionDocument Document, RecognizedCell[] Cells);

    public async Task<TableResult> RecognizeTable(BitmapSource image, AppSettings settings, CancellationToken token = default)
    {
        if (!OnnxWorker.IsWorker)
        {
            var response = await OnnxWorker.Execute("table", image, settings, token).ConfigureAwait(false);
            return new TableResult(OnnxWorker.ToDocument(response), response.Cells ?? []);
        }
        // Finish the structural inference before acquiring the same serial runtime for OCR.
        var structure = await OnnxImageRuntime.Shared.Execute(settings, (models, cancellation) =>
        {
            OnnxImageRuntime.ValidateImage(image);
            var session = models.Open(settings.TableStructureModel, L.Get("SLANetTableStructureModel"));
            if (!session.ModelMetadata.CustomMetadataMap.TryGetValue("character", out var vocabulary))
                throw new InvalidDataException(L.Get("TableModelIsMissingTheCharacterDictionary"));
            var labels = vocabulary.Split('\n').Select(x => x.TrimEnd('\r')).Where(x => x.Length > 0 && x != "<td>").ToList();
            if (!labels.Contains("<td></td>")) labels.Add("<td></td>");
            labels.Insert(0, "sos"); labels.Add("eos");
            using var bitmap = _images.ToSkBitmap(image);
            var tensor = TableTensor(bitmap);
            var output = OnnxImageRuntime.ModelAccess.RunAll(session,
                [NamedOnnxValue.CreateFromTensor(session.InputMetadata.First().Key, tensor)], cancellation).Values.ToArray();
            var probabilities = output.Single(x => x.Shape.Length == 3 && x.Shape[2] == labels.Count);
            var boxes = output.Single(x => x.Shape.Length == 3 && x.Shape[2] is 4 or 8);
            return DecodeTable(probabilities.Data, probabilities.Shape, boxes.Data, boxes.Shape, labels, image.PixelWidth, image.PixelHeight);
        }, token).ConfigureAwait(false);
        var ocr = await Recognize(image, settings, token).ConfigureAwait(false);
        var texts = structure.Select(_ => new List<RecognizedBlock>()).ToArray();
        foreach (var block in ocr.Blocks)
        {
            token.ThrowIfCancellationRequested();
            var best = -1; double score = 0;
            for (var i = 0; i < structure.Length; i++)
            {
                var overlap = Rect.Intersect(structure[i].Bounds, block.Bounds);
                var fraction = overlap.IsEmpty ? 0 : overlap.Width * overlap.Height / Math.Max(1, block.Bounds.Width * block.Bounds.Height);
                if (fraction > score) { score = fraction; best = i; }
            }
            if (best >= 0) texts[best].Add(block);
        }
        for (var i = 0; i < structure.Length; i++) structure[i].Cell.Text = string.Join(Environment.NewLine, ReadingOrder(texts[i]).Select(x => x.Text));
        var cells = structure.Select(x => x.Cell).ToArray();
        RecognitionExport.Validate(cells);
        return new TableResult(new RecognitionDocument(structure.Select(x => new RecognizedBlock(x.Cell.Text, x.Bounds, Kind: "cell")).ToArray(),
            L.Get("LocalPPStructureSLANet") + ocr.Backend), cells);
    }

    private static DenseTensor<float> TableTensor(SKBitmap source)
    {
        const int edge = 488;
        var ratio = edge / (double)Math.Max(source.Width, source.Height);
        var width = Math.Max(1, (int)(source.Width * ratio)); var height = Math.Max(1, (int)(source.Height * ratio));
        using var resized = source.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear))!;
        var result = new DenseTensor<float>(new[] { 1, 3, edge, edge });
        float[] mean = [.485f, .456f, .406f], std = [.229f, .224f, .225f];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var p = resized.GetPixel(x, y); var a = p.Alpha / 255f;
            // PP-Structure's upstream loader supplies BGR, including its ImageNet normalization.
            result[0, 0, y, x] = ((p.Blue / 255f * a + 1 - a) - mean[0]) / std[0];
            result[0, 1, y, x] = ((p.Green / 255f * a + 1 - a) - mean[1]) / std[1];
            result[0, 2, y, x] = ((p.Red / 255f * a + 1 - a) - mean[2]) / std[2];
        }
        return result;
    }

    public sealed record TableCellRegion(RecognizedCell Cell, Rect Bounds);
    public static TableCellRegion[] DecodeTable(float[] probabilities, int[] shape, float[] boxes, int[] boxShape, IReadOnlyList<string> labels, int width, int height)
    {
        if (shape.Length != 3 || shape[0] != 1 || shape[2] != labels.Count || shape[1] > 4096 || probabilities.Length != shape[1] * shape[2] ||
            boxShape.Length != 3 || boxShape[0] != 1 || boxShape[1] != shape[1] || boxShape[2] is not (4 or 8) || boxes.Length != boxShape[1] * boxShape[2])
            throw new InvalidDataException(L.Get("SLANetOutputStructureMismatch"));
        var tokens = new List<(string Text, int Step)>();
        for (var t = 0; t < shape[1]; t++)
        {
            var best = 0;
            for (var c = 0; c < shape[2]; c++)
            {
                if (!float.IsFinite(probabilities[t * shape[2] + c])) throw new InvalidDataException(L.Get("TableOutputContainsInvalidValues"));
                if (probabilities[t * shape[2] + c] > probabilities[t * shape[2] + best]) best = c;
            }
            if (labels[best] == "eos") break;
            if (labels[best] != "sos") tokens.Add((labels[best], t));
        }
        var result = new List<TableCellRegion>(); var occupied = new HashSet<(int, int)>(); var row = 0; var column = 1;
        for (var i = 0; i < tokens.Count; i++)
        {
            var (text, step) = tokens[i];
            if (text == "<tr>") { row++; column = 1; continue; }
            if (text is not ("<td>" or "<td" or "<td></td>")) continue;
            if (row < 1) throw new InvalidDataException(L.Get("TableModelReturnedCellsWithoutRowStructure"));
            var tag = new StringBuilder(text);
            if (text == "<td")
            {
                while (i + 1 < tokens.Count && !tag.ToString().Contains('>')) tag.Append(tokens[++i].Text);
                if (!tag.ToString().Contains('>')) throw new InvalidDataException(L.Get("IncompleteTableCellTags"));
            }
            int Span(string name)
            {
                var match = Regex.Match(tag.ToString(), name + "\\s*=\\s*[\"']?(\\d+)", RegexOptions.CultureInvariant);
                return match.Success && int.TryParse(match.Groups[1].Value, out var n) && n is >= 1 and <= 2000 ? n : 1;
            }
            while (occupied.Contains((row, column))) column++;
            var cell = new RecognizedCell { Row = row, Column = column, RowSpan = Span("rowspan"), ColumnSpan = Span("colspan") };
            if (row + cell.RowSpan > 2001 || column + cell.ColumnSpan > 2001) throw new InvalidDataException(L.Get("MergedTableRangeIsTooLarge"));
            for (var r = row; r < row + cell.RowSpan; r++) for (var c = column; c < column + cell.ColumnSpan; c++)
                if (!occupied.Add((r, c)) || occupied.Count > 100000) throw new InvalidDataException(L.Get("MergedTableRangesOverlapOrAreToo"));
            var coords = boxes.AsSpan(step * boxShape[2], boxShape[2]).ToArray();
            if (coords.Any(x => !float.IsFinite(x))) throw new InvalidDataException(L.Get("InvalidTableCellCoordinates"));
            var xs = coords.Where((_, n) => n % 2 == 0).Select(x => Math.Clamp(x * width, 0, width)).ToArray();
            var ys = coords.Where((_, n) => n % 2 == 1).Select(y => Math.Clamp(y * height, 0, height)).ToArray();
            var bounds = new Rect(xs.Min(), ys.Min(), xs.Max() - xs.Min(), ys.Max() - ys.Min());
            result.Add(new TableCellRegion(cell, bounds)); column += cell.ColumnSpan;
        }
        if (result.Count == 0) throw new InvalidDataException(L.Get("NoTableStructureDetectedCropToOne"));
        return result.ToArray();
    }
}
