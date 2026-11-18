using SnapSharp.Models;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace SnapSharp.Services;

public sealed class DxgiCaptureService : ICaptureService
{
    private readonly ScreenCaptureService _windowCapture;

    public DxgiCaptureService(bool includeCursor = false) => _windowCapture = new ScreenCaptureService(includeCursor);

    public IReadOnlyList<CapturedScreen> CaptureAllScreens()
    {
        var captures = new List<CapturedScreen>();
        using var factory = CreateDXGIFactory1<IDXGIFactory1>();
        for (uint adapterIndex = 0; factory.EnumAdapters1(adapterIndex, out var adapter).Success; adapterIndex++)
        {
            using (adapter)
            {
                CreateDevice(adapter, out var device, out var context);
                using (device)
                using (context)
                {
                    for (uint outputIndex = 0; adapter.EnumOutputs(outputIndex, out var output).Success; outputIndex++)
                    {
                        using (output)
                        {
                            var description = output.Description;
                            if (!description.AttachedToDesktop) continue;
                            using var output1 = output.QueryInterface<IDXGIOutput1>();
                            using var duplication = output1.DuplicateOutput(device);
                            var bounds = Rectangle.FromLTRB(description.DesktopCoordinates.Left, description.DesktopCoordinates.Top,
                                description.DesktopCoordinates.Right, description.DesktopCoordinates.Bottom);
                            var capture = CaptureOutput(duplication, device, context, bounds);
                            var rotation = description.Rotation.ToString();
                            captures.Add(rotation.Contains("Identity", StringComparison.OrdinalIgnoreCase) ||
                                         rotation.Contains("Unspecified", StringComparison.OrdinalIgnoreCase) || rotation == "0"
                                ? capture
                                : NormalizeRotation(capture, bounds, rotation));
                        }
                    }
                }
            }
        }
        if (captures.Count == 0) throw new InvalidOperationException("DXGI reported no active outputs.");
        return captures;
    }

    public CapturedScreen CaptureVirtualScreen() => DesktopCaptureComposer.Compose(CaptureAllScreens());

    public BitmapSource CaptureRegion(Rectangle bounds)
    {
        var desktop = CaptureVirtualScreen();
        var relative = new System.Windows.Int32Rect(bounds.Left - desktop.Bounds.Left,
            bounds.Top - desktop.Bounds.Top, bounds.Width, bounds.Height);
        var cropped = new CroppedBitmap(desktop.Image, relative);
        cropped.Freeze();
        return cropped;
    }

    public CapturedScreen? CaptureWindow(nint windowHandle) => _windowCapture.CaptureWindow(windowHandle);
    public IReadOnlyList<WindowCandidate> EnumerateTopLevelWindows() => _windowCapture.EnumerateTopLevelWindows();
    public WindowCandidate? FindWindowAt(System.Drawing.Point screenPoint) => _windowCapture.FindWindowAt(screenPoint);

