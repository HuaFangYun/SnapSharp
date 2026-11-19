using L = SnapSharp.Localization.UiText;
using SnapSharp.Models;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;

namespace SnapSharp.Services;

public sealed record PinContent(BitmapSource Image, string? Text = null, Color? Color = null);

public static class PinContentService
{
    public static bool TryParseColor(string? value, out Color color)
    {
        color = default;
        var text = value?.Trim() ?? "";
        if (Regex.IsMatch(text, @"^0x[0-9a-fA-F]{6}$", RegexOptions.IgnoreCase)) text = "#" + text[2..];
        if (Regex.IsMatch(text, @"^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$"))
        {
            color = (Color)ColorConverter.ConvertFromString(text);
            return true;
        }
        var match = Regex.Match(text, @"^(rgb|rgba)\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})(?:\s*,\s*(0?(?:\.\d+)|[01](?:\.\d+)?))?\s*\)$", RegexOptions.IgnoreCase);
        if (!match.Success || !byte.TryParse(match.Groups[2].Value, out var r) || !byte.TryParse(match.Groups[3].Value, out var g) || !byte.TryParse(match.Groups[4].Value, out var b)) return false;
        if (match.Groups[1].Value.Equals("rgba", StringComparison.OrdinalIgnoreCase) != match.Groups[5].Success) return false;
        var alpha = match.Groups[5].Success ? double.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture) : 1;
        if (alpha > 1) return false;
        color = Color.FromArgb((byte)Math.Round(alpha * 255), r, g, b); return true;
    }

    public static PinContent FromClipboard(BitmapSource? image, string? text, AppSettings settings)
    {
        if (TryParseColor(text, out var color)) return FromColor(color);
        if (image is not null) { if (image.CanFreeze) image.Freeze(); return new PinContent(image); }
        return FromText(text ?? "", settings);
    }

    public static string Hex(Color color) => color.A == 255 ? $"#{color.R:X2}{color.G:X2}{color.B:X2}" : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    public static string Rgb(Color color) => $"rgb({color.R}, {color.G}, {color.B})";

    public static PinContent FromText(string text, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException(L.Get("TextCannotBeEmpty"));
        if (text.Length > 50_000) throw new ArgumentException(L.Get("TextExceeds50000CharactersSplitItInto"));
        var width = Math.Clamp(settings.TextPinWidth, 200, 1200);
        var fontSize = Math.Clamp(settings.TextPinFontSize, 10, 64);
        var foreground = TryParseColor(settings.TextPinForeground, out var fg) ? fg : Colors.Black;
        var background = TryParseColor(settings.TextPinBackground, out var bg) ? bg : Color.FromRgb(255, 246, 206);
        var formatted = new FormattedText(text.Replace("\r\n", "\n"), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(settings.TextPinFontFamily), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), fontSize, new SolidColorBrush(foreground), 1)
        { MaxTextWidth = width - 40, Trimming = TextTrimming.None };
        var height = Math.Max(80, (int)Math.Ceiling(formatted.Height) + 40);
        if (height > 16384 || (long)width * height > 16_000_000) throw new ArgumentException(L.Get("LaidOutTextIsTooLongSplit"));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, width, height));
            dc.DrawText(formatted, new Point(20, 20));
        }
        return new PinContent(Render(visual, width, height), text);
    }

    public static PinContent FromColor(Color color)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 340, 220));
            dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, 340, 125));
            void Text(string text, double size, double top) => dc.DrawText(new FormattedText(text,
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, Brushes.Black, 1), new Point(20, top));
            Text(Hex(color), 24, 137); Text(Rgb(color), 16, 175);
        }
        return new PinContent(Render(visual, 340, 220), Color: color);
    }

    private static BitmapSource Render(DrawingVisual visual, int width, int height)
    {
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual); image.Freeze(); return image;
    }
}
