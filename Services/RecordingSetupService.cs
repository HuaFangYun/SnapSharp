using L = NimCap.Screenshot.Localization.UiText;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace NimCap.Screenshot.Services;

internal sealed record RecordingEnvironment(bool Supported, bool VisualCpp, bool MediaFoundation, bool IsNEdition, string Description)
{
    public bool Ready => Supported && VisualCpp && MediaFoundation;
}

/// <summary>Only downloads Microsoft's runtime; media components are installed by Windows servicing.</summary>
internal sealed class RecordingSetupService
{
    internal const string VisualCppUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
    private static readonly SemaphoreSlim SetupGate = new(1);
    internal static RecordingEnvironment Check()
    {
        var supported = Environment.Is64BitProcess && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);
        bool Present(string file) => File.Exists(Path.Combine(Environment.SystemDirectory, file));
        var vc = Present("vcruntime140.dll") && Present("vcruntime140_1.dll") && Present("msvcp140.dll");
        var media = Present("mfplat.dll") && Present("mf.dll") && Present("mfreadwrite.dll");
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var edition = key?.GetValue("EditionID") as string ?? "";
        var n = edition.EndsWith("N", StringComparison.OrdinalIgnoreCase);
        return new(supported, vc, media, n,
            L.Format("Windows102004X640VisualC", (supported ? L.Get("CheckPassed") : L.Get("CheckFailed")), (vc ? L.Get("ComponentReady") : L.Get("MissingRuntime")), (media ? L.Get("ComponentReady") : L.Get("MissingMediaComponents")), edition));
    }

    public async Task<string> Configure(IProgress<string> progress, CancellationToken token)
    {
        if (!await SetupGate.WaitAsync(0, token)) throw new InvalidOperationException(L.Get("RecordingSetupIsAlreadyInProgressElsewhere"));
        try
        {
            var state = Check();
            if (!state.Supported) throw new NotSupportedException(L.Get("RecordingRequiresWindows102004OrLater"));
            var restart = false;
            if (!state.VisualCpp)
            {
                var installer = await DownloadVisualCpp(progress, token);
                token.ThrowIfCancellationRequested();
                progress.Report(L.Get("MicrosoftSignatureVerifiedInstallingVisualCX64"));
                restart = await Install(installer, "/install /passive /norestart", token);
                state = Check();
                if (!restart && !state.VisualCpp) throw new InvalidOperationException(L.Get("InstallationFinishedButTheRuntimeIsNot"));
            }
            if (!state.MediaFoundation)
            {
                if (!state.IsNEdition) throw new InvalidOperationException(L.Get("MediaFoundationIsMissingRepairMediaComponents"));
                progress.Report(L.Get("InstallingMediaFeaturePackThroughWindowsApprove"));
                restart |= await Install(Path.Combine(Environment.SystemDirectory, "Dism.exe"), "/Online /Add-Capability /CapabilityName:Media.MediaFeaturePack~~~~0.0.1.0 /NoRestart", token);
                state = Check();
            }
            if (restart) return L.Get("DependenciesInstalledRestartWindowsBeforeRecordingSave");
            if (!state.Ready) throw new InvalidOperationException(L.Get("ComponentsAreNotReadyRestartWindowsAnd"));
            progress.Report(L.Get("ComponentsAreReadyTestingH264Encoding"));
            var probe = await Task.Run(() => RecordingEncoderProbe.Check(token), token);
            return L.Get("RecordingRequirementsAreReady") + probe + L.Get("ReturnToTheRecordingWindowToStart");
        }
        finally { SetupGate.Release(); }
    }

    internal static async Task<string> DownloadVisualCpp(IProgress<string> progress, CancellationToken token)
    {
        var directory = Path.Combine(AppPaths.Root, "downloads", "vc-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, "vc_redist.x64.download");
        var installer = Path.Combine(directory, "vc_redist.x64.exe");
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromMinutes(10) };
            var address = new Uri(VisualCppUrl);
            for (var redirects = 0; redirects < 8; redirects++)
            {
                if (!IsMicrosoftDownload(address)) throw new InvalidDataException(L.Get("RuntimeDownloadRedirectedToANonMicrosoft"));
                progress.Report(L.Get("ConnectingToMicrosoftSVisualCX64"));
                using var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, token);
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } next) { address = new Uri(address, next); continue; }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > 100_000_000) throw new InvalidDataException(L.Get("RuntimeInstallerExceedsTheSizeLimit"));
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await using (var input = await response.Content.ReadAsStreamAsync(token))
                {
                    var buffer = new byte[81920]; int read;
                    while ((read = await input.ReadAsync(buffer, token)) != 0)
                    {
                        if (output.Length + read > 100_000_000) throw new InvalidDataException(L.Get("RuntimeInstallerExceedsTheSizeLimit"));
                        await output.WriteAsync(buffer.AsMemory(0, read), token);
                        var total = response.Content.Headers.ContentLength;
                        progress.Report(L.Format("VisualCX64Download0F1MB", output.Length / 1048576d) + (total > 0 ? $" / {total / 1048576d:F1} MB" : ""));
                    }
                }
                token.ThrowIfCancellationRequested();
                progress.Report(L.Get("VerifyingInstallerWindowsTrustAndMicrosoftPublisher"));
                await Task.Run(() => MicrosoftInstallerSignature.Verify(temporary), token);
                File.Move(temporary, installer);
                return installer;
            }
            throw new InvalidDataException(L.Get("TooManyRuntimeDownloadRedirects"));
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (!File.Exists(installer)) Directory.Delete(directory, false);
            throw;
        }
    }
    internal static bool IsMicrosoftDownload(Uri uri) => uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) && uri.IsDefaultPort &&
        (uri.Host == "aka.ms" || uri.Host == "download.microsoft.com" || uri.Host == "download.visualstudio.microsoft.com" || uri.Host == "download.visualstudio.com");

    private static async Task<bool> Install(string executable, string arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            using var process = Process.Start(new ProcessStartInfo(executable, arguments) { UseShellExecute = true, Verb = "runas" })
                ?? throw new InvalidOperationException(L.Get("CouldNotStartTheSystemInstaller"));
            // Never kill Windows servicing or a runtime installer on cancellation.
            await process.WaitForExitAsync();
            return process.ExitCode switch
            {
                0 or 1638 => false,
                3010 or 1641 => true,
                _ => throw new InvalidOperationException(L.Format("SystemComponentInstallationFailedWithExitCode", process.ExitCode))
            };
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { throw new OperationCanceledException(L.Get("WindowsAdministratorApprovalWasCanceledInstallationIs"), ex, token); }
    }
}
