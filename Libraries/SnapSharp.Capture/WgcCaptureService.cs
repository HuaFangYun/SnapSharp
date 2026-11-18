using SnapSharp.Models;
using SkiaSharp;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Foundation;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;
using static Vortice.Direct3D11.D3D11;

namespace SnapSharp.Services;

public sealed class WgcCaptureService : ICaptureService
{
    private static readonly Guid CaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Lazy<CaptureDevice> SharedDevice = new(CreateCaptureDevice,
        LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly ScreenCaptureService _desktopInterop = new();
    private readonly SkiaImageService _images = new();
    private readonly bool _includeCursor;

    public WgcCaptureService(bool includeCursor = false) => _includeCursor = includeCursor;

    public IReadOnlyList<CapturedScreen> CaptureAllScreens()
    {
        if (!GraphicsCaptureSession.IsSupported()) throw new PlatformNotSupportedException("Windows Graphics Capture is unavailable.");
        return DisplayMonitorService.Enumerate()
            .Select(monitor => new CapturedScreen(monitor.Bounds,
                CaptureItem(CreateItemForMonitor(monitor.Handle))))
            .ToArray();
    }

    public CapturedScreen CaptureVirtualScreen() => DesktopCaptureComposer.Compose(CaptureAllScreens());

    public BitmapSource CaptureRegion(Rectangle bounds)
    {
        var desktop = CaptureVirtualScreen();
        using var source = _images.ToSkBitmap(desktop.Image);
        var subset = new SKRectI(bounds.Left - desktop.Bounds.Left, bounds.Top - desktop.Bounds.Top,
            bounds.Right - desktop.Bounds.Left, bounds.Bottom - desktop.Bounds.Top);
        using var cropped = new SKBitmap(subset.Width, subset.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(cropped);
        canvas.DrawBitmap(source, subset, SKRect.Create(subset.Width, subset.Height));
        return _images.ToBitmapSource(cropped);
    }

    public CapturedScreen? CaptureWindow(nint windowHandle)
    {
        if (!ScreenCaptureService.TryGetWindowBounds(windowHandle, out var bounds)) return null;
        return new CapturedScreen(bounds, CaptureItem(CreateItemForWindow(windowHandle)));
    }

    public IReadOnlyList<WindowCandidate> EnumerateTopLevelWindows() => _desktopInterop.EnumerateTopLevelWindows();
    public WindowCandidate? FindWindowAt(System.Drawing.Point screenPoint) => _desktopInterop.FindWindowAt(screenPoint);

    private BitmapSource CaptureItem(GraphicsCaptureItem item)
    {
        Direct3D11CaptureFramePool? pool = null;
        GraphicsCaptureSession? session = null;
        Direct3D11CaptureFrame? frame = null;
        TypedEventHandler<Direct3D11CaptureFramePool, object>? frameArrived = null;
        try
        {
            var size = item.Size;
            pool = Direct3D11CaptureFramePool.CreateFreeThreaded(SharedDevice.Value.WinRtDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, size);
            session = pool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = _includeCursor;
            using var signal = new ManualResetEventSlim();
            frameArrived = (sender, _) =>
            {
                var next = sender.TryGetNextFrame();
                if (Interlocked.CompareExchange(ref frame, next, null) is not null)
                    ReleaseWinRtObject(next);
                else
                    signal.Set();
            };
            pool.FrameArrived += frameArrived;
            session.StartCapture();
            if (!signal.Wait(TimeSpan.FromSeconds(2)) || frame is null)
                throw new TimeoutException("Windows Graphics Capture did not produce a frame.");

            var surface = frame.Surface;
            try
            {
                var software = SoftwareBitmap.CreateCopyFromSurfaceAsync(surface, BitmapAlphaMode.Ignore)
                    .AsTask().GetAwaiter().GetResult();
                try { return SoftwareBitmapToImage(software); }
                finally { ReleaseWinRtObject(software); }
            }
            finally
            {
                ReleaseWinRtObject(surface);
            }
        }
        finally
        {
            if (pool is not null && frameArrived is not null) pool.FrameArrived -= frameArrived;
            ReleaseWinRtObject(frame);
            ReleaseWinRtObject(session);
            ReleaseWinRtObject(pool);
            ReleaseWinRtObject(item);
        }
    }

    private static CaptureDevice CreateCaptureDevice()
    {
        var device = D3D11CreateDevice(DriverType.Hardware,
            DeviceCreationFlags.BgraSupport, FeatureLevel.Level_11_1, FeatureLevel.Level_11_0);
        using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(NativeCreateDirect3D11DeviceFromDXGIDevice(
            dxgiDevice.NativePointer, out var pointer));
        try
        {
            var winRtDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(pointer);
            return new CaptureDevice(device, winRtDevice);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    private static BitmapSource SoftwareBitmapToImage(SoftwareBitmap source)
    {
        var converted = source.BitmapPixelFormat == BitmapPixelFormat.Bgra8
            ? SoftwareBitmap.Copy(source)
            : SoftwareBitmap.Convert(source, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
        try
        {
            var stride = checked(converted.PixelWidth * 4);
            var buffer = new Windows.Storage.Streams.Buffer((uint)checked(stride * converted.PixelHeight));
            byte[] pixels;
            try
            {
                converted.CopyToBuffer(buffer);
                CryptographicBuffer.CopyToByteArray(buffer, out pixels);
            }
            finally
            {
                ReleaseWinRtObject(buffer);
            }
            for (var index = 3; index < pixels.Length; index += 4) pixels[index] = byte.MaxValue;
            var image = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null, pixels, stride);
            image.Freeze();
            return image;
        }
        finally
        {
            ReleaseWinRtObject(converted);
        }
    }

    private static void ReleaseWinRtObject(object? value)
    {
        if (value is IDisposable disposable)
        {
            disposable.Dispose();
            return;
        }
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    private static GraphicsCaptureItem CreateItemForMonitor(nint monitor) => CreateItem(interop =>
        interop.CreateForMonitor(monitor, CaptureItemGuid, out var item).ThrowOnFailure(item));

    private static GraphicsCaptureItem CreateItemForWindow(nint window) => CreateItem(interop =>
        interop.CreateForWindow(window, CaptureItemGuid, out var item).ThrowOnFailure(item));

    private static GraphicsCaptureItem CreateItem(Func<IGraphicsCaptureItemInterop, nint> create)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var pointer = create(interop);
        try { return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
    }

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig] int CreateForWindow(nint window, in Guid iid, out nint result);
        [PreserveSig] int CreateForMonitor(nint monitor, in Guid iid, out nint result);
    }

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", ExactSpelling = true)]
    private static extern int NativeCreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    private sealed record CaptureDevice(ID3D11Device Device, IDirect3DDevice WinRtDevice);
}

internal static class HResultExtensions
{
    internal static nint ThrowOnFailure(this int result, nint value)
    {
        Marshal.ThrowExceptionForHR(result);
        return value;
    }
}
