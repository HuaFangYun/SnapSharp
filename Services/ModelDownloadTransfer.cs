using System.Net.Http;
using L = SnapSharp.Localization.UiText;

namespace SnapSharp.Services;

internal static class ModelDownloadTransfer
{
    // The caller owns the destination and validates the model/hash after transfer.
    internal static async Task CopyTo(HttpClient client, string source, string sourceKind,
        Stream destination, long maximumBytes, Action<long> report, CancellationToken token,
        long? expectedBytes = null)
    {
        using var response = await ModelDownloadSource.Get(client, source, sourceKind, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length)
        {
            if (length > maximumBytes)
                throw new InvalidDataException(L.Get("DownloadExceedsThePinnedFileSize"));
            if (expectedBytes is long expected && length != expected)
                throw new InvalidDataException(L.Get("DownloadLengthMismatch"));
        }

        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            if (read > maximumBytes - total)
                throw new InvalidDataException(L.Get("DownloadExceedsThePinnedFileSize"));
            await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            total += read;
            report(total);
        }
        if (expectedBytes is long size && total != size)
            throw new InvalidDataException(L.Get("FileDownloadIsIncomplete"));
        token.ThrowIfCancellationRequested();
    }
}
