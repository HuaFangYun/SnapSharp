using L = SnapSharp.Localization.UiText;
using ScreenRecorderLib;
using SnapSharp.Models;
using System.IO;
using System.Windows.Media.Imaging;

namespace SnapSharp.Services;

/// <summary>A single recording session, serialized on a caller-owned synchronization context.</summary>
/// <remarks>
/// Construct the service and call its commands on the context's single owner thread.
/// Native callbacks, monitoring and Changed notifications run on that same context.
/// The context must remain running until Completion finishes; the service does not own it.
/// FrameCount alone may also be read from other threads.
/// </remarks>
public sealed class RecordingService
{
    private readonly SynchronizationContext _context;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private RecordingClock _clock = new();
    private System.Threading.Timer? _timer;
    private int _tickPending;
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
    // Per-session fault injection for integration checks; never enabled by application UI.
    internal Action<bool>? BeforeNativeRecord { get; init; }
    public string? SoftwareFallbackReason { get; private set; }
    public bool UsedSoftwareFallback => _retried;
    private System.Drawing.Rectangle _monitor;
    public RecordingState State { get; private set; } = RecordingState.Ready;
    public bool IsActive => State is RecordingState.Starting or RecordingState.Recording or RecordingState.Paused or RecordingState.Stopping or RecordingState.Encoding;
    public TimeSpan Elapsed => _clock.Elapsed;
    public int FrameCount => Volatile.Read(ref _frameCount);
    public double AverageFrameRate => Elapsed.TotalSeconds > 0 ? FrameCount / Elapsed.TotalSeconds : 0;
    public int TargetFrameRate => _preferences.Format == "GIF" ? 15 : _preferences.FrameRate;
    public bool IsFrameRateLimited => Elapsed.TotalSeconds >= 2 && !IsFrameRateWithinTolerance(AverageFrameRate, TargetFrameRate);
    public static bool IsFrameRateWithinTolerance(double actual, int target) =>
        target > 0 && double.IsFinite(actual) && Math.Abs(actual - target) <= target * .05 + 1e-9;
    public long OutputBytes { get; private set; }
    public string? RecoveryDirectory => State == RecordingState.Failed && Directory.Exists(_workDirectory) ? _workDirectory : null;
    public Task Completion => _completion.Task;
    public string? SavedPath { get; private set; }
    public string Status { get; private set; } = L.Get("Ready");
    public event Action? Changed;
    public RecordingService(SynchronizationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }
    public static RecordingDevice[] Microphones() => RecordingDeviceService.Microphones();
    public static RecordingDevice[] Speakers() => RecordingDeviceService.Speakers();
    public static RecordingDevice[] Cameras() => RecordingDeviceService.Cameras();
    public void Start(System.Drawing.Rectangle region, System.Drawing.Rectangle monitor, string display, string destination, RecordingPreferences preferences, string? microphone, string? speaker, string? camera)
    {
        VerifyAccess();
        if (State != RecordingState.Ready) throw new InvalidOperationException(L.Get("ARecordingSessionCannotBeStartedTwice"));
        preferences.Validate();
        if (!monitor.Contains(region) || region.Width < 16 || region.Height < 16 || region.Width > 7680 || region.Height > 4320 || (long)region.Width * region.Height > 16_600_000) throw new ArgumentException(L.Get("TheRegionMustFitWithinOneMonitor"));
        if (!Path.IsPathFullyQualified(destination) || File.Exists(destination) || !string.Equals(Path.GetExtension(destination), preferences.Format == "GIF" ? ".gif" : ".mp4", StringComparison.OrdinalIgnoreCase)) throw new IOException(L.Get("ChooseANewOutputFileWithThe"));
        _preferences = preferences.Copy(); _destination = destination;
        _microphone = microphone; _speaker = speaker;
        _display = display; _monitor = monitor;
        var gif = preferences.Format == "GIF";
        if (gif && (microphone is not null || speaker is not null)) throw new ArgumentException(L.Get("GIFDoesNotSupportAudioDisableMicrophone"));
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
        SetState(RecordingState.Starting, L.Get("InitializingCaptureAndEncoder"));
        try { CreateNative(); } catch (Exception ex) { NativeFailure(ex.Message); }
        if (IsActive) _timer = new System.Threading.Timer(QueueTick, null, 250, 250);
    }
    private void CreateNative()
    {
        _recorder = Recorder.CreateRecorder(_options!);
        var recorder = _recorder;
        recorder.OnStatusChanged += (_, e) => Dispatch(() =>
        {
            if (_recorder != recorder || _finishing) return;
            if (e.Status == RecorderStatus.Recording && State == RecordingState.Starting)
            { _clock.Start(); SetState(RecordingState.Recording, L.Get("RecordingPausedTimeIsExcludedFromOutput")); }
        });
        recorder.OnFrameRecorded += (_, _) => { if (_recorder == recorder) Interlocked.Increment(ref _frameCount); };
        recorder.OnRecordingComplete += (_, e) => Dispatch(() => { if (_recorder == recorder) _ = Finish(e.FrameInfos); });
        recorder.OnRecordingFailed += (_, e) => Dispatch(() =>
        {
            if (_recorder != recorder || _finishing) return;
            NativeFailure(e.Error);
        });
        BeforeNativeRecord?.Invoke(_options!.VideoEncoderOptions.IsHardwareEncodingEnabled);
        recorder.Record(_preferences.Format == "GIF" ? Path.Combine(_workDirectory, "frames") : Path.Combine(_workDirectory, _retried ? "software.mp4" : "video.mp4"));
    }
    internal static bool CanRetrySoftware(bool retried, bool hardware, string format, int frames, RecordingState state) =>
        !retried && hardware && format == "MP4" && frames == 0 && state is RecordingState.Starting or RecordingState.Recording;
    private void NativeFailure(string reason)
    {
        if (_finishing) return;
        if (!CanRetrySoftware(_retried, _preferences.HardwareEncoding, _preferences.Format, FrameCount, State)) { Fail(reason); return; }
        _retried = true;
        SoftwareFallbackReason = reason;
        var previous = _recorder; _recorder = null;
        try { previous?.Dispose(); } catch { }
        _options!.VideoEncoderOptions.IsHardwareEncodingEnabled = false; _clock = new RecordingClock();
        SetState(RecordingState.Starting, L.Get("HardwareEncoderInitializationFailedTryingTheSystem"));
        try { CreateNative(); } catch (Exception ex) { Fail(ex.Message); }
    }
    private void VerifyAccess()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Recording commands must run on the synchronization context's owner thread.");
    }
    private void Dispatch(Action action) => _context.Post(_ =>
    {
        VerifyAccess();
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(_context);
            action();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }, null);
    private void QueueTick(object? state)
    {
        // Keep at most one pending notification while the owner thread is busy.
        if (Interlocked.Exchange(ref _tickPending, 1) != 0) return;
        Dispatch(() =>
        {
            Interlocked.Exchange(ref _tickPending, 0);
            if (IsActive && !_finishing) Tick();
        });
    }
    private async void Tick()
    {
        Changed?.Invoke();
        if (State == RecordingState.Starting && System.Diagnostics.Stopwatch.GetElapsedTime(_stateTimestamp).TotalSeconds > 30) { Fail(L.Get("CaptureInitializationExceeded30SecondsCheckThe")); return; }
        if (State == RecordingState.Stopping && System.Diagnostics.Stopwatch.GetElapsedTime(_stateTimestamp).TotalSeconds > 60) { Fail(L.Get("EncoderFinalizationExceeded60SecondsCheckThe")); return; }
        if (State is not (RecordingState.Recording or RecordingState.Paused)) return;
        var limit = _preferences.Format == "GIF" ? Math.Min(60, _preferences.StopAfterSeconds == 0 ? 60 : _preferences.StopAfterSeconds) : _preferences.StopAfterSeconds;
        if (State == RecordingState.Recording && limit > 0 && Elapsed.TotalSeconds >= limit) { Stop(L.Format("Reached0SecondsOfRecordedTime", limit)); return; }
        if (++_ticks % 8 != 0 || _diskCheck) return;
        _diskCheck = true;
        try
        {
            if (!System.Windows.Forms.Screen.AllScreens.Any(x => x.DeviceName == _display && x.Bounds == _monitor))
            { Stop(L.Get("TheRecordedMonitorDisconnectedOrItsLayout")); return; }
            var bytes = await Task.Run(() => Directory.EnumerateFiles(_workDirectory, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length));
            if (!IsActive || _finishing) return;
            OutputBytes = bytes;
            var disk = new DriveInfo(Path.GetPathRoot(_workDirectory)!);
            if (disk.IsReady && disk.AvailableFreeSpace < 200_000_000) { Stop(L.Get("LessThan200MBOfDiskSpace")); return; }
            if (_preferences.Format == "GIF" && bytes > 300_000_000) { Stop(L.Get("GIFTemporaryDataReached300MB")); return; }
            var camera = _camera?.DeviceName; var microphone = _microphone; var speaker = _speaker;
            if (camera is not null || microphone is not null || speaker is not null)
            {
                var missing = await Task.Run(() => camera is not null && !Cameras().Any(x => x.Id == camera) ? L.Get("CameraDisconnected")
                    : microphone is not null && !Microphones().Any(x => x.Id == microphone) ? L.Get("MicrophoneDisconnected")
                    : speaker is not null && !Speakers().Any(x => x.Id == speaker) ? L.Get("SystemAudioDeviceDisconnected") : null);
                if (missing is not null && State is RecordingState.Recording or RecordingState.Paused) Stop(missing + L.Get("RecordingStoppedAutomaticallyToPreserveTheCurrent"));
            }
        }
        catch (Exception ex) { if (IsActive && !_finishing) { Status = L.Get("DeviceDiskCheck") + ex.Message; Changed?.Invoke(); } }
        finally { _diskCheck = false; }
    }
    public void PauseOrResume()
    {
        VerifyAccess();
        if (State == RecordingState.Recording) { _recorder!.Pause(); _clock.Pause(); SetState(RecordingState.Paused, L.Get("PausedClickResumeToContinueRecording")); }
        else if (State == RecordingState.Paused) { _recorder!.Resume(); _clock.Resume(); SetState(RecordingState.Recording, L.Get("Recording2")); }
    }
    public void Stop() => Stop("");
    private void Stop(string reason)
    {
        VerifyAccess();
        if (State is not (RecordingState.Recording or RecordingState.Paused or RecordingState.Starting)) return;
        _stopReason = reason;
        _clock.Stop(); SetState(RecordingState.Stopping, (reason.Length > 0 ? reason + "。" : "") + L.Get("StoppingAndWritingTheFileIndexPlease"));
        try { _recorder!.Stop(); } catch (Exception ex) { Fail(ex.Message); }
    }
    private async Task Finish(List<FrameData> frames)
    {
        if (_finishing) return; _finishing = true; _timer?.Dispose(); _clock.Stop();
        try
        {
            _recorder?.Dispose(); _recorder = null;
            var result = Path.Combine(_workDirectory, _preferences.Format == "GIF" ? "result.gif" : _retried ? "software.mp4" : "video.mp4");
            if (_preferences.Format == "GIF")
            {
                SetState(RecordingState.Encoding, L.Get("QuantizingFramesAndGeneratingGIF"));
                await Task.Run(() => EncodeGif(frames, result, _workDirectory));
            }
            if (!File.Exists(result) || new FileInfo(result).Length == 0) throw new IOException(L.Get("TheEncoderDidNotProduceAValid"));
            OutputBytes = new FileInfo(result).Length;
            File.Move(result, _destination, overwrite: false); SavedPath = _destination;
            // Only this session's newly-created directory; never remove an arbitrary caller path.
            var cleanup = "";
            try { Directory.Delete(_workDirectory, recursive: true); } catch (IOException) { cleanup = L.Get("TemporaryFrameCleanupIncomplete") + _workDirectory; } catch (UnauthorizedAccessException) { cleanup = L.Get("TemporaryFrameCleanupIncomplete") + _workDirectory; }
            SetState(RecordingState.Completed, (_stopReason.Length > 0 ? _stopReason + "。" : "") + L.Get("Saved") + _destination + cleanup);
        }
        catch (Exception ex) { SetState(RecordingState.Failed, L.Get("FinalizationFailed") + ex.Message + L.Get("TemporaryDataRetainedAt") + _workDirectory); }
        finally { _completion.TrySetResult(); }
    }
    internal static void EncodeGif(IReadOnlyList<FrameData> frames, string result, string workDirectory)
    {
        if (frames.Count is < 1 or > 1000) throw new IOException(L.Get("GIFRequires11000Frames"));
        using var stream = new FileStream(result, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        StreamingGifWriter? writer = null; double elapsed = 0; var writtenTicks = 0;
        try
        {
            foreach (var frame in frames)
            {
                var path = Path.GetFullPath(frame.Path);
                if (!path.StartsWith(Path.GetFullPath(workDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException(L.Get("TheEncoderReturnedAFramePathOutside"));
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze();
                writer ??= new StreamingGifWriter(stream, bitmap.PixelWidth, bitmap.PixelHeight);
                elapsed += Math.Clamp(frame.Delay, 10, 1000) / 10d; var end = (int)Math.Round(elapsed); writer.Frame(bitmap, Math.Max(1, end - writtenTicks)); writtenTicks = end;
                if (stream.Position > 100_000_000) throw new IOException(L.Get("GIFReachedThe100MBLimitTemporary"));
            }
        }
        finally { writer?.Dispose(); }
    }
    private void Fail(string reason)
    {
        if (_finishing) return; _finishing = true; _timer?.Dispose(); _clock.Stop();
        try { _recorder?.Dispose(); } catch { } _recorder = null;
        SetState(RecordingState.Failed, L.Get("RecordingFailed") + reason + L.Get("TemporaryDataRetainedAt") + _workDirectory); _completion.TrySetResult();
    }
    private void SetState(RecordingState state, string status) { State = state; _stateTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(); Status = status; Changed?.Invoke(); }
}
