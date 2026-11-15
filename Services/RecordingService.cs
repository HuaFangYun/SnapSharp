using ScreenRecorderLib;
using SnapSharp.Models;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SnapSharp.Services;

internal sealed record RecordingDevice(string Id, string Name) { public override string ToString() => Name; }

internal sealed class RecordingService
{
    private readonly Dispatcher _dispatcher;
    private RecordingClock _clock = new();
    private readonly DispatcherTimer _timer;
    private Recorder? _recorder;
    private RecordingPreferences _preferences = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _workDirectory = "", _destination = "";
    private bool _finishing, _retried, _diskCheck;
    private RecorderOptions? _options;
    private VideoCaptureOverlay? _camera;
    private int _ticks;
    private long _stateTimestamp;
    private int _frameCount;
    private string? _microphone, _speaker;
    private string _stopReason = "";
    private string _display = "";
    private System.Drawing.Rectangle _monitor;
    public RecordingState State { get; private set; } = RecordingState.Ready;
    public bool IsActive => State is RecordingState.Starting or RecordingState.Recording or RecordingState.Paused or RecordingState.Stopping or RecordingState.Encoding;
    public TimeSpan Elapsed => _clock.Elapsed;
    public int FrameCount => Volatile.Read(ref _frameCount);
    public double AverageFrameRate => Elapsed.TotalSeconds > 0 ? FrameCount / Elapsed.TotalSeconds : 0;
    public int TargetFrameRate => _preferences.Format == "GIF" ? 15 : _preferences.FrameRate;
    public bool IsFrameRateLimited => Elapsed.TotalSeconds >= 2 && AverageFrameRate < TargetFrameRate * .9;
    public long OutputBytes { get; private set; }
    public string? RecoveryDirectory => State == RecordingState.Failed && Directory.Exists(_workDirectory) ? _workDirectory : null;
    public Task Completion => _completion.Task;
    public string? SavedPath { get; private set; }
    public string Status { get; private set; } = "准备就绪";
    public event Action? Changed;
    public RecordingService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher; _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, Tick, dispatcher); _timer.Stop();
    }
    public static RecordingDevice[] Microphones() => RecordingDeviceService.Microphones();
    public static RecordingDevice[] Speakers() => RecordingDeviceService.Speakers();
    public static RecordingDevice[] Cameras() => RecordingDeviceService.Cameras();
    public void Start(System.Drawing.Rectangle region, System.Drawing.Rectangle monitor, string display, string destination, RecordingPreferences preferences, string? microphone, string? speaker, string? camera)
    {
        if (State != RecordingState.Ready) throw new InvalidOperationException("录屏会话不可重复启动。");
        preferences.Validate();
        if (!monitor.Contains(region) || region.Width < 16 || region.Height < 16 || region.Width > 7680 || region.Height > 4320 || (long)region.Width * region.Height > 16_600_000) throw new ArgumentException("选区需在单个显示器内，至少 16×16，最大 7680×4320／1660 万像素。");
        if (!Path.IsPathFullyQualified(destination) || File.Exists(destination) || !string.Equals(Path.GetExtension(destination), preferences.Format == "GIF" ? ".gif" : ".mp4", StringComparison.OrdinalIgnoreCase)) throw new IOException("请选择扩展名匹配且尚不存在的输出文件，录屏不会覆盖已有文件。");
        _preferences = preferences.Copy(); _destination = destination;
        _microphone = microphone; _speaker = speaker;
        _display = display; _monitor = monitor;
        var gif = preferences.Format == "GIF";
        if (gif && (microphone is not null || speaker is not null)) throw new ArgumentException("GIF 不支持音频，请关闭麦克风与系统声音。");
        var width = region.Width & ~1; var height = region.Height & ~1;
        if (gif) { var scale = Math.Min(1, preferences.GifMaximumEdge / (double)Math.Max(width, height)); width = Math.Max(2, (int)(width * scale) & ~1); height = Math.Max(2, (int)(height * scale) & ~1); }
        var audio = new List<AudioSourceBase>();
        if (microphone is not null) audio.Add(new CaptureAudioSource(microphone) { ForceMono = true });
        if (speaker is not null) audio.Add(new LoopbackAudioSource(speaker));
        _camera = camera is null ? null : new VideoCaptureOverlay(camera) { AnchorPoint = Anchor.BottomRight, Offset = new ScreenSize(12, 12), Size = new ScreenSize(Math.Min(240, width / 3), Math.Min(180, height / 3)), Stretch = StretchMode.Uniform };
        _options = new RecorderOptions
        {
            SourceOptions = new SourceOptions { RecordingSources = [new DisplayRecordingSource(display) { RecorderApi = preferences.CaptureBackend == "DXGI" ? RecorderApi.DesktopDuplication : RecorderApi.WindowsGraphicsCapture, IsCursorCaptureEnabled = preferences.IncludeCursor, SourceRect = new ScreenRect(region.Left - monitor.Left, region.Top - monitor.Top, width: region.Width & ~1, height: region.Height & ~1) }] },
            OutputOptions = new OutputOptions { RecorderMode = gif ? RecorderMode.Slideshow : RecorderMode.Video, OutputFrameSize = new ScreenSize(width, height) },
            // Variable timestamps keep wall-clock duration when capture/encoding cannot
            // sustain the requested FPS. Fixed-frame mode can shorten slow recordings.
            VideoEncoderOptions = new VideoEncoderOptions { Encoder = new H264VideoEncoder { BitrateMode = preferences.BitrateKbps > 0 ? H264BitrateControlMode.CBR : H264BitrateControlMode.Quality }, Bitrate = (preferences.BitrateKbps > 0 ? preferences.BitrateKbps : 4000) * 1000, Framerate = gif ? 15 : preferences.FrameRate, Quality = preferences.Quality switch { "流畅" => 50, "高清" => 90, _ => 70 }, IsHardwareEncodingEnabled = preferences.HardwareEncoding, IsFixedFramerate = false, IsThrottlingDisabled = false, IsMp4FastStartEnabled = true },
            AudioOptions = new AudioOptions { IsAudioEnabled = audio.Count > 0, AudioSources = audio, Channels = AudioChannels.Stereo, Bitrate = AudioBitrate.bitrate_128kbps },
            MouseOptions = new MouseOptions { IsMousePointerEnabled = preferences.IncludeCursor, IsMouseClicksDetected = preferences.HighlightClicks, MouseClickDetectionMode = MouseDetectionMode.Polling, MouseClickDetectionRadius = 20, MouseClickDetectionDuration = 200, MouseLeftClickDetectionColor = "#FFFFC107", MouseRightClickDetectionColor = "#FF4FC3F7" },
            SnapshotOptions = new SnapshotOptions { SnapshotFormat = ImageFormat.PNG, SnapshotsIntervalMillis = 67 },
            OverlayOptions = new OverLayOptions { Overlays = _camera is null ? [] : [_camera] }
        };
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        _workDirectory = Path.Combine(Path.GetDirectoryName(destination)!, ".SnapSharp-record-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
        SetState(RecordingState.Starting, "正在初始化采集与编码器…");
        try { CreateNative(); } catch (Exception ex) { NativeFailure(ex.Message); }
        if (IsActive) _timer.Start();
    }
    private void CreateNative()
    {
        _recorder = Recorder.CreateRecorder(_options!);
        var recorder = _recorder;
        recorder.OnStatusChanged += (_, e) => Dispatch(() =>
        {
            if (_recorder != recorder || _finishing) return;
            if (e.Status == RecorderStatus.Recording && State == RecordingState.Starting)
            { _clock.Start(); SetState(RecordingState.Recording, "录制中；暂停不计入输出时长。"); }
        });
        recorder.OnFrameRecorded += (_, _) => { if (_recorder == recorder) Interlocked.Increment(ref _frameCount); };
        recorder.OnRecordingComplete += (_, e) => Dispatch(() => { if (_recorder == recorder) _ = Finish(e.FrameInfos); });
        recorder.OnRecordingFailed += (_, e) => Dispatch(() =>
        {
            if (_recorder != recorder || _finishing) return;
            NativeFailure(e.Error);
        });
        recorder.Record(_preferences.Format == "GIF" ? Path.Combine(_workDirectory, "frames") : Path.Combine(_workDirectory, _retried ? "software.mp4" : "video.mp4"));
    }
    internal static bool CanRetrySoftware(bool retried, bool hardware, string format, int frames, RecordingState state) =>
        !retried && hardware && format == "MP4" && frames == 0 && state is RecordingState.Starting or RecordingState.Recording;
    private void NativeFailure(string reason)
    {
        if (_finishing) return;
        if (!CanRetrySoftware(_retried, _preferences.HardwareEncoding, _preferences.Format, FrameCount, State)) { Fail(reason); return; }
        _retried = true;
        var previous = _recorder; _recorder = null;
        try { previous?.Dispose(); } catch { }
        _options!.VideoEncoderOptions.IsHardwareEncodingEnabled = false; _clock = new RecordingClock();
        SetState(RecordingState.Starting, "硬件编码初始化失败，正在尝试系统软件编码器…");
        try { CreateNative(); } catch (Exception ex) { Fail(ex.Message); }
    }
    private void Dispatch(Action action) { if (!_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(action); }
    private async void Tick(object? sender, EventArgs e)
    {
        Changed?.Invoke();
        if (State == RecordingState.Starting && System.Diagnostics.Stopwatch.GetElapsedTime(_stateTimestamp).TotalSeconds > 30) { Fail("采集初始化超过 30 秒。请检查显示器、设备权限及系统编码器。"); return; }
        if (State == RecordingState.Stopping && System.Diagnostics.Stopwatch.GetElapsedTime(_stateTimestamp).TotalSeconds > 60) { Fail("编码器收尾超过 60 秒。请检查保留的临时文件。"); return; }
        if (State is not (RecordingState.Recording or RecordingState.Paused)) return;
        var limit = _preferences.Format == "GIF" ? Math.Min(60, _preferences.StopAfterSeconds == 0 ? 60 : _preferences.StopAfterSeconds) : _preferences.StopAfterSeconds;
        if (State == RecordingState.Recording && limit > 0 && Elapsed.TotalSeconds >= limit) { Stop($"已达到 {limit} 秒有效录制时长"); return; }
        if (++_ticks % 8 != 0 || _diskCheck) return;
        _diskCheck = true;
        try
        {
            if (!System.Windows.Forms.Screen.AllScreens.Any(x => x.DeviceName == _display && x.Bounds == _monitor))
            { Stop("录制显示器已断开或布局已改变"); return; }
            var bytes = await Task.Run(() => Directory.EnumerateFiles(_workDirectory, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length));
            if (!IsActive || _finishing) return;
            OutputBytes = bytes;
            var disk = new DriveInfo(Path.GetPathRoot(_workDirectory)!);
            if (disk.IsReady && disk.AvailableFreeSpace < 200_000_000) { Stop("磁盘可用空间不足 200 MB"); return; }
            if (_preferences.Format == "GIF" && bytes > 300_000_000) { Stop("GIF 临时数据达到 300 MB"); return; }
            var camera = _camera?.DeviceName; var microphone = _microphone; var speaker = _speaker;
            if (camera is not null || microphone is not null || speaker is not null)
            {
                var missing = await Task.Run(() => camera is not null && !Cameras().Any(x => x.Id == camera) ? "摄像头已断开"
                    : microphone is not null && !Microphones().Any(x => x.Id == microphone) ? "麦克风已断开"
                    : speaker is not null && !Speakers().Any(x => x.Id == speaker) ? "系统声音设备已断开" : null);
                if (missing is not null && State is RecordingState.Recording or RecordingState.Paused) Stop(missing + "，为保留当前录制自动停止");
            }
        }
        catch (Exception ex) { if (IsActive && !_finishing) { Status = "设备／磁盘检查：" + ex.Message; Changed?.Invoke(); } }
        finally { _diskCheck = false; }
    }
    public void PauseOrResume()
    {
        if (State == RecordingState.Recording) { _recorder!.Pause(); _clock.Pause(); SetState(RecordingState.Paused, "已暂停；点击继续录制。"); }
        else if (State == RecordingState.Paused) { _recorder!.Resume(); _clock.Resume(); SetState(RecordingState.Recording, "录制中"); }
    }
    public void Stop() => Stop("");
    private void Stop(string reason)
    {
        if (State is not (RecordingState.Recording or RecordingState.Paused or RecordingState.Starting)) return;
        _stopReason = reason;
        _clock.Stop(); SetState(RecordingState.Stopping, (reason.Length > 0 ? reason + "。" : "") + "正在停止并写入文件索引，请勿退出…");
        try { _recorder!.Stop(); } catch (Exception ex) { Fail(ex.Message); }
    }
    private async Task Finish(List<FrameData> frames)
    {
        if (_finishing) return; _finishing = true; _timer.Stop(); _clock.Stop();
        try
        {
            _recorder?.Dispose(); _recorder = null;
            var result = Path.Combine(_workDirectory, _preferences.Format == "GIF" ? "result.gif" : _retried ? "software.mp4" : "video.mp4");
            if (_preferences.Format == "GIF")
            {
                SetState(RecordingState.Encoding, "正在逐帧量化并生成 GIF…");
                await Task.Run(() => EncodeGif(frames, result, _workDirectory));
            }
            if (!File.Exists(result) || new FileInfo(result).Length == 0) throw new IOException("编码器未生成有效文件。");
            OutputBytes = new FileInfo(result).Length;
            File.Move(result, _destination, overwrite: false); SavedPath = _destination;
            // Only this session's newly-created directory; never remove an arbitrary caller path.
            var cleanup = "";
            try { Directory.Delete(_workDirectory, recursive: true); } catch (IOException) { cleanup = "；临时帧清理未完成：" + _workDirectory; } catch (UnauthorizedAccessException) { cleanup = "；临时帧清理未完成：" + _workDirectory; }
            SetState(RecordingState.Completed, (_stopReason.Length > 0 ? _stopReason + "。" : "") + "已保存：" + _destination + cleanup);
        }
        catch (Exception ex) { SetState(RecordingState.Failed, "收尾失败：" + ex.Message + "；临时数据保留于：" + _workDirectory); }
        finally { _completion.TrySetResult(); }
    }
    internal static void EncodeGif(IReadOnlyList<FrameData> frames, string result, string workDirectory)
    {
        if (frames.Count is < 1 or > 1000) throw new IOException("GIF 帧数需为 1–1000。");
        using var stream = new FileStream(result, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        StreamingGifWriter? writer = null; double elapsed = 0; var writtenTicks = 0;
        try
        {
            foreach (var frame in frames)
            {
                var path = Path.GetFullPath(frame.Path);
                if (!path.StartsWith(Path.GetFullPath(workDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("编码器返回的帧路径不在本次临时目录内。");
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze();
                writer ??= new StreamingGifWriter(stream, bitmap.PixelWidth, bitmap.PixelHeight);
                elapsed += Math.Clamp(frame.Delay, 10, 1000) / 10d; var end = (int)Math.Round(elapsed); writer.Frame(bitmap, Math.Max(1, end - writtenTicks)); writtenTicks = end;
                if (stream.Position > 100_000_000) throw new IOException("GIF 已达到 100 MB 上限，保留临时帧，请缩短时长或降低尺寸。");
            }
        }
        finally { writer?.Dispose(); }
    }
    private void Fail(string reason)
    {
        if (_finishing) return; _finishing = true; _timer.Stop(); _clock.Stop();
        try { _recorder?.Dispose(); } catch { } _recorder = null;
        SetState(RecordingState.Failed, "录制失败：" + reason + "；临时数据保留于：" + _workDirectory); _completion.TrySetResult();
    }
    private void SetState(RecordingState state, string status) { State = state; _stateTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(); Status = status; Changed?.Invoke(); }
}
