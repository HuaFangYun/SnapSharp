using SnapSharp.Models;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace SnapSharp.Services;

public sealed class ScrollProjectService
{
    private sealed record Manifest(int Version, int Width, int Height, int Count, int[] Offsets, ScrollMatch[] Matches,
        ScrollDirection Direction, string StopReason, ScrollTrim FrameTrim, ScrollTrim ResultCrop);
    private readonly SkiaImageService _images = new();

    public void Save(string path, ScrollProject project, bool overwrite = false)
    {
        var session = project.Session;
        if (session.Frames.Count == 0 || (long)session.Frames.Count * session.Frames[0].PixelWidth * session.Frames[0].PixelHeight > ScrollingStitcher.MaximumFramePixels)
            throw new ArgumentException("原始帧超过工程内存上限。");
        _ = new ScrollingStitcher().Stitch(session, project.FrameTrim);
        var manifest = new Manifest(1, session.Frames[0].PixelWidth, session.Frames[0].PixelHeight, session.Frames.Count,
            session.Offsets.ToArray(), session.Matches.ToArray(), session.Direction, session.StopReason, project.FrameTrim, project.ResultCrop);
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var stream = archive.CreateEntry("project.json").Open()) JsonSerializer.Serialize(stream, manifest);
            for (var index = 0; index < session.Frames.Count; index++)
            {
                using var stream = archive.CreateEntry($"frames/{index:D3}.png", CompressionLevel.NoCompression).Open();
                stream.Write(_images.Encode(session.Frames[index], ".png"));
            }
        }
        OutputFileService.Write(path, output.ToArray(), overwrite);
    }

    public ScrollProject Load(string path)
    {
        using var file = File.OpenRead(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        if (archive.Entries.Count > 81) throw new InvalidDataException("工程包含过多条目。");
        var manifestEntry = archive.GetEntry("project.json") ?? throw new InvalidDataException("缺少工程信息。");
        var manifest = JsonSerializer.Deserialize<Manifest>(Read(manifestEntry, 128 * 1024)) ?? throw new InvalidDataException("工程信息无效。");
        if (manifest.Version != 1 || manifest.Count is < 1 or > 80 || manifest.Width < 32 || manifest.Height < 32 ||
            manifest.Width > 16384 || manifest.Height > 16384 || (long)manifest.Width * manifest.Height * manifest.Count > ScrollingStitcher.MaximumFramePixels ||
            manifest.Offsets is null || manifest.Offsets.Length != manifest.Count - 1 || manifest.Matches is null || manifest.Matches.Length != manifest.Count - 1 ||
            manifest.Direction is not (ScrollDirection.Vertical or ScrollDirection.Horizontal) || manifest.FrameTrim is null || manifest.ResultCrop is null)
            throw new InvalidDataException("工程版本、尺寸、帧数或偏移无效。");
        var frames = new List<BitmapSource>();
        for (var index = 0; index < manifest.Count; index++)
        {
            var entry = archive.GetEntry($"frames/{index:D3}.png") ?? throw new InvalidDataException("工程缺少原始帧。");
            var bytes = Read(entry, 64 * 1024 * 1024);
            // Check dimensions before invoking the decoder; don't extract archive paths to disk.
            if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
                BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)) != manifest.Width || BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)) != manifest.Height)
                throw new InvalidDataException("原始帧头或尺寸不符。");
            frames.Add(_images.Decode(bytes));
        }
        var session = new ScrollCaptureSession(frames, manifest.Offsets, manifest.Matches, manifest.Direction, manifest.StopReason);
        var result = new ScrollingStitcher().Stitch(session, manifest.FrameTrim);
        if (manifest.ResultCrop.Left < 0 || manifest.ResultCrop.Top < 0 || manifest.ResultCrop.Right < 0 || manifest.ResultCrop.Bottom < 0 ||
            (long)manifest.ResultCrop.Left + manifest.ResultCrop.Right >= result.Image.PixelWidth || (long)manifest.ResultCrop.Top + manifest.ResultCrop.Bottom >= result.Image.PixelHeight)
            throw new InvalidDataException("工程结果裁剪参数无效。");
        return new ScrollProject(session, manifest.FrameTrim, manifest.ResultCrop);
    }
    private static byte[] Read(ZipArchiveEntry entry, int maximum)
    {
        if (entry.Length > maximum) throw new InvalidDataException("工程条目过大。");
        using var input = entry.Open(); using var output = new MemoryStream();
        var buffer = new byte[8192]; int count;
        while ((count = input.Read(buffer)) != 0)
        { if (output.Length + count > maximum) throw new InvalidDataException("工程条目超出解压限制。"); output.Write(buffer, 0, count); }
        return output.ToArray();
    }
}
