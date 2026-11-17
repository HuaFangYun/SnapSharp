using L = SnapSharp.Localization.UiText;
using System.Globalization;
using System.Windows.Media;

namespace SnapSharp.Services;

internal enum ScreenColorFormat { Hex, Rgb, Rgba, Hsl, Hsv, Cmyk, Name }

internal static class ScreenColorFormatter
{
    private static readonly (string Name, Color Color)[] Names = typeof(Colors).GetProperties()
        .Where(p => p.PropertyType == typeof(Color))
        .Select(p => (p.Name.ToLowerInvariant(), (Color)p.GetValue(null)!))
        .Where(p => p.Item2.A == 255).OrderBy(p => p.Item1, StringComparer.Ordinal).ToArray();

    internal static ScreenColorFormat Next(ScreenColorFormat format) => (ScreenColorFormat)(((int)format + 1) % Enum.GetValues<ScreenColorFormat>().Length);

    internal static (string Value, string Label) Format(Color color, ScreenColorFormat format)
    {
        var r = color.R / 255d; var g = color.G / 255d; var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var delta = max - min;
        var hue = delta == 0 ? 0 : max == r ? 60 * (((g - b) / delta) % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        if (hue < 0) hue += 360;
        var lightness = (max + min) / 2;
        var saturation = delta == 0 ? 0 : delta / (1 - Math.Abs(2 * lightness - 1));
        static string N(double value) => Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        var h = N(hue % 360); if (h == "360") h = "0";
        switch (format)
        {
            case ScreenColorFormat.Hex: return ($"#{color.R:X2}{color.G:X2}{color.B:X2}", "HEX");
            case ScreenColorFormat.Rgb: return ($"rgb({color.R}, {color.G}, {color.B})", "RGB");
            case ScreenColorFormat.Rgba: return ($"rgba({color.R}, {color.G}, {color.B}, 1)", L.Get("RGBAScreenPixel1"));
            case ScreenColorFormat.Hsl: return ($"hsl({h}, {N(saturation * 100)}%, {N(lightness * 100)}%)", "HSL");
            case ScreenColorFormat.Hsv: return ($"hsv({h}, {N((max == 0 ? 0 : delta / max) * 100)}%, {N(max * 100)}%)", "HSV / HSB");
            case ScreenColorFormat.Cmyk:
                return ($"C{N((max == 0 ? 0 : (max - r) / max) * 100)} M{N((max == 0 ? 0 : (max - g) / max) * 100)} Y{N((max == 0 ? 0 : (max - b) / max) * 100)} K{N((1 - max) * 100)}", L.Get("CMYKApproximateConversionNotAPrintColor"));
            case ScreenColorFormat.Name:
                static int Distance(Color x, Color y) => (x.R - y.R) * (x.R - y.R) + (x.G - y.G) * (x.G - y.G) + (x.B - y.B) * (x.B - y.B);
                var nearest = Names.MinBy(x => Distance(x.Color, color));
                return (nearest.Name, Distance(nearest.Color, color) == 0 ? L.Get("ColorName") : L.Get("ColorNameApproximate"));
            default: throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    internal static bool RunSelfTest()
    {
        var sample = Color.FromRgb(255, 87, 51);
        var expected = new[] { "#FF5733", "rgb(255, 87, 51)", "rgba(255, 87, 51, 1)", "hsl(11, 100%, 60%)", "hsv(11, 80%, 100%)", "C0 M66 Y80 K0" };
        for (var i = 0; i < expected.Length; i++) if (Format(sample, (ScreenColorFormat)i).Value != expected[i]) return false;
        if (Format(Colors.Red, ScreenColorFormat.Name) != ("red", "颜色名称") || Format(Colors.Tomato, ScreenColorFormat.Name).Value != "tomato") return false;
        if (!Format(Color.FromRgb(18, 19, 20), ScreenColorFormat.Name).Label.Contains("近似")) return false;
        foreach (var color in new[] { Colors.Black, Colors.White, Colors.Gray })
        foreach (var format in Enum.GetValues<ScreenColorFormat>())
        { var value = Format(color, format).Value; if (value.Contains("NaN") || value.Contains("Infinity")) return false; }
        if (Format(Colors.Black, ScreenColorFormat.Cmyk).Value != "C0 M0 Y0 K100") return false;
        var current = ScreenColorFormat.Hex;
        for (var i = 0; i < 7; i++) current = Next(current);
        return current == ScreenColorFormat.Hex;
    }
}
