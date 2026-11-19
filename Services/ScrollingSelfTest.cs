using NimCap.Screenshot.Models;
using NimCap.Screenshot.Views;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace NimCap.Screenshot.Services;

internal static class ScrollingSelfTest
{
    private sealed class Driver(BitmapSource[] frames, ScrollDirection axis) : IScrollCaptureDriver
    {
        private int _index;
        public List<System.Drawing.Rectangle> Regions { get; } = [];
        public List<ScrollDirection> Inputs { get; } = [];
        public bool Ended { get; private set; }
        public bool StopRequested { get; set; }
        public Action<int>? OnDelay { get; set; }
        public Queue<BitmapSource>? CaptureOverrides { get; set; }
        public void Begin() { }
        public void Advance() => _index = Math.Min(frames.Length - 1, _index + 1);
        public void End() => Ended = true;
        public BitmapSource Capture(System.Drawing.Rectangle region)
        { Regions.Add(region); return CaptureOverrides is { Count: > 0 } ? CaptureOverrides.Dequeue() : frames[_index]; }
        public void Scroll(System.Drawing.Rectangle region, ScrollDirection direction, int notches)
        { Inputs.Add(direction); if (direction == axis) _index = Math.Min(frames.Length - 1, _index + 1); }
        public Task Delay(int milliseconds, CancellationToken cancellationToken)
        { OnDelay?.Invoke(Inputs.Count); cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    }
    private static BitmapSource Pattern(int width, int height, int seed = 1)
    {
        var random = new Random(seed); var pixels = new byte[width * height * 4]; random.NextBytes(pixels);
        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); result.Freeze(); return result;
    }
    private static bool Same(BitmapSource left, BitmapSource right)
    {
        if (left.PixelWidth != right.PixelWidth || left.PixelHeight != right.PixelHeight) return false;
        byte[] Bytes(BitmapSource image)
        {
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            var bytes = new byte[image.PixelWidth * image.PixelHeight * 4]; converted.CopyPixels(bytes, image.PixelWidth * 4, 0); return bytes;
        }
        return Bytes(left).SequenceEqual(Bytes(right));
    }
    public static bool Run(string directory)
    {
        if (!ScrollCaptureRegionWindow.RunVisualSelfTest()) return false;
        var images = new SkiaImageService(); var stitcher = new ScrollingStitcher();
        var document = Pattern(240, 1000);
        var textVisual = new DrawingVisual();
        using (var context = textVisual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, 300, 1000));
            for (var line = 0; line < 27; line++) context.DrawText(new FormattedText($"第 {line} 段  {(line * 7391):X}  长截图示例文字",
                System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 13, Brushes.Black, 1), new Point(12, line * 37 + 10));
        }
        var textDocument = new RenderTargetBitmap(300, 1000, 96, 96, PixelFormats.Pbgra32); textDocument.Render(textVisual); textDocument.Freeze();
        var textMatch = stitcher.Match(images.Crop(textDocument, new Int32Rect(0, 0, 300, 200)), images.Crop(textDocument, new Int32Rect(0, 73, 300, 200)));
        if (!textMatch.Reliable || textMatch.Offset != 73 || textMatch.Direction != ScrollDirection.Vertical) throw new Exception("scroll-sparse-text-match");
        var frames = Enumerable.Range(0, 5).Select(i => images.Crop(document, new Int32Rect(0, i * 53, 240, 180))).ToArray();
        var matches = Enumerable.Range(1, frames.Length - 1).Select(i => stitcher.Match(frames[i - 1], frames[i])).ToArray();
        if (matches.Any(x => !x.Reliable || x.Direction != ScrollDirection.Vertical || x.Offset != 53)) throw new Exception("scroll-match-vertical");
        var session = new ScrollCaptureSession(frames, matches.Select(x => x.Offset).ToArray(), matches, ScrollDirection.Vertical, "test");
        var manualDriver = new Driver(frames, ScrollDirection.Vertical); var wheelEvents = 0;
        var baselineReady = true;
        var manual = new ScrollingCaptureService(manualDriver).CaptureSessionAsync(new System.Drawing.Rectangle(-1200, 20, 240, 180),
            new ScrollCaptureOptions(ScrollDirection.Vertical, 120, 1, 20), waitForManualScroll: _ =>
            {
                baselineReady &= manualDriver.Regions.Count > 0;
                wheelEvents++;
                if (wheelEvents < 4) return Task.FromResult(true); // Idle/no-change must not finish a manual session.
                manualDriver.Advance(); return Task.FromResult(wheelEvents < 8);
            }).GetAwaiter().GetResult();
        if (!baselineReady || manualDriver.Inputs.Count != 0 || manual.Frames.Count != 5 || !manualDriver.Ended || manual.Offsets.Any(x => x != 53)) throw new Exception("manual-scroll-baseline-before-input");
        var stitched = stitcher.Stitch(session);
        if (!Same(stitched.Image, images.Crop(document, new Int32Rect(0, 0, 240, 392))) || !stitched.Seams.SequenceEqual(new[] { 180, 233, 286, 339 })) throw new Exception("scroll-vertical-pixels");
        var horizontal = Pattern(1000, 180, 2);
        var horizontalFrames = Enumerable.Range(0, 4).Select(i => images.Crop(horizontal, new Int32Rect(i * 67, 0, 220, 180))).ToArray();
        var horizontalMatches = Enumerable.Range(1, 3).Select(i => stitcher.Match(horizontalFrames[i - 1], horizontalFrames[i])).ToArray();
        if (horizontalMatches.Any(x => !x.Reliable || x.Direction != ScrollDirection.Horizontal || x.Offset != 67)) throw new Exception("scroll-match-horizontal");
        var horizontalSession = new ScrollCaptureSession(horizontalFrames, [67, 67, 67], horizontalMatches, ScrollDirection.Horizontal, "horizontal");
        if (!Same(stitcher.Stitch(horizontalSession).Image, images.Crop(horizontal, new Int32Rect(0, 0, 421, 180)))) throw new Exception("scroll-horizontal-pixels");
        var manualHorizontalDriver = new Driver(horizontalFrames, ScrollDirection.Horizontal);
        var manualHorizontalEvents = 0;
        var manualHorizontal = new ScrollingCaptureService(manualHorizontalDriver).CaptureSessionAsync(
            new System.Drawing.Rectangle(-500, 50, 220, 180), new ScrollCaptureOptions(ScrollDirection.Auto),
            waitForManualScroll: _ =>
            {
                manualHorizontalDriver.Advance();
                return Task.FromResult(++manualHorizontalEvents < 4);
            }, manualDirectionProvider: () => ScrollDirection.Horizontal).GetAwaiter().GetResult();
        if (manualHorizontal.Direction != ScrollDirection.Horizontal || manualHorizontal.Frames.Count != 4 ||
            manualHorizontal.Offsets.Any(x => x != 67) || manualHorizontalDriver.Inputs.Count != 0 ||
            !Same(stitcher.Stitch(manualHorizontal).Image, stitcher.Stitch(horizontalSession).Image))
            throw new Exception("manual-horizontal-auto-axis-and-pixels");
        // Even if image similarity favors horizontal movement, vertical wheel input
        // must prevent the session from turning into a wide, horizontally stitched image.
        var axisDriver = new Driver(horizontalFrames, ScrollDirection.Horizontal);
        var axisEvents = 0;
        var axisSession = new ScrollingCaptureService(axisDriver).CaptureSessionAsync(
            new System.Drawing.Rectangle(0, 0, 220, 180), new ScrollCaptureOptions(),
            waitForManualScroll: _ => { axisDriver.Advance(); return Task.FromResult(++axisEvents < 4); },
            manualDirectionProvider: () => ScrollDirection.Vertical).GetAwaiter().GetResult();
        if (axisSession.Direction != ScrollDirection.Vertical || axisSession.Matches.Any(x => x.Direction != ScrollDirection.Vertical) ||
            stitcher.Stitch(axisSession).Image.PixelWidth != 220) throw new Exception("manual-wheel-axis-overrides-image-similarity");
        var trim = new ScrollTrim(7, 20, 9, 18);
        var trimmed = stitcher.Stitch(session, trim);
        if (!Same(trimmed.Image, images.Crop(document, new Int32Rect(7, 20, 224, 354)))) throw new Exception("scroll-frame-trim");
        var manualOffsets = session.Offsets.ToArray(); manualOffsets[1]++;
        if (stitcher.Stitch(session with { Offsets = manualOffsets }).Image.PixelHeight != stitched.Image.PixelHeight + 1) throw new Exception("scroll-manual-offset");
        var whitePixels = Enumerable.Repeat((byte)255, 240 * 180 * 4).ToArray();
        var white = BitmapSource.Create(240, 180, 96, 96, PixelFormats.Bgra32, null, whitePixels, 240 * 4); white.Freeze();
        if (stitcher.Match(white, white).Reliable) throw new Exception("scroll-blank-confidence");
        if (stitcher.Match(frames[0], Pattern(240, 180, 82)).Reliable) throw new Exception("scroll-unrelated-confidence");
        foreach (var bad in new[] { new ScrollTrim(0, 180), new ScrollTrim(-1), new ScrollTrim(240) })
        { try { stitcher.Stitch(session, bad); return false; } catch (ArgumentException) { } }
        try { stitcher.Stitch(session with { Offsets = [0, 53, 53, 53] }); return false; } catch (ArgumentException) { }
        var large = Pattern(64, 512);
        try { stitcher.Stitch(new ScrollCaptureSession(Enumerable.Repeat(large, 40).ToArray(), Enumerable.Repeat(512, 39).ToArray(), [], ScrollDirection.Vertical, "")); return false; } catch (ArgumentException) { }

        var region = new System.Drawing.Rectangle(-1600, -100, 240, 180);
        var driver = new Driver(frames, ScrollDirection.Vertical);
        var captured = new ScrollingCaptureService(driver).CaptureSessionAsync(region, new()).GetAwaiter().GetResult();
        if (captured.Frames.Count != frames.Length || captured.Offsets.Any(x => x != 53) || !driver.Ended || driver.Regions.Any(x => x != region) || !captured.StopReason.Contains("连续 3 帧")) throw new Exception("scroll-driver-fixed-region");
        var stickyFrames = frames.Select((frame, index) =>
        {
            using var bitmap = images.ToSkBitmap(frame); using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint { Color = new SKColor((byte)(40 + index * 35), 90, 120) };
            canvas.DrawRect(0, 0, 240, 20, paint); canvas.DrawRect(0, 162, 240, 18, paint); canvas.Flush();
            return images.ToBitmapSource(bitmap);
        }).ToArray();
        var stickySession = new ScrollingCaptureService(new Driver(stickyFrames, ScrollDirection.Vertical)).CaptureSessionAsync(region,
            new ScrollCaptureOptions(MatchingTrim: trim)).GetAwaiter().GetResult();
        if (stickySession.Frames.Count != frames.Length || stickySession.Offsets.Any(x => x != 53) || !Same(stitcher.Stitch(stickySession, trim).Image, trimmed.Image)) throw new Exception("scroll-fixed-bar-exclusion");
        var horizontalDriver = new Driver(horizontalFrames, ScrollDirection.Horizontal);
        var horizontalCapture = new ScrollingCaptureService(horizontalDriver).CaptureSessionAsync(new System.Drawing.Rectangle(100, 100, 220, 180), new()).GetAwaiter().GetResult();
        if (horizontalCapture.Direction != ScrollDirection.Horizontal || horizontalCapture.Frames.Count != horizontalFrames.Length || !horizontalDriver.Inputs.Contains(ScrollDirection.Horizontal)) throw new Exception("scroll-auto-input-axis");
        var delayedDriver = new Driver(frames, ScrollDirection.Vertical) { CaptureOverrides = new Queue<BitmapSource>([frames[0], frames[0]]) };
        var delayedCapture = new ScrollingCaptureService(delayedDriver).CaptureSessionAsync(region,
            new ScrollCaptureOptions(ScrollDirection.Vertical, MaximumFrames: 3)).GetAwaiter().GetResult();
        if (delayedDriver.Inputs.Count != 1 || delayedCapture.Frames.Count != 2 || delayedCapture.Offsets[0] != 53)
            throw new Exception("scroll-waits-for-delayed-render-without-stacking-wheel-input");
        using var stop = new CancellationTokenSource();
        var cancelDriver = new Driver(frames, ScrollDirection.Vertical) { OnDelay = count => { if (count >= 2) stop.Cancel(); } };
        var partial = new ScrollingCaptureService(cancelDriver).CaptureSessionAsync(region, new(), cancellationToken: stop.Token).GetAwaiter().GetResult();
        if (partial.Frames.Count != 2 || !cancelDriver.Ended || !partial.StopReason.Contains("停止")) throw new Exception("scroll-cancel-partial");
        var badDriver = new Driver([frames[0], Pattern(240, 180, 92)], ScrollDirection.Vertical);
        var uncertain = new ScrollingCaptureService(badDriver).CaptureSessionAsync(region, new()).GetAwaiter().GetResult();
        if (uncertain.Frames.Count != 2 || !uncertain.StopReason.Contains("不确定")) throw new Exception("scroll-preserve-unmatched");
        var badManualDriver = new Driver([frames[0], Pattern(240, 180, 93)], ScrollDirection.Vertical);
        var badManualEvents = 0;
        var uncertainManual = new ScrollingCaptureService(badManualDriver).CaptureSessionAsync(region, new(), waitForManualScroll: _ =>
        {
            if (++badManualEvents == 1) { badManualDriver.Advance(); return Task.FromResult(true); }
            return Task.FromResult(false);
        }).GetAwaiter().GetResult();
        if (uncertainManual.Frames.Count != 2 || uncertainManual.Matches.Count != 1 || uncertainManual.Matches[0].Reliable ||
            uncertainManual.Offsets[0] != 0 || stitcher.Stitch(uncertainManual).Image.PixelHeight != 180)
            throw new Exception("manual-scroll-preserves-unresolved-frame-without-inventing-document-position");

        var projectService = new ScrollProjectService();
        var path = Path.Combine(directory, "中文 scrolling.ss scroll.ssscroll");
        var project = new ScrollProject(session, trim, new ScrollTrim(1, 2, 3, 4));
        projectService.Save(path, project);
        var loaded = projectService.Load(path);
        if (!loaded.Session.Offsets.SequenceEqual(session.Offsets) || loaded.FrameTrim != trim || loaded.ResultCrop != project.ResultCrop || !Same(stitcher.Stitch(loaded.Session, loaded.FrameTrim).Image, trimmed.Image)) throw new Exception("scroll-project-roundtrip");
        try { projectService.Save(path, project); return false; } catch (IOException) { }
        var invalid = Path.Combine(directory, "invalid.ssscroll");
        using (var zip = ZipFile.Open(invalid, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("project.json").Open())) writer.Write("{\"Version\":999}");
        try { projectService.Load(invalid); return false; } catch (InvalidDataException) { }
        return ScrollCaptureWindow.RunLayoutSelfTest(session);
    }
}
