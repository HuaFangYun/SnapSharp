using System.Diagnostics;
using System.Drawing;
using System.Windows.Media.Imaging;
using NimCap.Screenshot.Models;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.DXGI.DXGI;

namespace NimCap.Screenshot.Services;

public sealed partial class DxgiCaptureService
{
    internal static IRegionFrameSource OpenRegion(Rectangle region) => new RegionSource(region);

    private sealed class RegionSource : IRegionFrameSource
    {
        private readonly Rectangle _region;
        private readonly List<OutputSource> _outputs = [];
        internal RegionSource(Rectangle region)
        {
            _region = region;
            try
            {
                using var factory = CreateDXGIFactory1<IDXGIFactory1>();
                for (uint ai = 0; factory.EnumAdapters1(ai, out var adapter).Success; ai++)
                {
                    using (adapter)
                    {
                        for (uint oi = 0; adapter.EnumOutputs(oi, out var output).Success; oi++)
                        {
                            using (output)
                            {
                                var description = output.Description;
                                var rect = description.DesktopCoordinates;
                                var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                                if (description.AttachedToDesktop && bounds.IntersectsWith(region))
                                    _outputs.Add(new OutputSource(adapter, output, bounds, description.Rotation.ToString()));
                            }
                        }
                    }
                }
                if (_outputs.Count == 0) throw new InvalidOperationException("DXGI region has no active output.");
            }
            catch { Dispose(); throw; }
        }
        public BitmapSource Capture(CancellationToken token)
        {
            var monitors = DisplayMonitorService.EnumerateBounds();
            if (_outputs.Any(o => !monitors.Contains(o.Bounds)))
                throw new InvalidOperationException("Capture monitor layout changed.");
            var screens = _outputs.Select(o => o.Capture(token)).ToArray();
            return RegionCaptureSession.CropDesktop(DesktopCaptureComposer.Compose(screens), _region);
        }
        public void Dispose()
        {
            Exception? failure = null;
            foreach (var output in _outputs)
                try { output.Dispose(); } catch (Exception error) { failure ??= error; }
            _outputs.Clear();
            if (failure is not null) throw failure;
        }
    }

    private sealed class OutputSource : IDisposable
    {
        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;
        private IDXGIOutputDuplication? _duplication;
        private CapturedScreen? _last;
        private readonly string _rotation;
        internal Rectangle Bounds { get; }
        internal OutputSource(IDXGIAdapter1 adapter, IDXGIOutput output, Rectangle bounds, string rotation)
        {
            Bounds = bounds;
            _rotation = rotation;
            try
            {
                CreateDevice(adapter, out var device, out var context);
                _device = device; _context = context;
                using var output1 = output.QueryInterface<IDXGIOutput1>();
                _duplication = output1.DuplicateOutput(device);
            }
            catch { Dispose(); throw; }
        }
        internal CapturedScreen Capture(CancellationToken token)
        {
            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var capture = CaptureOutput(_duplication!, _device!, _context!, Bounds, 40);
                    token.ThrowIfCancellationRequested();
                    _last = _rotation.Contains("Identity", StringComparison.OrdinalIgnoreCase) ||
                        _rotation.Contains("Unspecified", StringComparison.OrdinalIgnoreCase) || _rotation == "0"
                        ? capture : NormalizeRotation(capture, Bounds, _rotation);
                    return _last;
                }
                catch (TimeoutException error)
                {
                    if (_last is not null) return _last; // Static desktop: no new duplication frame.
                    if (elapsed.ElapsedMilliseconds >= 2000) throw new TimeoutException("DXGI did not produce a frame.", error);
                }
            }
        }
        public void Dispose()
        {
            try { _duplication?.Dispose(); }
            finally
            {
                try { _context?.Dispose(); }
                finally { _device?.Dispose(); }
            }
            _duplication = null; _context = null; _device = null;
        }
    }
}
