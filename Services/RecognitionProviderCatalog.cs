namespace SnapSharp.Services;

internal static class RecognitionProviderCatalog
{
    internal sealed record Information(string Notice, string Website);
    internal static Information Translation(string provider) => provider switch
    {
        "Local" => new("本地推理，无 API 费用；Qwen2.5 1.5B 模型 Apache-2.0，llama.cpp 为 MIT。仅下载时访问官方分发源，翻译使用本机进程，不上传外部服务。", "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF"),
        "MyMemory" => new("API：公共免费额度有限，可能限流；具体免费额度、商业使用条款和付费方案请查看 MyMemory 官方说明。", "https://mymemory.translated.net/doc/usagelimits.php"),
        "Google" => new("API：当前使用 GTranslate 非官方网页适配，并非 Google Cloud Translation API。开源包免费不代表接口永久免费或稳定。下方链接是 Google 官方 Cloud API 定价，仅供评估官方服务，不代表当前适配器的配额。", "https://cloud.google.com/translate/pricing"),
        "LibreTranslate" => new("API：LibreTranslate 软件开源不代表托管 API 免费，是否收费及需要密钥取决于实例运营商。官方托管方案见官网；自定义地址请向对应运营商确认费用。", "https://portal.libretranslate.com/"),
        _ => throw new ArgumentException("未知翻译引擎。")
    };
    internal static string Website(string provider) => provider switch
    {
        "PaddleOCR" => "https://github.com/PaddlePaddle/PaddleOCR",
        "DirectML" => "https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html",
        _ => Translation(provider).Website
    };
}
