using System.IO;
using System.Text.Json.Nodes;
using SnapSharp.Models;
using SnapSharp.Services;

namespace SnapSharp.RegressionChecks;

internal static partial class Program
{
    private static void PortableModelPathChecks()
    {
        var original = Path.Combine(Root, "portable original");
        var moved = Path.Combine(Root, "portable moved");
        var external = Path.Combine(Root, "external-model.onnx");
        var model = Path.Combine(original, "models", "ocr", "model.onnx");
        Directory.CreateDirectory(Path.GetDirectoryName(model)!);
        File.WriteAllText(model, "model fixture");
        File.WriteAllText(external, "external fixture");
        var settings = new SettingsService(Path.Combine(original, "settings.json"), original);
        settings.Current.OcrDetectionModel = model;
        settings.Current.OcrRecognitionModel = model;
        settings.Current.OcrDictionary = model;
        settings.Current.TesseractDataDirectory = Path.GetDirectoryName(model)!;
        settings.Current.CutoutModel = model;
        settings.Current.CutoutDefaultModelId = "u2netp";
        settings.Current.CutoutModels["u2netp"] = new() { Path = model };
        settings.Current.CutoutModels["external"] = new() { Path = external };
        settings.Current.TableStructureModel = model;
        settings.Current.FormulaModelDirectory = Path.GetDirectoryName(model)!;
        settings.Current.LocalTranslationModel = model;
        settings.Current.LocalTranslationRuntime = Path.Combine(original, "models", "llama", "llama-server.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(settings.Current.LocalTranslationRuntime)!);
        File.WriteAllText(settings.Current.LocalTranslationRuntime, "runtime fixture");
        settings.Save();
        var stored = JsonNode.Parse(File.ReadAllText(Path.Combine(original, "settings.json")))!;
        foreach (var name in new[] { "OcrDetectionModel", "OcrRecognitionModel", "OcrDictionary", "TesseractDataDirectory",
            "CutoutModel", "TableStructureModel", "FormulaModelDirectory", "LocalTranslationModel", "LocalTranslationRuntime" })
            Require(!Path.IsPathRooted(stored[name]!.GetValue<string>()), "Internal path was not made portable: " + name);
        Require(stored["CutoutModels"]!["external"]!["Path"]!.GetValue<string>() == external &&
            !Path.IsPathRooted(stored["CutoutModels"]!["u2netp"]!["Path"]!.GetValue<string>()), "Nested model paths have wrong portability.");
        Require(settings.Current.OcrDetectionModel == model && settings.Current.CutoutModels["u2netp"].Path == model,
            "Saving changed active runtime paths.");
        Require(ModelPathService.ForStorage(original + "-neighbor\\model.onnx", original) == original + "-neighbor\\model.onnx",
            "A neighboring folder was mistaken for an internal model.");
        Require(settings.ResolveModelPath("") == "" && settings.StoreModelPath("") == "", "Empty model paths were changed.");
        Require(settings.StoreModelPath("models\\ocr\\model.onnx") == "models\\ocr\\model.onnx", "Relative input did not round-trip.");
        Require(settings.StoreModelPath("..\\external-model.onnx") == external, "External relative input was not made absolute.");

        // Old absolute settings remain readable and upgrade when saved.
        stored["OcrDetectionModel"] = model;
        File.WriteAllText(Path.Combine(original, "settings.json"), stored.ToJsonString());
        settings.Load();
        Require(settings.Current.OcrDetectionModel == model, "Legacy absolute model configuration failed to load.");
        settings.Save();

        var legacy = Path.Combine(Root, "unused legacy");
        var location = new DataDirectoryService(original, legacy);
        // Simulate old location metadata to verify its portable upgrade as well.
        File.WriteAllText(Path.Combine(original, "data-location.json"), new JsonObject
        { ["CurrentDirectory"] = original, ["NextDirectory"] = null }.ToJsonString());
        location = new DataDirectoryService(original, legacy);
        location.ApplyPendingChange();
        Require(JsonNode.Parse(File.ReadAllText(Path.Combine(original, "data-location.json")))!["CurrentDirectory"]!.GetValue<string>() == ".",
            "The exe data location still prevents moving a portable folder.");

        Require(Path.GetFullPath(original).StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            Path.GetFullPath(moved).StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Fixture move escapes its root.");
        Directory.Move(original, moved);
        var movedLocation = new DataDirectoryService(moved, legacy);
        Require(movedLocation.Root == moved, "Moved data location points at the old executable folder.");
        var restored = new SettingsService(Path.Combine(moved, "settings.json"), moved);
        var workingDirectory = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = Path.GetPathRoot(Root)!;
            restored.Load();
            Require(restored.Current.OcrDetectionModel == Path.Combine(moved, "models", "ocr", "model.onnx") &&
                File.Exists(restored.Current.LocalTranslationRuntime) && File.Exists(restored.Current.CutoutModels["u2netp"].Path) &&
                restored.Current.CutoutModels["external"].Path == external, "Model paths depend on the old folder or current working directory.");
        }
        finally { Environment.CurrentDirectory = workingDirectory; }

        // Moving the data folder must rebase relative paths against the unchanged exe folder.
        var data = Path.Combine(Root, "separate data");
        movedLocation.ScheduleChange(data);
        movedLocation.ApplyPendingChange();
        var migrated = new SettingsService(Path.Combine(data, "settings.json"), moved);
        migrated.Load();
        Require(migrated.Current.OcrDetectionModel == Path.Combine(data, "models", "ocr", "model.onnx") &&
            File.Exists(migrated.Current.CutoutModels["u2netp"].Path) &&
            migrated.Current.CutoutModels["external"].Path == external, "Data migration lost portable or external model paths.");
        Require(Path.IsPathFullyQualified(JsonNode.Parse(File.ReadAllText(Path.Combine(data, "settings.json")))!["LocalTranslationRuntime"]!.GetValue<string>()),
            "External data must store an absolute runtime path.");
        movedLocation.ScheduleChange(moved);
        movedLocation.ApplyPendingChange();
        Require(!Path.IsPathRooted(JsonNode.Parse(File.ReadAllText(Path.Combine(moved, "settings.json")))!["LocalTranslationRuntime"]!.GetValue<string>()),
            "Migration back to the exe folder did not restore portable paths.");
        Console.WriteLine("PASS: all model/runtime paths, portable folder relocation, external paths, legacy settings, working-directory independence and data migration round-trip.");
    }

    private static void PortableModelPathWindowChecks(GlobalHotkeyHost hotkeys)
    {
        var executable = Path.Combine(Root, "portable-ui");
        var directory = Path.Combine(executable, "models", "formula");
        Directory.CreateDirectory(directory);
        foreach (var name in new[] { "encoder.onnx", "decoder.onnx", "image_resizer.onnx", "tokenizer.json" })
            File.WriteAllText(Path.Combine(directory, name), "UI validation fixture");
        var model = Path.Combine(directory, "encoder.onnx");
        var settings = new SettingsService(Path.Combine(executable, "settings.json"), executable);
        settings.Current.OcrDetectionModel = model;
        settings.Current.TableStructureModel = model;
        settings.Current.FormulaModelDirectory = directory;
        settings.Current.LocalTranslationModel = model;
        var window = new SnapSharp.Views.SettingsWindow(settings, new StartupRegistrationService(), hotkeys);
        try
        {
            var detection = (System.Windows.Controls.TextBox)window.FindName("OcrDetPath");
            Require(detection.Text == "models\\formula\\encoder.onnx", "Settings UI did not display a portable path.");
            detection.Text = ".\\models\\formula\\decoder.onnx";
            ((System.Windows.Controls.TextBox)window.FindName("TesseractDirectory")).Text = "models\\formula";
            var candidate = window.BuildCandidate();
            Require(candidate.OcrDetectionModel == Path.Combine(directory, "decoder.onnx") &&
                candidate.TableStructureModel == model && candidate.FormulaModelDirectory == directory &&
                candidate.TesseractDataDirectory == directory && candidate.LocalTranslationModel == model,
                "Settings did not validate relative model files/directories against the executable folder.");
        }
        finally { window.Close(); }
        Console.WriteLine("PASS: settings display, relative model input and model/dictionary/directory validation.");
    }
}
