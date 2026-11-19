using L = NimCap.Screenshot.Localization.UiText;
using NimCap.Screenshot.Models;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace NimCap.Screenshot.Services;

/// <summary>Observes real wheel input without swallowing it or moving the pointer.</summary>
internal sealed class ManualScrollInput : IDisposable
{
    private readonly Channel<ScrollDirection> _wheel = Channel.CreateBounded<ScrollDirection>(new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly HookProc _mouseProc, _keyProc;
    private readonly System.Drawing.Rectangle _region;
    private readonly Action _cancel;
    private nint _mouse, _keyboard;
    private volatile bool _preview;
    private readonly ManualScrollActivity _activity = new();
    public ScrollDirection LastDirection { get; private set; } = ScrollDirection.Auto;
    public ManualScrollInput(System.Drawing.Rectangle region, Action cancel)
    {
        // Low-level hooks are delivered on the installing thread. Keep installation
        // on the WPF thread with its message loop, never in the capture worker's Wait.
        System.Windows.Application.Current.Dispatcher.VerifyAccess();
        _region = region; _cancel = cancel;
        _mouseProc = (code, message, data) =>
        {
            if (code >= 0 && message == 0x020A)
            {
                var info = Marshal.PtrToStructure<MouseData>(data);
                if (_region.Contains(info.X, info.Y))
                    _wheel.Writer.TryWrite(ScrollDirection.Vertical);
            }
            return CallNextHookEx(0, code, message, data);
        };
        _keyProc = (code, message, data) =>
        {
            if (code >= 0 && message == 0x0100 && Marshal.ReadInt32(data) == 0x1B) _cancel();
            return CallNextHookEx(0, code, message, data);
        };
        _mouse = SetWindowsHookEx(14, _mouseProc, GetModuleHandle(null), 0);
        _keyboard = SetWindowsHookEx(13, _keyProc, GetModuleHandle(null), 0);
        if (_mouse == 0 || _keyboard == 0) { Dispose(); throw new InvalidOperationException(L.Get("CouldNotMonitorWheelEscInputScrolling")); }
    }
    public void Preview() { _preview = true; _wheel.Writer.TryWrite(LastDirection); }
    public async Task<bool> Wait(CancellationToken token)
    {
        // Idle blocks on actual input; only an active gesture samples subsequent
        // paints until the producer reports a stable viewport.
        using var tick = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (_activity.Active) tick.CancelAfter(16);
        try
        {
            LastDirection = await _wheel.Reader.ReadAsync(tick.Token);
            if (!_preview) _activity.Input(Environment.TickCount64);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        token.ThrowIfCancellationRequested();
        return !_preview;
    }
    public void ObserveFrame(bool changed) => _activity.Observe(changed, Environment.TickCount64);
    public void Dispose() { if (_mouse != 0) UnhookWindowsHookEx(_mouse); if (_keyboard != 0) UnhookWindowsHookEx(_keyboard); _mouse = _keyboard = 0; _wheel.Writer.TryComplete(); }
    private delegate nint HookProc(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public int X, Y; public uint Mouse, Flags, Time; public nuint Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}
