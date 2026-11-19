using L = NimCap.Screenshot.Localization.UiText;

namespace NimCap.Screenshot.Models;

public sealed record WatermarkModelDefinition(string Id, string LabelKey, string Profile, int InputSize,
    string Source, string Sha256, long Size, string LicenseFile)
{
    public string Label => L.Get(LabelKey);
    public override string ToString() => Label;
}

public static class WatermarkModels
{
    public sealed record Asset(string Name, string Sha256, long Size);
    public const string MoebiusRevision = "5bf1ef5d2861ec01a727183a3f95dc64f352120e";
    public static IReadOnlyList<Asset> MoebiusAssets { get; } = new[]
    {
        new Asset("unet.onnx", "e3f90f52f72378339b990459fadb29a68d3c7b5c6851545ba42774f489160b08", 906698976),
        new Asset("vae_encoder.onnx", "b8b81d41e757222a0707665ba9d826703987855e5bed056036b90b988968042f", 136757093),
        new Asset("vae_decoder.onnx", "d90ef0b7f6c8c8b7234459c8b449d70be0033bf1576c842e8b9991baf3934280", 198078671)
    };
    public static string MoebiusSource(string name) => $"https://huggingface.co/simonw/Moebius-ONNX/resolve/{MoebiusRevision}/{name}";
    // Pin both revisions and bytes; these are separate contracts, never interchangeable ONNX files.
    public static IReadOnlyList<WatermarkModelDefinition> All { get; } = new[]
    {
        new WatermarkModelDefinition("migan", "WatermarkModelFast", "MiganPipeline", 512,
            "https://huggingface.co/andraniksargsyan/migan/resolve/406830d0fa60666da0071c342ad2fbc8f30c5c64/migan_pipeline_v2.onnx",
            "6f1f3530a1a2324b19752018ce756088b07973cda8d7d890034ace5c8a48c40b", 28079181, "MI-GAN-LICENSE.txt"),
        new WatermarkModelDefinition("lama", "WatermarkModelDetail", "Lama", 512,
            "https://huggingface.co/Carve/LaMa-ONNX/resolve/c3c0c9e468934d62e79c329e35d82dd09ff8c444/lama_fp32.onnx",
            "1faef5301d78db7dda502fe59966957ec4b79dd64e16f03ed96913c7a4eb68d6", 208044816, "LaMa-ONNX-MODEL-CARD.md"),
        new WatermarkModelDefinition("moebius", "WatermarkModelMoebius", "Moebius", 512,
            MoebiusSource("unet.onnx"), MoebiusAssets[0].Sha256, MoebiusAssets.Sum(x => x.Size), "Moebius-ONNX-MODEL-CARD.md")
    };
    public const string DetectorId = "slbr";
    public static WatermarkModelDefinition Detector { get; } = new(DetectorId, "WatermarkDetectorModel", "Slbr", 256,
        "https://drive.usercontent.google.com/download?id=1uTCzubnWZtu3HIXaK8xsXX-7x302ss13&export=download&confirm=t",
        "c151347105c2ecfdba1887223bc4b8732ce5d5b828ff2005d5086174c0faa0ec", 86773294, "SLBR-MODEL-CARD.md");
    public static WatermarkModelDefinition Find(string id) => id == DetectorId ? Detector : All.FirstOrDefault(x => x.Id == id)
        ?? throw new ArgumentException(L.Get("WatermarkInvalidModel"));
}
