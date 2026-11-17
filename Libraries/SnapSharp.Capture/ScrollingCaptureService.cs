using L = SnapSharp.Localization.UiText;
using SnapSharp.Models;
using SnapSharp.Native;
using System.Drawing;
using System.Windows.Media.Imaging;

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
        Func<CancellationToken, Task<bool>>? waitForManualScroll = null) => Task.Run(async () =>
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
        var reason = L.Get("MaximumFrameCountReached");
        var totalLength = 0;
        var trim = options.MatchingTrim ?? new();
        var contentWidth = region.Width - trim.Left - trim.Right; var contentHeight = region.Height - trim.Top - trim.Bottom;
        if (trim.Left < 0 || trim.Top < 0 || trim.Right < 0 || trim.Bottom < 0 || contentWidth < 32 || contentHeight < 32)
            throw new ArgumentException(L.Get("AtLeast3232PixelsPerFrame2"));
        var images = new SkiaImageService();
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
            var finishManual = false;
            for (var index = 1; index < options.MaximumFrames; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (waitForManualScroll is null && _driver.StopRequested) { reason = L.Get("StoppedWithEscCapturedFramesPreserved"); break; }
                if ((long)(frames.Count + 1) * region.Width * region.Height > ScrollingStitcher.MaximumFramePixels)
                { reason = L.Get("RawFrameMemoryLimitReachedCaptureIn"); break; }
                var manualDirection = direction == ScrollDirection.Auto ? L.Get("DownOrRight") : direction == ScrollDirection.Horizontal ? L.Get("Right4") : L.Get("Down2");
                progress?.Report(waitForManualScroll is null ? L.Format("Captured0FramesScroll1EscTo", frames.Count, (inputAxis == ScrollDirection.Vertical ? L.Get("Down2") : L.Get("Right4"))) : L.Format("Captured0FramesScrollSlowly1Preview", frames.Count, manualDirection));
                if (waitForManualScroll is null) _driver.Scroll(region, inputAxis, options.WheelNotches);
                else finishManual = !await waitForManualScroll(cancellationToken);
                await _driver.Delay(options.DelayMilliseconds, cancellationToken);
                if (waitForManualScroll is null && _driver.StopRequested) { reason = L.Get("StoppedWithEscCapturedFramesPreserved"); break; }
                var next = Capture();
                if (ScrollingStitcher.Difference(Content(frames[^1]), Content(next)) < .8)
                {
                    if (waitForManualScroll is not null) { if (finishManual) { reason = L.Get("PreviewConfirmed"); break; } index--; continue; }
                    unchanged++;
                    if (direction == ScrollDirection.Auto && inputAxis == ScrollDirection.Vertical && unchanged >= 2)
                    { inputAxis = ScrollDirection.Horizontal; unchanged = 0; continue; }
                    if (unchanged >= 3) { reason = L.Get("ReachedTheEndOrNoChangeIn"); break; }
                    continue;
                }
                unchanged = 0;
                var match = _stitcher.Match(Content(frames[^1]), Content(next), direction);
                if (waitForManualScroll is not null && !match.Reliable)
                {
                    progress?.Report(L.Format("ScrollJumpWasTooLargeOrContent", manualDirection));
                    if (finishManual) { reason = L.Get("LastFrameCouldNotBeMatchedReliably"); break; }
                    index--; continue;
                }
                if (direction == ScrollDirection.Auto) direction = match.Direction;
                inputAxis = direction;
                var length = direction == ScrollDirection.Vertical ? contentHeight : contentWidth;
                totalLength = frames.Count == 1 ? length : totalLength;
                if ((long)totalLength + match.Offset > ScrollingStitcher.MaximumEdge ||
                    ((long)totalLength + match.Offset) * (direction == ScrollDirection.Vertical ? region.Width : region.Height) > ScrollingStitcher.MaximumPixels)
                { reason = L.Get("OutputSizeLimitReachedCaptureInSmaller"); break; }
                frames.Add(next); offsets.Add(match.Offset); matches.Add(match); totalLength += match.Offset;
                if (finishManual) { reason = L.Get("PreviewConfirmed"); break; }
                if (!match.Reliable)
                { reason = L.Format("Frame0MatchIsUncertainError1", frames.Count, match.Error); break; }
            }
        }
        catch (OperationCanceledException) when (frames.Count > 0)
        { reason = L.Get("StoppedCapturedFramesPreserved"); }
        catch (Exception ex) when (frames.Count > 0)
        { reason = L.Get("CaptureInterruptedOriginalFramesPreserved") + ex.Message; }
        finally { _driver.End(); }
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
