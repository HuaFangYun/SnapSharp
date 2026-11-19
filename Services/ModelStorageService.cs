using Microsoft.VisualBasic.FileIO;
using L = NimCap.Screenshot.Localization.UiText;

namespace NimCap.Screenshot.Services;

internal static class ModelStorageService
{
    internal sealed record Entry(string Path, bool IsDirectory, long? Bytes, bool Referenced, bool Unsafe)
    {
        public string Name => System.IO.Path.GetFileName(Path);
        public string SizeLabel => Bytes is long bytes ? bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.##} GiB"
            : $"{bytes / 1048576d:0.##} MiB" : L.Get("Unknown");
        public string Status => L.Get(Referenced ? "ModelStorageReferenced" : Unsafe ? "ModelStorageUnavailable" : "ModelStorageUnreferenced");
        public bool CanDelete => !Referenced && !Unsafe;
    }

    internal static IReadOnlyList<Entry> Scan(string root, IReadOnlyCollection<string> references)
    {
        root = Normalize(root);
        if (!Directory.Exists(root)) return [];
        CheckAncestors(root);
        var entries = new List<Entry>();
        foreach (var path in Directory.GetFileSystemEntries(root))
        {
            // Translation keeps several model files and runtime packages in one shared folder.
            if (System.IO.Path.GetFileName(path).Equals("local-translation", StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(path) && !IsLink(path))
                entries.AddRange(Directory.GetFileSystemEntries(path).Select(item => Inspect(item, references)));
            else entries.Add(Inspect(path, references));
        }
        return entries.OrderByDescending(entry => entry.Referenced).ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static void Delete(string root, string path, IReadOnlyCollection<string> references,
        Action<string, bool>? recycle = null)
    {
        root = Normalize(root);
        path = Normalize(path);
        var parent = System.IO.Path.GetDirectoryName(path);
        var translation = System.IO.Path.Combine(root, "local-translation");
        if (!string.Equals(parent, root, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(parent, translation, StringComparison.OrdinalIgnoreCase))
            throw new IOException(L.Get("ModelStorageOutsideRoot"));
        CheckAncestors(path);
        var entry = Inspect(path, references);
        if (!entry.CanDelete) throw new IOException(entry.Status);
        // Reinspect at deletion time, rather than trusting the displayed snapshot.
        (recycle ?? Recycle)(path, entry.IsDirectory);
    }

    private static void Recycle(string path, bool directory)
    {
        if (directory) FileSystem.DeleteDirectory(path, UIOption.AllDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
        else FileSystem.DeleteFile(path, UIOption.AllDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
    }

    private static Entry Inspect(string path, IReadOnlyCollection<string> references)
    {
        var referenced = references.Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Any(reference => Contains(path, reference) || Contains(reference, path));
        var directory = Directory.Exists(path);
        try { return new(path, directory, Measure(path), referenced, false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return new(path, directory, null, referenced, true); }
    }

    private static long Measure(string path)
    {
        if (IsLink(path)) throw new IOException(L.Get("ModelStorageUnavailable"));
        if (!Directory.Exists(path)) return new FileInfo(path).Length;
        long bytes = 0;
        foreach (var item in Directory.EnumerateFileSystemEntries(path)) bytes = checked(bytes + Measure(item));
        return bytes;
    }

    private static void CheckAncestors(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
            if (IsLink(current.FullName)) throw new IOException(L.Get("ModelStorageUnavailable"));
    }

    private static bool IsLink(string path) => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
    private static string Normalize(string path) => System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
    private static bool Contains(string parent, string child) => Normalize(child).Equals(Normalize(parent), StringComparison.OrdinalIgnoreCase)
        || Normalize(child).StartsWith(Normalize(parent) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
