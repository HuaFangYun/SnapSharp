using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace SnapSharp.Services;

/// <summary>Observes real wheel input without swallowing it or moving the pointer.</summary>
internal sealed class ManualScrollInput : IDisposable
{
    private readonly Channel<bool> _wheel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly HookProc _mouseProc, _keyProc;
    private nint _mouse, _keyboard;
    private bool _preview;
    public ManualScrollInput(System.Drawing.Rectangle region, Action cancel)
    {
        _mouseProc = (code, message, data) =>
        {
            if (code >= 0 && (message == 0x020A || message == 0x020E))
            {
                var info = Marshal.PtrToStructure<MouseData>(data);
                if (region.Contains(info.X, info.Y)) _wheel.Writer.TryWrite(true);
            }
            return CallNextHookEx(0, code, message, data);
        };
        _keyProc = (code, message, data) =>
        {
            if (code >= 0 && message == 0x0100 && Marshal.ReadInt32(data) == 0x1B) cancel();
            return CallNextHookEx(0, code, message, data);
        };
        _mouse = SetWindowsHookEx(14, _mouseProc, GetModuleHandle(null), 0);
        _keyboard = SetWindowsHookEx(13, _keyProc, GetModuleHandle(null), 0);
        if (_mouse == 0 || _keyboard == 0) { Dispose(); throw new InvalidOperationException("无法监听滚轮／Esc，长截图未启动。"); }
    }
    public void Preview() { _preview = true; _wheel.Writer.TryWrite(true); }
    public async Task<bool> Wait(CancellationToken token) { await _wheel.Reader.ReadAsync(token); return !_preview; }
    public void Dispose() { if (_mouse != 0) UnhookWindowsHookEx(_mouse); if (_keyboard != 0) UnhookWindowsHookEx(_keyboard); _mouse = _keyboard = 0; _wheel.Writer.TryComplete(); }
    private delegate nint HookProc(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public int X, Y; public uint Mouse, Flags, Time; public nuint Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}
