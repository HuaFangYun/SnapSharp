using L = SnapSharp.Localization.UiText;
namespace SnapSharp.Services;

internal static class RecognitionProviderCatalog
{
    internal sealed record Information(string Notice, string Website);
    internal static Information Translation(string provider) => provider switch
    {
        "Local" => new(L.Get("LocalInferenceHasNoAPIFeeQwen2"), "https://huggingface.co/bartowski/Qwen_Qwen3.5-4B-GGUF"),
        "MyMemory" => new(L.Get("APILimitedPublicFreeQuotaAndPossible"), "https://mymemory.translated.net/doc/usagelimits.php"),
        "Google" => new(L.Get("APIGTranslateUsesAnUnofficialGoogleWeb"), "https://cloud.google.com/translate/pricing"),
        "LibreTranslate" => new(L.Get("APILibreTranslateBeingOpenSourceDoesNot"), "https://portal.libretranslate.com/"),
        _ => throw new ArgumentException(L.Get("UnknownTranslationEngine"))
    };
    internal static string Website(string provider) => provider switch
    {
        "PaddleOCR" => "https://github.com/PaddlePaddle/PaddleOCR",
        "DirectML" => "https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html",
        _ => Translation(provider).Website
    };
}
