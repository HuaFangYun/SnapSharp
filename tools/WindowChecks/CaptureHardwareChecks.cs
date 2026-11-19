using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using NimCap.Screenshot.Services;
using Application = System.Windows.Application;
using Point = System.Windows.Point;
using Rectangle = System.Drawing.Rectangle;

namespace NimCap.Screenshot.WindowChecks;

internal static partial class Program
{
    private static void CaptureHardwareChecks(string[] args)
    {
        var requested = args.SkipWhile(a => a != "--backend").Skip(1).FirstOrDefault();
        if (requested is not (null or "WGC" or "DXGI" or "GDI")) throw new ArgumentException("--backend must be WGC, DXGI or GDI.");
        var frame = new DispatcherFrame();
        Exception? failure = null;
        Application.Current.Dispatcher.BeginInvoke(async () =>
        {
            var fixture = new Window
            {
                Style = new Style(typeof(Window)), WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize, Width = 200, Height = 160,
                Left = 100, Top = 100, Topmost = true, ShowInTaskbar = false,
                Background = System.Windows.Media.Brushes.Lime
            };
            try
            {
                fixture.Show();
                await Task.Delay(300);
                var origin = fixture.PointToScreen(new Point(60, 60));
                var region = new Rectangle((int)origin.X, (int)origin.Y, 32, 32);
                foreach (var backend in requested is null ? new[] { "WGC", "DXGI", "GDI" } : [requested])
                {
                    fixture.Background = System.Windows.Media.Brushes.Lime;
                    await Task.Delay(150);
                    await Task.Run(async () =>
                    {
                        using var session = new RegionCaptureSession(region, backend);
                        void Expect(bool blue)
                        {
                            var deadline = System.Diagnostics.Stopwatch.StartNew();
                            while (true)
                            {
                                var image = session.Capture();
                                Require(session.Backend == backend, backend + " failed and selected " + session.Backend + ": " + session.FallbackReason);
                                Require(image.IsFrozen && image.PixelWidth == 32 && image.PixelHeight == 32, "Hardware capture did not preserve physical dimensions.");
                                var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
                                var pixel = new byte[4]; converted.CopyPixels(new Int32Rect(16, 16, 1, 1), pixel, 4, 0);
                                if (blue ? pixel[0] > 220 && pixel[1] < 30 : pixel[1] > 220 && pixel[0] < 30) return;
                                if (deadline.ElapsedMilliseconds >= 2000) throw new InvalidOperationException(backend + " did not observe the fixture's new frame.");
                            }
                        }
                        Expect(false);
                        for (var i = 0; i < 4; i++)
                        {
                            var blue = i % 2 == 0;
                            await fixture.Dispatcher.InvokeAsync(() => fixture.Background = blue ? System.Windows.Media.Brushes.Blue : System.Windows.Media.Brushes.Lime);
                            await Task.Delay(100);
                            Expect(blue);
                        }
                    });
                    Console.WriteLine("PASS: live " + backend + " session observes changing fixture pixels without fallback; fixed physical region; disposal.");
                }
                if (requested is null or "WGC") await Task.Run(() =>
                {
                    var monitor = DisplayMonitorService.Enumerate().First(m => m.Bounds.Contains(region));
                    for (var i = 0; i < 8; i++)
                    {
                        using var source = new WgcFrameSource(WgcCaptureService.CreateItemForMonitor(monitor.Handle), false);
                        using var canceled = new CancellationTokenSource(); canceled.Cancel();
                        try { source.Capture(canceled.Token); throw new Exception("Canceled WGC frame was delivered."); }
                        catch (OperationCanceledException) { }
                    }
                });
                if (requested is null or "WGC") Console.WriteLine("PASS: live WGC cancel/close cycles with active native frame callbacks.");
            }
            catch (Exception error) { failure = error; }
            finally { fixture.Close(); frame.Continue = false; }
        });
        Dispatcher.PushFrame(frame);
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
