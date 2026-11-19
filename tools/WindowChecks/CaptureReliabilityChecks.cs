using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SnapSharp.Models;
using SnapSharp.Services;
using SnapSharp.ViewModels;
using Rectangle = System.Drawing.Rectangle;
using Application = System.Windows.Application;

namespace SnapSharp.WindowChecks;

internal static partial class Program
{
    private static BitmapSource ReliabilityImage()
    {
        var bytes = Enumerable.Repeat((byte)255, 32 * 32 * 4).ToArray();
        var image = BitmapSource.Create(32, 32, 96, 96, PixelFormats.Bgra32, null, bytes, 128);
        image.Freeze(); return image;
    }

    private sealed class TestClock : TimeProvider
    {
        internal DateTimeOffset Now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
        internal void Advance(int seconds) => Now = Now.AddSeconds(seconds);
    }
    private sealed class TestCapture(BitmapSource image) : ICaptureService
    {
        internal Func<CapturedScreen>? Capture;
        internal Func<CancellationToken, CapturedScreen>? CancelableCapture;
        internal int Calls;
        internal bool MissingWindow;
        public CapturedScreen CaptureVirtualScreen()
        {
            Interlocked.Increment(ref Calls);
            return Capture?.Invoke() ?? new CapturedScreen(new Rectangle(0, 0, 32, 32), image);
        }
        public CapturedScreen CaptureVirtualScreen(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (CancelableCapture is null) return CaptureVirtualScreen();
            Interlocked.Increment(ref Calls);
            return CancelableCapture(token);
        }
        public IReadOnlyList<CapturedScreen> CaptureAllScreens() => [CaptureVirtualScreen()];
        public BitmapSource CaptureRegion(Rectangle bounds) => CaptureVirtualScreen().Image;
        public CapturedScreen? CaptureWindow(nint handle) => MissingWindow ? null : CaptureVirtualScreen();
        public IReadOnlyList<WindowCandidate> EnumerateTopLevelWindows() => [];
        public WindowCandidate? FindWindowAt(System.Drawing.Point point) => null;
    }

    private static void CaptureReliabilityChecks()
    {
        var image = ReliabilityImage();
        CaptureHealthChecks(image);
        HistoryLimitChecks(image);
        RegionSessionChecks(image);
        CallbackLifetimeChecks();
        var frame = new DispatcherFrame();
        Exception? failure = null;
        Application.Current.Dispatcher.BeginInvoke(async () =>
        {
            try { await AsyncCaptureChecks(image); }
            catch (Exception error) { failure = error; }
            finally { frame.Continue = false; }
        });
        Dispatcher.PushFrame(frame);
        if (failure is not null) throw failure;
        Console.WriteLine("PASS: capture health/backoff, asynchronous UI ownership, duplicate suppression, late cancellation, bounded history, persistent backend fallback and callback teardown.");
    }

    private static void CaptureHealthChecks(BitmapSource image)
    {
        var clock = new TestClock();
        var preferred = new TestCapture(image) { Capture = () => throw new TimeoutException() };
        var fallback = new TestCapture(image);
        var capture = new ResilientCaptureService(preferred, fallback, clock);
        capture.CaptureVirtualScreen();
        clock.Advance(4); capture.CaptureVirtualScreen();
        Require(preferred.Calls == 1 && fallback.Calls == 2, "WGC retried during the initial cooldown.");
        clock.Advance(1); capture.CaptureVirtualScreen(); // Second failure: 15 seconds.
        clock.Advance(14); capture.CaptureVirtualScreen();
        Require(preferred.Calls == 2, "Second cooldown did not last 15 seconds.");
        clock.Advance(1); capture.CaptureVirtualScreen(); // Third failure: 60 seconds.
        clock.Advance(59); capture.CaptureVirtualScreen();
        Require(preferred.Calls == 3, "Third cooldown did not last 60 seconds.");
        preferred.Capture = null;
        clock.Advance(1); capture.CaptureVirtualScreen(); capture.CaptureVirtualScreen();
        Require(preferred.Calls == 5, "WGC did not recover on a real capture or reset its cooldown.");
        var fallbackCount = fallback.Calls;
        preferred.Capture = () => throw new OperationCanceledException();
        try { capture.CaptureVirtualScreen(); throw new Exception("Cancellation was swallowed."); }
        catch (OperationCanceledException) { }
        preferred.Capture = () => throw new ArgumentException();
        try { capture.CaptureVirtualScreen(); throw new Exception("Argument error was swallowed."); }
        catch (ArgumentException) { }
        preferred.Capture = null;
        capture.CaptureVirtualScreen();
        Require(fallback.Calls == fallbackCount, "Business/cancellation errors degraded the backend.");

        // Exactly one probe may touch WGC. Concurrent requests can use GDI.
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        preferred.Capture = () => { entered.Set(); Require(release.Wait(3000), "Probe release timed out."); return new CapturedScreen(new Rectangle(0, 0, 32, 32), image); };
        var probe = Task.Run(capture.CaptureVirtualScreen);
        try
        {
            Require(entered.Wait(3000), "Probe did not start.");
            var calls = preferred.Calls;
            capture.CaptureVirtualScreen();
            Require(preferred.Calls == calls && fallback.Calls == fallbackCount + 1, "Concurrent WGC probe was not suppressed.");
        }
        finally { release.Set(); probe.GetAwaiter().GetResult(); }
        var gdi = new TestCapture(image);
        var manual = new ResilientCaptureService(gdi, gdi, clock);
        manual.CaptureVirtualScreen(); clock.Advance(1000); manual.CaptureVirtualScreen();
        Require(gdi.Calls == 2, "Explicit GDI mode performed extra probes.");

        var closedClock = new TestClock();
        var closedPreferred = new TestCapture(image) { Capture = () => throw new TimeoutException() };
        var closedFallback = new TestCapture(image);
        var closedCapture = new ResilientCaptureService(closedPreferred, closedFallback, closedClock);
        closedCapture.CaptureVirtualScreen(); closedClock.Advance(5);
        closedPreferred.MissingWindow = true;
        Require(closedCapture.CaptureWindow(1) is null && closedFallback.Calls == 1, "A closed target triggered GDI fallback.");
        closedCapture.CaptureVirtualScreen(); closedClock.Advance(5); closedCapture.CaptureVirtualScreen();
        Require(closedPreferred.Calls == 2, "A null window result incorrectly reset the recovery backoff.");
    }

