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
    private static int Main()
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
            WindowLayeringChecks();
            CapturePrecisionChecks();
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
