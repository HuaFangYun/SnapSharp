using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SnapSharp.Localization;
using SnapSharp.Models;
using SnapSharp.Views;

namespace SnapSharp.Services;

internal static class LocalizationSelfTest
{
    public static string Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var previous = UiText.Culture;
        var numberCulture = CultureInfo.CurrentCulture;
        try
        {
            var english = UiText.Resources.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, true)!;
            var chinese = UiText.Resources.GetResourceSet(CultureInfo.GetCultureInfo("zh-CN"), true, false)!;
            var keys = english.Cast<DictionaryEntry>().Select(entry => (string)entry.Key).ToHashSet();
            Require(keys.SetEquals(chinese.Cast<DictionaryEntry>().Select(entry => (string)entry.Key)), "Resource key parity");
            foreach (var key in keys)
            {
                var en = english.GetString(key)!;
                var zh = chinese.GetString(key)!;
                Require(!en.Any(c => c is >= '\u4e00' and <= '\u9fff'), "Untranslated English: " + key);
                static string[] Placeholders(string value) => Regex.Matches(value, @"(?<!\{)\{\d+(?:,[^}:]+)?(?::[^}]+)?\}(?!\})")
                    .Select(match => match.Value).Order().ToArray();
                Require(Placeholders(en).SequenceEqual(Placeholders(zh)), "Placeholder parity: " + key);
                if (Placeholders(en).Length > 0) { CompositeFormat.Parse(en); CompositeFormat.Parse(zh); }
            }
            Require(LocalizationService.ResolveCulture("", CultureInfo.GetCultureInfo("zh-Hans")).Name == "zh-CN", "Follow Chinese system");
            Require(LocalizationService.ResolveCulture("", CultureInfo.GetCultureInfo("en-GB")).Name == "en", "Follow English system");
            Require(LocalizationService.ResolveCulture("fr-CA").Name == "en", "Unsupported language fallback");
            Require(LocalizationService.ResolveCulture("not_a_culture").Name == "en", "Invalid preference fallback");
            Require(UiText.Resources.GetString("Save", CultureInfo.GetCultureInfo("fr-CA")) == "Save", "ResourceManager fallback");
            Require(JsonSerializer.Deserialize<AppSettings>("{}")!.InterfaceLanguage == "", "Old settings compatibility");
            Require(TrayService.RunLocalizationSelfTest(), "Live tray localization");

            using var hotkey = new GlobalHotkeyHost();
            var settings = new SettingsService();
            var description = OutputFormat.Find(".png");
            var model = CutoutModels.All[0];
            foreach (var code in new[] { "en", "zh-CN" })
            {
                LocalizationService.Apply(code);
                Require(CultureInfo.CurrentCulture.Equals(numberCulture), "UI language preserves numeric parsing culture");
                Require(description.Description == UiText.Get(description.DescriptionKey) && model.Label == UiText.Get(model.LabelKey), "Model metadata follows culture");
                Require(MenuIcons.Kind(UiText.Get("RegionCapture")) == MahApps.Metro.IconPacks.PackIconLucideKind.Scan, "Menu icons follow resource identity");
                var window = new SettingsWindow(settings, new StartupRegistrationService(), hotkey);
                try
                {
                    Flush();
                    Require(window.Title == UiText.Get("SnapSharpSettings"), $"WPF resource lookup: {code}; actual '{window.Title}', expected '{UiText.Get("SnapSharpSettings")}'");
                    var languageBox = (System.Windows.Controls.ComboBox)window.FindName("InterfaceLanguageBox");
                    languageBox.SelectedValue = code;
                    Require(languageBox.SelectedItem.ToString() == (code == "en" ? "English" : "简体中文"), "Readable language selection");
                    var candidate = window.BuildCandidate();
                    Require(candidate.InterfaceLanguage == code, "Selected language candidate");
                    Require(settings.Current.InterfaceLanguage == "", "Unsaved preference does not mutate current settings");
                    Require(JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(candidate))!.InterfaceLanguage == code, "Language JSON roundtrip");
                    foreach (var quality in new[] { "流畅", "标准", "高清" })
                    {
                        var box = (System.Windows.Controls.ComboBox)window.FindName("RecordingQuality");
                        box.SelectedItem = box.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, quality));
                        var recording = window.BuildCandidate().Recording;
                        Require(recording.Quality == quality, "Quality identifier must not be translated");
                        recording.Validate();
                    }
                    window.SelectCategory("Shield");
                    var categories = (System.Windows.Controls.TabControl)window.FindName("Categories");
                    Require(Equals(((TabItem)categories.SelectedItem).Tag, "Shield"), "Language-independent category navigation");
                    foreach (var category in new[] { "Settings", "ScanText", "Video" })
                    {
                        window.SelectCategory(category);
                        Render(window, 1040, 820, Path.Combine(outputDirectory, $"settings-{category}-{code}.png"));
                    }
                    window.SelectCategory("Settings");
                    Render(window, 700, 560, Path.Combine(outputDirectory, $"settings-compact-{code}.png"));
                    // Existing XAML controls update in place when the language changes.
                    LocalizationService.Apply(code == "en" ? "zh-CN" : "en");
                    Flush();
                    Require(window.Title == UiText.Get("SnapSharpSettings"), "Live XAML language switch");
                }
                finally { window.Close(); }
                LocalizationService.Apply(code);
                RenderFeatureWindows(settings, code, outputDirectory);
            }
            return $"PASS: {keys.Count} bilingual resource keys, placeholders, fallback, system language, settings compatibility/roundtrip/cancel, stable recording values, live XAML/tray updates, icons, model labels, 8 settings previews and 16 feature window previews.";
        }
        finally { LocalizationService.Apply(previous.Name); }
    }

    private static void Flush() => Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void RenderFeatureWindows(SettingsService settings, string code, string outputDirectory)
    {
        var pixels = Enumerable.Repeat((byte)210, 96 * 64 * 4).ToArray();
        for (var index = 3; index < pixels.Length; index += 4) pixels[index] = 255;
        var image = BitmapSource.Create(96, 64, 96, 96, PixelFormats.Pbgra32, null, pixels, 96 * 4);
        image.Freeze();
        var images = new SkiaImageService();
        var region = new System.Drawing.Rectangle(0, 0, 96, 64);
        Func<Window>[] factories =
        [
            () => new RecognitionWindow(image, settings, autoRun: false),
            () => new SaveImageWindow(image, images, settings),
            () => new RecordingWindow(region, settings),
            () => new ScrollCaptureWindow(region, image),
            () => new EditorWindow(new CaptureResult(image, region, region), _ => { }),
            () => new CanvasExpandDialog(96, 64),
            () => new WatermarkDialog(),
            () => new TextInputDialog()
        ];
        foreach (var factory in factories)
        {
            var window = factory();
            try
            {
                var width = double.IsNaN(window.Width) ? 600 : window.Width;
                var height = double.IsNaN(window.Height) ? 380 : window.Height;
                Render(window, width, height, Path.Combine(outputDirectory, $"{window.GetType().Name}-{code}.png"));
            }
            finally { window.Close(); }
        }
    }

    private static void Render(Window window, double width, double height, string path)
    {
        var content = (FrameworkElement)window.Content;
        var size = new System.Windows.Size(width, height);
        content.Measure(size); content.Arrange(new Rect(size)); content.UpdateLayout(); Flush();
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var context = background.RenderOpen()) context.DrawRectangle(window.Background, null, new Rect(size));
        bitmap.Render(background); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
