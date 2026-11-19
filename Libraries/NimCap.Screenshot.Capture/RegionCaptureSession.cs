using System.Drawing;
using System.Windows;
using System.Windows.Media.Imaging;

namespace NimCap.Screenshot.Services;

internal interface IRegionFrameSource : IDisposable
{
    BitmapSource Capture(CancellationToken token);
}

// Construct cheaply on the UI thread; initialize/use/dispose on the capture worker.
public sealed class RegionCaptureSession : IDisposable
{
    private readonly Rectangle _region;
    private readonly string[] _backends;
    private readonly Func<string, Rectangle, IRegionFrameSource> _open;
    private readonly List<Exception> _failures = [];
    private int _index;
    private IRegionFrameSource? _source;
    private bool _disposed;
    public string Backend => _backends[_index];
    public string? FallbackReason { get; private set; }

    public RegionCaptureSession(Rectangle region, string backend) : this(region, backend, OpenSource) { }

    internal RegionCaptureSession(Rectangle region, string backend, Func<string, Rectangle, IRegionFrameSource> open)
    {
        if (region.Width <= 0 || region.Height <= 0) throw new ArgumentOutOfRangeException(nameof(region));
        _region = region;
        _open = open;
        _backends = backend switch
        {
            "Auto" => ["WGC", "DXGI", "GDI"],
            "WGC" => ["WGC", "GDI"],
            "DXGI" => ["DXGI", "GDI"],
            "GDI" => ["GDI"],
            _ => throw new ArgumentException("Unknown scrolling capture backend.", nameof(backend))
        };
    }

    public BitmapSource Capture(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                _source ??= _open(Backend, _region);
                var image = _source.Capture(token);
                token.ThrowIfCancellationRequested();
                if (image.PixelWidth != _region.Width || image.PixelHeight != _region.Height)
                    throw new InvalidOperationException("Capture dimensions changed during the session.");
                return image;
            }
            catch (Exception error) when (CaptureFailure.IsBackendFailure(error))
            {
                token.ThrowIfCancellationRequested();
                if (_index + 1 == _backends.Length)
                {
                    if (_failures.Count == 0) throw;
                    throw new AggregateException("All selected capture backends failed.", _failures.Append(error));
                }
                _failures.Add(new InvalidOperationException(Backend + " capture failed.", error));
                var previous = _source;
                _source = null;
                try { previous?.Dispose(); }
                catch (Exception cleanup) when (CaptureFailure.IsBackendFailure(cleanup))
                { _failures.Add(new InvalidOperationException(Backend + " cleanup failed.", cleanup)); }
                FallbackReason = error.GetType().Name;
                _index++;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var owned = _source;
        _source = null;
        owned?.Dispose();
    }

    private static IRegionFrameSource OpenSource(string backend, Rectangle region) => backend switch
    {
        "WGC" => new WgcRegionSource(region),
        "DXGI" => DxgiCaptureService.OpenRegion(region),
        _ => new GdiSource(region)
    };

    private sealed class GdiSource(Rectangle region) : IRegionFrameSource
    {
        private readonly ScreenCaptureService _capture = new(false);
        public BitmapSource Capture(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return _capture.CaptureRegion(region); }
        public void Dispose() { }
    }

    private sealed class WgcRegionSource : IRegionFrameSource
    {
        private readonly Rectangle _region;
        private readonly List<(DisplayMonitorService.Monitor Monitor, WgcFrameSource Source)> _sources = [];
        internal WgcRegionSource(Rectangle region)
        {
            _region = region;
            try
            {
                foreach (var monitor in DisplayMonitorService.Enumerate().Where(m => m.Bounds.IntersectsWith(region)))
                    _sources.Add((monitor, new WgcFrameSource(WgcCaptureService.CreateItemForMonitor(monitor.Handle), false)));
                if (_sources.Count == 0) throw new InvalidOperationException("Capture region has no active monitor.");
            }
            catch { Dispose(); throw; }
        }
        public BitmapSource Capture(CancellationToken token)
        {
            var monitors = DisplayMonitorService.Enumerate();
            foreach (var source in _sources)
                if (!monitors.Any(m => m.Handle == source.Monitor.Handle && m.Bounds == source.Monitor.Bounds))
                    throw new InvalidOperationException("Capture monitor layout changed.");
            var captures = _sources.Select(s => new NimCap.Screenshot.Models.CapturedScreen(s.Monitor.Bounds, s.Source.Capture(token))).ToArray();
            return CropDesktop(DesktopCaptureComposer.Compose(captures), _region);
        }
        public void Dispose()
        {
            // Attempt every release even if one source fails to close.
            Exception? failure = null;
            foreach (var source in _sources)
                try { source.Source.Dispose(); } catch (Exception error) { failure ??= error; }
            _sources.Clear();
            if (failure is not null) throw failure;
        }
    }

    internal static BitmapSource CropDesktop(NimCap.Screenshot.Models.CapturedScreen desktop, Rectangle region)
    {
        if (!desktop.Bounds.Contains(region)) throw new InvalidOperationException("Capture region is outside the active desktop.");
        var result = new CroppedBitmap(desktop.Image, new Int32Rect(region.Left - desktop.Bounds.Left,
            region.Top - desktop.Bounds.Top, region.Width, region.Height));
        result.Freeze();
        return result;
    }
}
