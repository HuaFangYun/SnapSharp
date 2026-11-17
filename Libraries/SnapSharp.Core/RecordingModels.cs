using L = SnapSharp.Localization.UiText;
namespace SnapSharp.Models;

public enum RecordingState { Ready, Starting, Recording, Paused, Stopping, Encoding, Completed, Failed }

public sealed class RecordingPreferences
{
    public string Format { get; set; } = "MP4";
    public int FrameRate { get; set; } = 30;
    public string Quality { get; set; } = "标准";
    public bool HardwareEncoding { get; set; } = true;
    public bool IncludeCursor { get; set; } = true;
    public bool HighlightClicks { get; set; }
    public int StopAfterSeconds { get; set; }
    public int GifMaximumEdge { get; set; } = 800;
    public string CaptureBackend { get; set; } = "WGC";
    public int BitrateKbps { get; set; }
    public int CountdownSeconds { get; set; } = 3;
    public void Validate()
    {
        if (Format is not ("MP4" or "GIF") || FrameRate is not (15 or 30 or 60) || Quality is not ("流畅" or "标准" or "高清")) throw new ArgumentException(L.Get("InvalidRecordingFormatFrameRateOrQuality"));
        if (StopAfterSeconds is < 0 or > 86400 || GifMaximumEdge is < 160 or > 800) throw new ArgumentException(L.Get("StopTimeMustBe086400Seconds"));
        if (CaptureBackend is not ("WGC" or "DXGI")) throw new ArgumentException(L.Get("RecordingCaptureEngineMustBeWGCOr"));
        if (BitrateKbps != 0 && BitrateKbps is < 500 or > 100000) throw new ArgumentException(L.Get("VideoBitrateMustBe0AutomaticOr"));
        if (CountdownSeconds is < 0 or > 10) throw new ArgumentException(L.Get("RecordingCountdownMustBe010Seconds"));
    }
    public RecordingPreferences Copy() => (RecordingPreferences)MemberwiseClone();
}
