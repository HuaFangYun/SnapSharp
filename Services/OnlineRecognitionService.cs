using SnapSharp.Models;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using GTranslate.Translators;

namespace SnapSharp.Services;

/// <summary>No request is created until both the saved policy and per-request consent pass.</summary>
public sealed class OnlineRecognitionService : IDisposable
{
    public const string MyMemoryEndpoint = "https://api.mymemory.translated.net/get";
    public const string GoogleEndpoint = "https://translate.googleapis.com/translate_a/single";
    public static Uri TranslationUri(AppSettings settings) => Endpoint(settings.TranslationProvider switch
    {
        "MyMemory" => MyMemoryEndpoint,
        "Google" => GoogleEndpoint,
        "LibreTranslate" => settings.TranslationEndpoint,
        _ => throw new ArgumentException("未知翻译引擎，请在设置中重新选择。")
    });
    private readonly HttpClient _client;
    public OnlineRecognitionService(HttpMessageHandler? handler = null) => _client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(25) };
    public static Uri Endpoint(string text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) || uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
            throw new ArgumentException("服务地址必须为 HTTPS，或本机回环 HTTP 地址，且不能包含账号信息。");
        return uri;
    }
    public async Task<string> Translate(string text, AppSettings settings, bool consent, CancellationToken token)
    {
        if (!settings.AllowOnlineTranslation || !consent) throw new InvalidOperationException("尚未授权在线翻译；未发送任何内容。");
        if (text.Length is < 1 or > 50000) throw new ArgumentException("翻译文本需为 1–50000 个字符。");
        token.ThrowIfCancellationRequested();
        _ = TranslationUri(settings);
        if (settings.TranslationProvider == "Google")
        {
            if (text.Length > 5000) throw new ArgumentException("Google 免费接口单次最多 5000 字符，请选中一段文字翻译。");
            using var client = new HttpClient(new TranslationPackageHandler(_client, token)) { Timeout = TimeSpan.FromSeconds(25) };
            using var translator = new GoogleTranslator(client);
            var translated = await translator.TranslateAsync(text, settings.TranslationTarget, settings.TranslationSource == "auto" ? null : settings.TranslationSource);
            token.ThrowIfCancellationRequested();
            return translated.Translation;
        }
        if (settings.TranslationProvider == "MyMemory")
        {
            if (settings.TranslationSource == "auto") throw new ArgumentException("免密钥翻译需指定源语言，例如 en 英文或 zh 中文，不支持 auto。");
            if (Encoding.UTF8.GetByteCount(text) > 500) throw new ArgumentException("免密钥翻译单次最多 500 UTF-8 字节，请选中一小段文字再翻译，或配置自有 LibreTranslate 服务。");
            var address = MyMemoryEndpoint + "?q=" + Uri.EscapeDataString(text) + "&langpair=" + Uri.EscapeDataString(settings.TranslationSource + "|" + settings.TranslationTarget);
            using var result = await _client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, token);
            using var document = await Read(result, token);
            if (document.RootElement.GetProperty("responseStatus").ToString() != "200")
                throw new InvalidOperationException("翻译服务拒绝请求或免费额度已用尽：" + document.RootElement.GetProperty("responseDetails").GetString());
            return System.Net.WebUtility.HtmlDecode(document.RootElement.GetProperty("responseData").GetProperty("translatedText").GetString() ?? throw new InvalidDataException("服务未返回译文。"));
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(settings.TranslationEndpoint))
        { Content = JsonContent.Create(new { q = text, source = settings.TranslationSource, target = settings.TranslationTarget, format = "text" }) };
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        using var json = await Read(response, token);
        return json.RootElement.GetProperty("translatedText").GetString() ?? throw new InvalidDataException("服务未返回 translatedText。");
    }
    public async Task<Uri> Search(byte[] png, AppSettings settings, bool consent, CancellationToken token)
    {
        if (!settings.AllowImageSearchUpload || !consent) throw new InvalidOperationException("尚未授权识图上传；未发送图像。");
        if (png.Length is < 1 or > 8_000_000) throw new ArgumentException("识图上传图片最多 8 MB，请先裁剪或缩小。");
        using var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(png); part.Headers.ContentType = new("image/png"); content.Add(part, "image", "selection.png");
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(settings.ImageSearchEndpoint)) { Content = content };
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        using var json = await Read(response, token);
        var result = Endpoint(json.RootElement.GetProperty("url").GetString() ?? "");
        return result;
    }
    private static async Task<JsonDocument> Read(HttpResponseMessage response, CancellationToken token)
    {
        response.EnsureSuccessStatusCode();
        if (response.Headers.Location is not null || (int)response.StatusCode is >= 300 and < 400) throw new InvalidOperationException("不自动跟随服务重定向，请确认实际接收地址。");
        using var stream = await response.Content.ReadAsStreamAsync(token); using var output = new MemoryStream(); var buffer = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(buffer, token)) != 0) { if (output.Length + read > 1_000_000) throw new InvalidDataException("服务响应过大。"); output.Write(buffer, 0, read); }
        return JsonDocument.Parse(output.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
    }
    public void Dispose() => _client.Dispose();

    // Keep NuGet translation behind the same consent, host, redirect, cancellation and size limits.
    private sealed class TranslationPackageHandler(HttpClient transport, CancellationToken operation) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is null || request.RequestUri.Scheme != "https" || request.RequestUri.Host != "translate.googleapis.com" || request.RequestUri.AbsolutePath != "/translate_a/single")
                throw new InvalidOperationException("翻译包尝试访问未确认的接收地址，已阻止。");
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(operation, cancellationToken);
            // Each HttpClient marks its input request as sent; forward a separate message.
            using var forwarded = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers) forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (request.Content is not null)
            {
                forwarded.Content = new ByteArrayContent(await request.Content.ReadAsByteArrayAsync(linked.Token));
                foreach (var header in request.Content.Headers) forwarded.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            var response = await transport.SendAsync(forwarded, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            try
            {
                response.EnsureSuccessStatusCode();
                if (response.Headers.Location is not null || (int)response.StatusCode is >= 300 and < 400)
                    throw new InvalidOperationException("翻译接口发生重定向，未跟随。");
                using var stream = await response.Content.ReadAsStreamAsync(linked.Token); using var data = new MemoryStream();
                var buffer = new byte[8192]; int read;
                while ((read = await stream.ReadAsync(buffer, linked.Token)) != 0)
                {
                    if (data.Length + read > 1_000_000) throw new InvalidDataException("翻译响应过大。");
                    data.Write(buffer, 0, read);
                }
                var original = response.Content;
                var content = new ByteArrayContent(data.ToArray());
                content.Headers.ContentType = new("application/json"); response.Content = content; original.Dispose();
                return response;
            }
            catch { response.Dispose(); throw; }
        }
    }
}
