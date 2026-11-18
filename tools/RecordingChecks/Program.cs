using System.IO;
using System.Windows;
using SnapSharp.Services;
using SnapSharp.Views;

namespace SnapSharp.RegressionChecks;

internal static partial class Program
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(".artifacts", "recording-checks", Guid.NewGuid().ToString("N")));

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0) return RecordingAcceptance.Run(args, Root);
        try
        {
            Directory.CreateDirectory(Root);
            RecordingChecks();
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try
            {
                // Load the production resources without starting the tray or controller.
                System.Xml.Linq.XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                var document = System.Xml.Linq.XDocument.Load("App.xaml");
                var resources = document.Root!.Element(wpf + "Application.Resources")!.Elements().Single();
                resources.SetAttributeValue(System.Xml.Linq.XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml");
                resources.SetAttributeValue(System.Xml.Linq.XNamespace.Xmlns + "services", "clr-namespace:SnapSharp.Services;assembly=SnapSharp");
                app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resources.ToString(),
                    new System.Windows.Markup.ParserContext { BaseUri = new Uri("pack://application:,,,/SnapSharp;component/") });
                using var theme = new ThemeService(); theme.Start();
                Require(RecordingWindow.RunLayoutSelfTest(), "Recording window and toolbar layout self-test failed.");
                Require(RecordingSetupWindow.RunLayoutSelfTest(), "Recording setup layout self-test failed.");
                Console.WriteLine("PASS: application recording configuration, toolbar and setup layout.");
            }
            finally { app.Shutdown(); }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
