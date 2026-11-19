using Cursor = System.Windows.Input.Cursor;

namespace NimCap.Screenshot.Views;

/// <summary>A fixed-size cross with adjacent one-pixel black and white arms.</summary>
public static class PrecisionCursor
{
    public static Cursor Cross { get; } = CreateCross();

    private static Cursor CreateCross()
    {
        const int size = 32, center = 15, imageBytes = size * size * 4, maskStride = 4;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)0); writer.Write((ushort)2); writer.Write((ushort)1); // CUR directory
        writer.Write((byte)size); writer.Write((byte)size); writer.Write((ushort)0);
        writer.Write((ushort)center); writer.Write((ushort)center); // hotspot
        writer.Write(40 + imageBytes + size * maskStride); writer.Write(22);
        writer.Write(40); writer.Write(size); writer.Write(size * 2); // DIB and AND-mask heights
        writer.Write((ushort)1); writer.Write((ushort)32);
        writer.Write(0); writer.Write(imageBytes);
        writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
        var pixels = new byte[imageBytes];
        var mask = Enumerable.Repeat((byte)255, size * maskStride).ToArray();
        for (var offset = 0; offset < 2; offset++)
        for (var position = 3; position < size - 3; position++)
        {
            SetPixel(position, center + offset, offset == 0 ? (byte)0 : (byte)255);
            SetPixel(center + offset, position, offset == 0 ? (byte)0 : (byte)255);
        }
        writer.Write(pixels); writer.Write(mask); writer.Flush();
        stream.Position = 0;
        return new Cursor(stream, scaleWithDpi: false);

        void SetPixel(int x, int y, byte color)
        {
            var row = size - 1 - y;
            var index = (row * size + x) * 4;
            pixels[index] = pixels[index + 1] = pixels[index + 2] = color;
            pixels[index + 3] = 255;
            mask[row * maskStride + x / 8] &= (byte)~(0x80 >> (x % 8));
        }
    }
}
