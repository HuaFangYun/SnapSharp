using L = SnapSharp.Localization.UiText;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace SnapSharp.Services;

internal static class ResumableModelDownload
{
    internal static string LogPath => Path.Combine(AppPaths.DiagnosticsRoot, "local-translation-download.log");
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(45);

    internal static bool Allowed(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
        (uri.Host is "github.com" or "release-assets.githubusercontent.com" or "huggingface.co" or "hf.co" ||
         uri.Host.EndsWith(".huggingface.co", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".hf.co", StringComparison.OrdinalIgnoreCase));

    internal static async Task Download(string source, string target, string hash, long size, IProgress<string> progress,
        CancellationToken token, IProgress<double?>? percentage = null, HttpClient? httpClient = null, string? logPath = null)
    {
        var name = Path.GetFileName(target);
        using var ownedClient = httpClient is null
            ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan }
            : null;
        var client = httpClient ?? ownedClient!;
        var temporary = target + ".part";
        try
        {
            percentage?.Report(null);
            if (File.Exists(target))
            {
                progress.Report(L.Format("LocalDownloadChecking", name));
                if (new FileInfo(target).Length != size || !await HashMatches(target, hash, token))
                    throw new InvalidDataException(L.Get("AnExistingDownloadFailedVerificationMoveThe") + target);
                percentage?.Report(100);
                return;
            }
            for (var attempt = 1; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var offset = File.Exists(temporary) ? new FileInfo(temporary).Length : 0;
                    if (offset > size) { File.Delete(temporary); offset = 0; }
                    if (offset != size)
                        await Transfer(client, new Uri(source), temporary, offset, size, progress, percentage, token);
                    percentage?.Report(null);
                    progress.Report(L.Format("LocalDownloadChecking", name));
                    if (!await HashMatches(temporary, hash, token))
                    {
                        File.Delete(temporary);
                        throw new InvalidDataException(L.Get("SHA256VerificationFailedConfigurationWasNotApplied"));
                    }
                    token.ThrowIfCancellationRequested();
                    File.Move(temporary, target, overwrite: false);
                    percentage?.Report(100);
                    return;
                }
                catch (Exception ex) when (attempt < 3 && IsTransient(ex) && !token.IsCancellationRequested)
                {
                    Log(name, ex, logPath);
                    percentage?.Report(null);
                    progress.Report(L.Format("LocalDownloadRetry", name, ex.Message, attempt + 1));
                    await Task.Delay(TimeSpan.FromSeconds(attempt), token);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Log(name, ex, logPath);
            throw new IOException(L.Format("LocalDownloadFailed", name, ex.Message), ex);
        }
        // Keep partial files on cancellation or network failure so the next click resumes.
    }

    private static bool IsTransient(Exception ex) => ex switch
    {
        HttpRequestException http => http.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)http.StatusCode.Value >= 500,
        TimeoutException => true,
        IOException when ex is not InvalidDataException => true,
        _ => false
    };

    private static async Task Transfer(HttpClient client, Uri source, string temporary, long offset, long size,
        IProgress<string> progress, IProgress<double?>? percentage, CancellationToken token)
    {
        var name = Path.GetFileName(temporary)[..^5];
        var uri = source;
        HttpResponseMessage? response = null;
        try
        {
            for (var redirects = 0; ; redirects++)
            {
                if (!Allowed(uri) || redirects > 8) throw new InvalidDataException(L.Get("DownloadRedirectedOutsideOfficialDistributionSites"));
                percentage?.Report(null);
                progress.Report(L.Format("LocalDownloadConnecting", name, uri.Host));
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.ParseAdd("SnapSharp/1.0");
                if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
                using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
                connection.CancelAfter(StallTimeout);
                try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, connection.Token); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                { throw new TimeoutException(L.Get("LocalDownloadTimeout")); }
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is Uri next)
                {
                    uri = next.IsAbsoluteUri ? next : new Uri(uri, next);
                    response.Dispose(); response = null;
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException(L.Format("LocalDownloadHttpError", uri.Host, (int)response.StatusCode), null, response.StatusCode);
                break;
            }
            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                var range = response.Content.Headers.ContentRange;
                if (range is null || range.Unit != "bytes" || range.From != offset || range.Length != size || range.To >= size)
                    throw new InvalidDataException(L.Get("LocalDownloadInvalidRange"));
            }
            else offset = 0; // A server ignoring Range returns the whole file; replace the partial.
            if (response.Content.Headers.ContentLength is long length && length > size - offset)
                throw new InvalidDataException(L.Get("DownloadExceedsThePinnedFileSize"));
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(temporary, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                var received = offset;
                var clock = Stopwatch.StartNew();
                var last = TimeSpan.FromSeconds(-1);
                Report();
                while (true)
                {
                    using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    readTimeout.CancelAfter(StallTimeout);
                    int read;
                    try { read = await input.ReadAsync(buffer, readTimeout.Token); }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    { throw new TimeoutException(L.Get("LocalDownloadTimeout")); }
                    if (read == 0) break;
                    if (received + read > size) throw new InvalidDataException(L.Get("DownloadExceedsThePinnedFileSize"));
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    received += read;
                    if (clock.Elapsed - last >= TimeSpan.FromMilliseconds(250) || received == size) Report();
                }
                if (received != size) throw new IOException(L.Get("FileDownloadIsIncomplete"));

                void Report()
                {
                    last = clock.Elapsed;
                    var percent = received * 100d / size;
                    percentage?.Report(percent);
                    progress.Report(L.Format("LocalDownloadProgress", name, percent, received / 1048576d, size / 1048576d,
                        (received - offset) / 1048576d / Math.Max(0.001, clock.Elapsed.TotalSeconds)));
                }
            }
        }
        finally { response?.Dispose(); }
    }

    private static async Task<bool> HashMatches(string path, string expected, CancellationToken token)
    {
        await using var file = File.OpenRead(path);
        return string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(file, token)), expected, StringComparison.OrdinalIgnoreCase);
    }

    internal static void Log(string name, Exception error, string? path = null)
    {
        try
        {
            path ??= LogPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {name}{Environment.NewLine}{error}{Environment.NewLine}");
        }
        catch { /* Reporting a download failure must not fail because a log is unwritable. */ }
    }
}
