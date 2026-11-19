using System.IO;
using System.Reflection;
using System.Windows;
using SnapSharp.Services;

namespace SnapSharp.WindowChecks;

internal static partial class Program
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(".artifacts", "window-checks", Guid.NewGuid().ToString("N")));
    private static readonly BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    [STAThread]
    private static int Main(string[] args)
    {
        Directory.CreateDirectory(Root);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            // Load production styles without starting App's tray/controller or touching user data.
            System.Xml.Linq.XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var document = System.Xml.Linq.XDocument.Load("App.xaml");
            var resources = document.Root!.Element(wpf + "Application.Resources")!.Elements().Single();
            resources.SetAttributeValue(System.Xml.Linq.XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml");
            resources.SetAttributeValue(System.Xml.Linq.XNamespace.Xmlns + "services", "clr-namespace:SnapSharp.Services;assembly=SnapSharp");
            app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resources.ToString(),
                new System.Windows.Markup.ParserContext { BaseUri = new Uri("pack://application:,,,/SnapSharp;component/") });
            using var theme = new ThemeService(); theme.Start();
            if (args.Contains("--redaction-only"))
            {
                RedactionChecks();
                var models = Array.IndexOf(args, "--recognition-model-dir");
                if (models >= 0 && models + 1 < args.Length) RedactionModelPositions(args[models + 1]);
                return 0;
            }
            if (args.Contains("--dpi-only"))
            {
                MixedDpiChecks();
                CapturePrecisionChecks();
                GuidePreferencesChecks();
                return 0;
            }
            var replayArgument = Array.IndexOf(args, "--document-replay");
            if (replayArgument >= 0)
            {
                if (replayArgument + 1 >= args.Length) throw new ArgumentException("--document-replay <diagnostics-folder> is required.");
                DocumentReplayDiagnostics(args[replayArgument + 1]);
                return 0;
            }
            var imageArgument = Array.IndexOf(args, "--document-images");
            if (imageArgument >= 0)
            {
                var modelArgument = Array.IndexOf(args, "--recognition-model-dir");
                if (imageArgument + 1 >= args.Length || modelArgument < 0 || modelArgument + 1 >= args.Length)
                    throw new ArgumentException("--document-images <folder> requires --recognition-model-dir <folder>.");
                DocumentImageDiagnostics(args[imageArgument + 1], args[modelArgument + 1]);
                return 0;
            }
            if (args.Contains("--recognition-only"))
            {
                var modelArgument = Array.IndexOf(args, "--recognition-model-dir");
                RecognitionChecks(modelArgument >= 0 && modelArgument + 1 < args.Length ? args[modelArgument + 1] : null); return 0;
            }
            ReticlePixelChecks();
            MixedDpiChecks();
            if (args.Contains("--precision-only"))
            {
                RulerMagnifierChecks();
                CapturePrecisionChecks();
                GuidePreferencesChecks();
                return 0;
            }
            WindowLayeringChecks();
            CapturePrecisionChecks();
            WindowShapeChecks();
            DesktopRulerChecks();
            RulerLabelChecks();
            RulerMagnifierChecks();
            RulerClipboardChecks();
            GuidePreferencesChecks();
            GuideLocalizationChecks();
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { app.Shutdown(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static object? Invoke(object target, string method, params object?[] arguments) =>
        target.GetType().GetMethod(method, PrivateInstance)!.Invoke(target, arguments);
}
