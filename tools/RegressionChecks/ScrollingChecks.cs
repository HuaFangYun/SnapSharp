using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NimCap.Screenshot.Models;
using NimCap.Screenshot.Services;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FlowDirection = System.Windows.FlowDirection;
using Point = System.Windows.Point;

namespace NimCap.Screenshot.RegressionChecks;

internal static partial class Program
{
    private sealed class ScrollDriver(BitmapSource[] frames) : IScrollCaptureDriver
    {
        public int Index;
        public int Captures;
        public bool Ended;
        public bool StopRequested => false;
        public void Begin() { }
        public void End() => Ended = true;
        public BitmapSource Capture(System.Drawing.Rectangle region)
        { Interlocked.Increment(ref Captures); return frames[Index]; }
        public void Scroll(System.Drawing.Rectangle region, ScrollDirection direction, int notches) => throw new Exception("Manual capture injected scroll input");
        public Task Delay(int milliseconds, CancellationToken token) => Task.CompletedTask;
    }

    private static void ScrollingChecks()
    {
        var activity = new ManualScrollActivity();
        Require(!activity.Active, "Idle session sampled without a wheel event");
        activity.Input(1000);
        activity.Observe(false, 1016); activity.Observe(false, 1200);
        Require(activity.Active, "Initial old viewport ended capture before delayed painting");
        activity.Observe(true, 1300); activity.Observe(false, 1320); activity.Observe(true, 1450);
        Require(activity.Active, "Inertial painting failed to extend the gesture");
        activity.Observe(false, 1470); activity.Observe(false, 1630);
        Require(!activity.Active, "Stable viewport did not return to event waiting");
        activity.Input(1700); activity.Observe(false, 1720); activity.Input(1900); activity.Observe(false, 2060);
        Require(activity.Active, "New wheel event did not renew the settling period");
        activity.Observe(false, 2260);
        Require(!activity.Active, "Second gesture failed to settle");
        var images = new SkiaImageService(); var stitcher = new ScrollingStitcher();
        var random = new Random(317);
        var pixels = new byte[720 * 720 * 4]; random.NextBytes(pixels);
        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var document = BitmapSource.Create(720, 720, 96, 96, PixelFormats.Bgra32, null, pixels, 720 * 4); document.Freeze();
        static byte[] Bytes(BitmapSource image)
        {
            var bytes = new byte[image.PixelWidth * image.PixelHeight * 4];
            image.CopyPixels(bytes, image.PixelWidth * 4, 0); return bytes;
        }
        foreach (var axis in new[] { ScrollDirection.Vertical, ScrollDirection.Horizontal })
        foreach (var positions in new[] { new[] { 160, 233, 306, 233, 160, 87, 14, 87, 160, 233, 306, 379 }, new[] { 379, 306, 233, 160, 87, 14 } })
        {
            var vertical = axis == ScrollDirection.Vertical;
            var frames = positions.Select(p => images.Crop(document, vertical ? new Int32Rect(0, p, 240, 200) : new Int32Rect(p, 0, 200, 240))).ToArray();
            var matches = Enumerable.Range(1, frames.Length - 1).Select(i => stitcher.Match(frames[i - 1], frames[i], axis)).ToArray();
            Require(matches.Select(x => x.Offset).SequenceEqual(positions.Skip(1).Select((p, i) => p - positions[i])) && matches.All(x => x.Reliable), "Signed frame matching failed");
            var session = new ScrollCaptureSession(frames, matches.Select(x => x.Offset).ToArray(), matches, axis, "test");
            var result = stitcher.Stitch(session);
            var expected = images.Crop(document, vertical ? new Int32Rect(0, positions.Min(), 240, positions.Max() - positions.Min() + 200) : new Int32Rect(positions.Min(), 0, positions.Max() - positions.Min() + 200, 240));
            Require(result.Image.PixelWidth == expected.PixelWidth && result.Image.PixelHeight == expected.PixelHeight && Bytes(result.Image).SequenceEqual(Bytes(expected)), "Round-trip scrolling duplicated, reordered or lost pixels");
            Require(result.FrameStarts.SequenceEqual(positions.Select(p => p - positions.Min())), "Frame coordinates not normalized");
            var driver = new ScrollDriver(frames);
            var captured = new ScrollingCaptureService(driver).CaptureSessionAsync(new System.Drawing.Rectangle(0, 0, frames[0].PixelWidth, frames[0].PixelHeight),
                new ScrollCaptureOptions(MaximumFrames: 30), waitForManualScroll: _ =>
                { if (driver.Index == frames.Length - 1) return Task.FromResult(false); driver.Index++; return Task.FromResult(true); },
                manualDirectionProvider: () => axis).GetAwaiter().GetResult();
            Require(captured.Offsets.SequenceEqual(session.Offsets) && Bytes(stitcher.Stitch(captured).Image).SequenceEqual(Bytes(expected)), "Capture lost reverse scroll frames");
            var path = Path.Combine(Root, $"scroll-{axis}-{positions.Length}.ssscroll");
            var projects = new ScrollProjectService(); projects.Save(path, new ScrollProject(session, new(), new()));
            Require(Bytes(stitcher.Stitch(projects.Load(path).Session).Image).SequenceEqual(Bytes(expected)), "Signed project roundtrip failed");
        }
        var visual = new DrawingVisual();
        using (var canvas = visual.RenderOpen())
        {
            canvas.DrawRectangle(new SolidColorBrush(Color.FromRgb(17, 19, 20)), null, new Rect(0, 0, 680, 3000));
            var y = 0;
            foreach (var line in File.ReadAllLines("docs/releases/v1.1.0.md"))
            {
                for (var index = 0; index < Math.Max(1, line.Length); index += 70)
                {
                    var text = line.Substring(index, Math.Min(70, line.Length - index));
                    canvas.DrawText(new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface("Consolas"), 13, Brushes.LightGray, 1), new Point(8, y)); y += 19;
                }
            }
        }
        var textDocument = new RenderTargetBitmap(680, 3000, 96, 96, PixelFormats.Pbgra32); textDocument.Render(visual); textDocument.Freeze();
        foreach (var viewportHeight in new[] { 160, 400 })
        foreach (var shift in new[] { 1, 7, 19, 73 })
        foreach (var pair in Enumerable.Range(0, 32).SelectMany(i => new[] { (i * 41, i * 41 + shift), (i * 41 + shift, i * 41) }))
        {
            var first = images.Crop(textDocument, new Int32Rect(0, pair.Item1, 680, viewportHeight));
            var second = images.Crop(textDocument, new Int32Rect(0, pair.Item2, 680, viewportHeight));
            var match = stitcher.Match(first, second, ScrollDirection.Vertical);
            Require(match.Reliable && match.Offset == pair.Item2 - pair.Item1, $"Dark release-note matching failed: height={viewportHeight}, {pair}, {match}");
        }
        var shortPositions = new[] { 451, 524, 597, 524, 451, 378, 451, 524, 597, 670 };
        // Only 21 rows overlap: the previous height/6 search bound excluded this
        // valid seam entirely. Exercise both signs on the release-note text.
        foreach (var shift in new[] { 139, -139 })
        {
            var origin = shift > 0 ? 200 : 339;
            var first = images.Crop(textDocument, new Int32Rect(0, origin, 680, 160));
            var second = images.Crop(textDocument, new Int32Rect(0, origin + shift, 680, 160));
            var match = stitcher.Match(first, second, ScrollDirection.Vertical);
            Require(match.Reliable && match.Offset == shift, $"Short overlap row matching failed: {match}");
        }
        var blankPixels = new byte[680 * 160 * 4];
        for (var i = 3; i < blankPixels.Length; i += 4) blankPixels[i] = 255;
        var blank = BitmapSource.Create(680, 160, 96, 96, PixelFormats.Bgra32, null, blankPixels, 680 * 4); blank.Freeze();
        Require(!stitcher.Match(blank, blank, ScrollDirection.Vertical).Reliable, "Blank rows certified a seam");
        var shortFrames = shortPositions.Select(p => images.Crop(textDocument, new Int32Rect(0, p, 680, 160))).ToArray();
        var shortDriver = new ScrollDriver(shortFrames);
        var sampledWhileMatching = false;
        var shortCapture = new ScrollingCaptureService(shortDriver).CaptureSessionAsync(new System.Drawing.Rectangle(0, 0, 680, 160),
            new ScrollCaptureOptions(MaximumFrames: 30), waitForManualScroll: _ =>
            {
                if (shortDriver.Index == shortFrames.Length - 1) return Task.FromResult(false);
                shortDriver.Index++; return Task.FromResult(true);
            }, manualDirectionProvider: () => ScrollDirection.Vertical, diagnostic: message =>
            {
                if (message.StartsWith("Sample 1:"))
                    sampledWhileMatching = SpinWait.SpinUntil(() => Volatile.Read(ref shortDriver.Captures) >= shortFrames.Length, 2000);
            }).GetAwaiter().GetResult();
        Require(sampledWhileMatching, "Capture stalled while the matching consumer was busy");
        Require(shortCapture.Frames.Count == shortFrames.Length, $"Short text viewport capture retained only {shortCapture.Frames.Count} frames");
        var shortExpected = images.Crop(textDocument, new Int32Rect(0, shortPositions.Min(), 680, shortPositions.Max() - shortPositions.Min() + 160));
        var shortOutput = stitcher.Stitch(shortCapture).Image;
        // WPF's Pbgra32 text render and Skia's Bgra32 output can round a channel
        // by one level. Assert document offsets exactly and allow only that rounding.
        Require(shortCapture.Offsets.SequenceEqual(shortPositions.Skip(1).Select((p, i) => p - shortPositions[i])) &&
            shortOutput.PixelHeight == shortExpected.PixelHeight && Bytes(shortOutput).Zip(Bytes(shortExpected)).All(p => Math.Abs(p.First - p.Second) <= 1),
            $"Short text viewport capture lost or reordered paragraphs: height {shortOutput.PixelHeight}/{shortExpected.PixelHeight}, offsets {string.Join(',', shortCapture.Offsets)}");
        // A lost overlap must retain the uncertain original and hand off to correction,
        // without inventing a position or silently losing a viewport.
        var recoverFrames = new[] { 0, 100, 200, 300, 400, 500 }
            .Select(p => images.Crop(document, new Int32Rect(0, p, 240, 200))).ToList();
        recoverFrames.Insert(4, images.Crop(document, new Int32Rect(400, 0, 240, 200)));
        var recoverDriver = new ScrollDriver(recoverFrames.ToArray());
        var recovered = new ScrollingCaptureService(recoverDriver).CaptureSessionAsync(new System.Drawing.Rectangle(0, 0, 240, 200),
            new ScrollCaptureOptions(MaximumFrames: 20), waitForManualScroll: _ =>
            {
                if (recoverDriver.Index == recoverFrames.Count - 1) return Task.FromResult(false);
                recoverDriver.Index++; return Task.FromResult(true);
            }, manualDirectionProvider: () => ScrollDirection.Vertical).GetAwaiter().GetResult();
        Require(recovered.Frames.Count == 5 && recovered.Offsets.SequenceEqual(new[] { 100, 100, 100, 0 }) &&
            !recovered.Matches[^1].Reliable && recoverDriver.Ended && Bytes(recovered.Frames[^1]).SequenceEqual(Bytes(recoverFrames[4])),
            "Uncertain frame was lost or capture continued past an unresolved seam");
        Require(Bytes(stitcher.Stitch(recovered).Image).SequenceEqual(Bytes(images.Crop(document, new Int32Rect(0, 0, 240, 500)))),
            "Unresolved frame changed trusted document pixels");
        var unresolvedPath = Path.Combine(Root, "unresolved.ssscroll");
        var unresolvedProjects = new ScrollProjectService(); unresolvedProjects.Save(unresolvedPath, new(recovered, new(), new()));
        var unresolvedLoaded = unresolvedProjects.Load(unresolvedPath).Session;
        Require(unresolvedLoaded.Offsets[^1] == 0 && !unresolvedLoaded.Matches[^1].Reliable &&
            Bytes(unresolvedLoaded.Frames[^1]).SequenceEqual(Bytes(recoverFrames[4])), "Unresolved project lost raw pixels or review state");
        using var stop = new CancellationTokenSource(100);
        var idleDriver = new ScrollDriver([shortFrames[0]]);
        var cancelled = new ScrollingCaptureService(idleDriver).CaptureSessionAsync(new System.Drawing.Rectangle(0, 0, 680, 160),
            new ScrollCaptureOptions(), cancellationToken: stop.Token, waitForManualScroll: async token =>
            { await Task.Delay(Timeout.Infinite, token); return true; }).GetAwaiter().GetResult();
        Require(cancelled.Frames.Count == 1 && idleDriver.Ended, "Cancelling idle sampling did not release the driver");
        Console.WriteLine("PASS: signed matching, up/down and left/right reversals, no duplicated revisit pixels, capture pipeline, project roundtrip and dark release-note text.");
    }
}
