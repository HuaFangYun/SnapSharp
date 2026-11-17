using L = SnapSharp.Localization.UiText;
using SnapSharp.Models;
using SkiaSharp;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapSharp.Services;

public sealed class SkiaImageService
{
    public BitmapSource ApplySelectionMask(BitmapSource source, IReadOnlyList<System.Windows.Point> points, bool ellipse)
    {
        using var bitmap = ToSkBitmap(source);
        using var surface = SKSurface.Create(new SKImageInfo(bitmap.Width, bitmap.Height,
            SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        if (ellipse)
        {
            path.AddOval(new SKRect(0, 0, bitmap.Width, bitmap.Height));
        }
        else if (points.Count >= 3)
        {
            path.MoveTo((float)points[0].X, (float)points[0].Y);
            for (var i = 1; i < points.Count; i++) path.LineTo((float)points[i].X, (float)points[i].Y);
            path.Close();
        }
        using var paint = new SKPaint { IsAntialias = true };
        surface.Canvas.Save();
        surface.Canvas.ClipPath(path, SKClipOperation.Intersect, true);
        surface.Canvas.DrawBitmap(bitmap, 0, 0, paint);
        surface.Canvas.Restore();
        surface.Canvas.Flush();
        using var image = surface.Snapshot();
        return ToBitmapSource(image);
    }

    public BitmapSource Render(BitmapSource source, IEnumerable<Annotation> annotations)
    {
        using var bitmap = ToSkBitmap(source);
        using var surface = SKSurface.Create(new SKImageInfo(bitmap.Width, bitmap.Height,
            SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.DrawBitmap(bitmap, 0, 0);
        // Keep an independent source image. Surface.Snapshot may share the same
        // managed/native image with effect snapshots that are disposed during rendering.
        using var baseImage = SKImage.FromBitmap(bitmap);
        foreach (var annotation in annotations)
        {
            var bounds = AnnotationEditing.UnrotatedBounds(annotation);
            if (Math.Abs(annotation.Rotation) >= 0.001 && !bounds.IsEmpty)
            {
                surface.Canvas.Save();
                surface.Canvas.RotateDegrees((float)annotation.Rotation,
                    (float)(bounds.Left + bounds.Width / 2), (float)(bounds.Top + bounds.Height / 2));
            }
            if (annotation is RegionEffectAnnotation effect) effect.ApplySkia(surface);
            else if (annotation is SpotlightAnnotation spotlight) spotlight.ApplySkia(surface);
            else if (annotation is MagnifierAnnotation magnifier) magnifier.ApplySkia(surface, baseImage);
            else annotation.DrawSkia(surface.Canvas);
            if (Math.Abs(annotation.Rotation) >= 0.001 && !bounds.IsEmpty) surface.Canvas.Restore();
        }
        surface.Canvas.Flush();
        using var image = surface.Snapshot();
        return ToBitmapSource(image);
    }

    public BitmapSource Crop(BitmapSource source, Int32Rect region)
    {
        var clipped = new Int32Rect(
            Math.Clamp(region.X, 0, Math.Max(0, source.PixelWidth - 1)),
            Math.Clamp(region.Y, 0, Math.Max(0, source.PixelHeight - 1)),
            Math.Clamp(region.Width, 1, source.PixelWidth - Math.Clamp(region.X, 0, Math.Max(0, source.PixelWidth - 1))),
            Math.Clamp(region.Height, 1, source.PixelHeight - Math.Clamp(region.Y, 0, Math.Max(0, source.PixelHeight - 1))));
        var cropped = new CroppedBitmap(source, clipped);
        cropped.Freeze();
        return cropped;
    }

    public BitmapSource Rotate90(BitmapSource source, bool clockwise)
    {
        using var bitmap = ToSkBitmap(source);
        var info = new SKImageInfo(bitmap.Height, bitmap.Width, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Translate(clockwise ? bitmap.Height : 0, clockwise ? 0 : bitmap.Width);
        surface.Canvas.RotateDegrees(clockwise ? 90 : -90);
        surface.Canvas.DrawBitmap(bitmap, 0, 0);
        surface.Canvas.Flush();
        using var image = surface.Snapshot();
        return ToBitmapSource(image);
    }

    public BitmapSource Flip(BitmapSource source, bool horizontal)
    {
        using var bitmap = ToSkBitmap(source);
        using var surface = SKSurface.Create(new SKImageInfo(bitmap.Width, bitmap.Height,
            SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Translate(horizontal ? bitmap.Width : 0, horizontal ? 0 : bitmap.Height);
        surface.Canvas.Scale(horizontal ? -1 : 1, horizontal ? 1 : -1);
        surface.Canvas.DrawBitmap(bitmap, 0, 0);
        surface.Canvas.Flush();
        using var image = surface.Snapshot();
        return ToBitmapSource(image);
    }

    public BitmapSource ExpandCanvas(BitmapSource source, int width, int height, int offsetX, int offsetY, Color background)
    {
        width = Math.Max(width, source.PixelWidth);
        height = Math.Max(height, source.PixelHeight);
        using var bitmap = ToSkBitmap(source);
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(new SKColor(background.R, background.G, background.B, background.A));
        surface.Canvas.DrawBitmap(bitmap, offsetX, offsetY);
        surface.Canvas.Flush();
        using var image = surface.Snapshot();
        return ToBitmapSource(image);
    }

    public SKBitmap ToSkBitmap(BitmapSource source)
    {
        BitmapSource converted = source.Format == PixelFormats.Pbgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var bitmap = new SKBitmap(new SKImageInfo(converted.PixelWidth, converted.PixelHeight,
            SKColorType.Bgra8888, SKAlphaType.Premul));
        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        return bitmap;
    }

    public BitmapSource ToBitmapSource(SKImage image)
    {
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(encoded.ToArray());
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public BitmapSource ToBitmapSource(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        return ToBitmapSource(image);
    }

    public void Save(BitmapSource source, string path, int quality = 92)
    {
        File.WriteAllBytes(path, Encode(source, Path.GetExtension(path), quality));
    }

    public byte[] Encode(BitmapSource source, string extension, int quality = 92, bool webpLossless = false,
        int? width = null, int? height = null)
    {
        extension = OutputFormat.NormalizeExtension(extension);
        if (!OutputFormat.IsSupported(extension)) throw new ArgumentException(L.Get("UnsupportedOutputFormat"));
        var targetWidth = width ?? source.PixelWidth;
        var targetHeight = height ?? source.PixelHeight;
        if (targetWidth < 1 || targetHeight < 1 || targetWidth > 16384 || targetHeight > 16384 ||
            (long)targetWidth * targetHeight > 64_000_000) throw new ArgumentException(L.Get("OutputDimensionsMustBe116384Pixels"));
        using var bitmap = ToSkBitmap(source);
        using var surface = SKSurface.Create(new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        // JPEG has no alpha; composite on white instead of leaving transparent pixels black.
        surface.Canvas.Clear(extension is ".jpg" or ".bmp" or ".pdf" ? SKColors.White : SKColors.Transparent);
        using var image = SKImage.FromBitmap(bitmap);
        if (targetWidth == bitmap.Width && targetHeight == bitmap.Height) surface.Canvas.DrawImage(image, 0, 0);
        else surface.Canvas.DrawImage(image, new SKRect(0, 0, targetWidth, targetHeight), new SKSamplingOptions(SKCubicResampler.Mitchell));
        surface.Canvas.Flush();
        using var resized = surface.Snapshot();
        if (extension == ".pdf")
        {
            using var output = new MemoryStream();
            using (var document = SKDocument.CreatePdf(output))
            {
                // A4 at 72 points/inch, 36pt margins. Wide images use landscape;
                // tall images are sliced into successive pages without dropping rows.
                var landscape = targetWidth > targetHeight;
                var pageWidth = landscape ? 842f : 595f;
                var pageHeight = landscape ? 595f : 842f;
                var scale = Math.Min(.75f, (pageWidth - 72) / targetWidth);
                var rowsPerPage = Math.Max(1, (int)Math.Floor((pageHeight - 72) / scale));
                for (var top = 0; top < targetHeight; top += rowsPerPage)
                {
                    var rows = Math.Min(rowsPerPage, targetHeight - top);
                    var canvas = document.BeginPage(pageWidth, pageHeight);
                    canvas.Clear(SKColors.White);
                    canvas.DrawImage(resized, new SKRect(0, top, targetWidth, top + rows),
                        new SKRect(36, 36, 36 + targetWidth * scale, 36 + rows * scale), new SKSamplingOptions(SKFilterMode.Linear));
                    document.EndPage();
                }
                document.Close();
            }
            return output.ToArray();
        }
        if (extension is ".tiff" or ".bmp")
        {
            var raster = ToBitmapSource(resized);
            var pixels = new byte[checked(targetWidth * targetHeight * 4)];
            var bgra = new FormatConvertedBitmap(raster, PixelFormats.Bgra32, null, 0);
            bgra.CopyPixels(pixels, targetWidth * 4, 0);
            var dpi = extension == ".tiff" ? 300 : 96;
            var frame = BitmapSource.Create(targetWidth, targetHeight, dpi, dpi, PixelFormats.Bgra32, null, pixels, targetWidth * 4);
            BitmapEncoder encoder = extension == ".tiff" ? new TiffBitmapEncoder { Compression = TiffCompressOption.Zip } : new BmpBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(frame));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        if (extension.Equals(".webp", StringComparison.OrdinalIgnoreCase))
        {
            using var pixmap = resized.PeekPixels();
            using var webp = pixmap.Encode(new SKWebpEncoderOptions(webpLossless ? SKWebpEncoderCompression.Lossless : SKWebpEncoderCompression.Lossy,
                webpLossless ? 100 : Math.Clamp(quality, 1, 100)));
            return webp?.ToArray() ?? throw new IOException(L.Get("WebPEncodingFailed"));
        }
        var format = extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            ".png" => SKEncodedImageFormat.Png,
            _ => throw new ArgumentException(L.Get("OnlyPNGJPEGAndWebPAreSupported"))
        };
        using var encoded = resized.Encode(format, Math.Clamp(quality, 1, 100));
        return encoded?.ToArray() ?? throw new IOException(L.Get("ImageEncodingFailed"));
    }

    public BitmapSource Decode(byte[] data)
    {
        using var image = SKImage.FromEncodedData(data);
        if (image is not null) return ToBitmapSource(image);
        // TIFF is encoded/decoded using the Windows imaging codecs.
        using var stream = new MemoryStream(data);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        frame.Freeze();
        return frame;
    }

    public BitmapSource CreateThumbnail(BitmapSource source, int maximumEdge = 320)
    {
        using var bitmap = ToSkBitmap(source);
        var scale = Math.Min(1d, maximumEdge / (double)Math.Max(bitmap.Width, bitmap.Height));
        var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var resized = bitmap.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell));
        return ToBitmapSource(resized);
    }
}
