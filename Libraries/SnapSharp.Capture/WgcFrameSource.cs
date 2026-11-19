using System.Diagnostics;
using System.Windows.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;

namespace SnapSharp.Services;

// A single consumer owns Capture/Dispose. WinRT callbacks only publish owned frames.
internal sealed class WgcFrameSource : IDisposable
{
    private readonly object _gate = new();
    private readonly CaptureCallbackLifetime _callbacks = new();
    private readonly GraphicsCaptureItem _item;
    private WgcDeviceManager.Lease? _device;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private Direct3D11CaptureFrame? _pending;
    private Exception? _error;
    private bool _closed;
    private BitmapSource? _lastImage;
    private readonly TypedEventHandler<Direct3D11CaptureFramePool, object> _handler;

    internal WgcFrameSource(GraphicsCaptureItem item, bool includeCursor)
    {
        _item = item;
        _handler = OnFrame;
        try
        {
            _device = WgcDeviceManager.Acquire();
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device.Device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
            _session = _pool.CreateCaptureSession(item);
            _session.IsCursorCaptureEnabled = includeCursor;
            _pool.FrameArrived += _handler;
            _session.StartCapture();
        }
        catch
        {
            _device?.Invalidate();
            Dispose();
            throw;
        }
    }

    private void OnFrame(Direct3D11CaptureFramePool sender, object args)
    {
        if (!_callbacks.TryEnter()) return;
        Direct3D11CaptureFrame? next = null, previous = null;
        try
        {
            next = sender.TryGetNextFrame();
            if (next is null) return;
            lock (_gate)
            {
                if (!_closed)
                {
                    previous = _pending;
                    _pending = next;
                    next = null;
                    Monitor.PulseAll(_gate);
                }
            }
        }
        catch (Exception error)
        {
            lock (_gate) { _error = error; Monitor.PulseAll(_gate); }
        }
        finally
        {
            // Disposal failures are delivered to the consumer, never thrown on a WinRT thread.
            try
            {
                try { WgcCaptureService.ReleaseWinRtObject(previous); }
                finally { WgcCaptureService.ReleaseWinRtObject(next); }
            }
            catch (Exception error) { lock (_gate) { _error = error; Monitor.PulseAll(_gate); } }
            finally { _callbacks.Exit(); }
        }
    }

    internal BitmapSource Capture(CancellationToken token)
    {
        Direct3D11CaptureFrame frame;
        var elapsed = Stopwatch.StartNew();
        try
        {
            token.ThrowIfCancellationRequested();
            _device!.CheckHealth();
            lock (_gate)
            {
                while (_pending is null)
                {
                    token.ThrowIfCancellationRequested();
                    ObjectDisposedException.ThrowIf(_closed, this);
                    if (_error is not null) throw new InvalidOperationException("WGC frame callback failed.", _error);
                    // WGC need not send another frame for an unchanged desktop.
                    if (_lastImage is not null && elapsed.ElapsedMilliseconds >= 40) return _lastImage;
                    if (elapsed.ElapsedMilliseconds >= 2000) throw new TimeoutException("WGC did not produce a frame.");
                    Monitor.Wait(_gate, 20);
                }
                token.ThrowIfCancellationRequested();
                if (_error is not null) throw new InvalidOperationException("WGC frame callback failed.", _error);
                frame = _pending;
                _pending = null;
            }
            try
            {
                if (frame.ContentSize.Width != _item.Size.Width || frame.ContentSize.Height != _item.Size.Height)
                    throw new InvalidOperationException("Capture dimensions changed during the session.");
                var surface = frame.Surface;
                try
                {
                    var software = SoftwareBitmap.CreateCopyFromSurfaceAsync(surface, BitmapAlphaMode.Ignore)
                        .AsTask().GetAwaiter().GetResult();
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        return _lastImage = WgcCaptureService.SoftwareBitmapToImage(software);
                    }
                    finally { WgcCaptureService.ReleaseWinRtObject(software); }
                }
                finally { WgcCaptureService.ReleaseWinRtObject(surface); }
            }
            finally { WgcCaptureService.ReleaseWinRtObject(frame); }
        }
        catch (OperationCanceledException) { throw; }
        catch { _device?.Invalidate(); throw; }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_closed) return;
            _closed = true;
            Monitor.PulseAll(_gate);
        }
        try { _callbacks.Close(() => { if (_pool is not null) _pool.FrameArrived -= _handler; }); }
        finally
        {
            Direct3D11CaptureFrame? pending;
            lock (_gate)
            {
                pending = _pending;
                _pending = null;
            }
            try { WgcCaptureService.ReleaseWinRtObject(pending); }
            finally
            {
                try { WgcCaptureService.ReleaseWinRtObject(_session); }
                finally
                {
                    try { WgcCaptureService.ReleaseWinRtObject(_pool); }
                    finally
                    {
                        try { WgcCaptureService.ReleaseWinRtObject(_item); }
                        finally { _device?.Dispose(); }
                    }
                }
            }
        }
    }
}
