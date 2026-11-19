using L = NimCap.Screenshot.Localization.UiText;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NimCap.Screenshot.Services;

/// <summary>Bounded-memory GIF89a, RGB332 palette with ordered dither, streaming LZW.</summary>
internal sealed class StreamingGifWriter : IDisposable
{
    private readonly BinaryWriter _writer;
    private readonly int _width, _height;
    private static readonly int[] Bayer = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];
    public StreamingGifWriter(Stream output, int width, int height)
    {
        if (width < 1 || height < 1 || width > 800 || height > 800) throw new ArgumentException(L.Get("GIFDimensionsExceedTheLimit"));
        _writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true); _width = width; _height = height;
        _writer.Write(Encoding.ASCII.GetBytes("GIF89a")); _writer.Write((ushort)width); _writer.Write((ushort)height); _writer.Write((byte)0xF7); _writer.Write((byte)0); _writer.Write((byte)0);
        for (var i = 0; i < 256; i++) { _writer.Write((byte)(((i >> 5) & 7) * 255 / 7)); _writer.Write((byte)(((i >> 2) & 7) * 255 / 7)); _writer.Write((byte)((i & 3) * 255 / 3)); }
        _writer.Write(new byte[] { 0x21, 0xFF, 11 }); _writer.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0")); _writer.Write(new byte[] { 3, 1, 0, 0, 0 });
    }
    public void Frame(BitmapSource image, int delayCentiseconds)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        if (converted.PixelWidth != _width || converted.PixelHeight != _height) throw new ArgumentException(L.Get("GIFFrameDimensionsAreInconsistent"));
        var pixels = new byte[_width * _height * 4]; converted.CopyPixels(pixels, _width * 4, 0); var indices = new byte[_width * _height];
        for (var y = 0; y < _height; y++) for (var x = 0; x < _width; x++)
        {
            var index = y * _width + x; var offset = index * 4; var d = (Bayer[(y & 3) * 4 + (x & 3)] - 7.5) / 16;
            int Quantize(byte value, int levels) => Math.Clamp((int)Math.Round(value * levels / 255d + d), 0, levels);
            indices[index] = (byte)(Quantize(pixels[offset + 2], 7) << 5 | Quantize(pixels[offset + 1], 7) << 2 | Quantize(pixels[offset], 3));
        }
        _writer.Write(new byte[] { 0x21, 0xF9, 4, 4 }); _writer.Write((ushort)Math.Clamp(delayCentiseconds, 1, 65535)); _writer.Write(new byte[] { 0, 0, 0x2C });
        _writer.Write((ushort)0); _writer.Write((ushort)0); _writer.Write((ushort)_width); _writer.Write((ushort)_height); _writer.Write((byte)0); _writer.Write((byte)8);
        var block = new List<byte>(255); uint bits = 0; var bitCount = 0;
        void Byte(byte value) { block.Add(value); if (block.Count == 255) Flush(); }
        void Flush() { if (block.Count == 0) return; _writer.Write((byte)block.Count); _writer.Write(block.ToArray()); block.Clear(); }
        void Code(int code, int width) { bits |= (uint)code << bitCount; bitCount += width; while (bitCount >= 8) { Byte((byte)bits); bits >>= 8; bitCount -= 8; } }
        // Reset before 9-bit dictionary exhaustion. This avoids decoder width-transition ambiguity.
        // Repeated sequences still compress; dictionary and block storage stay bounded.
        var dictionary = new Dictionary<int, int>(); var next = 258; Code(256, 9); var prefix = (int)indices[0];
        for (var i = 1; i < indices.Length; i++)
        {
            var key = prefix << 8 | indices[i];
            if (dictionary.TryGetValue(key, out var found)) { prefix = found; continue; }
            Code(prefix, 9); dictionary[key] = next++; prefix = indices[i];
            if (next == 510) { Code(256, 9); dictionary.Clear(); next = 258; }
        }
        Code(prefix, 9); Code(257, 9); if (bitCount > 0) Byte((byte)bits); Flush(); _writer.Write((byte)0);
    }
    public void Dispose() { _writer.Write((byte)0x3B); _writer.Dispose(); }
}
