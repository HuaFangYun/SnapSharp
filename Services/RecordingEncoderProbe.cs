using L = SnapSharp.Localization.UiText;
using ScreenRecorderLib;
using SnapSharp.Models;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Editing;
using Windows.Storage;

namespace SnapSharp.Services;

/// <summary>Checks actual encoding using generated pixels, with no screen, audio or camera source.</summary>
internal static class RecordingEncoderProbe
{
    public static async Task<string> Check(CancellationToken token, int frameRate = 30, int bitrateKbps = 0)
    {
        new RecordingPreferences { FrameRate = frameRate, BitrateKbps = bitrateKbps }.Validate();
        var directory = Path.Combine(Path.GetTempPath(), "SnapSharp-encoder-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "source.png"); var destination = Path.Combine(directory, "probe.mp4");
        try
        {
            token.ThrowIfCancellationRequested();
            var pixels = new byte[160 * 96 * 4];
            for (var index = 0; index < pixels.Length; index += 4)
            { pixels[index] = (byte)(index / 4 % 160); pixels[index + 1] = 180; pixels[index + 2] = 60; pixels[index + 3] = 255; }
            var bitmap = BitmapSource.Create(160, 96, 96, 96, PixelFormats.Bgra32, null, pixels, 160 * 4); bitmap.Freeze();
            new SkiaImageService().Save(bitmap, source);
            var options = new RecorderOptions
            {
                SourceOptions = new SourceOptions { RecordingSources = [new ImageRecordingSource { SourcePath = source }] },
                OutputOptions = new OutputOptions { RecorderMode = RecorderMode.Video, OutputFrameSize = new ScreenSize(160, 96) },
                AudioOptions = new AudioOptions { IsAudioEnabled = false, AudioSources = [] },
                MouseOptions = new MouseOptions { IsMousePointerEnabled = false, IsMouseClicksDetected = false },
                VideoEncoderOptions = new VideoEncoderOptions
                {
                    Encoder = new H264VideoEncoder { BitrateMode = bitrateKbps > 0 ? H264BitrateControlMode.CBR : H264BitrateControlMode.Quality },
                    Bitrate = (bitrateKbps > 0 ? bitrateKbps : 4000) * 1000, Quality = 70, Framerate = frameRate,
                    IsHardwareEncodingEnabled = false, IsFixedFramerate = true, IsThrottlingDisabled = false
                }
            };
            using (var recorder = Recorder.CreateRecorder(options))
            {
                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                recorder.OnStatusChanged += (_, e) => { if (e.Status == RecorderStatus.Recording) started.TrySetResult(); };
                recorder.OnRecordingComplete += (_, _) => completed.TrySetResult();
                recorder.OnRecordingFailed += (_, e) =>
                {
                    var error = new InvalidOperationException(L.Get("SystemH264EncoderCheckFailed") + e.Error);
                    started.TrySetException(error); completed.TrySetException(error);
                };
                try
                {
                    recorder.Record(destination);
                    await started.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
                    await Task.Delay(500, token);
                    recorder.Stop();
                    await completed.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
                }
                catch { try { recorder.Stop(); } catch { } throw; }
            }
            var file = await StorageFile.GetFileFromPathAsync(destination).AsTask(token);
            var clip = await MediaClip.CreateFromFileAsync(file).AsTask(token);
            var properties = clip.GetVideoEncodingProperties();
            var fps = properties.FrameRate.Denominator > 0 ? properties.FrameRate.Numerator / (double)properties.FrameRate.Denominator : 0;
            if (properties.Width != 160 || properties.Height != 96 || clip.OriginalDuration <= TimeSpan.Zero || Math.Abs(fps - frameRate) > .1)
                throw new InvalidDataException(L.Get("EncodedMP4DimensionsDurationOrFrameRate"));
            return L.Format("H264SoftwareEncodingVerifiedWithA", properties.Width, properties.Height, fps);
        }
        finally
        {
            // Only generated test pixels; never a desktop frame. Do not delete other files.
            try
            {
                if (File.Exists(source)) File.Delete(source);
                if (File.Exists(destination)) File.Delete(destination);
                Directory.Delete(directory, false);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
