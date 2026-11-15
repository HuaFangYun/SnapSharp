using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace SnapSharp.Services;

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
            $"Windows 10 2004+／x64：{(supported ? "通过" : "不满足")}\nVisual C++ x64：{(vc ? "已就绪" : "缺少运行库")}\nMedia Foundation：{(media ? "已就绪" : "缺少媒体组件")}\n系统版本：{edition}\n录屏引擎 ScreenRecorderLib 已随程序提供，无需另装 NuGet 或 FFmpeg。");
    }

    public async Task<string> Configure(IProgress<string> progress, CancellationToken token)
    {
        if (!await SetupGate.WaitAsync(0, token)) throw new InvalidOperationException("另一处正在配置录屏环境，请稍后重新检查。");
        try
        {
            var state = Check();
            if (!state.Supported) throw new NotSupportedException("录屏需要 Windows 10 2004 及以上版本和 x64 程序。");
            var restart = false;
            if (!state.VisualCpp)
            {
                var installer = await DownloadVisualCpp(progress, token);
                token.ThrowIfCancellationRequested();
                progress.Report("微软签名验证通过，正在安装 Visual C++ x64。请确认 Windows 管理员授权；安装过程中请等待。");
                restart = await Install(installer, "/install /passive /norestart", token);
                state = Check();
                if (!restart && !state.VisualCpp) throw new InvalidOperationException("安装已结束，但运行库仍未就绪。请重启 Windows 后重新检查。");
            }
            if (!state.MediaFoundation)
            {
                if (!state.IsNEdition) throw new InvalidOperationException("此系统缺少 Media Foundation。请在 Windows 可选功能中修复媒体组件；Windows Server 需管理员安装 Server-Media-Foundation。可点击下方“Windows 可选功能”。");
                progress.Report("正在通过 Windows 安装 Media Feature Pack。请确认管理员授权；系统会自行下载匹配当前 Windows 版本的组件。");
                restart |= await Install(Path.Combine(Environment.SystemDirectory, "Dism.exe"), "/Online /Add-Capability /CapabilityName:Media.MediaFeaturePack~~~~0.0.1.0 /NoRestart", token);
                state = Check();
            }
            if (restart) return "依赖安装完成，需要重启 Windows 后再录屏。请先保存其他工作；程序不会自动重启系统。";
            if (!state.Ready) throw new InvalidOperationException("组件尚未就绪，请重启 Windows 后重新检查，或打开 Windows 可选功能确认安装结果。");
            progress.Report("组件已就绪，正在用生成的测试图检查 H.264 编码器，不录制桌面或设备…");
            var probe = await Task.Run(() => RecordingEncoderProbe.Check(token), token);
            return "录屏环境已就绪。" + probe + "可返回录屏窗口直接开始；麦克风／摄像头需本次勾选并允许系统访问。";
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
                if (!IsMicrosoftDownload(address)) throw new InvalidDataException("运行库下载被重定向到非微软地址，已中止。");
                progress.Report("正在从微软官方源连接 Visual C++ x64…");
                using var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, token);
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } next) { address = new Uri(address, next); continue; }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > 100_000_000) throw new InvalidDataException("运行库安装包超过大小限制。");
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await using (var input = await response.Content.ReadAsStreamAsync(token))
                {
                    var buffer = new byte[81920]; int read;
                    while ((read = await input.ReadAsync(buffer, token)) != 0)
                    {
                        if (output.Length + read > 100_000_000) throw new InvalidDataException("运行库安装包超过大小限制。");
                        await output.WriteAsync(buffer.AsMemory(0, read), token);
                        var total = response.Content.Headers.ContentLength;
                        progress.Report($"Visual C++ x64 下载 {output.Length / 1048576d:F1} MB" + (total > 0 ? $" / {total / 1048576d:F1} MB" : ""));
                    }
                }
                token.ThrowIfCancellationRequested();
                progress.Report("正在验证安装包的 Windows 信任签名与微软发布者…");
                await Task.Run(() => MicrosoftInstallerSignature.Verify(temporary), token);
                File.Move(temporary, installer);
                return installer;
            }
            throw new InvalidDataException("运行库下载重定向次数过多。");
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
                ?? throw new InvalidOperationException("无法启动系统安装程序。");
            // Never kill Windows servicing or a runtime installer on cancellation.
            await process.WaitForExitAsync();
            return process.ExitCode switch
            {
                0 or 1638 => false,
                3010 or 1641 => true,
                _ => throw new InvalidOperationException($"系统组件安装失败，退出代码 {process.ExitCode}。请检查 Windows Update 或手动重试。")
            };
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { throw new OperationCanceledException("已取消 Windows 管理员授权，未完成安装。", ex, token); }
    }
}
