using SnapSharp.Models;

namespace SnapSharp.Services;

public sealed record LaunchCommand(CaptureMode? Mode, int DelaySeconds, bool CopyToClipboard)
{
    public static bool TryParse(IReadOnlyList<string> args, out LaunchCommand command, out string error)
    {
        CaptureMode? mode = null;
        var delay = 0;
        var copy = false;
        error = string.Empty;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "-region":
                case "--region":
                    if (!SetMode(CaptureMode.Region)) return Fail(out command, out error, "只能指定一种截图模式。");
                    break;
                case "-full":
                case "--full":
                    if (!SetMode(CaptureMode.FullScreen)) return Fail(out command, out error, "只能指定一种截图模式。");
                    break;
                case "-window":
                case "--window":
                    if (!SetMode(CaptureMode.ActiveWindow)) return Fail(out command, out error, "只能指定一种截图模式。");
                    break;
                case "-delay":
                case "--delay":
                    if (++i >= args.Count || !int.TryParse(args[i], out delay) || delay is < 1 or > 3600)
                        return Fail(out command, out error, "-delay 后需要 1 到 3600 之间的秒数。");
                    break;
                case "-clip":
                case "--clip":
                    copy = true;
                    break;
                default:
                    return Fail(out command, out error, $"未知命令行参数：{args[i]}");
            }
        }

        if (delay > 0 && mode is null) mode = CaptureMode.Region;
        command = new LaunchCommand(mode, delay, copy);
        return true;

        bool SetMode(CaptureMode value)
        {
            if (mode is not null && mode != value) return false;
            mode = value;
            return true;
        }

    }

    private static bool Fail(out LaunchCommand command, out string error, string message)
    {
        error = message;
        command = new LaunchCommand(null, 0, false);
        return false;
    }
}
