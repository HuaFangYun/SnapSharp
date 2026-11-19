using System.IO;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using NimCap.Screenshot.Services;
using NimCap.Screenshot.Views;
using Button = System.Windows.Controls.Button;
using Control = System.Windows.Controls.Control;
using ComboBox = System.Windows.Controls.ComboBox;

namespace NimCap.Screenshot.RegressionChecks;

internal static partial class Program
{
    private static async Task ModelTransferChecks()
    {
        byte[] payload = [1, 3, 5, 7, 9];
        const string source = "https://huggingface.co/test/model.onnx";
        async Task Transfer(Stream output, long maximum, long? expected = null, bool chunked = false,
            Action<long>? progress = null, CancellationToken token = default)
        {
            using var client = new HttpClient(new Handler((_, _) => new(HttpStatusCode.OK)
            {
                Content = chunked ? new StreamContent(new ChunkedModelStream(payload)) : new ByteArrayContent(payload)
            }));
            await ModelDownloadTransfer.CopyTo(client, source, ModelDownloadSource.Official,
                output, maximum, progress ?? (_ => { }), token, expected);
        }

        using (var output = new MemoryStream())
        {
            var progress = new List<long>();
            await Transfer(output, payload.Length, payload.Length, progress: progress.Add);
            Require(output.CanWrite && output.ToArray().SequenceEqual(payload) && progress[^1] == payload.Length,
                "Shared transfer lost bytes/progress or disposed the caller's output.");
        }
        using (var output = new MemoryStream())
        {
            await Transfer(output, payload.Length + 10, chunked: true);
            Require(output.ToArray().SequenceEqual(payload), "A size ceiling was treated as an exact length.");
        }
        foreach (var (maximum, expected, chunked) in new (long, long?, bool)[]
                 { (4, null, false), (4, null, true), (6, 6, false), (6, 6, true) })
        {
            using var output = new MemoryStream();
            try
            {
                await Transfer(output, maximum, expected, chunked);
                throw new Exception("Oversized or truncated model download was accepted.");
            }
            catch (InvalidDataException)
            {
                Require(output.Length <= maximum, "Oversized bytes reached the destination.");
                if (!chunked) Require(output.Length == 0, "Invalid headers were not checked before writing.");
            }
        }
        using (var canceled = new CancellationTokenSource())
        using (var output = new MemoryStream())
        {
            try
            {
                await Transfer(output, payload.Length, payload.Length, progress: _ => canceled.Cancel(), token: canceled.Token);
                throw new Exception("Cancellation at the end of a model transfer was ignored.");
            }
            catch (OperationCanceledException) when (canceled.IsCancellationRequested) { }
        }
        Console.WriteLine("PASS: shared model transfer limits, missing Content-Length, exact lengths, progress, destination ownership and cancellation.");
    }

    // Deliberately hides the stream length so checks exercise streamed size limits.
    private sealed class ChunkedModelStream(byte[] payload) : Stream
    {
        private readonly MemoryStream _input = new(payload);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => _input.ReadAsync(buffer, token);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _input.Dispose(); base.Dispose(disposing); }
    }

    private static void SettingsDownloadChecks(SettingsWindow window)
    {
        var previousContext = SynchronizationContext.Current;
        using var callbacks = new BlockingCollection<Action>();
        SynchronizationContext.SetSynchronizationContext(new ManualUiContext(callbacks));
        try
        {
            var status = (TextBlock)window.FindName("TesseractDownloadStatus");
            var cancel = (Button)window.FindName("CancelTesseractButton");
            var selector = (ComboBox)window.FindName("TesseractPackageBox");
            var otherDownload = (Button)window.FindName("DownloadCutoutButton");
            var ocrBackend = (ComboBox)window.FindName("OcrBackendBox");
            var ocrButton = (Button)window.FindName("DownloadOcrButton");
            var run = typeof(SettingsWindow).GetMethod("RunModelDownload", PrivateInstance)!.MakeGenericMethod(typeof(string));
            var applied = 0;
            Task Run(Func<IProgress<string>, CancellationToken, Task<string>> download) =>
                (Task)run.Invoke(window, [status, cancel, new Control[] { selector }, download,
                    (Action<string>)(_ => { applied++; ocrBackend.SelectedIndex = 2; }),
                    null, "DownloadFailedExistingConfigurationPreservedYouCan"])!;
            void Complete(Task task)
            {
                while (!task.IsCompleted)
                {
                    Require(callbacks.TryTake(out var callback, TimeSpan.FromSeconds(5)), "Settings download completion timed out.");
                    callback!();
                }
                task.GetAwaiter().GetResult();
            }
            Require(window.IsLoaded, "Settings window was not loaded for the download fixture.");

            selector.IsEnabled = false;
            var pending = new TaskCompletionSource<string>();
            IProgress<string>? oldProgress = null;
            var task = Run((progress, _) => { oldProgress = progress; return pending.Task; });
            Require(!window.DownloadSourceEnabled && cancel.IsEnabled && !otherDownload.IsEnabled,
                "Download did not lock its source and other download buttons.");
            var overlap = false;
            Complete(Run((_, _) => { overlap = true; return Task.FromResult("overlap"); }));
            Require(!overlap && !window.DownloadSourceEnabled, "Overlapping download changed the active operation.");
            pending.SetResult("success"); Complete(task);
            Require(applied == 1 && window.DownloadSourceEnabled && !cancel.IsEnabled && !selector.IsEnabled && otherDownload.IsEnabled,
                "Success did not commit once and restore the original enabled states.");
            Require(!ocrButton.IsEnabled, "Completion re-enabled the PaddleOCR download after selecting Tesseract.");

            pending = new TaskCompletionSource<string>();
            task = Run((_, _) => pending.Task);
            oldProgress!.Report("stale download progress");
            Require(callbacks.TryTake(out var staleCallback), "Expected queued progress from the previous download.");
            staleCallback!();
            Require(status.Text != "stale download progress", "A completed download overwrote a later operation's status.");
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            // Simulate a provider completing successfully just after cancellation.
            pending.SetResult("late success"); Complete(task);
            Require(applied == 1 && window.DownloadSourceEnabled && !cancel.IsEnabled,
                "Canceled download committed a late result or kept the source locked.");

            Complete(Run((_, _) => Task.FromException<string>(new IOException("fixture failure"))));
            Require(applied == 1 && status.Text.Contains("fixture failure") && window.DownloadSourceEnabled && otherDownload.IsEnabled,
                "Failed download committed data or did not restore its controls.");
            Console.WriteLine("PASS: settings download exclusivity, success/failure/cancellation, stale progress and original control states.");
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    private sealed class ManualUiContext(BlockingCollection<Action> callbacks) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callbacks.Add(() => callback(state));
    }
}
