using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SnapSharp.Models;
using SnapSharp.Services;
using SnapSharp.Views;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Point = System.Windows.Point;
using Rectangle = System.Drawing.Rectangle;
using TextBox = System.Windows.Controls.TextBox;

namespace SnapSharp.RegressionChecks;

internal static partial class Program
{
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, PrivateInstance)!.GetValue(target)!;

    private sealed class DeferredElements : IElementDetectionService
    {
        internal sealed record Query(WindowCandidate Window, System.Drawing.Point Point, CancellationToken Token,
            TaskCompletionSource<IReadOnlyList<ElementCandidate>> Completion);
        public List<Query> Queries { get; } = [];
        public Task<IReadOnlyList<ElementCandidate>> FindElementsAsync(WindowCandidate window, System.Drawing.Point point, CancellationToken token)
        {
            var completion = new TaskCompletionSource<IReadOnlyList<ElementCandidate>>(TaskCreationOptions.RunContinuationsAsynchronously);
            Queries.Add(new Query(window, point, token, completion));
            // Deliberately ignore cancellation to verify late third-party responses cannot modify selection.
            return completion.Task;
        }
    }

    private static BitmapSource ElementTestImage(int width, int height)
    {
        var bytes = new byte[width * height * 4];
        for (var i = 0; i < bytes.Length; i += 4)
        { bytes[i] = (byte)(i / 4 % 251); bytes[i + 1] = 87; bytes[i + 2] = 255; bytes[i + 3] = 255; }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bytes, width * 4);
        image.Freeze();
        return image;
    }

    private static void ElementInteractionChecks()
    {
        var image = ElementTestImage(800, 600);
        var first = new WindowCandidate(new Rectangle(-780, 20, 380, 400), 123, new Rectangle(-772, 52, 364, 360));
        var second = new WindowCandidate(new Rectangle(-380, 20, 360, 400), 456, new Rectangle(-372, 52, 344, 360));
        var leaf = new ElementCandidate(new Rectangle(-700, 90, 80, 32), "button", "Fixture button");
        var panel = new ElementCandidate(new Rectangle(-740, 65, 300, 160), "panel");
        var secondLeaf = new ElementCandidate(new Rectangle(-250, 90, 80, 32), "edit", "Fixture input");
        var detector = new DeferredElements();
        var overlay = new SelectionWindow(new CapturedScreen(new Rectangle(-800, 0, 800, 600), image),
            [first, second], new CandidateCapture([first, second]), elementDetectionService: detector);
        try
        {
            overlay.Show(); overlay.UpdateLayout();
            var picker = (ComboBox)overlay.FindName("CaptureModePicker");
            var timer = Field<DispatcherTimer>(overlay, "_elementHoverTimer");
            Require(((ComboBoxItem)picker.SelectedItem).Tag.ToString() == "Element" && timer.IsEnabled,
                "A new screenshot must start in element mode with hover detection enabled.");
            picker.SelectedIndex = 0;
            Point Logical(int x, int y) => new((x + 800) * overlay.ActualWidth / 800, y * overlay.ActualHeight / 600);
            Rect Expected(Rectangle r) => new((r.X + 800) * overlay.ActualWidth / 800,
                r.Y * overlay.ActualHeight / 600, r.Width * overlay.ActualWidth / 800, r.Height * overlay.ActualHeight / 600);
            void Move(int x, int y) => Invoke(overlay, "UpdatePointer", Logical(x, y));
            Rect Suggest() => (Rect)Invoke(overlay, "SuggestedElementAt", Field<Point>(overlay, "_lastPointer"))!;
            Task Query() => (Task)Invoke(overlay, "RefreshElementSuggestionAsync")!;
            void Reply(Task query, params ElementCandidate[] candidates)
            {
                detector.Queries[^1].Completion.SetResult(candidates);
                PumpUntil(() => query.IsCompleted); query.GetAwaiter().GetResult();
            }
            foreach (var expected in new[] { "Element", "Window", "FullScreen", "Region" })
            {
                Invoke(overlay, "Window_KeyDown", overlay, new KeyEventArgs(Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(overlay), 0, Key.Tab) { RoutedEvent = Keyboard.KeyDownEvent });
                Require(((ComboBoxItem)picker.SelectedItem).Tag.ToString() == expected, "Tab omitted or reordered a capture mode.");
            }
            picker.SelectedIndex = 1; timer.Stop(); Move(-660, 104);
            Require(Suggest() == Expected(first.ClientBounds!.Value), "Pending element detection must leave the window selectable.");
            var initial = Query();
            Require(detector.Queries.Count == 1 && detector.Queries[0].Window == first, "Detection targeted the overlay instead of the saved window.");
            Reply(initial, leaf, leaf, panel, new ElementCandidate(new Rectangle(100, 100, 20, 20), "offscreen"));
            Require(Suggest() == Expected(leaf.Bounds), "Element bounds were not converted from negative physical coordinates.");
            Invoke(overlay, "AdjustElementScope", Logical(-660, 104), -120);
            Require(Suggest() == Expected(panel.Bounds), "Wheel did not expand to the parent, or duplicate bounds were retained.");
            Invoke(overlay, "AdjustElementScope", Logical(-660, 104), 120);
            Require(Suggest() == Expected(leaf.Bounds), "Wheel did not return to the child.");
            for (var i = 0; i < 8; i++) Invoke(overlay, "AdjustElementScope", Logical(-660, 104), -120);
            Require(Suggest() == Expected(first.Bounds), "Wheel expansion must stop at the full window.");
            for (var i = 0; i < 8; i++) Invoke(overlay, "AdjustElementScope", Logical(-660, 104), 120);

            Move(-658, 104); var stale = Query(); var staleRequest = detector.Queries[^1];
            Move(-210, 104); Suggest(); var current = Query();
            Require(staleRequest.Token.IsCancellationRequested, "Moving between windows did not cancel the old lookup.");
            staleRequest.Completion.SetResult([leaf]); PumpUntil(() => stale.IsCompleted);
            Require(Suggest() == Expected(second.ClientBounds!.Value), "A late result changed a different window's selection.");
            Reply(current, secondLeaf);
            Require(Suggest() == Expected(secondLeaf.Bounds), "The current window's result was lost after a stale response.");

            Move(-600, 270); Suggest(); var empty = Query(); Reply(empty);
            Require(Suggest() == Expected(first.ClientBounds.Value), "Unsupported controls must fall back to window content.");
            Move(-599, 270); var timeout = Query();
            PumpUntil(() => timeout.IsCompleted); timeout.GetAwaiter().GetResult();
            Require(Suggest() == Expected(first.ClientBounds.Value), "A stalled detector prevented window fallback.");
            Move(-598, 270); var canceled = Query(); var canceledRequest = detector.Queries[^1];
            picker.SelectedIndex = 0; canceledRequest.Completion.SetResult([panel]); PumpUntil(() => canceled.IsCompleted);
            Require(Field<Rect>(overlay, "_selection").IsEmpty, "A response changed selection after exiting element mode.");

            picker.SelectedIndex = 1; timer.Stop(); Move(-210, 104); Suggest(); var ready = Query(); Reply(ready, secondLeaf);
            Move(-208, 104); var afterLock = Query(); var lockRequest = detector.Queries[^1];
            typeof(SelectionWindow).GetField("_selection", PrivateInstance)!.SetValue(overlay, Suggest());
            Invoke(overlay, "BeginInlineEditing");
            Require(lockRequest.Token.IsCancellationRequested, "Locking the selection did not cancel detection.");
            lockRequest.Completion.SetResult([panel]); PumpUntil(() => afterLock.IsCompleted);
            var editor = Field<SnapSharp.ViewModels.EditorViewModel>(overlay, "_editor");
            Require(editor.Image.PixelWidth == secondLeaf.Bounds.Width && editor.Image.PixelHeight == secondLeaf.Bounds.Height,
                "Element export includes content outside the selected control.");
            var actual = new byte[secondLeaf.Bounds.Width * secondLeaf.Bounds.Height * 4];
            var expectedPixels = new byte[actual.Length];
            editor.Image.CopyPixels(actual, secondLeaf.Bounds.Width * 4, 0);
            image.CopyPixels(new Int32Rect(secondLeaf.Bounds.X + 800, secondLeaf.Bounds.Y, secondLeaf.Bounds.Width, secondLeaf.Bounds.Height),
                expectedPixels, secondLeaf.Bounds.Width * 4, 0);
            Require(actual.SequenceEqual(expectedPixels), "The element crop differs from the original snapshot or contains overlay pixels.");
            Require(!timer.IsEnabled, "Element polling continued while editing.");
            Invoke(overlay, "RestartRegionSelection");
            Require(picker.SelectedIndex == 0 && !timer.IsEnabled, "Reselecting did not restore ordinary region selection.");
            picker.SelectedIndex = 1; timer.Stop(); Move(-660, 104); Suggest();
            var afterClose = Query(); var closeRequest = detector.Queries[^1];
            overlay.Close();
            Require(closeRequest.Token.IsCancellationRequested, "Closing the overlay did not cancel detection.");
            closeRequest.Completion.SetResult([leaf]); PumpUntil(() => afterClose.IsCompleted);
            Require(!timer.IsEnabled && !overlay.IsVisible, "A late result restarted a closed capture overlay.");
        }
        finally { overlay.Close(); }
        Console.WriteLine("PASS: element mode/Tab, hierarchy, duplicate filtering, negative coordinates, stale/canceled/slow detection, fallback, pixel-exact element crop.");
    }

    private sealed record ElementFixture(long Handle, Rectangle Button, Rectangle Input, long ButtonHandle);

    private static void NativeElementChecks()
    {
        foreach (var framework in new[] { "wpf", "winforms" })
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing test executable path.");
            var start = new ProcessStartInfo(executable)
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardInput = true, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(typeof(Program).Assembly.Location);
            start.ArgumentList.Add("--element-host"); start.ArgumentList.Add(framework);
            using var host = Process.Start(start)!;
            SelectionWindow? overlay = null;
            try
            {
                var ready = host.StandardOutput.ReadLineAsync(); PumpUntil(() => ready.IsCompleted);
                var fixture = JsonSerializer.Deserialize<ElementFixture>(ready.Result ?? throw new InvalidOperationException("Element fixture did not start."))!;
                var candidate = new ScreenCaptureService().EnumerateTopLevelWindows().Single(w => w.Handle == (nint)fixture.Handle);
                var screen = System.Windows.Forms.Screen.FromHandle((nint)fixture.Handle).Bounds;
                var image = ElementTestImage(screen.Width, screen.Height);
                overlay = new SelectionWindow(new CapturedScreen(screen, image), [candidate], new CandidateCapture([candidate]));
                overlay.Show(); overlay.UpdateLayout();
                var point = new System.Drawing.Point(fixture.Button.Left + fixture.Button.Width / 2, fixture.Button.Top + fixture.Button.Height / 2);
                if (framework == "winforms")
                {
                    var (native, seed) = ElementDetectionService.FindNativeChildren(candidate, point);
                    Require(seed == (nint)fixture.ButtonHandle && native.Any(item => item.Bounds == fixture.Button),
                        "Native child-window hit testing failed for a disabled control beneath the overlay.");
                }
                var detector = new ElementDetectionService();
                IReadOnlyList<ElementCandidate> found = [];
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    var query = detector.FindElementsAsync(candidate, point, CancellationToken.None);
                    PumpUntil(() => query.IsCompleted); found = query.GetAwaiter().GetResult();
                    if (found.Any(item => item.Name == "Fixture button")) break;
                }
                Require(found.Any(item => item.Bounds == fixture.Button && item.Name == "Fixture button"),
                    $"{framework}: UI Automation failed to identify the button beneath our overlay: {JsonSerializer.Serialize(found)}");
                var inputPoint = new System.Drawing.Point(fixture.Input.Left + fixture.Input.Width / 2, fixture.Input.Top + fixture.Input.Height / 2);
                var inputQuery = detector.FindElementsAsync(candidate, inputPoint, CancellationToken.None);
                PumpUntil(() => inputQuery.IsCompleted);
                Require(inputQuery.Result.Any(item => item.Bounds.Contains(inputPoint) && item.Bounds.Width <= fixture.Input.Width + 2 &&
                    item.Bounds.Height <= fixture.Input.Height + 2), $"{framework}: input-box detection selected a containing window.");
                // Geometry from a moved window must never crop an old desktop snapshot.
                Require(detector.FindElementsAsync(candidate with { Bounds = new Rectangle(0, 0, 1, 1) }, point, CancellationToken.None)
                    .GetAwaiter().GetResult().Count == 0, "Detection accepted stale window geometry.");
            }
            finally
            {
                overlay?.Close();
                if (!host.HasExited)
                {
                    host.StandardInput.WriteLine("close"); host.StandardInput.Flush();
                    if (!host.WaitForExit(3000)) host.Kill(entireProcessTree: true);
                }
            }
        }
        Console.WriteLine("PASS: real cross-process WPF/UIA and disabled Win32 controls under a capture overlay, input boxes, stale window geometry.");
    }

    private static int RunElementHost(string framework)
    {
        if (framework == "winforms")
        {
            using var form = new System.Windows.Forms.Form
            { Text = "NimCap Screenshot element fixture", ShowInTaskbar = false, StartPosition = System.Windows.Forms.FormStartPosition.Manual,
                Bounds = new Rectangle(60, 150, 500, 350) };
            var panel = new System.Windows.Forms.Panel { Bounds = new Rectangle(20, 20, 400, 200) };
            var button = new System.Windows.Forms.Button { Text = "Fixture button", AccessibleName = "Fixture button", Bounds = new Rectangle(20, 20, 200, 40), Enabled = false };
            var input = new System.Windows.Forms.TextBox { AccessibleName = "Fixture input", Bounds = new Rectangle(20, 90, 240, 30) };
            panel.Controls.Add(button); panel.Controls.Add(input); form.Controls.Add(panel);
            form.Shown += (_, _) =>
            {
                Console.WriteLine(JsonSerializer.Serialize(new ElementFixture(form.Handle.ToInt64(), HostBounds(button.Handle), HostBounds(input.Handle), button.Handle.ToInt64())));
                Console.Out.Flush();
            };
            _ = Task.Run(() => { Console.ReadLine(); if (form.IsHandleCreated) form.BeginInvoke(form.Close); });
            System.Windows.Forms.Application.Run(form);
        }
        else
        {
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var panel = new StackPanel { Margin = new Thickness(20), HorizontalAlignment = HorizontalAlignment.Left };
            var button = new Button { Content = "Fixture button", Width = 200, Height = 40, Margin = new Thickness(0, 0, 0, 30) };
            var input = new TextBox { Width = 240, Height = 30, Text = "Fixture input" };
            AutomationProperties.SetName(button, "Fixture button"); AutomationProperties.SetName(input, "Fixture input");
            panel.Children.Add(button); panel.Children.Add(input);
            var window = new Window { Title = "NimCap Screenshot element fixture", ShowInTaskbar = false, ShowActivated = false,
                Left = 60, Top = 150, Width = 500, Height = 350, Content = panel };
            window.ContentRendered += (_, _) =>
            {
                Rectangle Bounds(FrameworkElement element)
                {
                    var a = element.PointToScreen(new Point(0, 0)); var b = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
                    return Rectangle.FromLTRB((int)Math.Floor(a.X), (int)Math.Floor(a.Y), (int)Math.Ceiling(b.X), (int)Math.Ceiling(b.Y));
                }
                Console.WriteLine(JsonSerializer.Serialize(new ElementFixture(new WindowInteropHelper(window).Handle.ToInt64(), Bounds(button), Bounds(input), 0)));
                Console.Out.Flush();
            };
            _ = Task.Run(() => { Console.ReadLine(); app.Dispatcher.BeginInvoke(app.Shutdown); });
            app.Run(window);
        }
        return 0;
    }

    private static void BrowserElementChecks()
    {
        var browser = new[]
        {
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
        }.FirstOrDefault(File.Exists);
        if (browser is null) { Console.WriteLine("SKIP: no Chromium browser installed for the optional accessibility check."); return; }
        var page = Path.Combine(Root, "browser-element-fixture.html");
        File.WriteAllText(page, """
            <!doctype html><html><head><meta charset="utf-8"><title>SnapSharp browser element fixture</title>
            <style>body{margin:32px;font:16px sans-serif}button{width:220px;height:48px}input{display:block;margin-top:32px;width:240px;height:32px}</style>
            </head><body><button aria-label="Browser fixture button">Browser fixture button</button>
            <input aria-label="Browser fixture input" value="Local test only"></body></html>
            """);
        // This is the visible capture target; there is no console or background helper window.
        var start = new ProcessStartInfo(browser) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--user-data-dir=" + Path.Combine(Root, "browser-profile"));
        foreach (var option in new[] { "--no-first-run", "--no-default-browser-check", "--disable-background-networking", "--disable-sync", "--disable-extensions", "--window-position=60,150", "--window-size=600,500" })
            start.ArgumentList.Add(option);
        start.ArgumentList.Add("--app=" + new Uri(page).AbsoluteUri);
        using var host = Process.Start(start)!;
        SelectionWindow? overlay = null;
        try
        {
            WindowCandidate? candidate = null;
            PumpUntil(() =>
            {
                if (host.HasExited) throw new InvalidOperationException($"Browser fixture exited before creating its window (exit {host.ExitCode}).");
                candidate = new ScreenCaptureService().EnumerateTopLevelWindows().FirstOrDefault(window =>
                {
                    SnapSharp.Native.NativeMethods.GetWindowThreadProcessId(window.Handle, out var processId);
                    return processId == host.Id;
                });
                return candidate is not null;
            });
            // Obtain the fixture position through accessibility, never through a debugging port or extension.
            var locate = Task.Run(async () =>
            {
                var root = AutomationElement.FromHandle(candidate!.Handle);
                var clock = Stopwatch.StartNew();
                while (clock.Elapsed < TimeSpan.FromSeconds(8))
                {
                    var button = root.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.NameProperty, "Browser fixture button"));
                    var input = root.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.NameProperty, "Browser fixture input"));
                    if (button is not null && input is not null)
                        return (Button: (Rect?)button.Current.BoundingRectangle, Input: (Rect?)input.Current.BoundingRectangle);
                    // Chromium builds its accessibility tree asynchronously after the first client attaches.
                    await Task.Delay(150).ConfigureAwait(false);
                }
                var names = root.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition)
                    .Cast<AutomationElement>().Take(40).Select(item => item.Current.Name);
                throw new InvalidOperationException("Browser fixture accessibility tree: " + string.Join(" | ", names));
            }).WaitAsync(TimeSpan.FromSeconds(10));
            PumpUntil(() => locate.IsCompleted);
            var bounds = locate.GetAwaiter().GetResult();
            Require(bounds.Button is { IsEmpty: false } && bounds.Input is { IsEmpty: false }, "The browser did not expose the local fixture through accessibility.");
            var screen = System.Windows.Forms.Screen.FromHandle(candidate!.Handle).Bounds;
            overlay = new SelectionWindow(new CapturedScreen(screen, ElementTestImage(screen.Width, screen.Height)), [candidate], new CandidateCapture([candidate]));
            overlay.Show(); overlay.UpdateLayout();
            var detector = new ElementDetectionService();
            foreach (var (rect, name) in new[] { (bounds.Button!.Value, "Browser fixture button"), (bounds.Input!.Value, "Browser fixture input") })
            {
                var point = new System.Drawing.Point((int)(rect.Left + rect.Width / 2), (int)(rect.Top + rect.Height / 2));
                IReadOnlyList<ElementCandidate> found = [];
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    var query = detector.FindElementsAsync(candidate, point, CancellationToken.None);
                    PumpUntil(() => query.IsCompleted); found = query.GetAwaiter().GetResult();
                    if (found.Any(item => item.Name == name)) break;
                }
                Require(found.Any(item => item.Name == name && item.Bounds.Width <= Math.Ceiling(rect.Width) + 1 && item.Bounds.Height <= Math.Ceiling(rect.Height) + 1),
                    $"Browser element detection failed beneath the overlay: {name}: {JsonSerializer.Serialize(found)}");
            }
            Console.WriteLine("PASS: real Chromium page button/input detection under a capture overlay, with extensions disabled and no remote debugging.");
        }
        finally
        {
            overlay?.Close();
            if (!host.HasExited)
            {
                host.CloseMainWindow();
                if (!host.WaitForExit(3000)) host.Kill(entireProcessTree: true);
            }
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetHostWindowRect(nint hwnd, out HostRect rect);
    [StructLayout(LayoutKind.Sequential)]
    private struct HostRect { public int Left, Top, Right, Bottom; }
    private static Rectangle HostBounds(nint handle)
    {
        GetHostWindowRect(handle, out var bounds);
        return Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
    }
}
