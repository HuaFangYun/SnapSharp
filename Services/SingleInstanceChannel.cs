using System.IO.Pipes;
using System.Text.Json;

namespace SnapSharp.Services;

public sealed class SingleInstanceChannel : IDisposable
{
    private const string PipeName = "SnapSharp.SingleInstance.Commands";
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _listener;

    public void Start(Action<string[]> commandReceived)
    {
        _listener = Task.Run(async () =>
        {
            while (!_cancellation.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(_cancellation.Token);
                    using var reader = new StreamReader(server);
                    var payload = await reader.ReadLineAsync(_cancellation.Token);
                    var args = payload is null ? [] : JsonSerializer.Deserialize<string[]>(payload) ?? [];
                    commandReceived(args);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    if (!_cancellation.IsCancellationRequested)
                        await Task.Delay(100, _cancellation.Token);
                }
            }
        });
    }

    public static bool TrySend(IReadOnlyList<string> args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(JsonSerializer.Serialize(args));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        try { _listener?.Wait(500); } catch { }
        _cancellation.Dispose();
    }
}
