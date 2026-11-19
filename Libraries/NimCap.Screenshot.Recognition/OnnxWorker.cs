using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using NimCap.Screenshot.Models;
using L = NimCap.Screenshot.Localization.UiText;

namespace NimCap.Screenshot.Services;

/// <summary>One bounded inference operation per child. Images and recognized text travel only over a current-user pipe.</summary>
public static class OnnxWorker
{
    private const int MaximumMessageBytes = 192 * 1024 * 1024;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly HashSet<int> DisabledAdapters = [];
    public static bool IsWorker { get; private set; }
    public sealed record Request(string Operation, AppSettings Settings, byte[] Image, string Culture, byte[]? Mask = null);
    public sealed record TextSpan(int Start, int Length, double X, double Y, double Width, double Height);
    public sealed record Block(string Text, double X, double Y, double Width, double Height, double? Confidence, string Kind, TextSpan[]? TextSpans = null);
    public sealed record Response(Block[]? Blocks = null, string Backend = "", RecognizedCell[]? Cells = null,
        byte[]? Image = null, string? Error = null, bool GpuFailed = false, string? GpuMemory = null,
        string? ErrorDiagnostic = null, byte[]? Mask = null);

    public static async Task<Response> Execute(string operation, BitmapSource image, AppSettings settings, CancellationToken token, byte[]? mask = null)
    {
        OnnxImageRuntime.ValidateImage(image);
        if (!image.IsFrozen) { image = image.CloneCurrentValue(); image.Freeze(); }
        if (mask is not null && mask.Length != image.PixelWidth * image.PixelHeight) throw new ArgumentException("Invalid mask dimensions.");
        var maskSnapshot = mask?.ToArray();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.WatermarkDefaultModelId == "moebius" && operation is ("inpaint" or "watermark-check") ? 600 : Math.Clamp(settings.RecognitionTimeoutSeconds, 1, 120)));
        await Gate.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            // Snapshot settings: never mutate the user's preferred device when falling back.
            var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.SerializeToUtf8Bytes(settings))!;
            var watermark = operation is "inpaint" or "watermark-detect" or "watermark-check";
            var device = watermark ? copy.WatermarkDevice : operation == "cutout" ?
                (copy.CutoutOnnxDevice is "CPU" or "DirectML" ? copy.CutoutOnnxDevice : copy.OnnxDevice) : copy.OnnxDevice;
            var adapter = operation == "cutout" && copy.CutoutOnnxDeviceId >= 0 ? copy.CutoutOnnxDeviceId : copy.OnnxDeviceId;
            var gpu = device == "DirectML" && !DisabledAdapters.Contains(adapter);
            if (device == "DirectML" && !gpu) UseCpu(copy);
            var request = new Request(operation, copy, Encode(image), L.Culture.Name, maskSnapshot);
            Response response;
            try { response = await Send(request, timeout.Token).ConfigureAwait(false); }
            catch (IOException) when (gpu && !timeout.IsCancellationRequested)
            {
                DisabledAdapters.Add(adapter);
                WriteStatus($"GPU worker exited: adapter={adapter}; using CPU for this application session");
                UseCpu(copy);
                response = await Send(request, timeout.Token).ConfigureAwait(false);
            }
            if (response.GpuFailed)
            {
                DisabledAdapters.Add(adapter);
                WriteStatus($"GPU fallback: adapter={adapter}; {response.GpuMemory}");
            }
            else if (response.GpuMemory is { } memory) WriteStatus(memory);
            if (response.Error is not null)
            {
                WriteStatus("Managed inference failure:" + Environment.NewLine + (response.ErrorDiagnostic ?? response.Error));
                throw new InvalidOperationException(L.Get("IsolatedInferenceFailed"));
            }
            return response;
        }
        catch (IOException)
        {
            WriteStatus("Inference worker stopped or IPC failed; request discarded; application remains available.");
            throw new InvalidOperationException(L.Get("IsolatedInferenceFailed"));
        }
        finally { Gate.Release(); }
    }

    private static void UseCpu(AppSettings settings)
    { settings.OnnxDevice = "CPU"; settings.CutoutOnnxDevice = "CPU"; settings.WatermarkDevice = "CPU"; }

    private static async Task<Response> Send(Request request, CancellationToken token)
    {
        var pipeName = "NimCap.Screenshot.Onnx." + Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var executable = Path.Combine(AppContext.BaseDirectory, "NimCap.Screenshot.exe");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = AppContext.BaseDirectory };
        start.ArgumentList.Add("--onnx-worker"); start.ArgumentList.Add(pipeName);
        using var process = Process.Start(start) ?? throw new IOException("Inference worker did not start.");
        try
        {
            var connected = pipe.WaitForConnectionAsync(token);
            var exited = process.WaitForExitAsync(token);
            if (await Task.WhenAny(connected, exited).ConfigureAwait(false) != connected)
            { token.ThrowIfCancellationRequested(); throw new IOException("Inference worker exited before connecting."); }
            await connected.ConfigureAwait(false);
            await Write(pipe, request, token).ConfigureAwait(false);
            return await Read<Response>(pipe, token).ConfigureAwait(false);
        }
        finally
        {
            // Cancellation, driver hang, partial IPC response and successful completion all release native sessions.
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch { }
        }
    }

    public static async Task<int> Run(string pipeName)
    {
        IsWorker = true;
        SetErrorMode(0x0001 | 0x0002); // Do not display a native crash dialog in the background worker.
        if (!pipeName.StartsWith("NimCap.Screenshot.Onnx.", StringComparison.Ordinal) || pipeName.Length > 80) return 2;
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var connectionTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await pipe.ConnectAsync(connectionTimeout.Token).ConfigureAwait(false);
            var request = await Read<Request>(pipe, connectionTimeout.Token).ConfigureAwait(false);
            L.SetCulture(System.Globalization.CultureInfo.GetCultureInfo(request.Culture));
            Response response;
            try
            {
                var image = Decode(request.Image);
                var recognition = new LocalRecognitionService();
                response = request.Operation switch
                {
                    "paddle" => FromDocument(await recognition.RecognizePaddle(image, request.Settings).ConfigureAwait(false)),
                    "formula" => FromDocument(await recognition.RecognizeFormula(image, request.Settings).ConfigureAwait(false)),
                    "table" => FromTable(await recognition.RecognizeTable(image, request.Settings).ConfigureAwait(false)),
                    "cutout" => new Response(Image: Encode(await OnnxImageRuntime.Shared.RemoveBackground(image, request.Settings).ConfigureAwait(false))),
                    "inpaint" => new Response(Image: Encode(await OnnxImageRuntime.Shared.Inpaint(image, request.Mask ?? throw new InvalidDataException("Missing repair mask."), request.Settings).ConfigureAwait(false))),
                    "watermark-detect" => new Response(Mask: await OnnxImageRuntime.Shared.DetectWatermark(image, request.Mask, request.Settings).ConfigureAwait(false)),
                    "watermark-check" => new Response(Backend: await OnnxImageRuntime.Shared.CheckWatermarkModel(request.Settings).ConfigureAwait(false)),
                    _ => throw new InvalidDataException("Unknown inference operation.")
                };
            }
            catch (Exception ex) { response = new Response(Error: ex.GetType().FullName, ErrorDiagnostic: DescribeError(ex)); }
            response = response with { GpuFailed = OnnxImageRuntime.Shared.GpuDisabled, GpuMemory = OnnxImageRuntime.Shared.GpuMemoryStatus };
            OnnxImageRuntime.Shared.Dispose();
            await Write(pipe, response, CancellationToken.None).ConfigureAwait(false);
            return response.Error is null ? 0 : 1;
        }
        catch { return 1; }
        finally { OnnxImageRuntime.Shared.Dispose(); }
    }

    private static string DescribeError(Exception error)
    {
        // Format frames ourselves: Exception.ToString()/StackTrace can include user text or source paths.
        const int maximumLength = 4096;
        var text = new StringBuilder();
        var pending = new Queue<Exception>(); pending.Enqueue(error);
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        while (pending.TryDequeue(out var current) && seen.Count < 4)
        {
            if (!seen.Add(current)) continue;
            text.AppendLine($"{current.GetType().FullName}; HResult: 0x{current.HResult:X8}");
            foreach (var frame in new StackTrace(current, false).GetFrames().Take(8))
            {
                if (frame.GetMethod() is { } method)
                    text.AppendLine($"  at {method.DeclaringType?.FullName}.{method.Name}");
            }
            if (current is AggregateException aggregate)
                foreach (var inner in aggregate.InnerExceptions.Take(4)) pending.Enqueue(inner);
            else if (current.InnerException is { } inner) pending.Enqueue(inner);
        }
        return text.Length > maximumLength ? text.ToString(0, maximumLength) : text.ToString();
    }

    private static Response FromDocument(RecognitionDocument document) => new(document.Blocks.Select(b =>
        new Block(b.Text, b.Bounds.X, b.Bounds.Y, b.Bounds.Width, b.Bounds.Height, b.Confidence, b.Kind,
            b.TextSpans?.Select(s => new TextSpan(s.Start, s.Length, s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height)).ToArray())).ToArray(), document.Backend);
    private static Response FromTable(LocalRecognitionService.TableResult table) => FromDocument(table.Document) with { Cells = table.Cells };
    public static RecognitionDocument ToDocument(Response response) => new((response.Blocks ?? []).Select(b =>
        new RecognizedBlock(b.Text, new System.Windows.Rect(b.X, b.Y, b.Width, b.Height), b.Confidence, b.Kind)
        { TextSpans = b.TextSpans?.Select(s => new RecognizedTextSpan(s.Start, s.Length, new System.Windows.Rect(s.X, s.Y, s.Width, s.Height))).ToArray() }).ToArray(), response.Backend);
    public static byte[] Encode(BitmapSource image)
    {
        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(stream);
        return stream.ToArray();
    }
    public static BitmapSource Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var image = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        OnnxImageRuntime.ValidateImage(image); image.Freeze(); return image;
    }
    private static async Task Write<T>(Stream stream, T message, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        if (bytes.Length > MaximumMessageBytes) throw new InvalidDataException("Inference message exceeds limit.");
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length), token).ConfigureAwait(false);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }
    private static async Task<T> Read<T>(Stream stream, CancellationToken token)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
        var length = BitConverter.ToInt32(header);
        if (length is < 1 or > MaximumMessageBytes) throw new InvalidDataException("Invalid inference message length.");
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(bytes) ?? throw new InvalidDataException("Empty inference response.");
    }
    private static void WriteStatus(string message)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DiagnosticsRoot);
            File.AppendAllText(Path.Combine(AppPaths.DiagnosticsRoot, "inference-status.log"), $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch { }
    }
    [DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);
}
