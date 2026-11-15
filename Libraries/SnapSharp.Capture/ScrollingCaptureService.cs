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
            throw new ArgumentException("长截图选区需至少 32 × 32 像素，单帧不超过 1600 万像素。");
        if (options.DelayMilliseconds is < 100 or > 5000 || options.WheelNotches is < 1 or > 6 || options.MaximumFrames is < 2 or > 80 ||
            !Enum.IsDefined(options.Direction)) throw new ArgumentException("滚动采集参数无效。");
        var frames = new List<BitmapSource>(); var offsets = new List<int>(); var matches = new List<ScrollMatch>();
        var direction = options.Direction;
        var inputAxis = direction == ScrollDirection.Auto ? ScrollDirection.Vertical : direction;
        var unchanged = 0;
        var reason = "达到最大帧数。";
        var totalLength = 0;
        var trim = options.MatchingTrim ?? new();
        var contentWidth = region.Width - trim.Left - trim.Right; var contentHeight = region.Height - trim.Top - trim.Bottom;
        if (trim.Left < 0 || trim.Top < 0 || trim.Right < 0 || trim.Bottom < 0 || contentWidth < 32 || contentHeight < 32)
            throw new ArgumentException("每帧裁边后需至少保留 32 × 32 像素供匹配。");
        var images = new SkiaImageService();
        BitmapSource Content(BitmapSource source) => images.Crop(source, new System.Windows.Int32Rect(trim.Left, trim.Top, contentWidth, contentHeight));
        BitmapSource Capture()
        {
            var image = _driver.Capture(region);
            if (image.PixelWidth != region.Width || image.PixelHeight != region.Height) throw new InvalidOperationException("捕获帧偏离固定区域尺寸，已停止。");
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
                if (waitForManualScroll is null && _driver.StopRequested) { reason = "已按 Esc 停止，保留已采集帧。"; break; }
                if ((long)(frames.Count + 1) * region.Width * region.Height > ScrollingStitcher.MaximumFramePixels)
                { reason = "达到原始帧内存上限，请分段采集。"; break; }
                var manualDirection = direction == ScrollDirection.Auto ? "向下或向右" : direction == ScrollDirection.Horizontal ? "向右" : "向下";
                progress?.Report(waitForManualScroll is null ? $"已采集 {frames.Count} 帧 · {(inputAxis == ScrollDirection.Vertical ? "向下" : "向右")}滚动 · Esc 停止" : $"已采集 {frames.Count} 帧 · 缓慢{manualDirection}滚动 · √ 预览，× 退出");
                if (waitForManualScroll is null) _driver.Scroll(region, inputAxis, options.WheelNotches);
                else finishManual = !await waitForManualScroll(cancellationToken);
                await _driver.Delay(options.DelayMilliseconds, cancellationToken);
                if (waitForManualScroll is null && _driver.StopRequested) { reason = "已按 Esc 停止，保留已采集帧。"; break; }
                var next = Capture();
                if (ScrollingStitcher.Difference(Content(frames[^1]), Content(next)) < .8)
                {
                    if (waitForManualScroll is not null) { if (finishManual) { reason = "已确认预览。"; break; } index--; continue; }
                    unchanged++;
                    if (direction == ScrollDirection.Auto && inputAxis == ScrollDirection.Vertical && unchanged >= 2)
                    { inputAxis = ScrollDirection.Horizontal; unchanged = 0; continue; }
                    if (unchanged >= 3) { reason = "已到底或连续 3 帧未变化。"; break; }
                    continue;
                }
                unchanged = 0;
                var match = _stitcher.Match(Content(frames[^1]), Content(next), direction);
                if (waitForManualScroll is not null && !match.Reliable)
                {
                    progress?.Report($"本次滚动过大或内容不稳定，未追加。请回滚到上次位置附近，再缓慢{manualDirection}滚动。");
                    if (finishManual) { reason = "最后一帧未能可靠匹配，保留之前结果。"; break; }
                    index--; continue;
                }
                if (direction == ScrollDirection.Auto) direction = match.Direction;
                inputAxis = direction;
                var length = direction == ScrollDirection.Vertical ? contentHeight : contentWidth;
                totalLength = frames.Count == 1 ? length : totalLength;
                if ((long)totalLength + match.Offset > ScrollingStitcher.MaximumEdge ||
                    ((long)totalLength + match.Offset) * (direction == ScrollDirection.Vertical ? region.Width : region.Height) > ScrollingStitcher.MaximumPixels)
                { reason = "达到输出尺寸上限，请分段采集。"; break; }
                frames.Add(next); offsets.Add(match.Offset); matches.Add(match); totalLength += match.Offset;
                if (finishManual) { reason = "已确认预览。"; break; }
                if (!match.Reliable)
                { reason = $"第 {frames.Count} 帧匹配不确定（误差 {match.Error:F1}），已保留原帧，请手动修正接缝或排除固定栏后重新匹配。"; break; }
            }
        }
        catch (OperationCanceledException) when (frames.Count > 0)
        { reason = "已停止，保留已采集帧。"; }
        catch (Exception ex) when (frames.Count > 0)
        { reason = "采集中断，已保留原帧：" + ex.Message; }
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
                if (StopRequested) throw new OperationCanceledException("已按 Esc 停止。");
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

