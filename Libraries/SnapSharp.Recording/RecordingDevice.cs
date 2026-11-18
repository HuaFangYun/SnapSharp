namespace SnapSharp.Services;

public sealed record RecordingDevice(string Id, string Name)
{
    public override string ToString() => Name;
}
