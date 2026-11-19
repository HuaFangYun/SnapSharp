using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using System.Windows.Threading;
using SnapSharp.Models;
using SnapSharp.Services;
using Windows.Media.Editing;
using Windows.Storage;

namespace SnapSharp.RegressionChecks;

// Opt-in desktop/device capture. No PnP disable commands or machine-wide fault switches.
internal static class RecordingAcceptance
{
    private sealed record Sample(double Seconds, long PrivateBytes, int Handles, int Frames);
    public static int Run(string[] args, string root)
    {
        if (args[0] == "--devices")
        {
            Console.WriteLine(JsonSerializer.Serialize(new { Microphones = RecordingService.Microphones(),
                Speakers = RecordingService.Speakers(), Cameras = RecordingService.Cameras() }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        if (args[0] is not ("--soak" or "--fallback" or "--hotplug"))
        {
            Console.Error.WriteLine("Use --devices, --soak [15|30|60] [WGC|DXGI] [software|hardware], --fallback, or --hotplug microphone|speaker|camera DEVICE_ID.");
            return 1;
        }
        Directory.CreateDirectory(root);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var exit = 1;
        app.Startup += async (_, _) =>
        {
            try { exit = await Check(args, root); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(root, "failure.txt"), ex.ToString()); Console.Error.WriteLine(ex); }
            finally { app.Shutdown(); }
        };
        app.Run();
        Console.WriteLine($"Evidence: {root}");
        return exit;
    }

    private static async Task<int> Check(string[] args, string root)
    {
        var hotplug = args[0] == "--hotplug";
        var fallback = args[0] == "--fallback";
        if (hotplug && (args.Length != 3 || args[1] is not ("microphone" or "speaker" or "camera")))
            throw new ArgumentException("--hotplug requires microphone|speaker|camera and an exact device ID from --devices.");
        if (fallback && args.Length != 1) throw new ArgumentException("--fallback takes no arguments.");
        if (args[0] == "--soak" && (args.Length < 4 || args[3] is not ("hardware" or "software") || (args.Length - 4) % 2 != 0))
            throw new ArgumentException("--soak FPS WGC|DXGI software|hardware [--microphone ID] [--speaker ID] [--camera ID]");
        var selected = new Dictionary<string, string>();
        for (var index = 4; index < args.Length; index += 2)
        {
            if (args[index] is not ("--microphone" or "--speaker" or "--camera") || !selected.TryAdd(args[index], args[index + 1]))
                throw new ArgumentException("Unknown or duplicate device option.");
        }
        string? Device(string kind) => hotplug && args[1] == kind ? args[2] : selected.GetValueOrDefault("--" + kind);
        var fps = !hotplug && args.Length > 1 ? int.Parse(args[1]) : 30;
        var preferences = new RecordingPreferences { FrameRate = fps,
            CaptureBackend = !hotplug && args.Length > 2 ? args[2] : "WGC",
            HardwareEncoding = fallback || (!hotplug && args.Length > 3 && args[3] == "hardware"), IncludeCursor = false };
        preferences.Validate();
        RecordingDevice[] Devices() => args[1] switch { "microphone" => RecordingService.Microphones(),
            "speaker" => RecordingService.Speakers(), _ => RecordingService.Cameras() };
        if (hotplug && !Devices().Any(d => d.Id == args[2])) throw new InvalidOperationException("Selected device is absent.");
        var scene = new TextBlock { Foreground = Brushes.White, FontSize = 32, Text = "NimCap Screenshot recording acceptance" };
        var window = new Window { Title = "NimCap Screenshot recording acceptance", Width = 960, Height = 540,
            Topmost = true, Content = new Border { Background = Brushes.MidnightBlue, Child = scene } };
        var animation = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        var rendered = 0;
        animation.Tick += (_, _) => scene.Text = $"NimCap Screenshot acceptance\n{++rendered}\n{DateTime.UtcNow:O}";
        RecordingService? service = null;
        using var process = Process.GetCurrentProcess();
        var samples = new List<Sample>();
        using var csv = new StreamWriter(Path.Combine(root, "resources.csv")) { AutoFlush = true };
        csv.WriteLine("session,seconds,privateBytes,handles,frames");
        var nativeAttempts = new List<bool>();
        var results = new List<object>();
        using var events = new StreamWriter(Path.Combine(root, "events.jsonl")) { AutoFlush = true };
        var performance = true;
        var resourceAccepted = true;
        var disconnected = false;
        var reconnected = false;
        Sample? settledBaseline = null;
        try
        {
            window.Show(); animation.Start(); await Task.Delay(500);
            var corner = window.PointToScreen(new System.Windows.Point());
            var end = window.PointToScreen(new System.Windows.Point(window.ActualWidth, window.ActualHeight));
            var region = new System.Drawing.Rectangle((int)corner.X, (int)corner.Y, (int)(end.X - corner.X) & ~1, (int)(end.Y - corner.Y) & ~1);
            var screen = System.Windows.Forms.Screen.FromRectangle(region);
            // Warm up native capture and decoding before establishing the process baseline.
            var sessions = hotplug ? 2 : 3;
            for (var index = 0; index < sessions; index++)
            {
                var sessionIndex = index;
                var inject = fallback && index == 1;
                service = new RecordingService(new DispatcherSynchronizationContext(window.Dispatcher, DispatcherPriority.Background))
                {
                    BeforeNativeRecord = hardware =>
                    {
                        if (!inject) return;
                        nativeAttempts.Add(hardware);
                        if (hardware) throw new InvalidOperationException("Acceptance: injected hardware initialization failure before first frame.");
                    }
                };
                var session = service;
                RecordingState? lastState = null;
                string? lastStatus = null;
                service.Changed += () =>
                {
                    if (session.State == lastState && session.Status == lastStatus) return;
                    lastState = session.State; lastStatus = session.Status;
                    events.WriteLine(JsonSerializer.Serialize(new { At = DateTimeOffset.UtcNow, Session = sessionIndex,
                        State = session.State.ToString(), session.Status, session.UsedSoftwareFallback, session.SoftwareFallbackReason }));
                };
                var duration = hotplug ? (index == 0 ? 600 : 10) : index == 1 && !fallback ? 600 : 5;
                var path = Path.Combine(root, $"session-{index}.mp4");
                service.Start(region, screen.Bounds, screen.DeviceName, path, preferences,
                    Device("microphone"), Device("speaker"), Device("camera"));
                var startup = Stopwatch.StartNew();
                while (service.State == RecordingState.Starting && startup.Elapsed.TotalSeconds < 40) await Task.Delay(100);
                if (service.State != RecordingState.Recording) throw new InvalidOperationException(service.Status);
                Console.WriteLine(hotplug && index == 0 ? "Unplug the selected device after 10 seconds, then reconnect it. Waiting up to 600 seconds." : $"Recording session {index}: {duration}s, {fps}fps.");
                var baseline = ReadSample(0, service);
                var last = -1;
                while (service.IsActive && service.Elapsed.TotalSeconds < duration)
                {
                    await Task.Delay(250);
                    var seconds = (int)service.Elapsed.TotalSeconds;
                    if (seconds / 5 != last)
                    {
                        last = seconds / 5;
                        var sample = ReadSample(service.Elapsed.TotalSeconds, service);
                        csv.WriteLine(FormattableString.Invariant($"{index},{sample.Seconds:F3},{sample.PrivateBytes},{sample.Handles},{sample.Frames}"));
                        if (!hotplug && !fallback && index == 1 && seconds >= 60) samples.Add(sample);
                    }
                    if (hotplug && index == 0 && !disconnected && !Devices().Any(d => d.Id == args[2]))
                    {
                        disconnected = true;
                        events.WriteLine(JsonSerializer.Serialize(new { At = DateTimeOffset.UtcNow, Event = "device-missing", Device = args[2] }));
                    }
                    if (service.State is RecordingState.Stopping or RecordingState.Encoding) break;
                }
                if (service.State == RecordingState.Recording) service.Stop();
                await service.Completion.WaitAsync(TimeSpan.FromSeconds(75));
                if (service.State != RecordingState.Completed || service.FrameCount < 1) throw new InvalidOperationException(service.Status);
                if ((!hotplug || index == 1) && service.Elapsed.TotalSeconds < duration - .5)
                    throw new InvalidOperationException("Recording completed before the requested test duration: " + service.Status);
                if (inject && (!service.UsedSoftwareFallback || !nativeAttempts.SequenceEqual(new[] { true, false })))
                    throw new InvalidOperationException("Hardware -> software retry was not exercised exactly once.");
                var file = await StorageFile.GetFileFromPathAsync(path);
                var clip = await MediaClip.CreateFromFileAsync(file);
                var video = clip.GetVideoEncodingProperties();
                if (video.Width != region.Width || video.Height != region.Height || Math.Abs(clip.OriginalDuration.TotalSeconds - service.Elapsed.TotalSeconds) > .6)
                    throw new InvalidDataException("Decoded MP4 dimensions or wall-clock duration mismatch.");
                var composition = new MediaComposition(); composition.Clips.Add(clip);
                foreach (var fraction in new[] { .1, .5, .9 })
                {
                    using var thumbnail = await composition.GetThumbnailAsync(TimeSpan.FromSeconds(clip.OriginalDuration.TotalSeconds * fraction), 160, 96, VideoFramePrecision.NearestFrame);
                    if (thumbnail.Size == 0) throw new InvalidDataException("MP4 thumbnail decode failed.");
                }
                var withinTolerance = RecordingService.IsFrameRateWithinTolerance(service.AverageFrameRate, fps);
                performance &= withinTolerance;
                Console.WriteLine($"{(withinTolerance ? "FPS ACCEPTED" : "PERFORMANCE NOT ACCEPTED")}: target={fps}, actual={service.AverageFrameRate:F2}");
                await Task.Delay(2000);
                var after = ReadSample(0, service);
                resourceAccepted &= after.Handles - baseline.Handles <= 100 && after.PrivateBytes - baseline.PrivateBytes <= 128L * 1024 * 1024;
                if (settledBaseline is not null)
                    resourceAccepted &= after.Handles - settledBaseline.Handles <= 100 && after.PrivateBytes - settledBaseline.PrivateBytes <= 128L * 1024 * 1024;
                else settledBaseline = after;
                results.Add(new { Session = index, path, service.FrameCount, service.AverageFrameRate, Target = fps, WithinFivePercent = withinTolerance,
                    Duration = clip.OriginalDuration.TotalSeconds, service.UsedSoftwareFallback, service.SoftwareFallbackReason,
                    ActiveBaseline = baseline, AfterCompletion = after });
                if (hotplug && index == 0)
                {
                    disconnected |= !Devices().Any(d => d.Id == args[2]);
                    if (!disconnected || service.Elapsed.TotalSeconds < 10 || service.Elapsed.TotalSeconds >= 600)
                        throw new InvalidOperationException("A mid-recording device removal and automatic safe completion were not observed.");
                    var wait = Stopwatch.StartNew();
                    while (!Devices().Any(d => d.Id == args[2]) && wait.Elapsed.TotalSeconds < 120) await Task.Delay(500);
                    reconnected = Devices().Any(d => d.Id == args[2]);
                    if (!reconnected) throw new InvalidOperationException("Device did not reconnect within 120 seconds.");
                    events.WriteLine(JsonSerializer.Serialize(new { At = DateTimeOffset.UtcNow, Event = "device-reconnected", Device = args[2] }));
                }
            }
            // Compare stable windows rather than startup allocations. A bounded trend is evidence, not proof of absence of all leaks.
            if (samples.Count >= 12)
            {
                var first = samples.Take(6).ToArray(); var final = samples.TakeLast(6).ToArray();
                resourceAccepted &= final.Average(s => s.Handles) - first.Average(s => s.Handles) <= 100 &&
                    final.Average(s => s.PrivateBytes) - first.Average(s => s.PrivateBytes) <= 64L * 1024 * 1024;
            }
            File.WriteAllText(Path.Combine(root, "report.json"), JsonSerializer.Serialize(new { Mode = args[0],
                OS = Environment.OSVersion.ToString(), Runtime = Environment.Version.ToString(), Region = region.ToString(),
                preferences, selected, results, nativeAttempts, ResourceAccepted = resourceAccepted, PerformanceAccepted = performance,
                ResourceLimits = new { SteadyPrivateBytes = 64L * 1024 * 1024, ReleasedPrivateBytes = 128L * 1024 * 1024, Handles = 100 },
                disconnected, reconnected, Samples = samples,
                Pending = "Audio/video synchronization, physical device matrix, external players and clean Windows N VM remain separate acceptance." }, new JsonSerializerOptions { WriteIndented = true }));
            return resourceAccepted && performance ? 0 : 2;
        }
        finally
        {
            if (service?.IsActive == true)
            {
                service.Stop();
                try { await service.Completion.WaitAsync(TimeSpan.FromSeconds(75)); } catch (TimeoutException) { }
            }
            animation.Stop(); window.Close();
        }

        Sample ReadSample(double seconds, RecordingService session)
        {
            process.Refresh();
            return new(seconds, process.PrivateMemorySize64, process.HandleCount, session.FrameCount);
        }
    }
}
