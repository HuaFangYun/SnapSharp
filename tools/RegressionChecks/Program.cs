using System.Net;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SnapSharp.Models;
using SnapSharp.Services;
using SnapSharp.Views;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ProgressBar = System.Windows.Controls.ProgressBar;

namespace SnapSharp.RegressionChecks;

internal static class Program
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(".artifacts", "regression-checks", Guid.NewGuid().ToString("N")));
    private static readonly BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    [STAThread]
    private static int Main()
    {
        Directory.CreateDirectory(Root);
        try
        {
            DownloadChecks().GetAwaiter().GetResult();
            SettingsChecks();
            WindowChecks();
            Console.WriteLine("PASS: all recording preferences, output paths, overlay visibility and download regression checks.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static void SettingsChecks()
    {
        var file = Path.Combine(Root, "settings.json");
        File.WriteAllText(file, "{}");
        var settings = new SettingsService(file); settings.Load();
        Require(Path.IsPathFullyQualified(settings.Current.RecordingSaveDirectory), "Old settings must get a recording directory.");
        settings.Current.RecordingSaveDirectory = Path.Combine(Root, "recordings");
        settings.Current.Recording = new RecordingPreferences
        {
            RecordSystemAudio = true, RecordMicrophone = true, RecordCamera = false,
            SpeakerDeviceId = "speaker-2", MicrophoneDeviceId = "mic-2", CameraDeviceId = "camera-2", FrameRate = 60
        };
        settings.Save();
        var restored = new SettingsService(file); restored.Load();
        Require(restored.Current.Recording.RecordSystemAudio && restored.Current.Recording.RecordMicrophone &&
            restored.Current.Recording.SpeakerDeviceId == "speaker-2" && restored.Current.Recording.MicrophoneDeviceId == "mic-2" &&
            restored.Current.Recording.FrameRate == 60 && restored.Current.RecordingSaveDirectory == settings.Current.RecordingSaveDirectory,
            "Recording preferences did not survive disk reload.");
        var date = new DateTime(2026, 11, 17, 12, 0, 0);
        var first = RecordingOutputPath.Create(restored.Current.RecordingSaveDirectory, "MP4", date);
        File.WriteAllText(first, "existing video");
        var next = RecordingOutputPath.Create(restored.Current.RecordingSaveDirectory, "MP4", date);
        Require(next != first && File.ReadAllText(first) == "existing video", "Output collision overwrote a recording.");
        Require(RecordingOutputPath.Create(restored.Current.RecordingSaveDirectory, "GIF", date).EndsWith(".gif"), "Wrong GIF extension.");
        Console.WriteLine("PASS: old settings migration, audio/device/path persistence, unique filenames.");
    }

    private static void WindowChecks()
    {
        // Load the production resources without starting the tray/controller or touching user data.
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        System.Xml.Linq.XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var document = System.Xml.Linq.XDocument.Load("App.xaml");
        var resources = document.Root!.Element(wpf + "Application.Resources")!.Elements().Single();
        resources.SetAttributeValue(System.Xml.Linq.XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml");
        resources.SetAttributeValue(System.Xml.Linq.XNamespace.Xmlns + "services", "clr-namespace:SnapSharp.Services;assembly=SnapSharp");
        app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resources.ToString(),
            new System.Windows.Markup.ParserContext { BaseUri = new Uri("pack://application:,,,/SnapSharp;component/") });
        using var theme = new ThemeService(); theme.Start();
        var settings = new SettingsService(Path.Combine(Root, "window-settings.json"));
        settings.Current.SaveDirectory = Root;
        settings.Current.Recording.RecordSystemAudio = true;
        settings.Current.Recording.RecordMicrophone = false;
        settings.Current.Recording.SpeakerDeviceId = "speaker-2";
        settings.Current.Recording.MicrophoneDeviceId = "mic-2";
        settings.Save();
        var window = new RecordingWindow(new System.Drawing.Rectangle(40, 40, 320, 192), settings);
        window.ShowRecordingTools();
        var toolbar = (RecordingToolbarWindow)typeof(RecordingWindow).GetField("_toolbar", PrivateInstance)!.GetValue(window)!;
        var border = (Window)typeof(RecordingWindow).GetField("_regionIndicator", PrivateInstance)!.GetValue(window)!;
        Require(toolbar.IsVisible && border.IsVisible && !window.IsVisible, "Initial recording tools are missing.");
        ((System.Windows.Controls.Button)toolbar.FindName("ConfigurationButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Require(window.IsVisible && !toolbar.IsVisible && !border.IsVisible, "Floating tools cover recording configuration.");
        PumpUntil(() => !(bool)typeof(RecordingWindow).GetField("_refreshing", PrivateInstance)!.GetValue(window)!);
        var speaker = (CheckBox)window.FindName("SpeakerBox");
        var microphone = (CheckBox)window.FindName("MicBox");
        Require(speaker.IsChecked == true && microphone.IsChecked == false, "Recording window lost saved audio switches.");
        microphone.IsChecked = true;
        var devices = (ComboBox)window.FindName("SpeakerDevices");
        devices.ItemsSource = new[] { new RecordingDevice("", "Windows default"), new RecordingDevice("speaker-2", "Device 2") };
        devices.SelectedIndex = 0;
        var selected = typeof(RecordingWindow).GetMethod("Selected", BindingFlags.NonPublic | BindingFlags.Static)!;
        var currentDefault = "first-default";
        Func<string> resolve = () => currentDefault;
        Require((string?)selected.Invoke(null, [speaker, devices, resolve]) == "first-default", "Default output was not resolved.");
        currentDefault = "new-default";
        Require((string?)selected.Invoke(null, [speaker, devices, resolve]) == "new-default", "Default output was cached across recordings.");
        devices.SelectedIndex = 1;
        Require((string?)selected.Invoke(null, [speaker, devices, resolve]) == "speaker-2", "Explicit device was replaced by the default.");
        ((ComboBox)window.FindName("FormatBox")).SelectedIndex = 1;
        Require(!speaker.IsEnabled && speaker.IsChecked == true, "GIF erased the remembered MP4 audio preference.");
        ((ComboBox)window.FindName("FormatBox")).SelectedIndex = 0;
        window.Close();
        Require(!window.IsVisible && toolbar.IsVisible && border.IsVisible, "Tools did not return after configuration closed.");
        var restored = new SettingsService(Path.Combine(Root, "window-settings.json")); restored.Load();
        Require(restored.Current.Recording.RecordSystemAudio && restored.Current.Recording.RecordMicrophone &&
            restored.Current.Recording.SpeakerDeviceId == "speaker-2", "Closing configuration did not remember audio options.");
        ((System.Windows.Controls.Button)toolbar.FindName("ConfigurationButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Require(window.IsVisible && !toolbar.IsVisible && !border.IsVisible, "Reopened configuration is covered by tools.");
        window.CloseSession();

        using var hotkeys = new GlobalHotkeyHost();
        var global = new SettingsWindow(restored, new StartupRegistrationService(), hotkeys);
        ((System.Windows.Controls.TextBox)global.FindName("RecordingSaveDirectory")).Text = Path.Combine(Root, "new-recording-folder");
        var candidate = global.BuildCandidate();
        Require(candidate.Recording.RecordSystemAudio && candidate.Recording.RecordMicrophone &&
            candidate.Recording.SpeakerDeviceId == "speaker-2" && candidate.RecordingSaveDirectory.EndsWith("new-recording-folder"),
            "Global settings lost audio preferences or the recording directory.");
        Require(((ProgressBar)global.FindName("LocalTranslationProgress")) is not null &&
            global.FindName("LocalTranslationDownloadStatus") is TextBlock, "Download feedback controls are missing.");
        global.Close(); app.Shutdown();
        Console.WriteLine("PASS: configuration overlay hiding/restoration, remembered audio, changing default output, GIF audio preservation, global settings.");
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var frame = new DispatcherFrame();
        var start = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { if (condition() || start.Elapsed > TimeSpan.FromSeconds(15)) frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
        Require(condition(), "Device initialization timed out.");
    }

    private sealed class Inline<T>(Action<T> callback) : IProgress<T> { public void Report(T value) => callback(value); }
    private sealed class Handler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(respond(request, ++Calls));
    }

    private static HttpResponseMessage Reply(byte[] data, int offset = 0, bool partial = false)
    {
        var reply = new HttpResponseMessage(partial ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
        { Content = new ByteArrayContent(data[offset..]) };
        if (partial) reply.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, data.Length - 1, data.Length);
        return reply;
    }

    private static async Task DownloadChecks()
    {
        var data = Enumerable.Range(0, 4096).Select(i => (byte)(i % 251)).ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(data));
        const string source = "https://huggingface.co/test/model.gguf";
        var progress = new List<double?>();
        var messages = new List<string>();
        async Task Fetch(string name, Handler handler, CancellationToken token = default)
        {
            using var client = new HttpClient(handler);
            await ResumableModelDownload.Download(source, Path.Combine(Root, name), hash, data.Length,
                new Inline<string>(messages.Add), token, new Inline<double?>(progress.Add), client, Path.Combine(Root, "download.log"));
        }
        void Complete(string name) => Require(File.ReadAllBytes(Path.Combine(Root, name)).SequenceEqual(data) &&
            !File.Exists(Path.Combine(Root, name + ".part")), "Download content or partial cleanup is wrong: " + name);

        await Fetch("fresh", new Handler((_, _) => Reply(data))); Complete("fresh");
        Require(progress.Contains(null) && progress.Contains(100) && messages.Any(s => s.Contains("MB/s")), "Download progress/stage/speed feedback is missing.");
        var valid = new Handler((_, _) => throw new Exception("Verified file should not be downloaded again."));
        await Fetch("fresh", valid); Require(valid.Calls == 0, "Verified file was redownloaded.");

        File.WriteAllBytes(Path.Combine(Root, "resume.part"), data[..317]);
        await Fetch("resume", new Handler((request, _) =>
        {
            Require(request.Headers.Range?.Ranges.Single().From == 317, "Resume Range header is wrong.");
            return Reply(data, 317, partial: true);
        })); Complete("resume");

        File.WriteAllBytes(Path.Combine(Root, "ignored-range.part"), data[..317]);
        await Fetch("ignored-range", new Handler((_, _) => Reply(data))); Complete("ignored-range");

        var truncated = new Handler((request, call) =>
        {
            if (call == 1) return Reply(data[..513]);
            Require(request.Headers.Range?.Ranges.Single().From == 513, "Retry discarded partial bytes.");
            return Reply(data, 513, partial: true);
        });
        await Fetch("retry", truncated); Complete("retry"); Require(truncated.Calls == 2, "Interrupted transfer did not retry.");

        var unavailable = new Handler((_, call) => call == 1 ? new(HttpStatusCode.ServiceUnavailable) : Reply(data));
        await Fetch("service-retry", unavailable); Complete("service-retry"); Require(unavailable.Calls == 2, "HTTP 503 did not retry.");

        using var canceled = new CancellationTokenSource();
        var cancelHandler = new Handler((_, _) => new(HttpStatusCode.OK)
        { Content = new StreamContent(new CancelStream(data, canceled)) });
        try { await Fetch("cancel", cancelHandler, canceled.Token); throw new Exception("Cancellation ignored."); }
        catch (OperationCanceledException) when (canceled.IsCancellationRequested) { }
        var kept = new FileInfo(Path.Combine(Root, "cancel.part")).Length;
        Require(kept is > 0 and < 4096, "Cancel discarded the partial download.");
        await Fetch("cancel", new Handler((request, _) =>
        {
            Require(request.Headers.Range?.Ranges.Single().From == kept, "New download did not resume canceled transfer.");
            return Reply(data, (int)kept, true);
        })); Complete("cancel");

        async Task Reject(string name, Handler handler)
        {
            try { await Fetch(name, handler); throw new Exception("Invalid download accepted: " + name); }
            catch (IOException) { Require(!File.Exists(Path.Combine(Root, name)), "Invalid download was published."); }
        }
        var corrupt = data.ToArray(); corrupt[10] ^= 1;
        await Reject("bad-hash", new Handler((_, _) => Reply(corrupt)));
        Require(!File.Exists(Path.Combine(Root, "bad-hash.part")), "Corrupt partial was kept for reuse.");
        File.WriteAllBytes(Path.Combine(Root, "bad-range.part"), data[..317]);
        await Reject("bad-range", new Handler((_, _) => Reply(data, 318, true)));
        var redirect = new Handler((_, _) => new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://example.com/model") } });
        await Reject("bad-redirect", redirect); Require(redirect.Calls == 1, "Download followed an untrusted redirect.");
        var missing = new Handler((_, _) => new(HttpStatusCode.NotFound));
        await Reject("missing", missing); Require(missing.Calls == 1, "Permanent HTTP error was retried.");
        var disconnected = new Handler((_, _) => throw new HttpRequestException("simulated connection failure"));
        File.WriteAllBytes(Path.Combine(Root, "disconnect.part"), data[..317]);
        await Reject("disconnect", disconnected);
        Require(disconnected.Calls == 3 && new FileInfo(Path.Combine(Root, "disconnect.part")).Length == 317,
            "Exhausted retries lost partial data or exceeded retry limit.");
        Require(File.Exists(Path.Combine(Root, "download.log")), "Download failures were not logged.");
        Console.WriteLine("PASS: verified downloads, progress/speed, HTTP Range, ignored Range, interrupted retry, HTTP 503 retry, cancel/resume, hash/range/redirect rejection, HTTP 404, retry limit and diagnostics.");
    }

    private sealed class CancelStream(byte[] data, CancellationTokenSource cancellation) : MemoryStream(data)
    {
        private bool _first = true;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (_first) { _first = false; return base.ReadAsync(buffer[..Math.Min(317, buffer.Length)], token); }
            cancellation.Cancel();
            return ValueTask.FromCanceled<int>(cancellation.Token);
        }
    }
}
