using L = NimCap.Screenshot.Localization.UiText;
using NimCap.Screenshot.Models;
using NimCap.Screenshot.Native;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace NimCap.Screenshot.Services;

public sealed class GlobalHotkeyHost : IDisposable
{
    private const int RegionHotkeyId = 0x5011;
    private const int FullScreenHotkeyId = 0x5012;
    private const int ActiveWindowHotkeyId = 0x5013;
    private readonly ThreadMessageEventHandler _messageHandler;
    private readonly DispatcherTimer _keyStateTimer;
    private Dictionary<int, (GlobalAction Action, HotkeyGesture Gesture)> _registered = [];
    private readonly Dictionary<int, bool> _keyStates = [];
    private readonly HashSet<int> _fallbackIds = [];
    private readonly HashSet<GlobalAction> _activeActions = [];
    private bool _suspended;
    private readonly Func<uint, bool> _isKeyDown;
    private readonly Func<int, uint, uint, bool> _register;
    private readonly Action<int> _unregister;

    public event EventHandler<CaptureModeEventArgs>? CaptureRequested;
    public event EventHandler? ScreenRulerRequested;
    public event Action<GlobalAction>? PinActionRequested;

    public void Suspend()
    {
        if (_suspended) return;
        _suspended = true;
        foreach (var id in _registered.Keys.Except(_fallbackIds)) _unregister(id);
        _keyStateTimer.Stop();
    }
    public void Resume()
    {
        if (!_suspended) return;
        RegisterAll(_registered);
        _suspended = false;
    }

    public GlobalHotkeyHost() : this(
        key => (NativeMethods.GetAsyncKeyState((int)key) & 0x8000) != 0,
        (id, modifiers, key) => NativeMethods.RegisterHotKey(nint.Zero, id, modifiers, key),
        id => NativeMethods.UnregisterHotKey(nint.Zero, id)) { }

