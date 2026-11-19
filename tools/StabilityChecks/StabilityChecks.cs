using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SnapSharp.Models;
using SnapSharp.Services;

namespace SnapSharp.StabilityChecks;

internal static partial class Program
{
    private static void StabilityChecks()
    {
        // Isolate diagnostics from existing installations; restore the runner's configuration afterwards.
        var location = Path.Combine(AppContext.BaseDirectory, "data-location.json");
        var original = File.Exists(location) ? File.ReadAllBytes(location) : null;
        File.WriteAllText(location, JsonSerializer.Serialize(new { CurrentDirectory = Root }));
        try
        {
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            CrashChecks(app);
            HotkeyChecks();
            InferenceChecks().GetAwaiter().GetResult();
            Console.WriteLine("PASS: global exception recovery/privacy, single-path hotkeys, isolated ONNX CPU/fallback/cancellation/worker death.");
        }
        finally
        {
            if (original is null) File.Delete(location); else File.WriteAllBytes(location, original);
        }
    }

    private static void CrashChecks(System.Windows.Application app)
    {
        var dialogs = 0; string? reportPath = null;
        using var diagnostics = new CrashDiagnostics(app)
        {
            ReportDialog = (path, _) => { dialogs++; reportPath = path; }
        };
        var frame = new DispatcherFrame();
        var continued = false;
        var button = new System.Windows.Controls.Button();
        button.Click += (_, _) => throw new Exception("test-private-ocr-content");
        app.Dispatcher.BeginInvoke(() => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)));
        app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => { continued = true; frame.Continue = false; });
        Dispatcher.PushFrame(frame);
        Require(continued && dialogs == 1 && reportPath is not null && File.Exists(reportPath), "UI button exception did not recover and write a report.");
        var report = File.ReadAllText(reportPath!);
        Require(report.Contains("System.Exception") && report.Contains("OS:") && report.Contains("Runtime:") && report.Contains("WorkingSet:"), "Crash report lacks required metadata.");
        Require(!report.Contains("test-private-ocr-content") && !report.Contains("StabilityChecks.cs"), "Crash report leaked exception text or source paths.");
        var taskArgs = new UnobservedTaskExceptionEventArgs(new AggregateException(new InvalidOperationException("private-task-text")));
        typeof(CrashDiagnostics).GetMethod("OnTask", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [null, taskArgs]);
        Require(taskArgs.Observed, "Unobserved task exception was not marked observed.");
        Require(CrashDiagnostics.IsFatal(new AggregateException(new OutOfMemoryException())), "Nested fatal exception was treated as recoverable.");
        Require(Directory.GetFiles(AppPaths.DiagnosticsRoot, "crash-*.txt").Length == 2, "Task report missing or concurrent reports overwritten.");
        Console.WriteLine("PASS: actual Dispatcher button exception recovered, task observed, fatal classification, unique private text reports.");
    }

    private static void HotkeyChecks()
    {
        var down = new HashSet<uint>(); var fail = false;
        var registered = new List<(int Id, uint Modifiers)>(); var released = new List<int>();
        using var host = new GlobalHotkeyHost(down.Contains,
            (id, modifiers, _) => { registered.Add((id, modifiers)); return !fail; }, released.Add);
        var settings = new AppSettings { FullScreenHotkey = "", ActiveWindowHotkey = "", ClipboardPinHotkey = "", TogglePinsHotkey = "",
            RestorePinHotkey = "", RestorePinInteractionHotkey = "", ScreenRulerHotkey = "", RegionHotkey = "Ctrl+Shift+A" };
        var count = 0; host.CaptureRequested += (_, _) => count++;
        var poll = typeof(GlobalHotkeyHost).GetMethod("PollPhysicalKeys", PrivateInstance)!;
        var timer = (DispatcherTimer)typeof(GlobalHotkeyHost).GetField("_keyStateTimer", PrivateInstance)!.GetValue(host)!;
        var message = typeof(GlobalHotkeyHost).GetMethod("OnThreadMessage", PrivateInstance)!;
        void Poll() => poll.Invoke(host, [null, EventArgs.Empty]);
        void Wm()
        {
            object[] arguments = [new MSG { message = 0x0312, wParam = 0x5011 }, false];
            message.Invoke(host, arguments);
        }
        host.Register(settings);
        Require(!timer.IsEnabled && (registered.Single().Modifiers & 0x4000) != 0, "Registered key must use WM_HOTKEY with MOD_NOREPEAT and no polling.");
        down.UnionWith([0x11u, 0x10u, 0x41u]);
        for (var i = 0; i < 40; i++) Poll();
        Require(count == 0, "Registered key also fired via polling.");
        Wm(); Require(count == 1, "WM_HOTKEY not routed.");
        down.Clear(); fail = true; host.Register(settings);
        Require(timer.IsEnabled, "Failed registration did not enable fallback.");
        down.UnionWith([0x11u, 0x10u, 0x41u]);
        for (var i = 0; i < 40; i++) Poll();
        Wm(); Require(count == 2, "Held fallback key repeated or also accepted WM_HOTKEY.");
        down.Remove(0x10); Poll(); down.Add(0x10); Poll();
        Require(count == 2, "Changing modifiers while held rearmed fallback.");
        down.Remove(0x41); Poll(); down.Add(0x41); Poll();
        Require(count == 3, "Released fallback key did not rearm.");
        host.Suspend(); Require(!timer.IsEnabled, "Suspended host kept polling.");
        host.Resume(); Poll(); Require(count == 3, "Resume fired a still-held key.");
        Require(GlobalHotkeyHost.RunPinRoutingSelfTest(), "Action routing changed.");
        Console.WriteLine("PASS: exclusive native/polling routes, no held-key repeat, modifier latch, release/rearm and suspend/resume.");
    }

    private static async Task InferenceChecks()
    {
        var model = Path.Combine(Root, "mean.onnx");
        // Synthetic 1x3x32x32 -> channel mean mask, opset 13. No downloaded models or user screenshots.
        File.WriteAllBytes(model, Convert.FromBase64String("CAg6gAEKKAoFaW5wdXQSBm91dHB1dCIKUmVkdWNlTWVhbioLCgRheGVzQAGgAQcSEXN0YWJpbGl0eS1maXh0dXJlWh8KBWlucHV0EhYKFAgBEhAKAggBCgIIAwoCCCAKAgggYiAKBm91dHB1dBIWChQIARIQCgIIAQoCCAEKAgggCgIIIEICEA0="));
        var bytes = Enumerable.Repeat((byte)128, 32 * 32 * 4).ToArray();
        for (var i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
        var image = BitmapSource.Create(32, 32, 96, 96, PixelFormats.Bgra32, null, bytes, 128); image.Freeze();
        var settings = new AppSettings { CutoutModel = model, CutoutProfile = "BiRefNetAlpha", CutoutInputSize = 32,
            CutoutOnnxDevice = "CPU", RecognitionTimeoutSeconds = 30 };
        var output = await OnnxImageRuntime.Shared.RemoveBackground(image, settings);
        Require(output.IsFrozen && output.PixelWidth == 32 && output.PixelHeight == 32, "CPU child-process inference failed.");
        settings.CutoutOnnxDevice = "DirectML"; settings.CutoutOnnxDeviceId = int.MaxValue;
        output = await OnnxImageRuntime.Shared.RemoveBackground(image, settings);
        Require(output.PixelWidth == 32 && settings.CutoutOnnxDevice == "DirectML", "GPU initialization failure did not fall back without mutating settings.");
        Require(File.ReadAllText(Path.Combine(AppPaths.DiagnosticsRoot, "inference-status.log")).Contains("GPU fallback"), "Fallback was not recorded.");
        settings.CutoutOnnxDevice = "CPU";
        var invalidModel = Path.Combine(Root, "private-worker-ocr-text.onnx");
        File.WriteAllText(invalidModel, "private-worker-ocr-text");
        settings.CutoutModel = invalidModel;
        try
        {
            await OnnxImageRuntime.Shared.RemoveBackground(image, settings);
            throw new Exception("Invalid model returned success.");
        }
        catch (InvalidOperationException error) when (error.Message == SnapSharp.Localization.UiText.Get("IsolatedInferenceFailed")) { }
        var failureLog = File.ReadAllText(Path.Combine(AppPaths.DiagnosticsRoot, "inference-status.log"));
        Require(failureLog.Contains("Managed inference failure:") && failureLog.Contains("OnnxRuntimeException") &&
            failureLog.Contains("  at ") && failureLog.Contains("InferenceSession"), "Worker stack did not cross IPC into diagnostics.");
        Require(!failureLog.Contains("private-worker-ocr-text") && !failureLog.Contains(Root) &&
            !failureLog.Contains("OnnxImageRuntime.cs"), "Worker diagnostic leaked input, model path or source filenames.");
        settings.CutoutModel = model;
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            try { await OnnxImageRuntime.Shared.RemoveBackground(image, settings, canceled.Token); throw new Exception("Canceled inference ran."); }
            catch (OperationCanceledException) { }
        }
        // Kill only a worker started from this test's isolated output directory.
        var request = OnnxImageRuntime.Shared.RemoveBackground(image, settings);
        var workerPath = Path.Combine(AppContext.BaseDirectory, "NimCap.Screenshot.exe");
        var killed = false;
        var deadline = Stopwatch.StartNew();
        while (!request.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            foreach (var process in Process.GetProcessesByName("SnapSharp"))
            {
                using (process)
                {
                    if (string.Equals(process.MainModule?.FileName, workerPath, StringComparison.OrdinalIgnoreCase))
                    { process.Kill(entireProcessTree: true); killed = true; }
                }
            }
            if (killed) break;
            await Task.Delay(5);
        }
        Require(killed, "Did not observe the inference worker to kill.");
        try { await request; throw new Exception("Killed worker returned success."); }
        catch (InvalidOperationException error) when (error.Message == SnapSharp.Localization.UiText.Get("IsolatedInferenceFailed")) { }
        // A fresh worker after failure proves the parent and queue remain usable.
        output = await OnnxImageRuntime.Shared.RemoveBackground(image, settings);
        Require(output.PixelWidth == 32, "Worker death poisoned subsequent inference.");
        Require(!OnnxWorker.IsWorker, "The test host unexpectedly became a worker.");
        Console.WriteLine("PASS: real ONNX CPU IPC, GPU fallback, private managed failure stacks, cancellation, worker kill and recovery.");
    }
}
