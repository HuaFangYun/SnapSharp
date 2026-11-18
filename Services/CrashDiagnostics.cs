using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using L = SnapSharp.Localization.UiText;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace SnapSharp.Services;

/// <summary>Text-only reports: never collect exception messages, Data, source paths or process memory dumps.</summary>
internal sealed class CrashDiagnostics : IDisposable
{
    private readonly Application _app;
    private int _showing;
    internal Action<string?, string>? ReportDialog { get; init; }
    internal CrashDiagnostics(Application app)
    {
        _app = app;
        app.DispatcherUnhandledException += OnDispatcher;
        AppDomain.CurrentDomain.UnhandledException += OnDomain;
        TaskScheduler.UnobservedTaskException += OnTask;
    }

    internal static string BuildReport(Exception exception, string source, bool fatal)
    {
        using var process = Process.GetCurrentProcess();
        var report = new StringBuilder()
            .AppendLine($"UTC: {DateTimeOffset.UtcNow:O}")
            .AppendLine($"Source: {source}; Fatal: {fatal}")
            .AppendLine($"OS: {Environment.OSVersion}")
            .AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}; CLR: {Environment.Version}")
            .AppendLine($"WorkingSet: {process.WorkingSet64}; PrivateBytes: {process.PrivateMemorySize64}; ManagedBytes: {GC.GetTotalMemory(false)}");
        var pending = new Queue<Exception>(); pending.Enqueue(exception);
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        while (pending.TryDequeue(out var error) && seen.Count < 32)
        {
            if (!seen.Add(error)) continue;
            report.AppendLine($"Exception: {error.GetType().FullName}; HResult: 0x{error.HResult:X8}");
            // StackTrace(Exception, false) excludes source filenames and user-controlled exception text.
            report.AppendLine(new StackTrace(error, false).ToString());
            if (error is AggregateException aggregate)
                foreach (var inner in aggregate.InnerExceptions) pending.Enqueue(inner);
            else if (error.InnerException is { } inner) pending.Enqueue(inner);
        }
        return report.ToString();
    }

    internal static string? WriteReport(Exception error, string source, bool fatal, out string report)
    {
        report = "";
        try
        {
            report = BuildReport(error, source, fatal);
            Directory.CreateDirectory(AppPaths.DiagnosticsRoot);
            var path = Path.Combine(AppPaths.DiagnosticsRoot, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.txt");
            File.WriteAllText(path, report, Encoding.UTF8);
            return path;
        }
        catch { return null; } // Reporting must never trigger another unhandled exception.
    }

    internal static bool IsFatal(Exception error) => error is OutOfMemoryException or StackOverflowException
        or AccessViolationException or SEHException ||
        (error is AggregateException aggregate ? aggregate.InnerExceptions.Any(IsFatal) :
            error.InnerException is { } inner && IsFatal(inner));

    private void OnDispatcher(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var fatal = IsFatal(e.Exception);
        var path = WriteReport(e.Exception, "Dispatcher", fatal, out var report);
        e.Handled = true;
        if (fatal) { Environment.Exit(1); return; }
        if (Interlocked.Exchange(ref _showing, 1) != 0) return;
        try { (ReportDialog ?? ShowReport)(path, report); }
        catch { /* The report remains available even if the dialog cannot be created. */ }
        finally { Volatile.Write(ref _showing, 0); }
    }

    private static void OnDomain(object sender, UnhandledExceptionEventArgs e) =>
        WriteReport(e.ExceptionObject as Exception ?? new Exception(), "AppDomain", e.IsTerminating, out _);

    private static void OnTask(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteReport(e.Exception, "UnobservedTask", IsFatal(e.Exception), out _);
        e.SetObserved();
        if (IsFatal(e.Exception)) Environment.Exit(1);
    }

    private static void ShowReport(string? path, string report)
    {
        var window = new Window { Title = L.Get("Error"), Width = 600, Height = 390,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, ShowInTaskbar = true };
        var panel = new DockPanel { Margin = new Thickness(18) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var copy = new Button { Content = L.Get("CopyErrorInformation"), Margin = new Thickness(6), Padding = new Thickness(12, 6, 12, 6) };
        copy.Click += (_, _) => { try { System.Windows.Clipboard.SetText(report); } catch { } };
        var close = new Button { Content = L.Get("OK"), IsDefault = true, Margin = new Thickness(6), Padding = new Thickness(12, 6, 12, 6) };
        close.Click += (_, _) => window.Close();
        actions.Children.Add(copy); actions.Children.Add(close);
        DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        panel.Children.Add(new TextBox { Text = L.Get("UnexpectedErrorRecovered") + Environment.NewLine +
            (path ?? L.Get("CrashReportWriteFailed")) + Environment.NewLine + report,
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        window.Content = panel;
        window.ShowDialog();
    }

    public void Dispose()
    {
        _app.DispatcherUnhandledException -= OnDispatcher;
        AppDomain.CurrentDomain.UnhandledException -= OnDomain;
        TaskScheduler.UnobservedTaskException -= OnTask;
    }
}
