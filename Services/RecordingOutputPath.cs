using L = SnapSharp.Localization.UiText;

namespace SnapSharp.Services;

internal static class RecordingOutputPath
{
    public static string Create(string directory, string format, DateTime now)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException(L.Get("RecordingFolderMustBeAbsolute"));
        Directory.CreateDirectory(directory);
        var name = "SnapSharp-record-" + now.ToString("yyyyMMdd-HHmmss-fff");
        var extension = format == "GIF" ? ".gif" : ".mp4";
        var path = Path.Combine(directory, name + extension);
        for (var sequence = 1; File.Exists(path) || Directory.Exists(path); sequence++)
            path = Path.Combine(directory, $"{name}-{sequence}{extension}");
        return path;
    }
}
