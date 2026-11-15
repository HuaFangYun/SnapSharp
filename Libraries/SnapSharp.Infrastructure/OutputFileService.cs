using SnapSharp.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SnapSharp.Services;

/// <summary>Image names and safe writes shared by manual export and automatic capture output.</summary>
public static class OutputFileService
{
    public const string DefaultTemplate = "SnapSharp-{date}-{time}-{seq}";
    private static long _sequence;

    public static string ExpandTemplate(string template, DateTime time, int width, int height, long sequence)
    {
        if (string.IsNullOrWhiteSpace(template)) throw new ArgumentException("命名模板不能为空。");
        var values = new Dictionary<string, string>
        {
            ["date"] = time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["time"] = time.ToString("HHmmss-fff", CultureInfo.InvariantCulture),
            ["width"] = width.ToString(CultureInfo.InvariantCulture),
            ["height"] = height.ToString(CultureInfo.InvariantCulture),
            ["seq"] = sequence.ToString("D4", CultureInfo.InvariantCulture)
        };
        var name = Regex.Replace(template, @"\{([^{}]*)\}", match => values.TryGetValue(match.Groups[1].Value, out var value)
            ? value : throw new ArgumentException($"未知模板字段：{match.Value}"));
        if (name.Contains('{') || name.Contains('}')) throw new ArgumentException("模板花括号不完整。");
        name = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.', ' ');
        if (name is "" or "." or "..") throw new ArgumentException("模板未生成有效文件名。");
        if (Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|\.)", RegexOptions.IgnoreCase)) name = "_" + name;
        if (name.Length > 180) name = name[..180].TrimEnd('.', ' ');
        return name;
    }

    public static string NewName(string template, int width, int height) =>
        ExpandTemplate(template, DateTime.Now, width, height, Interlocked.Increment(ref _sequence));

    public static string AutomaticDirectory(AppSettings settings, DateTime time) => settings.ArchiveByDate
        ? Path.Combine(settings.SaveDirectory, time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        : settings.SaveDirectory;

    public static string AvailablePath(string directory, string name, string extension)
    {
        var path = Path.Combine(directory, name + extension);
        for (var i = 1; File.Exists(path) || Directory.Exists(path); i++) path = Path.Combine(directory, $"{name}-{i}{extension}");
        return path;
    }

    public static void Write(string path, byte[] bytes, bool overwrite = false)
    {
        path = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        // A same-directory temporary ensures failures never leave a truncated destination.
        var temporary = Path.Combine(directory, ".snapsharp-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, path, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static string WriteUnique(string directory, string name, string extension, byte[] bytes)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var path = AvailablePath(directory, name, extension);
            try { Write(path, bytes); return path; }
            catch (IOException) when (File.Exists(path)) { }
        }
        throw new IOException("文件名冲突过多，请选择其他目录。");
    }
}