    private static void CreateDevice(IDXGIAdapter1 adapter, out ID3D11Device device, out ID3D11DeviceContext context)
    {
        FeatureLevel[] levels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1];
        var result = D3D11CreateDeviceForAdapter(adapter.NativePointer, DriverType.Unknown, nint.Zero,
            DeviceCreationFlags.BgraSupport, levels, (uint)levels.Length, SdkVersion,
            out var devicePointer, out _, out var contextPointer);
        Marshal.ThrowExceptionForHR(result);
        device = new ID3D11Device(devicePointer);
        context = new ID3D11DeviceContext(contextPointer);
    }

    [DllImport("d3d11.dll", EntryPoint = "D3D11CreateDevice")]
    private static extern int D3D11CreateDeviceForAdapter(nint adapter, DriverType driverType, nint software,
        DeviceCreationFlags flags, [In] FeatureLevel[] featureLevels, uint featureLevelCount, uint sdkVersion,
        out nint device, out FeatureLevel selectedFeatureLevel, out nint immediateContext);

    private static CapturedScreen CaptureOutput(IDXGIOutputDuplication duplication, ID3D11Device device,
        ID3D11DeviceContext context, Rectangle bounds)
    {
        var acquired = false;
        try
        {
            var result = duplication.AcquireNextFrame(500, out _, out var resource);
            result.CheckError();
            acquired = true;
            using (resource)
            using (var texture = resource.QueryInterface<ID3D11Texture2D>())
            {
                var sourceDescription = texture.Description;
                var stagingDescription = sourceDescription;
                stagingDescription.BindFlags = BindFlags.None;
                stagingDescription.CPUAccessFlags = CpuAccessFlags.Read;
                stagingDescription.Usage = ResourceUsage.Staging;
                stagingDescription.MiscFlags = ResourceOptionFlags.None;
                using var staging = device.CreateTexture2D(stagingDescription);
                context.CopyResource(staging, texture);
                context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out var mapped).CheckError();
                try
                {
                    var width = checked((int)sourceDescription.Width);
                    var height = checked((int)sourceDescription.Height);
                    var stride = checked(width * 4);
                    var pixels = GC.AllocateUninitializedArray<byte>(checked(stride * height));
                    for (var row = 0; row < height; row++)
                        Marshal.Copy(mapped.DataPointer + row * checked((int)mapped.RowPitch), pixels, row * stride, stride);
                    if (!HasVisibleContent(pixels))
                        throw new InvalidOperationException("DXGI returned a blank desktop frame.");
                    // DXGI does not guarantee meaningful desktop alpha. Some drivers
                    // return zero, which WPF interprets as a fully transparent image.
                    MakeOpaque(pixels);
                    var image = BitmapSource.Create(width, height, 96, 96,
                        PixelFormats.Bgra32, null, pixels, stride);
                    image.Freeze();
                    return new CapturedScreen(bounds, image);
                }
                finally
                {
                    context.Unmap(staging, 0);
                }
            }
        }
        finally
        {
            if (acquired) duplication.ReleaseFrame();
        }
    }

    private static void MakeOpaque(Span<byte> pixels)
    {
        for (var index = 3; index < pixels.Length; index += 4) pixels[index] = byte.MaxValue;
    }

    private static bool HasVisibleContent(ReadOnlySpan<byte> pixels)
    {
        var required = Math.Max(16, pixels.Length / 4 / 10_000);
        var visible = 0;
        for (var index = 0; index + 3 < pixels.Length; index += 4)
        {
            if (pixels[index] <= 2 && pixels[index + 1] <= 2 && pixels[index + 2] <= 2) continue;
            if (++visible >= required) return true;
        }
        return false;
    }

    private static CapturedScreen NormalizeRotation(CapturedScreen capture, Rectangle bounds, string rotation)
    {
        var degrees = rotation.Contains("270", StringComparison.OrdinalIgnoreCase) ? 270
            : rotation.Contains("180", StringComparison.OrdinalIgnoreCase) ? 180 : 90;
        var swapsAxes = degrees is 90 or 270;
        var expectedWidth = swapsAxes ? bounds.Height : bounds.Width;
        var expectedHeight = swapsAxes ? bounds.Width : bounds.Height;
        if (capture.Image.PixelWidth != expectedWidth || capture.Image.PixelHeight != expectedHeight)
            throw new InvalidOperationException("DXGI output dimensions do not match the monitor geometry.");

        // Desktop Duplication exposes rotated outputs as an unrotated texture.
        var rotated = new TransformedBitmap(capture.Image, new System.Windows.Media.RotateTransform(degrees));
        rotated.Freeze();
        return new CapturedScreen(bounds, rotated);
    }
}

public sealed class ResilientCaptureService : ICaptureService
{
    private readonly ICaptureService _preferred;
    private readonly ICaptureService _fallback;
    private int _preferredFailed;

    public ResilientCaptureService(bool preferWgc, bool includeCursor)
    {
        _fallback = new ScreenCaptureService(includeCursor);
        _preferred = preferWgc ? new WgcCaptureService(includeCursor) : _fallback;
    }

    public IReadOnlyList<CapturedScreen> CaptureAllScreens() => Try(x => x.CaptureAllScreens());
    public BitmapSource CaptureRegion(Rectangle bounds) => Try(x => x.CaptureRegion(bounds));
    public CapturedScreen CaptureVirtualScreen() => Try(x => x.CaptureVirtualScreen());
    public CapturedScreen? CaptureWindow(nint windowHandle) => Try(x => x.CaptureWindow(windowHandle));
    public IReadOnlyList<WindowCandidate> EnumerateTopLevelWindows() => _fallback.EnumerateTopLevelWindows();
    public WindowCandidate? FindWindowAt(System.Drawing.Point screenPoint) => _fallback.FindWindowAt(screenPoint);

    private T Try<T>(Func<ICaptureService, T> capture)
    {
        if (ReferenceEquals(_preferred, _fallback) || Volatile.Read(ref _preferredFailed) != 0)
            return capture(_fallback);
        try { return capture(_preferred); }
        catch (Exception first)
        {
            Interlocked.Exchange(ref _preferredFailed, 1);
            try { return capture(_fallback); }
            catch (Exception second) { throw new AggregateException("WGC and GDI capture both failed.", first, second); }
        }
    }
}
