using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapSharp.Services;
using SnapSharp.Views;
using SnapSharp.Localization;

namespace SnapSharp.RegressionChecks;

internal static partial class Program
{
    private static void ModelStorageChecks()
    {
        var root = Path.Combine(Root, "model-storage", "models");
        var active = Path.Combine(root, "ppocr-active");
        var duplicate = Path.Combine(root, "ppocr-old-random-name");
        var formula = Path.Combine(root, "latex-ocr-legacy");
        var translation = Path.Combine(root, "local-translation");
        var runtime = Path.Combine(translation, "runtime");
        foreach (var directory in new[] { active, duplicate, formula, runtime }) Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(active, "model.onnx"), new byte[123]);
        File.WriteAllBytes(Path.Combine(duplicate, "model.onnx"), new byte[456]);
        File.WriteAllBytes(Path.Combine(formula, "encoder.onnx"), new byte[100]);
        File.WriteAllBytes(Path.Combine(runtime, "llama-server.exe"), new byte[50]);
        var currentTranslation = Path.Combine(translation, "current.gguf");
        var oldTranslation = Path.Combine(translation, "old.gguf");
        File.WriteAllBytes(currentTranslation, new byte[12]); File.WriteAllBytes(oldTranslation, new byte[34]);
        var partial = Path.Combine(root, "download.tmp"); File.WriteAllBytes(partial, new byte[56]);
        var references = new[] { Path.Combine(active, "model.onnx").ToUpperInvariant(), formula, currentTranslation,
            Path.Combine(runtime, "llama-server.exe") };
        var entries = ModelStorageService.Scan(root, references);
        Require(entries.Count == 7, "Storage scan lost a directory, loose file or translation asset.");
        Require(entries.Single(e => e.Path == active) is { Referenced: true, CanDelete: false, Bytes: 123 }, "Active OCR was not protected or counted.");
        Require(entries.Single(e => e.Path == formula).Referenced, "A directory reference was not protected.");
        Require(entries.Single(e => e.Path == duplicate) is { CanDelete: true, Bytes: 456 }, "Old directory cannot be cleaned independently.");
        Require(entries.Single(e => e.Path == oldTranslation).CanDelete && !entries.Single(e => e.Path == runtime).CanDelete,
            "Translation models and runtimes were not managed separately.");
        var deleted = new List<string>();
        void RecycleProbe(string path, bool directory) => deleted.Add(path);
        void Reject(string path, IReadOnlyCollection<string> paths)
        {
            var calls = deleted.Count;
            try { ModelStorageService.Delete(root, path, paths, RecycleProbe); throw new InvalidOperationException("Unsafe deletion was accepted: " + path); }
            catch (IOException) { }
            Require(deleted.Count == calls, "Rejected deletion reached the recycler.");
        }
        Reject(active, references); Reject(formula, references); Reject(root, []);
        Reject(Path.GetDirectoryName(root)!, []);
        Reject(Path.Combine(root, "..", "models-neighbor"), []);
        Reject(Path.Combine(active, "model.onnx"), []);
        Reject(duplicate, [Path.Combine(duplicate, "model.onnx")]); // Reference added after the initial scan.
        Reject(duplicate, [root]); // A configured directory also protects its descendants.
        ModelStorageService.Delete(root, duplicate, references, RecycleProbe);
        ModelStorageService.Delete(root, oldTranslation, references, RecycleProbe);
        Require(deleted.SequenceEqual(new[] { duplicate, oldTranslation }), "Valid deletion targeted the wrong items.");
        ModelStorageService.Delete(root, partial, references, (path, directory) =>
        {
            Require(!directory && path == partial, "File deletion used a directory operation.");
            File.Delete(path); // Generated fixture only; production always uses the shell recycler.
        });
        Require(!File.Exists(partial), "Deletion did not update the filesystem.");

        var link = Path.Combine(duplicate, "linked");
        try
        {
            CreateStorageJunction(link, active);
            Require(ModelStorageService.Scan(root, references).Single(e => e.Path == duplicate) is { Unsafe: true, CanDelete: false, Bytes: null },
                "Linked trees were traversed or offered for deletion.");
            Reject(duplicate, references);
            var alias = Path.Combine(Root, "model-storage-alias");
            CreateStorageJunction(alias, root);
            try { ModelStorageService.Scan(alias, references); throw new InvalidOperationException("Linked root was followed."); }
            catch (IOException) { }
            Reject(Path.Combine(alias, "ppocr-active"), []);
            Directory.Delete(alias);
        }
        finally { if (Directory.Exists(link)) Directory.Delete(link); }

