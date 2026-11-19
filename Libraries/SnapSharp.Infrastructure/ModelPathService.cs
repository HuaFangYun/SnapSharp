using System.Reflection;
using System.Text.Json.Nodes;
using SnapSharp.Models;

namespace SnapSharp.Services;

/// <summary>Model paths on disk are relative to the executable, never the working directory.</summary>
public static class ModelPathService
{
    private static readonly PropertyInfo[] Properties = new[]
    {
        nameof(AppSettings.OcrDetectionModel), nameof(AppSettings.OcrRecognitionModel), nameof(AppSettings.OcrDictionary),
        nameof(AppSettings.TesseractDataDirectory), nameof(AppSettings.CutoutModel),
        nameof(AppSettings.TableStructureModel), nameof(AppSettings.FormulaModelDirectory),
        nameof(AppSettings.LocalTranslationModel), nameof(AppSettings.LocalTranslationRuntime)
    }.Select(name => typeof(AppSettings).GetProperty(name)!).ToArray();

    public static string Resolve(string? path, string executableDirectory)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        path = path.Trim();
        if (Path.IsPathFullyQualified(path)) return Path.GetFullPath(path);
        // Drive-relative and root-relative paths depend on process state and are not portable.
        if (Path.IsPathRooted(path)) throw new ArgumentException(SnapSharp.Localization.UiText.Get("ModelPathMustBeRelativeOrAbsolute"), nameof(path));
        return Path.GetFullPath(path, Path.GetFullPath(executableDirectory));
    }

    public static string ForStorage(string? path, string executableDirectory)
    {
        var full = Resolve(path, executableDirectory);
        if (full.Length == 0) return "";
        var relative = Path.GetRelativePath(executableDirectory, full);
        return Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? full : relative;
    }

    internal static void Resolve(AppSettings settings, string executableDirectory)
    {
        foreach (var property in Properties)
            property.SetValue(settings, Resolve((string?)property.GetValue(settings), executableDirectory));
        foreach (var model in settings.CutoutModels.Values)
            if (model is not null) model.Path = Resolve(model.Path, executableDirectory);
        foreach (var id in settings.WatermarkModels.Keys.ToArray())
            settings.WatermarkModels[id] = Resolve(settings.WatermarkModels[id], executableDirectory);
    }

    public static IEnumerable<string> ConfiguredPaths(AppSettings settings) =>
        Properties.Select(property => (string?)property.GetValue(settings) ?? "")
            .Concat(settings.CutoutModels.Values.Where(model => model is not null).Select(model => model.Path))
            .Concat(settings.WatermarkModels.Values);

    // Work on the serialized snapshot so saving never changes paths used by active inference.
    internal static void Transform(JsonObject settings, Func<string, string> transform)
    {
        foreach (var property in Properties) Change(settings, property.Name);
        if (settings[nameof(AppSettings.CutoutModels)] is JsonObject models)
            foreach (var entry in models)
                if (entry.Value is JsonObject model) Change(model, nameof(CutoutModelConfiguration.Path));
        if (settings[nameof(AppSettings.WatermarkModels)] is JsonObject watermarkModels)
            foreach (var entry in watermarkModels.ToArray()) Change(watermarkModels, entry.Key);

        void Change(JsonObject obj, string name)
        {
            if (obj[name] is JsonValue value && value.TryGetValue<string>(out var path))
                obj[name] = transform(path);
        }
    }
}
