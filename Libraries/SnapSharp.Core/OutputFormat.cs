namespace SnapSharp.Models;

public sealed record OutputFormat(string Name, string Extension, string Description, bool IsAvailable = true)
{
    public static IReadOnlyList<OutputFormat> All { get; } =
    [
        new("WebP", ".webp", "文件较小，支持透明背景，可选有损／无损"),
        new("PNG", ".png", "无损画质，支持透明背景"),
        new("TIFF", ".tiff", "无损画质，300 DPI，适合投稿"),
        new("JPG", ".jpg", "兼容性好，适合照片"),
        new("BMP", ".bmp", "无损位图，文件较大"),
        new("AVIF", ".avif", "高压缩率；当前版本暂不支持编码", false),
        new("PDF", ".pdf", "适合打印，支持长图分页")
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
        .Select(format => $"{format.Name} 文件|*{format.Extension}" + (format.Extension == ".jpg" ? ";*.jpeg" : format.Extension == ".tiff" ? ";*.tif" : "")));
    public static int FilterIndex(string extension) => All.Where(format => format.IsAvailable).ToList().FindIndex(format => format.Extension == NormalizeExtension(extension)) + 1;
}
