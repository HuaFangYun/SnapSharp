using Microsoft.ML.OnnxRuntime;
using NimCap.Screenshot.Models;
using SkiaSharp;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Text.RegularExpressions;

namespace NimCap.Screenshot.Services;

public sealed partial class LocalRecognitionService
{
    /// <summary>Read full document text, then retry complete text rows without the detector's tight crops.</summary>
    public async Task<RecognitionDocument> RecognizeIdentityDocument(BitmapSource image, AppSettings settings, CancellationToken token = default)
    {
        if (settings.OcrBackend == "PaddleOCR")
        {
            settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
            settings.OcrMaximumEdge = 2048;
        }
        var original = await Recognize(image, settings, token).ConfigureAwait(false);
        if (settings.OcrBackend != "PaddleOCR" || original.Blocks.Count == 0) return original;
        var business = IdentityDocumentRecognition.IsBusinessLicense(original);
        var rows = IdentityDocumentRecognition.AssembleRows(original.Blocks, business);
        var retries = await OnnxImageRuntime.Shared.Execute(settings, (models, cancellation) =>
        {
            var recognizer = models.Open(settings.OcrRecognitionModel, "Document OCR recognition model");
            var dictionary = File.ReadAllLines(settings.OcrDictionary, System.Text.Encoding.UTF8).Where(x => x.Length > 0).ToArray();
            using var source = _images.ToSkBitmap(image);
            var result = new List<RecognizedBlock>();
            RecognizedBlock? ReadRegion(int left, int top, int right, int bottom, string kind)
            {
                cancellation.ThrowIfCancellationRequested();
                if (right <= left || bottom <= top) return null;
                using var crop = new SKBitmap(right - left, bottom - top, SKColorType.Bgra8888, SKAlphaType.Premul);
                using (var canvas = new SKCanvas(crop)) canvas.DrawBitmap(source, new SKRect(left, top, right, bottom), new SKRect(0, 0, crop.Width, crop.Height));
                var size = OnnxImageRuntime.ModelAccess.Size(recognizer, 48, Math.Clamp((int)Math.Ceiling(48d * crop.Width / crop.Height / 8) * 8, 32, 2048));
                var output = OnnxImageRuntime.ModelAccess.Run(recognizer, OnnxImageRuntime.Tensor(crop, size.Height, size.Width, "MinusOne", true), cancellation);
                var (text, confidence) = DecodeCtc(output.Data, output.Shape, dictionary);
                return string.IsNullOrWhiteSpace(text) ? null : new(text, new Rect(left, top, right - left, bottom - top), confidence, kind);
            }
            foreach (var row in rows.Take(64))
            {
                cancellation.ThrowIfCancellationRequested();
                // Include neighboring glyphs cut off by text detection, with less vertical
                // padding so short Chinese text and long numbers remain large at 48px height.
                var r = row.Bounds;
                var left = Math.Max(0, (int)Math.Floor(r.Left - r.Height * .8));
                if (business)
                {
                    // Detection can miss the first glyphs of a printed label (注册
                    // before 资本). Retry from the established nearby column margin.
                    var margin = rows.Where(b => Math.Abs(b.Bounds.Top - r.Top) <= r.Height * 8
                        && b.Bounds.Left < r.Left && r.Left - b.Bounds.Left <= r.Height * 6)
                        .Select(b => b.Bounds.Left).DefaultIfEmpty(r.Left).Min();
                    left = Math.Max(0, Math.Min(left, (int)Math.Floor(margin - r.Height * .3)));
                }
                var right = Math.Min(source.Width, (int)Math.Ceiling(r.Right + r.Height * .8));
                var top = Math.Max(0, (int)Math.Floor(r.Top));
                var bottom = Math.Min(source.Height, (int)Math.Ceiling(r.Bottom));
                var retry = ReadRegion(left, top, right, bottom, "document-retry");
                if (retry is not null) result.Add(retry);

                // A long number plus Chinese label can be compressed by a recognizer's
                // fixed input width. Retry the number itself using its own detection box
                // when available, otherwise estimate a generous left edge from glyph widths.
                var compact = Regex.Replace(row.Text, @"\s+", "");
                if (!compact.Contains("公民身份号码") && !compact.Contains("身份证号")) continue;
                var number = Regex.Match(row.Text, @"[0-9](?:[0-9Xx\s]){13,}");
                if (!number.Success) continue;
                var numberBox = original.Blocks.Where(b => b.Bounds.IntersectsWith(r) && Regex.IsMatch(b.Text, @"^\s*[0-9][0-9Xx\s]{13,}\s*$"))
                    .OrderByDescending(b => b.Text.Length).FirstOrDefault();
                if (numberBox is not null) left = Math.Max(0, (int)Math.Floor(numberBox.Bounds.Left - r.Height * .2));
                else
                {
                    static double Width(string s) => s.Sum(c => char.IsWhiteSpace(c) ? .4 : c > 127 ? 2d : 1d);
                    left = Math.Max(left, (int)Math.Floor(r.Left + r.Width * Width(row.Text[..number.Index]) / Width(row.Text) - r.Height * .4));
                }
                var digits = ReadRegion(left, top, right, bottom, "document-number-retry");
                if (digits is null) continue;
                var candidate = Regex.Replace(digits.Text, @"\s+", "");
                if (Regex.IsMatch(candidate, @"^[0-9]{13,18}[0-9Xx]$")) result.Add(digits with { Text = "公民身份号码 " + candidate });
            }
            return result;
        }, token).ConfigureAwait(false);
        // Retain detector boxes: field extraction needs their original column and
        // value positions; retry crops are alternate evidence, not replacements.
        return original with { Blocks = [.. original.Blocks, .. retries] };
    }
}
