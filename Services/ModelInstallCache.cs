using System.Collections.Concurrent;
using System.Security.Cryptography;
using L = SnapSharp.Localization.UiText;

namespace SnapSharp.Services;

internal static class ModelInstallCache
{
    internal sealed record Asset(string Name, string Hash, long MaximumBytes, long? Size = null);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);

    // Serialize lookup and installation together, including calls from different service instances.
    internal static async Task<IDisposable> Lock(string root, string prefix, CancellationToken token)
    {
        var key = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + "|" + prefix;
        var gate = Gates.GetOrAdd(key, _ => new SemaphoreSlim(1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        return new Lease(gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }

    internal static async Task<string?> Find(string root, string prefix, IReadOnlyList<Asset> assets,
        IProgress<string>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Directory.Exists(root)) return null;
        progress?.Report(L.Get("ModelCacheChecking"));
        foreach (var directory in Directory.GetDirectories(root, prefix + "*", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(Directory.GetLastWriteTimeUtc).ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) continue;
            var valid = true;
            foreach (var asset in assets)
                if (!await Matches(Path.Combine(directory, asset.Name), asset, token).ConfigureAwait(false)) { valid = false; break; }
            if (!valid) continue;
            token.ThrowIfCancellationRequested();
            progress?.Report(L.Format("ModelCacheReused", Path.GetFileName(directory)));
            return directory;
        }
        return null;
    }

    internal static async Task<bool> Matches(string path, Asset asset, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            if (input.Length == 0 || input.Length > asset.MaximumBytes || asset.Size is long size && input.Length != size) return false;
            using var hash = IncrementalHash.CreateHash(asset.Hash.Length == 32 ? HashAlgorithmName.MD5 : HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            int count;
            while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0) hash.AppendData(buffer, 0, count);
            return Convert.ToHexString(hash.GetHashAndReset()).Equals(asset.Hash, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