        Require(ModelStorageService.Scan(Path.Combine(root, "missing"), []).Count == 0, "Absent model storage should be empty.");
        Console.WriteLine("PASS: model storage sizes, legacy folders, translation assets, reference protection, deletion boundary and link protection.");
        if (Environment.GetCommandLineArgs().Contains("--model-storage-only")
            || typeof(Program).Assembly.GetName().Name == "SnapSharp.ModelStorageChecks") ModelStorageWindowChecks(root, references);
    }

    private static void CreateStorageJunction(string link, string target)
    {
        // Junctions exercise the same reparse-point guard without requiring symlink privileges.
        // Both arguments are generated fixture paths under Root; no user paths are accepted.
        var start = new ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{link}\" \"{target}\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Require(process.ExitCode == 0, "Could not create fixture junction: " + output + error);
    }

    private static void ModelStorageWindowChecks(string root, IReadOnlyCollection<string> references)
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        System.Xml.Linq.XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var resources = System.Xml.Linq.XDocument.Load("App.xaml").Root!.Element(wpf + "Application.Resources")!.Elements().Single();
        resources.SetAttributeValue(System.Xml.Linq.XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml");
        resources.SetAttributeValue(System.Xml.Linq.XNamespace.Xmlns + "services", "clr-namespace:SnapSharp.Services;assembly=SnapSharp");
        app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resources.ToString(),
            new System.Windows.Markup.ParserContext { BaseUri = new Uri("pack://application:,,,/NimCap.Screenshot;component/") });
        using var theme = new ThemeService(); theme.Start();
        LocalizationService.Apply("zh-CN");
        Require(UiText.Get("ModelStorageTitle") == "管理已下载模型", "Chinese storage strings were corrupted.");
        using var hotkeys = new GlobalHotkeyHost();
        var settings = new SettingsService(Path.Combine(Root, "storage-settings.json"), Path.GetDirectoryName(root));
        settings.Current.OcrDetectionModel = Path.Combine(root, "ppocr-active", "model.onnx");
        settings.Current.CutoutModels["other"] = new() { Path = Path.Combine(root, "other-cutout", "model.onnx") };
        var preferences = new SettingsWindow(settings, new StartupRegistrationService(), hotkeys);
        preferences.OcrDetPath.Text = "models\\unsaved\\model.onnx";
        preferences.CutoutPath.Text = "models\\unsaved-cutout\\model.onnx";
        var paths = (IReadOnlyCollection<string>)typeof(SettingsWindow).GetMethod("ModelStorageReferences", PrivateInstance)!.Invoke(preferences, null)!;
        Require(paths.Contains(settings.Current.OcrDetectionModel) && paths.Contains(settings.Current.CutoutModels["other"].Path)
            && paths.Contains(settings.ResolveModelPath(preferences.OcrDetPath.Text)) && paths.Contains(settings.ResolveModelPath(preferences.CutoutPath.Text)),
            "Saved, unsaved or nondefault cutout references were lost.");
        preferences.Close();
        var window = new ModelStorageWindow(root, () => references);
        window.Show();
        PumpUntil(() => window.Entries.Items.Count > 0 && window.Actions.IsEnabled);
        Require(window.Title == "管理已下载模型", "Storage window XAML did not use the active language.");
        window.Entries.SelectedItem = window.Entries.Items.Cast<ModelStorageService.Entry>().First(entry => entry.Referenced);
        Require(!window.DeleteButton.IsEnabled && window.OpenButton.IsEnabled, "Referenced item exposes deletion.");
        window.Entries.SelectedItem = window.Entries.Items.Cast<ModelStorageService.Entry>().First(entry => entry.CanDelete);
        Require(window.DeleteButton.IsEnabled, "Old folder has no delete action.");
        foreach (var width in new[] { 640d, 840d })
        {
            window.Width = width; window.Height = 600;
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var content = (FrameworkElement)window.Content;
            Require(window.Entries.ActualWidth > 450 && window.Entries.ActualHeight > 160, "Storage list was clipped.");
            var deleteBounds = new Rect(window.DeleteButton.TranslatePoint(new System.Windows.Point(), content), window.DeleteButton.RenderSize);
            Require(deleteBounds.Right <= content.ActualWidth + 1 && deleteBounds.Bottom <= content.ActualHeight + 1, "Delete action is outside the window.");
            var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
                drawing.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            bitmap.Render(visual);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(Root, $"model-storage-{width}.png")); png.Save(output);
        }
        window.Close(); app.Shutdown();
        Console.WriteLine("PASS: saved/unsaved settings protection, Chinese storage UI, selected actions and compact/normal layouts.");
    }
}
