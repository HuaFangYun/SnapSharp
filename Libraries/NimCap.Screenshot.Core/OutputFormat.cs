using L = NimCap.Screenshot.Localization.UiText;
namespace NimCap.Screenshot.Models;

public sealed record OutputFormat(string Name, string Extension, string DescriptionKey, bool IsAvailable = true)
{
    public string Description => L.Get(DescriptionKey);
    public override string ToString() => Name;

    public static IReadOnlyList<OutputFormat> All { get; } =
    [
        new("WebP", ".webp", "CompactFilesTransparencyLossyOrLossless"),
        new("PNG", ".png", "LosslessQualityWithTransparency"),
        new("TIFF", ".tiff", "Lossless300DPISuitableForPublication"),
        new("JPG", ".jpg", "WidelyCompatibleSuitableForPhotos"),
        new("BMP", ".bmp", "LosslessBitmapLargerFiles"),
        new("AVIF", ".avif", "HighCompressionEncodingNotSupportedInThis", false),
        new("PDF", ".pdf", "ForPrintingWithPaginationForLongImages")
    ];

    public static string NormalizeExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpeg" => ".jpg",
        ".tif" => ".tiff",
        var value => value
    };

    public static OutputFormat Find(string extension) => All.First(format => format.Extension == NormalizeExtension(extension));
    public static bool IsSupported(string extension) => All.Any(format => format.IsAvailable && format.Extension == NormalizeExtension(extension));
    public static string SaveDialogFilter => string.Join("|", All.Where(format => format.IsAvailable)
        .Select(format => L.Format("0Files1", format.Name, format.Extension) + (format.Extension == ".jpg" ? ";*.jpeg" : format.Extension == ".tiff" ? ";*.tif" : "")));
    public static int FilterIndex(string extension) => All.Where(format => format.IsAvailable).ToList().FindIndex(format => format.Extension == NormalizeExtension(extension)) + 1;
}