    private static void HistoryLimitChecks(BitmapSource image)
    {
        var editor = new EditorViewModel(image);
        for (var i = 1; i <= 125; i++) editor.ReplaceAll([], false);
        var undos = 0;
        while (editor.CanUndo) { editor.UndoCommand.Execute(null); undos++; }
        Require(undos == EditorViewModel.MaximumHistorySteps, "Undo history exceeded/dropped the configured capacity.");
        var redos = 0;
        while (editor.CanRedo) { editor.RedoCommand.Execute(null); redos++; }
        Require(redos == EditorViewModel.MaximumHistorySteps, "Redo history capacity changed.");
        editor.UndoCommand.Execute(null); editor.ReplaceAll([]);
        Require(!editor.CanRedo && !editor.RedoCommand.CanExecute(null), "New edit retained the redo branch.");

        // Use distinct image references to verify oldest-first eviction and restoration order.
        var history = new EditorViewModel(image);
        var images = Enumerable.Range(0, 105).Select(_ => ReliabilityImage()).ToArray();
        foreach (var next in images) history.ReplaceImage(next);
        for (var i = 103; i >= 4; i--)
        {
            history.UndoCommand.Execute(null);
            Require(ReferenceEquals(history.Image, images[i]), "History eviction/reversal reordered snapshots.");
        }
        Require(!history.CanUndo, "Oldest snapshot was retained beyond the capacity.");
    }

    private sealed class TestFrameSource(BitmapSource image) : IRegionFrameSource
    {
        internal bool Fail, Canceled;
        internal int Calls, Releases;
        public BitmapSource Capture(CancellationToken token)
        {
            Calls++; token.ThrowIfCancellationRequested();
            if (Canceled) throw new OperationCanceledException();
            if (Fail) throw new COMException("Synthetic device loss.");
            return image;
        }
        public void Dispose() => Releases++;
    }
    private static void RegionSessionChecks(BitmapSource image)
    {
        var sources = new Dictionary<string, TestFrameSource>();
        var session = new RegionCaptureSession(new Rectangle(0, 0, 32, 32), "Auto", (backend, _) =>
        {
            var source = new TestFrameSource(image); sources.Add(backend, source); return source;
        });
        try
        {
            session.Capture(); session.Capture();
            Require(sources.Count == 1 && sources["WGC"].Calls == 2, "Region source was recreated per frame.");
            sources["WGC"].Fail = true; session.Capture();
            Require(session.Backend == "DXGI" && sources["WGC"].Releases == 1, "WGC fallback leaked its old session.");
            sources["DXGI"].Fail = true; session.Capture(); session.Capture();
            Require(session.Backend == "GDI" && sources["DXGI"].Releases == 1 && sources["GDI"].Calls == 2, "DXGI fallback or GDI reuse failed.");
        }
        finally { session.Dispose(); session.Dispose(); }
        Require(sources["GDI"].Releases == 1, "Region session did not dispose exactly once.");
        var canceled = new TestFrameSource(image) { Canceled = true };
        var opens = 0;
        using var cancelSession = new RegionCaptureSession(new Rectangle(0, 0, 32, 32), "Auto", (_, _) => { opens++; return canceled; });
        try { cancelSession.Capture(); throw new Exception("Region cancellation was swallowed."); }
        catch (OperationCanceledException) { }
        Require(opens == 1 && cancelSession.Backend == "WGC", "Cancellation selected a fallback backend.");

        var settingsPath = Path.Combine(Root, "capture-backend-settings.json");
        var settings = new SettingsService(settingsPath);
        settings.Current.ScrollingCaptureBackend = "DXGI"; settings.Save();
        var reloaded = new SettingsService(settingsPath); reloaded.Load();
        Require(reloaded.Current.ScrollingCaptureBackend == "DXGI", "Backend preference did not persist.");
        reloaded.Current.ScrollingCaptureBackend = "bad"; reloaded.Save();
        Require(reloaded.Current.ScrollingCaptureBackend == "Auto", "Invalid backend was not normalized.");
    }

