using SnapSharp.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Windows.Media.Editing;
using Windows.Storage;
using SnapSharp.Views;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SnapSharp.Services;

// Explicit opt-in only: exercise the actual display source and service state machine.
internal static class RecordingCaptureSelfTest
{
    public static async Task<string> Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "recording-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var screen = System.Windows.Forms.Screen.PrimaryScreen!;
        var window = new Window
        {
            Title = "NimCap Screenshot 录屏测试", Width = 320, Height = 192,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, Topmost = true, ShowActivated = false,
            Left = 0, Top = 0,
            Content = new Border
            {
                Background = Brushes.MidnightBlue,
                Child = new TextBlock { Text = "NimCap Screenshot 录屏测试\n仅生成画面 · 无音频／摄像头", Foreground = Brushes.White,
                    FontSize = 18, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            }
        };
        RecordingService? service = null;
        RecordingRegionWindow? regionIndicator = null;
        RecordingToolbarWindow? toolbar = null;
        var animation = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        var scene = (TextBlock)((Border)window.Content).Child;
        var frame = 0;
        animation.Tick += (_, _) => scene.Text = $"NimCap Screenshot 录屏测试 · {++frame}\n仅生成画面 · 无音频／摄像头";
        try
        {
            window.Show(); window.UpdateLayout();
            animation.Start();
            await Task.Delay(350);
            var corner = window.PointToScreen(new System.Windows.Point());
            var end = window.PointToScreen(new System.Windows.Point(window.ActualWidth, window.ActualHeight));
            var region = new System.Drawing.Rectangle((int)corner.X, (int)corner.Y, ((int)(end.X - corner.X)) & ~1, ((int)(end.Y - corner.Y)) & ~1);
            screen = System.Windows.Forms.Screen.FromRectangle(region);
            regionIndicator = new RecordingRegionWindow(region); regionIndicator.Show();
            toolbar = new RecordingToolbarWindow(); toolbar.ShowForRegion(region);
            var results = new List<string>();
            var warnings = new List<string>();
            foreach (var (backend, hardware, fps, quality, bitrate, timedPause) in new[]
            {
                ("WGC", false, 15, "流畅", 0, false),
                ("DXGI", false, 30, "标准", 0, false),
                ("WGC", true, 60, "高清", 0, false),
                ("WGC", false, 30, "标准", 4000, false),
                ("WGC", false, 30, "标准", 0, true)
            })
            {
                service = new RecordingService(new DispatcherSynchronizationContext(window.Dispatcher, DispatcherPriority.Background));
                var session = service;
                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                service.Changed += () =>
                {
                    if (session.State == RecordingState.Recording) started.TrySetResult();
                    if (session.State == RecordingState.Failed) started.TrySetException(new InvalidOperationException(session.Status));
                    toolbar.Update(session.State, false, false, session.Elapsed, session.AverageFrameRate);
                };
                var label = $"{backend}-{(hardware ? "prefer-hardware" : "software")}-{fps}fps-{bitrate}kbps" + (timedPause ? "-paused-timed" : "");
                var path = Path.Combine(directory, label + ".mp4");
                service.Start(region, screen.Bounds, screen.DeviceName, path,
                    new RecordingPreferences { CaptureBackend = backend, HardwareEncoding = hardware, IncludeCursor = false,
                        FrameRate = fps, Quality = quality, BitrateKbps = bitrate, StopAfterSeconds = timedPause ? 3 : 0 }, null, null, null);
                await started.Task.WaitAsync(TimeSpan.FromSeconds(20));
                await Task.Delay(timedPause ? 900 : 2500);
                if (timedPause)
                {
                    service.PauseOrResume();
                    var elapsed = service.Elapsed;
                    await Task.Delay(3500); // Longer than the stop limit; must not auto-stop while paused.
                    if (service.State != RecordingState.Paused || Math.Abs((service.Elapsed - elapsed).TotalMilliseconds) > 10)
                        throw new InvalidOperationException("暂停计时或定时停止未排除暂停段。");
                    service.PauseOrResume();
                }
                else service.Stop();
                await service.Completion.WaitAsync(TimeSpan.FromSeconds(25));
                if (service.State != RecordingState.Completed || service.FrameCount < 1 || service.SavedPath != path)
                    throw new InvalidOperationException(service.Status);
                var file = await StorageFile.GetFileFromPathAsync(path);
                var clip = await MediaClip.CreateFromFileAsync(file);
                var properties = clip.GetVideoEncodingProperties();
                if (properties.Width != region.Width || properties.Height != region.Height || clip.OriginalDuration <= TimeSpan.Zero)
                    throw new InvalidDataException("实际显示器采集的尺寸或时长不符合请求。");
                if (Math.Abs(clip.OriginalDuration.TotalSeconds - service.Elapsed.TotalSeconds) > .4)
                    throw new InvalidDataException($"录制时间轴不符：有效 {service.Elapsed.TotalSeconds:F2}s，文件 {clip.OriginalDuration.TotalSeconds:F2}s（{label}）。");
                var encodedFps = properties.FrameRate.Numerator / (double)properties.FrameRate.Denominator;
                if (!double.IsFinite(encodedFps) || encodedFps <= 0 || encodedFps > fps * 1.05)
                    throw new InvalidDataException($"输出帧率无效：{encodedFps:F2}，请求 {fps}。");
                if (Math.Abs(service.AverageFrameRate - fps) > .05 * fps || Math.Abs(encodedFps - fps) > .05 * fps)
                    warnings.Add($"PERFORMANCE NOT ACCEPTED: {label}, requested={fps}, observed={service.AverageFrameRate:F2}, metadata={encodedFps:F2}fps; timeline checked separately.");
                if (timedPause && (Math.Abs(clip.OriginalDuration.TotalSeconds - 3) > .6 ||
                    !service.Status.Contains("有效录制时长") || service.Elapsed.TotalSeconds is < 3 or > 3.6))
                    throw new InvalidDataException("定时停止后的 MP4 时长、暂停时间戳或完成原因不符合预期。");
                var composition = new MediaComposition(); composition.Clips.Add(clip);
                using var thumbnail = await composition.GetThumbnailAsync(TimeSpan.FromMilliseconds(300), region.Width, region.Height, VideoFramePrecision.NearestFrame);
                using var stream = thumbnail.AsStreamForRead();
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var decoded = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
                var pixel = new byte[4]; decoded.CopyPixels(new Int32Rect(1, decoded.PixelHeight / 2, 1, 1), pixel, 4, 0);
                if (Math.Abs(pixel[0] - 112) > 30 || Math.Abs(pixel[1] - 25) > 30 || Math.Abs(pixel[2] - 25) > 30)
                    throw new InvalidDataException("区域边框进入了录屏，或录屏未捕获生成的测试画面。");
                results.Add($"{label}: {properties.Width}x{properties.Height}, {service.FrameCount} frames, {clip.OriginalDuration.TotalSeconds:F2}s, " +
                    $"metadata={encodedFps:F2}fps, observed={service.AverageFrameRate:F2}fps, {service.OutputBytes} bytes, actual={service.OutputBytes * 8 / clip.OriginalDuration.TotalSeconds / 1000:F0}kbps");
            }
            toolbar.Close(); toolbar = null; regionIndicator.Close(); regionIndicator = null;
            await RecordingWindow.TestFloatingConfiguration();
            return "PASS (functional only): animated display capture, border excluded, requested 15/30/60fps, quality/CBR, wall-clock timeline, pause + timed stop with decoded duration, floating configuration, software/prefer-hardware MP4. No audio/camera; not a long-run or mixed-DPI acceptance.\n" +
                string.Join("\n", results) + "\n" + string.Join("\n", warnings) + "\n" + directory;
        }
        finally
        {
            animation.Stop();
            if (service?.IsActive == true)
            {
                service.Stop();
                try { await service.Completion.WaitAsync(TimeSpan.FromSeconds(25)); } catch (TimeoutException) { }
            }
            window.Close();
            toolbar?.Close(); regionIndicator?.Close();
        }
    }
}
