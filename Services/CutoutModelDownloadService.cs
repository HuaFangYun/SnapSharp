using Microsoft.ML.OnnxRuntime;
using SnapSharp.Models;
using System.Net.Http;
using System.Security.Cryptography;

namespace SnapSharp.Services;

internal sealed class CutoutModelDownloadService
{
    internal static string Source => CutoutModels.All[0].Source;
    internal static string Sha256 => CutoutModels.All[0].Hash;
    public Task<string> Download(string root, IProgress<string> progress, CancellationToken token) => Download(CutoutModels.All[0], root, progress, token);

    public async Task<string> Download(CutoutModelDefinition model, string root, IProgress<string> progress, CancellationToken token)
    {
        Directory.CreateDirectory(root);
        var temporary = Path.Combine(root, "download-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
            var address = new Uri(model.Source);
            for (var redirects = 0; redirects < 5; redirects++)
            {
                using var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, token);
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
                {
                    address = new Uri(address, location);
                    if (address.Scheme != "https" || !string.IsNullOrEmpty(address.UserInfo) || !address.IsDefaultPort || address.Host is not ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com" or "huggingface.co" or "cas-bridge.xethub.hf.co" or "us.aws.cdn.hf.co" or "cdn-lfs.huggingface.co" or "cdn-lfs-us-1.hf.co"))
                        throw new InvalidDataException("模型源重定向到不受信任的地址。");
                    continue;
                }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > model.MaximumBytes) throw new InvalidDataException("模型超过预期大小。");
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await using (var stream = await response.Content.ReadAsStreamAsync(token))
                {
                    var buffer = new byte[81920]; int read; long total = 0;
                    while ((read = await stream.ReadAsync(buffer, token)) != 0)
                    {
                        total += read;
                        if (total > model.MaximumBytes) throw new InvalidDataException("模型超过预期大小。");
                        await output.WriteAsync(buffer.AsMemory(0, read), token);
                        progress.Report($"正在下载 {model.Id} · {total / 1048576d:F1} MB");
                    }
                }
                progress.Report("正在校验文件与 ONNX 契约（大模型可能需要片刻）…");
                return await Task.Run(() => InstallFile(model, temporary, root, token), token);
            }
            throw new InvalidDataException("模型下载重定向次数过多。");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static string Install(byte[] data, string root, CancellationToken token)
    {
        if (!Convert.ToHexString(SHA256.HashData(data)).Equals(Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("抠图模型 SHA256 校验失败，原配置未更改。");
        Directory.CreateDirectory(root);
        var temporary = Path.Combine(root, Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllBytes(temporary, data); return InstallFile(CutoutModels.All[0], temporary, root, token); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string InstallFile(CutoutModelDefinition model, string temporary, string root, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using (var stream = File.OpenRead(temporary))
        {
            if (stream.Length > model.MaximumBytes) throw new InvalidDataException("模型超过预期大小。");
            using HashAlgorithm hash = model.Hash.Length == 64 ? SHA256.Create() : MD5.Create();
            if (!Convert.ToHexString(hash.ComputeHash(stream)).Equals(model.Hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("模型校验失败，原配置未更改。");
        }
        token.ThrowIfCancellationRequested();
        using (var options = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1, GraphOptimizationLevel = GraphOptimizationLevel.ORT_DISABLE_ALL })
        using (var session = new InferenceSession(temporary, options))
        {
            var input = session.InputMetadata.Values.First(); var output = session.OutputMetadata.Values.First();
            bool Matches(int[] dimensions, int channels) => dimensions.Length == 4 &&
                (dimensions[0] == 1 || dimensions[0] == -1) && dimensions[1] == channels &&
                dimensions.Skip(2).All(x => x == -1 || x == model.InputSize);
            if (session.InputMetadata.Count != 1 || input.ElementType != typeof(float) || !Matches(input.Dimensions, 3) ||
                output.ElementType != typeof(float) || !Matches(output.Dimensions, 1))
                throw new InvalidDataException("抠图模型输入输出与预设不匹配。");
        }
        token.ThrowIfCancellationRequested();
        var directory = Path.Combine(root, model.Id + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, model.Id + ".onnx");
        var provenance = Path.Combine(directory, "source.txt");
        try
        {
            File.WriteAllText(provenance, $"Source: {model.Source}\nChecksum: {model.Hash}\nProfile: {model.Profile}; input: {model.InputSize}\nLicense: {(model.Profile == "U2Net" ? "Apache-2.0" : "MIT")}; see upstream U-2-Net / ZhengPeng7/BiRefNet and rembg.\n");
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path);
            return path;
        }
        catch
        {
            if (File.Exists(provenance)) File.Delete(provenance);
            Directory.Delete(directory, false);
            throw;
        }
    }
}
