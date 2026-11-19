using NimCap.Screenshot.Models;
using SkiaSharp;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Windows.Graphics.Capture;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace NimCap.Screenshot.Services;

public sealed class WgcCaptureService : ICaptureService
{
    private static readonly Guid CaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
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

    public CapturedScreen CaptureVirtualScreen() => CaptureVirtualScreen(CancellationToken.None);

    public CapturedScreen CaptureVirtualScreen(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!GraphicsCaptureSession.IsSupported()) throw new PlatformNotSupportedException("Windows Graphics Capture is unavailable.");
        var captures = new List<CapturedScreen>();
        foreach (var monitor in DisplayMonitorService.Enumerate())
        {
            token.ThrowIfCancellationRequested();
            captures.Add(new CapturedScreen(monitor.Bounds, CaptureItem(CreateItemForMonitor(monitor.Handle), token)));
        }
        token.ThrowIfCancellationRequested();
        return DesktopCaptureComposer.Compose(captures);
    }

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

    public CapturedScreen? CaptureWindow(nint windowHandle) => CaptureWindow(windowHandle, CancellationToken.None);

    public CapturedScreen? CaptureWindow(nint windowHandle, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!ScreenCaptureService.TryGetWindowBounds(windowHandle, out var bounds)) return null;
        try { return new CapturedScreen(bounds, CaptureItem(CreateItemForWindow(windowHandle), token)); }
        catch (Exception error) when (CaptureFailure.IsBackendFailure(error) && !token.IsCancellationRequested && !ScreenCaptureService.TryGetWindowBounds(windowHandle, out _))
        { return null; } // A target that closed is not a device-health failure.
    }

    public IReadOnlyList<WindowCandidate> EnumerateTopLevelWindows() => _desktopInterop.EnumerateTopLevelWindows();
    public WindowCandidate? FindWindowAt(System.Drawing.Point screenPoint) => _desktopInterop.FindWindowAt(screenPoint);

    private BitmapSource CaptureItem(GraphicsCaptureItem item, CancellationToken token = default)
    {
        using var session = new WgcFrameSource(item, _includeCursor);
        return session.Capture(token);
    }

    internal static BitmapSource SoftwareBitmapToImage(SoftwareBitmap source)
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

    internal static void ReleaseWinRtObject(object? value)
    {
        if (value is IDisposable disposable)
        {
            disposable.Dispose();
            return;
        }
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    internal static GraphicsCaptureItem CreateItemForMonitor(nint monitor) => CreateItem(interop =>
    {
        var result = interop.CreateForMonitor(monitor, CaptureItemGuid, out var item);
        return (result, item);
    });

    private static GraphicsCaptureItem CreateItemForWindow(nint window) => CreateItem(interop =>
    {
        var result = interop.CreateForWindow(window, CaptureItemGuid, out var item);
        return (result, item);
    });

    private static GraphicsCaptureItem CreateItem(Func<IGraphicsCaptureItemInterop, (int Result, nint Pointer)> create)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var (result, pointer) = create(interop);
        try
        {
            Marshal.ThrowExceptionForHR(result);
            return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(pointer);
        }
        finally { if (pointer != 0) Marshal.Release(pointer); }
    }

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig] int CreateForWindow(nint window, in Guid iid, out nint result);
        [PreserveSig] int CreateForMonitor(nint monitor, in Guid iid, out nint result);
    }
}
