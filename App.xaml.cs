using L = NimCap.Screenshot.Localization.UiText;
using NimCap.Screenshot.Services;
using NimCap.Screenshot.Models;
using System.IO;
using System.Threading;
using System.Windows;

namespace NimCap.Screenshot;

public partial class App : Application
{
    private const string MutexName = "Local\\NimCap.Screenshot.SingleInstance";
    private Mutex? _singleInstanceMutex;
    private AppController? _controller;
    private SingleInstanceChannel? _instanceChannel;
    private ThemeService? _themeService;
    private CrashDiagnostics? _crashDiagnostics;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Worker mode must precede single-instance and UI startup, and must not write user data to disk.
        if (e.Args is ["--onnx-worker", var pipe])
        {
            Shutdown(OnnxWorker.Run(pipe).GetAwaiter().GetResult());
            return;
        }
        _crashDiagnostics = new CrashDiagnostics(this);
        base.OnStartup(e);
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));
        if (AppSelfTest.TryRun(this, e.Args)) return;
        _themeService = new ThemeService();
        _themeService.Start();
        _singleInstanceMutex = new Mutex(true, MutexName, out var ownsMutex);
        if (!ownsMutex)
        {
            if (!SingleInstanceChannel.TrySend(e.Args))
            {
                string? language = null;
                try
                {
                    if (File.Exists(AppPaths.SettingsFile))
                    {
                        var settings = new SettingsService();
                        settings.Load();
                        language = settings.Current.InterfaceLanguage;
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or TypeInitializationException) { }
                LocalizationService.Apply(language);
                MessageBox.Show(L.Get("NimCapIsAlreadyRunningButTheExisting"), L.Get("NimCapAppTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Shutdown();
            return;
        }

        try
        {
            StartPrimaryInstance(e.Args);
        }
        catch (Exception error)
        {
            try { CrashDiagnostics.ReportStartupFailure(error); }
            finally { Shutdown(1); }
        }
    }

    private void StartPrimaryInstance(string[] args)
    {
        DataDirectoryService dataLocation;
        try { dataLocation = AppPaths.DataLocation; }
        catch (Exception ex)
        {
            MessageBox.Show(L.Get("InvalidDataLocation") + Environment.NewLine + ex.Message,
                L.Get("DataFolder"), MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        try { dataLocation.ApplyPendingChange(); }
        catch (Exception ex)
        {
            MessageBox.Show(L.Get("DataMigrationFailed") + Environment.NewLine + ex.Message,
                L.Get("DataFolder"), MessageBoxButton.OK, MessageBoxImage.Error);
            try { dataLocation.ScheduleChange(dataLocation.Root); } catch { }
        }
        _controller = new AppController();
        _controller.Start();
        _instanceChannel = new SingleInstanceChannel();
        _instanceChannel.Start(args => Dispatcher.Invoke(() => _controller?.HandleLaunchArguments(args, true)));
        _controller.HandleLaunchArguments(args, false);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _instanceChannel?.Dispose();
            _controller?.Dispose();
            _singleInstanceMutex?.Dispose();
            _themeService?.Dispose();
            base.OnExit(e);
        }
        catch (Exception error)
        {
            CrashDiagnostics.WriteReport(error, "Shutdown", true, out _);
            Environment.ExitCode = 1;
        }
        finally { _crashDiagnostics?.Dispose(); }
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window) ThemeService.ApplyToWindow(window);
    }
}
