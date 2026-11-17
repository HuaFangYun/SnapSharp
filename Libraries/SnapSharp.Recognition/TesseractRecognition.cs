using L = SnapSharp.Localization.UiText;
using SnapSharp.Models;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using Tesseract;

namespace SnapSharp.Services;

public sealed partial class LocalRecognitionService
{
    private static readonly SemaphoreSlim TesseractGate = new(1);
    public static void CheckTesseract(string directory, string languages)
    {
        ValidateTesseractData(directory, languages);
        using var engine = new TesseractEngine(directory, languages, EngineMode.LstmOnly);
    }

    public static void ValidateTesseractData(string directory, string languages)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory) || !Directory.Exists(directory))
            throw new ArgumentException(L.Get("DownloadTesseractLanguageDataInSettingsOCR"));
        if (!Regex.IsMatch(languages, "^[a-z_]{2,20}(\\+[a-z_]{2,20}){0,7}$", RegexOptions.CultureInvariant))
            throw new ArgumentException(L.Get("UseTesseractDataNamesSuchAsChi"));
        foreach (var language in languages.Split('+'))
        {
            var path = Path.Combine(directory, language + ".traineddata");
            if (!File.Exists(path) || new FileInfo(path).Length is < 1000 or > 100_000_000)
                throw new FileNotFoundException(L.Get("MissingOrInvalidTesseractLanguageData") + language + L.Get("DownloadTheMatchingLanguageDataUsingSetup"), path);
        }
    }

    public async Task<RecognitionDocument> RecognizeTesseract(BitmapSource image, AppSettings settings, CancellationToken token = default)
    {
        OnnxImageRuntime.ValidateImage(image);
        ValidateTesseractData(settings.TesseractDataDirectory, settings.TesseractLanguages);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.RecognitionTimeoutSeconds, 1, 120)));
        await TesseractGate.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                timeout.Token.ThrowIfCancellationRequested();
                var input = _images.CreateThumbnail(image, Math.Clamp(settings.OcrMaximumEdge, 320, 2048));
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(input));
                using var data = new MemoryStream(); encoder.Save(data);
                using var pix = Pix.LoadFromMemory(data.ToArray());
                using var engine = new TesseractEngine(settings.TesseractDataDirectory, settings.TesseractLanguages, EngineMode.LstmOnly);
                using var page = engine.Process(pix, PageSegMode.Auto);
                using var iterator = page.GetIterator();
                var blocks = new List<RecognizedBlock>();
                var sx = image.PixelWidth / (double)input.PixelWidth; var sy = image.PixelHeight / (double)input.PixelHeight;
                iterator.Begin();
                do
                {
                    // Native recognition is synchronous: cancellation is observed before/after that call,
                    // never abandons the worker or releases the gate while native code is still running.
                    timeout.Token.ThrowIfCancellationRequested();
                    var text = iterator.GetText(PageIteratorLevel.TextLine)?.Trim();
                    if (!string.IsNullOrWhiteSpace(text) && iterator.TryGetBoundingBox(PageIteratorLevel.TextLine, out var box))
                        blocks.Add(new RecognizedBlock(text, new System.Windows.Rect(box.X1 * sx, box.Y1 * sy, box.Width * sx, box.Height * sy),
                            Math.Clamp(iterator.GetConfidence(PageIteratorLevel.TextLine) / 100d, 0, 1)));
                } while (blocks.Count < 256 && iterator.Next(PageIteratorLevel.TextLine));
                timeout.Token.ThrowIfCancellationRequested();
                return new RecognitionDocument(blocks, "Tesseract 5 / " + settings.TesseractLanguages);
            }, timeout.Token).ConfigureAwait(false);
        }
        finally { TesseractGate.Release(); }
    }
}
