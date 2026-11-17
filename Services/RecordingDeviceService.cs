using System.Runtime.InteropServices;
using Windows.Devices.Enumeration;

namespace SnapSharp.Services;

// Enumerate through checked Windows APIs: the recorder's native audio enumeration can
// fail with an access violation on a worker thread and terminate the whole process.
internal static class RecordingDeviceService
{
    public static RecordingDevice[] Microphones() => Audio(1);
    public static RecordingDevice[] Speakers() => Audio(0);
    public static string DefaultMicrophone() => DefaultAudio(1);
    public static string DefaultSpeaker() => DefaultAudio(0);

    private static string DefaultAudio(int flow)
    {
        var initialized = CoInitializeEx(nint.Zero, 0);
        if (initialized < 0 && initialized != unchecked((int)0x80010106)) Marshal.ThrowExceptionForHR(initialized);
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), throwOnError: true)!;
            enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(type)!;
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(flow, 1, out device));
            Marshal.ThrowExceptionForHR(device.GetId(out var id));
            return id;
        }
        finally
        {
            Release(device); Release(enumerator);
            if (initialized >= 0) CoUninitialize();
        }
    }
    public static RecordingDevice[] Cameras() => DeviceInformation.FindAllAsync(DeviceClass.VideoCapture)
        .AsTask().GetAwaiter().GetResult().Where(device => device.IsEnabled)
        .Select(device => new RecordingDevice(device.Id, device.Name)).ToArray();

    private static RecordingDevice[] Audio(int flow)
    {
        var initialized = CoInitializeEx(nint.Zero, 0);
        // An already initialized STA is usable too; only balance our successful calls.
        if (initialized < 0 && initialized != unchecked((int)0x80010106)) Marshal.ThrowExceptionForHR(initialized);
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), throwOnError: true)!;
            enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(type)!;
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(flow, 1, out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));
            var result = new List<RecordingDevice>();
            for (uint index = 0; index < count; index++)
            {
                IMMDevice? device = null;
                try
                {
                    Marshal.ThrowExceptionForHR(collection.Item(index, out device));
                    Marshal.ThrowExceptionForHR(device.GetId(out var id));
                    result.Add(new RecordingDevice(id, FriendlyName(device, id)));
                }
                finally { Release(device); }
            }
            return result.ToArray();
        }
        finally
        {
            Release(collection); Release(enumerator);
            if (initialized >= 0) CoUninitialize();
        }
    }

    private static string FriendlyName(IMMDevice device, string fallback)
    {
        IPropertyStore? properties = null;
        var value = new PropVariant();
        try
        {
            Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out properties));
            var key = new PropertyKey { Format = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 14 };
            Marshal.ThrowExceptionForHR(properties.GetValue(ref key, out value));
            return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) ?? fallback : fallback;
        }
        catch (COMException) { return fallback; }
        finally { PropVariantClear(ref value); Release(properties); }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint mode);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public nint Pointer;
    }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint state, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid id, uint context, nint parameters, out nint instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }
}
