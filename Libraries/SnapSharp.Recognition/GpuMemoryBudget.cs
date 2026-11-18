using System.Runtime.InteropServices;

namespace SnapSharp.Services;

/// <summary>DXGI local-segment budget for this process. Called only in the isolated worker.</summary>
internal static class GpuMemoryBudget
{
    internal readonly record struct Snapshot(ulong Budget, ulong Usage)
    {
        public override string ToString() => $"GPU local memory: usage={Usage}; budget={Budget}";
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct VideoMemoryInfo
    { public ulong Budget, CurrentUsage, AvailableForReservation, CurrentReservation; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapter(nint self, uint index, out nint adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryMemory(nint self, uint node, int segment, out VideoMemoryInfo info);
    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(in Guid iid, out nint factory);
    private static T Method<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));

    internal static Snapshot? Query(int adapterIndex)
    {
        nint factory = 0, adapter = 0, adapter3 = 0;
        try
        {
            // IID_IDXGIFactory1, IID_IDXGIAdapter3.
            var factoryId = new Guid(0x770aae78, unchecked((short)0xf26f), 0x4dba, 0xa8, 0x29, 0x25, 0x3c, 0x83, 0xd1, 0xb3, 0x87);
            if (adapterIndex < 0 || CreateDXGIFactory1(factoryId, out factory) < 0) return null;
            if (Method<EnumAdapter>(factory, 12)(factory, (uint)adapterIndex, out adapter) < 0) return null;
            var iid = new Guid(0x645967a4, 0x1392, 0x4310, 0xa7, 0x98, 0x80, 0x53, 0xce, 0x3e, 0x93, 0xfd);
            if (Marshal.QueryInterface(adapter, in iid, out adapter3) < 0) return null;
            if (Method<QueryMemory>(adapter3, 14)(adapter3, 0, 0, out var info) < 0) return null;
            return new Snapshot(info.Budget, info.CurrentUsage);
        }
        catch (COMException) { return null; }
        finally
        {
            if (adapter3 != 0) Marshal.Release(adapter3);
            if (adapter != 0) Marshal.Release(adapter);
            if (factory != 0) Marshal.Release(factory);
        }
    }
}
