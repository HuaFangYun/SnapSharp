using ScreenRecorderLib;
using SnapSharp.Models;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Editing;
using Windows.Storage;

namespace SnapSharp.Services;

internal static class RecordingSelfTest
{
    internal static string TestDevices()
    {
        // Match the window's worker-thread refresh and the active-session device checks.
        var counts = new int[3];
        for (var iteration = 0; iteration < 10; iteration++)
        {
            var groups = new[] { RecordingService.Microphones(), RecordingService.Speakers(), RecordingService.Cameras() };
            for (var index = 0; index < groups.Length; index++)
            {
                var devices = groups[index]; counts[index] = devices.Length;
                Require(devices.All(device => !string.IsNullOrWhiteSpace(device.Id) && !string.IsNullOrWhiteSpace(device.Name)), "Invalid recording device ID/name.");
                Require(devices.Select(device => device.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == devices.Length, "Duplicate recording device IDs.");
            }
        }
        return $"PASS: 10 worker-thread enumerations; microphones={counts[0]}, speakers={counts[1]}, cameras={counts[2]}. No desktop, audio or camera capture.";
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static bool Run(string directory)
    {
        foreach (var preference in new[]
        {
            new RecordingPreferences { BitrateKbps = 499 }, new RecordingPreferences { BitrateKbps = 100001 },
            new RecordingPreferences { CountdownSeconds = 11 }, new RecordingPreferences { CountdownSeconds = -1 },
            new RecordingPreferences { CaptureBackend = "unknown" }
        })
        {
            try { preference.Validate(); throw new InvalidOperationException("Invalid recording preference accepted."); } catch (ArgumentException) { }
        }
        new RecordingPreferences { CaptureBackend = "DXGI", BitrateKbps = 8000, CountdownSeconds = 5 }.Validate();
        Require(RecordingService.CanRetrySoftware(false, true, "MP4", 0, RecordingState.Starting), "Synchronous hardware failure cannot retry.");
        Require(RecordingService.CanRetrySoftware(false, true, "MP4", 0, RecordingState.Recording), "Hardware failure before first frame cannot retry.");
        Require(!RecordingService.CanRetrySoftware(true, true, "MP4", 0, RecordingState.Starting) &&
            !RecordingService.CanRetrySoftware(false, true, "MP4", 1, RecordingState.Recording) &&
            !RecordingService.CanRetrySoftware(false, false, "MP4", 0, RecordingState.Starting) &&
            !RecordingService.CanRetrySoftware(false, true, "GIF", 0, RecordingState.Starting) &&
            !RecordingService.CanRetrySoftware(false, true, "MP4", 0, RecordingState.Stopping), "Unsafe hardware retry allowed.");
        Require(RecordingSetupService.IsMicrosoftDownload(new Uri(RecordingSetupService.VisualCppUrl)) &&
            RecordingSetupService.IsMicrosoftDownload(new Uri("https://download.visualstudio.microsoft.com/download/prereq.exe")), "Official runtime hosts denied.");
        foreach (var address in new[] { "http://aka.ms/runtime.exe", "https://aka.ms.evil.example/runtime.exe", "https://example.com/", "https://user@aka.ms/runtime.exe", "https://aka.ms:444/runtime.exe" })
            Require(!RecordingSetupService.IsMicrosoftDownload(new Uri(address)), "Untrusted runtime download allowed.");
        var unsigned = Path.Combine(directory, "unsigned-installer.tmp"); File.WriteAllText(unsigned, "not an executable");
        try { MicrosoftInstallerSignature.Verify(unsigned); throw new InvalidOperationException("Unsigned installer accepted."); } catch (InvalidDataException) { }
        var now = TimeSpan.Zero; var clock = new RecordingClock(() => now);
        clock.Start(); now += TimeSpan.FromSeconds(10); clock.Pause(); now += TimeSpan.FromSeconds(40);
        Require(clock.Elapsed.TotalSeconds == 10, "Paused time counted as video time."); clock.Resume(); now += TimeSpan.FromSeconds(5); clock.Stop(); now += TimeSpan.FromSeconds(20);
        Require(clock.Elapsed.TotalSeconds == 15, "Monotonic recording clock failed.");
        var pixels = new byte[160 * 96 * 4]; var random = new Random(74); random.NextBytes(pixels); for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var image = BitmapSource.Create(160, 96, 96, 96, PixelFormats.Bgra32, null, pixels, 640); image.Freeze();
        using (var output = new MemoryStream())
        {
            using (var gif = new StreamingGifWriter(output, 160, 96)) { gif.Frame(image, 7); gif.Frame(image, 6); }
            output.Position = 0; var decoder = new GifBitmapDecoder(output, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Require(decoder.Frames.Count == 2 && decoder.Frames[1].PixelWidth == 160, "Streaming GIF decode failed.");
            Require(Convert.ToInt32(((BitmapMetadata)decoder.Frames[0].Metadata).GetQuery("/grctlext/Delay")) == 7, "GIF frame timing failed.");
            var decoded = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0); var restored = new byte[pixels.Length]; decoded.CopyPixels(restored, 640, 0);
            var error = Enumerable.Range(0, pixels.Length).Where(i => i % 4 != 3).Average(i => Math.Abs(pixels[i] - restored[i]));
            Require(error < 30, "GIF LZW or palette corruption.");
        }
        var input = Path.Combine(directory, "recording-source.png"); new SkiaImageService().Save(image, input);
        Task.Run(() => TestNative(input, directory)).GetAwaiter().GetResult();
        Task.Run(async () =>
        {
            await RecordingEncoderProbe.Check(default, 30, 4000);
            await RecordingEncoderProbe.Check(default, 60, 0);
        }).GetAwaiter().GetResult();
        return true;
    }
    private static async Task TestNative(string input, string directory)
    {
        var path = Path.Combine(directory, "recording-native.mp4");
        var options = new RecorderOptions
        {
            SourceOptions = new SourceOptions { RecordingSources = [new ImageRecordingSource { SourcePath = input }] },
            OutputOptions = new OutputOptions { RecorderMode = RecorderMode.Video, OutputFrameSize = new ScreenSize(160, 96) },
            AudioOptions = new AudioOptions { IsAudioEnabled = false, AudioSources = [] },
            MouseOptions = new MouseOptions { IsMousePointerEnabled = false, IsMouseClicksDetected = false },
            VideoEncoderOptions = new VideoEncoderOptions { Encoder = new H264VideoEncoder(), Framerate = 15, Quality = 60, IsHardwareEncodingEnabled = false, IsFixedFramerate = true }
        };
        using var recorder = Recorder.CreateRecorder(options);
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        recorder.OnRecordingComplete += (_, _) => complete.TrySetResult();
        recorder.OnRecordingFailed += (_, e) => { var error = new InvalidOperationException("Native recording: " + e.Error); started.TrySetException(error); complete.TrySetException(error); };
        recorder.OnStatusChanged += (_, e) => { if (e.Status == RecorderStatus.Recording) started.TrySetResult(); };
        recorder.Record(path);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(15)); await Task.Delay(500); recorder.Pause(); await Task.Delay(300); recorder.Resume(); await Task.Delay(500); recorder.Stop();
            await complete.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch { recorder.Stop(); throw; }
        Require(File.Exists(path) && new FileInfo(path).Length > 100, "Native MP4 missing.");
        var file = await StorageFile.GetFileFromPathAsync(path); var clip = await MediaClip.CreateFromFileAsync(file);
        var video = await file.Properties.GetVideoPropertiesAsync();
        Require(video.Width == 160 && video.Height == 96 && clip.OriginalDuration.TotalSeconds is > .3 and < 2, "MP4 dimensions or pause timeline failed.");
        var frameDirectory = Path.Combine(directory, "recording-frames"); Directory.CreateDirectory(frameDirectory);
        options.OutputOptions.RecorderMode = RecorderMode.Slideshow;
        options.SnapshotOptions = new SnapshotOptions { SnapshotFormat = ImageFormat.PNG, SnapshotsIntervalMillis = 67 };
        using var slideshow = Recorder.CreateRecorder(options);
        var framesDone = new TaskCompletionSource<List<FrameData>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var framesReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameCount = 0;
        slideshow.OnFrameRecorded += (_, _) => { if (Interlocked.Increment(ref frameCount) >= 3) framesReady.TrySetResult(); };
        slideshow.OnRecordingComplete += (_, e) => framesDone.TrySetResult(e.FrameInfos);
        slideshow.OnRecordingFailed += (_, e) =>
        {
            var error = new InvalidOperationException(e.Error);
            framesReady.TrySetException(error); framesDone.TrySetException(error);
        };
        slideshow.Record(frameDirectory);
        // Wait for actual frames rather than including native startup in a fixed 400 ms window.
        try { await framesReady.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
        finally { slideshow.Stop(); }
        var frames = await framesDone.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Require(frames.Count >= 2, "Native slideshow did not produce timed frames.");
        var gifPath = Path.Combine(directory, "native-slideshow.gif"); RecordingService.EncodeGif(frames, gifPath, frameDirectory);
        using var gifStream = File.OpenRead(gifPath); var gifDecoder = new GifBitmapDecoder(gifStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Require(gifDecoder.Frames.Count == frames.Count, "Native slideshow to GIF failed.");
    }
}
