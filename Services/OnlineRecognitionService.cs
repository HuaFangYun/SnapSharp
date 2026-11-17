using L = SnapSharp.Localization.UiText;
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
        _ => throw new ArgumentException(L.Get("UnknownTranslationEngineSelectAnEngineAgain"))
    });
    private readonly HttpClient _client;
    public OnlineRecognitionService(HttpMessageHandler? handler = null) => _client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(25) };
    public static Uri Endpoint(string text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) || uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
            throw new ArgumentException(L.Get("ServiceURLMustUseHTTPSOrLoopback"));
        return uri;
    }
    public async Task<string> Translate(string text, AppSettings settings, bool consent, CancellationToken token)
    {
        if (!settings.AllowOnlineTranslation || !consent) throw new InvalidOperationException(L.Get("OnlineTranslationIsNotAuthorizedNoContent"));
        if (text.Length is < 1 or > 50000) throw new ArgumentException(L.Get("TranslationTextMustContain150000Characters"));
        token.ThrowIfCancellationRequested();
        _ = TranslationUri(settings);
        if (settings.TranslationProvider == "Google")
        {
            if (text.Length > 5000) throw new ArgumentException(L.Get("GoogleSupportsUpTo5000CharactersPer"));
            using var client = new HttpClient(new TranslationPackageHandler(_client, token)) { Timeout = TimeSpan.FromSeconds(25) };
            using var translator = new GoogleTranslator(client);
            var translated = await translator.TranslateAsync(text, settings.TranslationTarget, settings.TranslationSource == "auto" ? null : settings.TranslationSource);
            token.ThrowIfCancellationRequested();
            return translated.Translation;
        }
        if (settings.TranslationProvider == "MyMemory")
        {
            if (settings.TranslationSource == "auto") throw new ArgumentException(L.Get("KeylessTranslationRequiresASourceLanguageSuch"));
            if (Encoding.UTF8.GetByteCount(text) > 500) throw new ArgumentException(L.Get("KeylessTranslationSupportsUpTo500UTF"));
            var address = MyMemoryEndpoint + "?q=" + Uri.EscapeDataString(text) + "&langpair=" + Uri.EscapeDataString(settings.TranslationSource + "|" + settings.TranslationTarget);
            using var result = await _client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, token);
            using var document = await Read(result, token);
            if (document.RootElement.GetProperty("responseStatus").ToString() != "200")
                throw new InvalidOperationException(L.Get("TranslationServiceRejectedTheRequestOrThe") + document.RootElement.GetProperty("responseDetails").GetString());
            return System.Net.WebUtility.HtmlDecode(document.RootElement.GetProperty("responseData").GetProperty("translatedText").GetString() ?? throw new InvalidDataException(L.Get("TheServiceDidNotReturnATranslation")));
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(settings.TranslationEndpoint))
        { Content = JsonContent.Create(new { q = text, source = settings.TranslationSource, target = settings.TranslationTarget, format = "text" }) };
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        using var json = await Read(response, token);
        return json.RootElement.GetProperty("translatedText").GetString() ?? throw new InvalidDataException(L.Get("TheServiceDidNotReturnTranslatedText"));
    }
    public async Task<Uri> Search(byte[] png, AppSettings settings, bool consent, CancellationToken token)
    {
        if (!settings.AllowImageSearchUpload || !consent) throw new InvalidOperationException(L.Get("ImageSearchUploadIsNotAuthorizedNo"));
        if (png.Length is < 1 or > 8_000_000) throw new ArgumentException(L.Get("ImageSearchUploadsAreLimitedTo8"));
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
        if (response.Headers.Location is not null || (int)response.StatusCode is >= 300 and < 400) throw new InvalidOperationException(L.Get("ServiceRedirectsAreNotFollowedAutomaticallyConfirm"));
        using var stream = await response.Content.ReadAsStreamAsync(token); using var output = new MemoryStream(); var buffer = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(buffer, token)) != 0) { if (output.Length + read > 1_000_000) throw new InvalidDataException(L.Get("ServiceResponseIsTooLarge")); output.Write(buffer, 0, read); }
        return JsonDocument.Parse(output.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
    }
    public void Dispose() => _client.Dispose();

    // Keep NuGet translation behind the same consent, host, redirect, cancellation and size limits.
    private sealed class TranslationPackageHandler(HttpClient transport, CancellationToken operation) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is null || request.RequestUri.Scheme != "https" || request.RequestUri.Host != "translate.googleapis.com" || request.RequestUri.AbsolutePath != "/translate_a/single")
                throw new InvalidOperationException(L.Get("TheTranslationPackageAttemptedToAccessAn"));
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
                    throw new InvalidOperationException(L.Get("TranslationEndpointRedirectedTheRedirectWasNot"));
                using var stream = await response.Content.ReadAsStreamAsync(linked.Token); using var data = new MemoryStream();
                var buffer = new byte[8192]; int read;
                while ((read = await stream.ReadAsync(buffer, linked.Token)) != 0)
                {
                    if (data.Length + read > 1_000_000) throw new InvalidDataException(L.Get("TranslationResponseIsTooLarge"));
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
