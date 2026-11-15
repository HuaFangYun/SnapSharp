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
        if (Format is not ("MP4" or "GIF") || FrameRate is not (15 or 30 or 60) || Quality is not ("流畅" or "标准" or "高清")) throw new ArgumentException("录屏格式、帧率或质量档位无效。");
        if (StopAfterSeconds is < 0 or > 86400 || GifMaximumEdge is < 160 or > 800) throw new ArgumentException("定时停止为 0–86400 秒，GIF 最长边为 160–800。");
        if (CaptureBackend is not ("WGC" or "DXGI")) throw new ArgumentException("录屏采集引擎需为 WGC 或 DXGI。");
        if (BitrateKbps != 0 && BitrateKbps is < 500 or > 100000) throw new ArgumentException("视频码率为 0（按质量自动），或 500–100000 kbps。");
        if (CountdownSeconds is < 0 or > 10) throw new ArgumentException("录屏倒计时为 0–10 秒。");
    }
    public RecordingPreferences Copy() => (RecordingPreferences)MemberwiseClone();
}
