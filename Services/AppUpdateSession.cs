using System.Net.Http;
using L = NimCap.Screenshot.Localization.UiText;

namespace NimCap.Screenshot.Services;

/// <summary>One automatic check per process; settings reuse its result without another request.</summary>
public sealed class AppUpdateSession
{
    private Task? _startup;
    private Task? _pending;
    internal AppRelease? Release { get; private set; }
    internal bool Checked { get; private set; }
    internal bool Failed { get; private set; }
    internal event Action? Changed;
    internal string StatusText => !Checked ? L.Get("AppUpdateChecking") : Failed ? L.Get("AppUpdateFailed")
        : Release is null ? L.Get("AppUpdateNoRelease")
        : Release.IsNewerThan(typeof(AppUpdateSession).Assembly.GetName().Version ?? new Version(0, 0))
            ? L.Format("AppUpdateAvailable", Release.Tag) : L.Get("AppUpdateLatest");

    internal Task CheckOnStartup(CancellationToken token, HttpClient? client = null) => _startup ??= Check(token, client);
    internal Task Check(CancellationToken token, HttpClient? client = null) => _pending ??= Run(token, client);

    private async Task Run(CancellationToken token, HttpClient? client)
    {
        // Always yield before completion so even a synchronous test handler can safely clear _pending.
        await Task.Yield();
        try { Release = await AppUpdateService.CheckAsync(token, client); Failed = false; Checked = true; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception) { Release = null; Failed = true; Checked = true; }
        finally { _pending = null; }
        Changed?.Invoke();
    }
}
