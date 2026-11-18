using L = SnapSharp.Localization.UiText;
using SnapSharp.Models;
using SnapSharp.Native;
using System.Drawing;
using System.Windows.Media.Imaging;
using System.Threading.Channels;

namespace SnapSharp.Services;

/// <summary>Injectable capture/input/timing boundary; regression tests never send real wheel input.</summary>
public interface IScrollCaptureDriver
{
    void Begin();
    void End();
    bool StopRequested { get; }
    BitmapSource Capture(Rectangle region);
    void Scroll(Rectangle region, ScrollDirection direction, int notches);
    Task Delay(int milliseconds, CancellationToken cancellationToken);
}

public sealed class ScrollingCaptureService
{
    private readonly IScrollCaptureDriver _driver;
    private readonly ScrollingStitcher _stitcher = new();

    public ScrollingCaptureService(ICaptureService? capture = null) : this(new DesktopDriver(capture ?? new ScreenCaptureService())) { }
    public ScrollingCaptureService(IScrollCaptureDriver driver) => _driver = driver;

    public async Task<BitmapSource> CaptureAsync(Rectangle region, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
        => _stitcher.Stitch(await CaptureSessionAsync(region, new(), progress, cancellationToken)).Image;

    public Task<ScrollCaptureSession> CaptureSessionAsync(Rectangle region, ScrollCaptureOptions options,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default,
        Func<CancellationToken, Task<bool>>? waitForManualScroll = null,
        Func<ScrollDirection>? manualDirectionProvider = null,
        Action<string>? diagnostic = null,
        Action<bool>? manualFrameObserved = null) => Task.Run(async () =>
    {
        if (region.Width < 32 || region.Height < 32 || region.Width > ScrollingStitcher.MaximumEdge ||
            region.Height > ScrollingStitcher.MaximumEdge || (long)region.Width * region.Height > 16_000_000)
            throw new ArgumentException(L.Get("ScrollingCaptureRegionMustBeAtLeast"));
        if (options.DelayMilliseconds is < 100 or > 5000 || options.WheelNotches is < 1 or > 6 || options.MaximumFrames is < 2 or > 80 ||
            !Enum.IsDefined(options.Direction)) throw new ArgumentException(L.Get("InvalidScrollingCaptureOptions"));
        var frames = new List<BitmapSource>(); var offsets = new List<int>(); var matches = new List<ScrollMatch>();
        var direction = options.Direction;
        var inputAxis = direction == ScrollDirection.Auto ? ScrollDirection.Vertical : direction;
        var unchanged = 0;
        var retryCapture = false;
        var reason = L.Get("MaximumFrameCountReached");
        var position = 0; var minimumPosition = 0; var maximumPosition = 0;
        var trim = options.MatchingTrim ?? new();
        var contentWidth = region.Width - trim.Left - trim.Right; var contentHeight = region.Height - trim.Top - trim.Bottom;
        if (trim.Left < 0 || trim.Top < 0 || trim.Right < 0 || trim.Bottom < 0 || contentWidth < 32 || contentHeight < 32)
            throw new ArgumentException(L.Get("AtLeast3232PixelsPerFrame2"));
        var images = new SkiaImageService();
        using var samplingStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? samplingTask = null;
        Channel<(BitmapSource Image, bool Finish, ScrollDirection Axis)>? samples = null;
        BitmapSource Content(BitmapSource source) => images.Crop(source, new System.Windows.Int32Rect(trim.Left, trim.Top, contentWidth, contentHeight));
        BitmapSource Capture()
        {
            var image = _driver.Capture(region);
            if (image.PixelWidth != region.Width || image.PixelHeight != region.Height) throw new InvalidOperationException(L.Get("CapturedFrameDimensionsDifferFromTheFixed"));
            if (!image.IsFrozen) { image = image.Clone(); image.Freeze(); }
            return image;
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _driver.Begin();
            await _driver.Delay(250, cancellationToken);
            frames.Add(Capture());
            diagnostic?.Invoke($"Baseline: {region.Width}x{region.Height}, trim={trim}, direction={direction}");
            if (waitForManualScroll is not null)
            {
                // Capture independently of matching. A slow overlap search must
                // not hide the intermediate viewports of a fast wheel gesture.
                var capacity = (int)Math.Clamp(ScrollingStitcher.MaximumFramePixels / ((long)region.Width * region.Height) / 2, 1, 12);
                samples = Channel.CreateBounded<(BitmapSource, bool, ScrollDirection)>(new BoundedChannelOptions(capacity)
                { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
                samplingTask = Task.Run(async () =>
                {
                    try
                    {
                        byte[] Pixels(BitmapSource source)
                        {
                            var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                            var bytes = new byte[checked(source.PixelWidth * source.PixelHeight * 4)];
                            converted.CopyPixels(bytes, source.PixelWidth * 4, 0);
                            return bytes;
                        }
                        var previousPixels = Pixels(frames[0]);
                        while (true)
                        {
                            var finish = !await waitForManualScroll(samplingStop.Token);
                            await _driver.Delay(16, samplingStop.Token);
                            var image = Capture();
                            var pixels = Pixels(image);
                            var changed = !pixels.AsSpan().SequenceEqual(previousPixels);
                            previousPixels = pixels;
                            manualFrameObserved?.Invoke(changed);
                            var axis = manualDirectionProvider?.Invoke() ?? ScrollDirection.Auto;
                            // Identical samples still drive settling, but do not
                            // consume matching time or queue capacity. Always send finish.
                            if (changed || finish) await samples.Writer.WriteAsync((image, finish, axis), samplingStop.Token);
                            if (finish) break;
                        }
                        samples.Writer.TryComplete();
                    }
                    catch (Exception ex) { samples.Writer.TryComplete(ex); }
                }, samplingStop.Token);
            }
            var finishManual = false;
            var attempts = 0;
            for (var index = 1; index < options.MaximumFrames; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (waitForManualScroll is null && _driver.StopRequested) { reason = L.Get("StoppedWithEscCapturedFramesPreserved"); break; }
                if ((long)(frames.Count + 1) * region.Width * region.Height > ScrollingStitcher.MaximumFramePixels)
                { reason = L.Get("RawFrameMemoryLimitReachedCaptureIn"); break; }
                var manualDirection = direction == ScrollDirection.Auto ? L.Get("DownOrRight") : direction == ScrollDirection.Horizontal ? L.Get("Right4") : L.Get("Down2");
                progress?.Report(waitForManualScroll is null ? L.Format("Captured0FramesScroll1EscTo", frames.Count, (inputAxis == ScrollDirection.Vertical ? L.Get("Down2") : L.Get("Right4"))) : L.Format("Captured0FramesScrollSlowly1Preview", frames.Count, manualDirection));
                BitmapSource next;
                if (samples is null)
                {
                    if (!retryCapture) _driver.Scroll(region, inputAxis, options.WheelNotches);
                    await _driver.Delay(options.DelayMilliseconds, cancellationToken);
                    if (_driver.StopRequested) { reason = L.Get("StoppedWithEscCapturedFramesPreserved"); break; }
                    next = Capture();
                }
                else
                {
                    var sample = await samples.Reader.ReadAsync(cancellationToken);
                    next = sample.Image;
                    finishManual = sample.Finish;
                    if (!finishManual && direction == ScrollDirection.Auto)
                    {
                        direction = sample.Axis;
                        if (!Enum.IsDefined(direction))
                            throw new ArgumentException(L.Get("InvalidMatchingDirection"));
                    }
                }
                var difference = ScrollingStitcher.Difference(Content(frames[^1]), Content(next));
                attempts++;
                if (difference < .8)
                {
                    if (attempts % 30 == 0 || finishManual) diagnostic?.Invoke($"Sample {attempts}: unchanged, difference={difference:F3}, retained={frames.Count}");
                    if (waitForManualScroll is not null) { if (finishManual) { reason = L.Get("PreviewConfirmed"); break; } index--; continue; }
                    unchanged++;
                    if (direction == ScrollDirection.Auto && inputAxis == ScrollDirection.Vertical && unchanged >= 3)
                    { inputAxis = ScrollDirection.Horizontal; unchanged = 0; retryCapture = false; continue; }
                    if (unchanged >= 3) { reason = L.Get("ReachedTheEndOrNoChangeIn"); break; }
                    if (waitForManualScroll is null) retryCapture = true;
                    continue;
                }
                unchanged = 0; retryCapture = false;
                var match = _stitcher.Match(Content(frames[^1]), Content(next), direction);
                diagnostic?.Invoke($"Sample {attempts}: difference={difference:F3}, match={match}, retained={frames.Count}, finishing={finishManual}");
                // Never discard an uncertain viewport: the preview needs both originals
                // for manual seam correction. Stop here rather than propagating a guessed
                // document coordinate into later frames (especially after a reversal).
                if (direction == ScrollDirection.Auto) direction = match.Direction;
                inputAxis = direction;
                // Zero is an explicitly unresolved seam, not a guessed document shift.
                // It keeps the original frame available without extending the output.
                if (!match.Reliable) match = match with { Offset = 0 };
                var length = direction == ScrollDirection.Vertical ? contentHeight : contentWidth;
                var nextPosition = position + match.Offset;
                var nextLength = (long)Math.Max(maximumPosition, nextPosition) - Math.Min(minimumPosition, nextPosition) + length;
                if (nextLength > ScrollingStitcher.MaximumEdge ||
                    nextLength * (direction == ScrollDirection.Vertical ? region.Width : region.Height) > ScrollingStitcher.MaximumPixels)
                { reason = L.Get("OutputSizeLimitReachedCaptureInSmaller"); break; }
                frames.Add(next); offsets.Add(match.Offset); matches.Add(match);
                position = nextPosition; minimumPosition = Math.Min(minimumPosition, position); maximumPosition = Math.Max(maximumPosition, position);
                if (!match.Reliable)
                { reason = L.Format("Frame0MatchIsUncertainError1", frames.Count, match.Error); break; }
                if (finishManual) { reason = L.Get("PreviewConfirmed"); break; }
            }
        }
        catch (OperationCanceledException) when (frames.Count > 0)
        { reason = L.Get("StoppedCapturedFramesPreserved"); }
        catch (Exception ex) when (frames.Count > 0)
        { reason = L.Get("CaptureInterruptedOriginalFramesPreserved") + ex.Message; }
        finally
        {
            samplingStop.Cancel();
            if (samplingTask is not null)
            {
                try { await samplingTask; }
                catch (OperationCanceledException) { }
            }
            _driver.End();
        }
        diagnostic?.Invoke($"Finished: frames={frames.Count}, direction={direction}, reason={reason}");
        return new ScrollCaptureSession(frames.ToArray(), offsets.ToArray(), matches.ToArray(),
            direction == ScrollDirection.Auto ? inputAxis : direction, reason);
    }, cancellationToken);

    private sealed class DesktopDriver(ICaptureService capture) : IScrollCaptureDriver
    {
        private NativeMethods.PointL _cursor;
        private bool _started;
        private bool _moved;
        public void Begin() { _started = NativeMethods.GetCursorPos(out _cursor); _ = NativeMethods.GetAsyncKeyState(0x1B); }
        public void End() { if (_started && _moved) NativeMethods.SetCursorPos(_cursor.X, _cursor.Y); _started = _moved = false; }
        public bool StopRequested => (NativeMethods.GetAsyncKeyState(0x1B) & 0x8001) != 0;
        public BitmapSource Capture(Rectangle region) => capture.CaptureRegion(region);
        public async Task Delay(int milliseconds, CancellationToken cancellationToken)
        {
            for (var elapsed = 0; elapsed < milliseconds; elapsed += 40)
            {
                await Task.Delay(Math.Min(40, milliseconds - elapsed), cancellationToken);
                if (StopRequested) throw new OperationCanceledException(L.Get("StoppedWithEsc"));
            }
        }
        public void Scroll(Rectangle region, ScrollDirection direction, int notches)
        {
            _moved = true;
            NativeMethods.SetCursorPos(region.Left + region.Width / 2, region.Top + region.Height / 2);
            NativeMethods.mouse_event(direction == ScrollDirection.Horizontal ? 0x1000u : NativeMethods.MouseEventWheel,
                0, 0, direction == ScrollDirection.Horizontal ? notches * 120 : -notches * 120, 0);
        }
    }
}