    private static void CallbackLifetimeChecks()
    {
        var callbacks = new CaptureCallbackLifetime();
        Require(callbacks.TryEnter(), "Callback could not enter a live session.");
        using var unsubscribed = new ManualResetEventSlim();
        var close = Task.Run(() => callbacks.Close(unsubscribed.Set));
        try
        {
            Require(unsubscribed.Wait(3000), "Close did not unsubscribe before waiting.");
            Require(!callbacks.TryEnter() && !close.IsCompleted, "Close admitted a late callback or failed to join the active callback.");
        }
        finally { callbacks.Exit(); close.GetAwaiter().GetResult(); }
        Require(!callbacks.TryEnter(), "Closed session accepted a late callback.");
        callbacks.Close(() => { });
    }

    private static async Task AsyncCaptureChecks(BitmapSource image)
    {
        var owner = Environment.CurrentManagedThreadId;
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new TestCapture(image)
        {
            Capture = () =>
            {
                Require(Environment.CurrentManagedThreadId != owner, "Capture ran on the UI thread.");
                entered.TrySetResult();
                Require(release.Wait(5000), "Background capture was not released.");
                return new CapturedScreen(new Rectangle(0, 0, 32, 32), image);
            }
        };
        var coordinator = new CaptureCoordinator(backend);
        var events = 0;
        coordinator.CaptureCompleted += (_, result) =>
        {
            Require(Environment.CurrentManagedThreadId == owner && result.Image.IsFrozen, "Completion ownership/frozen image contract failed.");
            events++;
        };
        var capture = coordinator.BeginAsync(CaptureMode.Region);
        try
        {
            Require(coordinator.IsActive, "Capture was not reserved before its first await.");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var heartbeat = false;
            await Application.Current.Dispatcher.InvokeAsync(() => heartbeat = true, DispatcherPriority.Background);
            Require(heartbeat && !capture.IsCompleted, "UI heartbeat stalled behind capture.");
            await coordinator.BeginAsync(CaptureMode.FullScreen);
            await new CaptureCoordinator(backend).BeginAsync(CaptureMode.FullScreen);
            Require(backend.Calls == 1, "Repeated/cross-coordinator capture was not suppressed.");
            coordinator.CancelPendingCapture();
        }
        finally { release.Set(); }
        try { await capture; throw new Exception("Late canceled capture returned success."); }
        catch (OperationCanceledException) { }
        Require(events == 0 && !coordinator.IsActive && !Application.Current.Windows.OfType<SnapSharp.Views.SelectionWindow>().Any(), "Canceled result opened an overlay or retained active state.");
        backend.Capture = null;
        await coordinator.BeginAsync(CaptureMode.FullScreen);
        Require(events == 1 && !coordinator.IsActive, "Capture did not recover after cancellation.");
        backend.Capture = () => throw new TimeoutException();
        try { await coordinator.BeginAsync(CaptureMode.FullScreen); throw new Exception("Capture failure was swallowed."); }
        catch (TimeoutException) { }
        Require(!coordinator.IsActive, "Capture failure retained the reservation.");

        var cancelEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.CancelableCapture = token =>
        {
            cancelEntered.TrySetResult();
            Require(token.WaitHandle.WaitOne(3000), "Coordinator did not forward cancellation to the backend wait.");
            token.ThrowIfCancellationRequested();
            throw new Exception("Canceled wait unexpectedly continued.");
        };
        var cancelWait = coordinator.BeginAsync(CaptureMode.FullScreen);
        await cancelEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        coordinator.CancelPendingCapture();
        try { await cancelWait; throw new Exception("Backend wait cancellation was swallowed."); }
        catch (OperationCanceledException) { }
        Require(!coordinator.IsActive && events == 1, "Canceled backend wait delivered a result.");
        backend.CancelableCapture = null; backend.Capture = null;

        // A caller on the UI thread need not have installed an ambient synchronization context.
        var context = SynchronizationContext.Current;
        Task withoutContext;
        try { SynchronizationContext.SetSynchronizationContext(null); withoutContext = coordinator.BeginAsync(CaptureMode.FullScreen); }
        finally { SynchronizationContext.SetSynchronizationContext(context); }
        await withoutContext;
        Require(events == 2, "Completion did not dispatch explicitly back to the UI owner.");
        coordinator.Shutdown();
        await coordinator.BeginAsync(CaptureMode.FullScreen);
        Require(events == 2, "Disposed coordinator delivered another capture.");
    }
}
