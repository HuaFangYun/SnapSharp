using System.Windows.Media.Imaging;

namespace SnapSharp.Models;

public enum ScrollDirection { Auto, Vertical, Horizontal }
public sealed record ScrollCaptureOptions(ScrollDirection Direction = ScrollDirection.Auto, int DelayMilliseconds = 600,
    int WheelNotches = 2, int MaximumFrames = 40, ScrollTrim? MatchingTrim = null);
public sealed record ScrollMatch(ScrollDirection Direction, int Offset, double Error, bool Reliable, double Texture);
public sealed record ScrollCaptureSession(IReadOnlyList<BitmapSource> Frames, IReadOnlyList<int> Offsets,
    IReadOnlyList<ScrollMatch> Matches, ScrollDirection Direction, string StopReason);
public sealed record ScrollTrim(int Left = 0, int Top = 0, int Right = 0, int Bottom = 0);
public sealed record ScrollStitchResult(BitmapSource Image, IReadOnlyList<int> Seams, IReadOnlyList<int> FrameStarts);
public sealed record ScrollProject(ScrollCaptureSession Session, ScrollTrim FrameTrim, ScrollTrim ResultCrop);
