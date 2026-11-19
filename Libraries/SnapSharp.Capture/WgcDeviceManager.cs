using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.DirectX.Direct3D11;
using static Vortice.Direct3D11.D3D11;

namespace SnapSharp.Services;

// Sessions lease a generation. A failed generation cannot accept new sessions;
// its resources are released only after its last session has finished cleaning up.
internal static class WgcDeviceManager
{
    private static readonly object Gate = new();
    private static Generation? _current;

    internal sealed class Generation(ID3D11Device native, IDirect3DDevice device)
    {
        internal readonly ID3D11Device Native = native;
        internal readonly IDirect3DDevice Device = device;
        internal int Users;
    }

    internal sealed class Lease : IDisposable
    {
        private Generation? _generation;
        internal Lease(Generation generation) => _generation = generation;
        internal IDirect3DDevice Device => (_generation ?? throw new ObjectDisposedException(nameof(Lease))).Device;
        internal void CheckHealth() => (_generation ?? throw new ObjectDisposedException(nameof(Lease))).Native.DeviceRemovedReason.CheckError();
        internal void Invalidate()
        {
            lock (Gate) { if (_generation is not null && ReferenceEquals(_current, _generation)) _current = null; }
        }
        public void Dispose()
        {
            var owned = Interlocked.Exchange(ref _generation, null);
            if (owned is null) return;
            bool release;
            lock (Gate)
            {
                release = --owned.Users == 0;
                if (release && ReferenceEquals(_current, owned)) _current = null;
            }
            // Never destroy COM objects while holding a manager/callback lock.
            if (release)
            {
                try { WgcCaptureService.ReleaseWinRtObject(owned.Device); }
                finally { owned.Native.Dispose(); }
            }
        }
    }

    internal static Lease Acquire()
    {
        lock (Gate)
        {
            if (_current is { } existing)
            {
                existing.Users++;
                return new Lease(existing);
            }
        }
        // Create outside the lock: rollback may release COM objects.
        var created = Create();
        Generation selected;
        lock (Gate)
        {
            selected = _current ??= created;
            selected.Users++;
        }
        var lease = new Lease(selected);
        try
        {
            if (!ReferenceEquals(selected, created))
            {
                try { WgcCaptureService.ReleaseWinRtObject(created.Device); }
                finally { created.Native.Dispose(); }
            }
        }
        catch { lease.Dispose(); throw; }
        return lease;
    }

    private static Generation Create()
    {
        var native = D3D11CreateDevice(DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            FeatureLevel.Level_11_1, FeatureLevel.Level_11_0);
        nint pointer = 0;
        try
        {
            using var dxgi = native.QueryInterface<IDXGIDevice>();
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out pointer));
            var device = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(pointer);
            return new Generation(native, device);
        }
        catch { native.Dispose(); throw; }
        finally { if (pointer != 0) Marshal.Release(pointer); }
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint device);
}