    internal GlobalHotkeyHost(Func<uint, bool> keyDown, Func<int, uint, uint, bool> register, Action<int> unregister)
    {
        _isKeyDown = keyDown; _register = register; _unregister = unregister;
        _messageHandler = OnThreadMessage;
        ComponentDispatcher.ThreadFilterMessage += _messageHandler;
        _keyStateTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(25)
        };
        _keyStateTimer.Tick += PollPhysicalKeys;
    }

    public void Register(AppSettings settings)
    {
        var desired = CreateRegistrations(settings);
        UnregisterAll();
        _registered = desired;
        if (!_suspended) RegisterAll(desired);
    }

    internal static Dictionary<int, (GlobalAction Action, HotkeyGesture Gesture)> CreateRegistrations(AppSettings settings)
    {
        var desired = new Dictionary<int, (GlobalAction, HotkeyGesture)>();
        foreach (var definition in AppActionCatalog.All)
        {
            var text = AppActionCatalog.Hotkey(settings, definition.Action);
            var id = definition.Action switch { GlobalAction.Region => RegionHotkeyId, GlobalAction.FullScreen => FullScreenHotkeyId,
                GlobalAction.ActiveWindow => ActiveWindowHotkeyId, _ => 0x5020 + (int)definition.Action };
            if (!string.IsNullOrWhiteSpace(text)) desired[id] = (definition.Action, Parse(text, definition.Label));
        }
        if (desired.Values.GroupBy(x => (x.Item2.Modifiers, x.Item2.VirtualKey)).Any(x => x.Count() > 1))
            throw new InvalidOperationException(L.Get("GlobalShortcutsMustBeUniqueIncludingCapture"));
        return desired;
    }

    private void RegisterAll(Dictionary<int, (GlobalAction Action, HotkeyGesture Gesture)> registrations)
    {
        _fallbackIds.Clear();
        _keyStates.Clear();
        _activeActions.Clear();
        foreach (var (id, registration) in registrations)
        {
            if (!_register(id,
                    registration.Gesture.Modifiers | NativeMethods.ModNoRepeat, registration.Gesture.VirtualKey))
            {
                var error = Marshal.GetLastWin32Error();
                _fallbackIds.Add(id);
                _keyStates[id] = _isKeyDown(registration.Gesture.VirtualKey);
                WriteStatus($"fallback id={id} action={registration.Action} gesture={registration.Gesture.DisplayText} error={error}");
            }
            else WriteStatus($"registered id={id} action={registration.Action} via WM_HOTKEY");
        }
        if (_fallbackIds.Count > 0) _keyStateTimer.Start();
        else _keyStateTimer.Stop();
    }

    private static HotkeyGesture Parse(string text, string label)
    {
        if (HotkeyGesture.TryParse(text, out var gesture, out var error)) return gesture!;
        throw new InvalidOperationException(L.Format("Invalid0Shortcut1", label, error));
    }

    private void OnThreadMessage(ref MSG message, ref bool handled)
    {
        if (!_suspended && message.message == NativeMethods.WmHotKey &&
            !_fallbackIds.Contains(message.wParam.ToInt32()) &&
            _registered.TryGetValue(message.wParam.ToInt32(), out var registration))
        {
            handled = true;
            Trigger(registration.Action, "WM_HOTKEY");
        }
    }

    private void PollPhysicalKeys(object? sender, EventArgs e)
    {
        if (_suspended) return;
        foreach (var id in _fallbackIds)
        {
            var registration = _registered[id];
            var down = _isKeyDown(registration.Gesture.VirtualKey) &&
                       AreModifiersDown(registration.Gesture.Modifiers);
            var wasDown = _keyStates.GetValueOrDefault(id);
            // Rearm only on primary-key release, not on modifier changes while held.
            if (!_isKeyDown(registration.Gesture.VirtualKey)) _keyStates[id] = false;
            else if (down && !wasDown) { _keyStates[id] = true; Trigger(registration.Action, "physical fallback"); }
            else _keyStates[id] = wasDown;
        }
    }

    private void Trigger(GlobalAction action, string source)
    {
        if (_suspended) return;
        if (!_activeActions.Add(action)) return;
        try
        {
            WriteStatus($"received {action} via {source}");
            if (action is GlobalAction.Region or GlobalAction.FullScreen or GlobalAction.ActiveWindow)
                CaptureRequested?.Invoke(this, new CaptureModeEventArgs(action switch
                { GlobalAction.Region => CaptureMode.Region, GlobalAction.FullScreen => CaptureMode.FullScreen, _ => CaptureMode.ActiveWindow }));
            else if (action == GlobalAction.ScreenRuler) ScreenRulerRequested?.Invoke(this, EventArgs.Empty);
            else PinActionRequested?.Invoke(action);
        }
        finally { _activeActions.Remove(action); }
    }

    internal static bool RunPinRoutingSelfTest()
    {
        using var host = new GlobalHotkeyHost();
        var captures = new List<CaptureMode>(); var pins = new List<GlobalAction>();
        var rulers = 0;
        host.CaptureRequested += (_, e) => captures.Add(e.Mode);
        host.PinActionRequested += pins.Add;
        host.ScreenRulerRequested += (_, _) => rulers++;
        foreach (var action in Enum.GetValues<GlobalAction>()) { host.Trigger(action, "self-test"); }
        if (rulers != 1 || !captures.SequenceEqual(new[] { CaptureMode.Region, CaptureMode.FullScreen, CaptureMode.ActiveWindow }) ||
            !pins.SequenceEqual(Enum.GetValues<GlobalAction>().Where(x => x is not (GlobalAction.Region or GlobalAction.FullScreen or GlobalAction.ActiveWindow or GlobalAction.ScreenRuler)))) return false;
        var actionCount = pins.Count;
        host.Suspend(); host.Trigger(GlobalAction.ClipboardPin, "self-test-suspended");
        if (pins.Count != actionCount) return false;
        host.Resume(); host.Trigger(GlobalAction.ClipboardPin, "self-test-resumed");
        if (pins.Count != actionCount + 1) return false;
        var settings = new AppSettings();
        if (CreateRegistrations(settings).Count != 5) return false;
        settings.ClipboardPinHotkey = "Shift+Ctrl+A";
        try { CreateRegistrations(settings); return false; } catch (InvalidOperationException) { }
        settings.ClipboardPinHotkey = ""; settings.TogglePinsHotkey = "";
        if (CreateRegistrations(settings).Count != 3) return false;
        settings.RegionHotkey = ""; settings.FullScreenHotkey = ""; settings.ActiveWindowHotkey = "";
        return CreateRegistrations(settings).Count == 0;
    }

    private bool AreModifiersDown(uint modifiers) =>
        ((modifiers & NativeMethods.ModControl) != 0) == _isKeyDown(0x11) &&
        ((modifiers & NativeMethods.ModShift) != 0) == _isKeyDown(0x10) &&
        ((modifiers & NativeMethods.ModAlt) != 0) == _isKeyDown(0x12) &&
        ((modifiers & NativeMethods.ModWin) != 0) == (_isKeyDown(0x5B) || _isKeyDown(0x5C));

    private void UnregisterAll()
    {
        foreach (var id in _registered.Keys.Except(_fallbackIds))
            _unregister(id);
        _registered.Clear();
        _fallbackIds.Clear();
        _keyStates.Clear();
        _activeActions.Clear();
        _keyStateTimer.Stop();
    }

    public void Dispose()
    {
        UnregisterAll();
        _keyStateTimer.Stop();
        _keyStateTimer.Tick -= PollPhysicalKeys;
        ComponentDispatcher.ThreadFilterMessage -= _messageHandler;
    }

    private static void WriteStatus(string status)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DiagnosticsRoot);
            File.AppendAllText(Path.Combine(AppPaths.DiagnosticsRoot, "hotkey-status.log"),
                $"{DateTimeOffset.Now:O} {status}{Environment.NewLine}");
        }
        catch { }
    }
}

public enum GlobalAction
{
    Region, FullScreen, ActiveWindow, ClipboardPin, TogglePins, RestorePin, RestoreInteraction, ScreenRuler,
    RecognizeClipboard, ScreenWords, Delay3, Delay5, Delay10, CancelDelay, History,
    ScrollingStart, ScrollingStop, OpenScrollProject, RecordingRegion, RecordingMonitor, RecordingPause, RecordingStop, ClosePins
}

public sealed class CaptureModeEventArgs(CaptureMode mode) : EventArgs
{
    public CaptureMode Mode { get; } = mode;
}
