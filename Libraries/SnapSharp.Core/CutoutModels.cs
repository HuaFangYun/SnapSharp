namespace SnapSharp.Models;

public sealed record CutoutModelDefinition(string Id, string Label, string Profile, int InputSize,
    string Asset, string Hash, long MaximumBytes, string? SourceOverride = null)
{
    public string Source => SourceOverride ?? "https://github.com/danielgatis/rembg/releases/download/v0.0.0/" + Asset;
    public override string ToString() => Label;
}

public static class CutoutModels
{
    // URLs and MD5 values from rembg's model adapters. U2Net additionally uses pinned SHA256.
    public static IReadOnlyList<CutoutModelDefinition> All { get; } = new[]
    {
        new CutoutModelDefinition("u2netp", "快速预览 / CPU · U²-Net 轻量（4.4 MB）", "U2Net", 320,
            "u2netp.onnx", "309c8469258dda742793dce0ebea8e6dd393174f89934733ecc8b14c76f4ddd8", 5_000_000),
        new CutoutModelDefinition("birefnet-general-lite", "默认精修 · BiRefNet General Lite（224 MB）", "BiRefNet", 1024,
            "BiRefNet-general-bb_swin_v1_tiny-epoch_232.onnx", "4fab47adc4ff364be1713e97b7e66334", 230_000_000),
        new CutoutModelDefinition("birefnet-portrait", "人像精修 · BiRefNet Portrait（973 MB）", "BiRefNet", 1024,
            "BiRefNet-portrait-epoch_150.onnx", "c3a64a6abf20250d090cd055f12a3b67", 980_000_000),
        new CutoutModelDefinition("birefnet-matting", "发丝 / 玻璃精修 · BiRefNet Matting（941 MB，社区导出）", "BiRefNetMatting", 1024,
            "birefnet-matting.onnx", "f0843e38f6a4e88efc8c5fad4178ad7ed6c818346ce12f82e7b579324fe7e0c5", 950_000_000,
            "https://huggingface.co/emrikol/birefnet-matting-onnx/resolve/0d58d809b3a360b44c556223d2f5812aeace9ba3/birefnet-matting.onnx"),
        new CutoutModelDefinition("birefnet-general", "高质量通用 · BiRefNet General 完整版（973 MB）", "BiRefNet", 1024,
            "BiRefNet-general-epoch_244.onnx", "7a35a0141cbbc80de11d9c9a28f52697", 980_000_000),
        new CutoutModelDefinition("birefnet-hr-matting", "高质量发丝 / 透明精修 · HR Matting 2048（932 MB，社区导出）", "BiRefNetAlpha", 2048,
            "model.onnx", "a548a1f3307250766bef50b174026b3cfa39a695bc1e42ec131c14fe82797ffd", 940_000_000,
            "https://huggingface.co/PinkPixel/birefnet-hr-matting-onnx/resolve/792518b9d4dfe9793891712677de25e554f948c6/model.onnx")
    };

    public static void Migrate(AppSettings settings)
    {
        settings.CutoutModels ??= new();
        if (!All.Any(x => x.Id == settings.CutoutDefaultModelId)) settings.CutoutDefaultModelId = "birefnet-general-lite";
        if (settings.CutoutModels.Count == 0 && !string.IsNullOrWhiteSpace(settings.CutoutModel))
        {
            var id = settings.CutoutProfile == "U2Net" ? "u2netp" : settings.CutoutDefaultModelId;
            settings.CutoutModels[id] = new() { Path = settings.CutoutModel, Profile = settings.CutoutProfile, InputSize = settings.CutoutInputSize };
            settings.CutoutDefaultModelId = id;
        }
    }

    public static AppSettings ForModel(AppSettings settings, string id)
    {
        // Snapshot: switching in the cutout window must never modify the saved default.
        var copy = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        copy.CutoutModel = "";
        if (copy.CutoutModels.TryGetValue(id, out var model) && model is not null)
        { copy.CutoutModel = model.Path; copy.CutoutProfile = model.Profile; copy.CutoutInputSize = model.InputSize; }
        return copy;
    }
}
