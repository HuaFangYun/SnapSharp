namespace SnapSharp.Models;

public static class ModelDeviceSupport
{
    public static bool OcrSupports(string packageId, string device) => (packageId, device) switch
    {
        ("tiny", "CPU") or ("small", "CPU") or ("small", "DirectML") or ("medium", "DirectML") => true,
        _ => false
    };
}
