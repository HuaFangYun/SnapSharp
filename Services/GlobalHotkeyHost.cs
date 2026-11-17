using L = SnapSharp.Localization.UiText;
using SnapSharp.Models;
using SnapSharp.Native;
using System.ComponentModel;
using System.Windows.Interop;
using System.Windows.Threading;

namespace SnapSharp.Services;

public sealed class GlobalHotkeyHost : IDisposable
{
    private const int RegionHotkeyId = 0x5011;
    private const int FullScreenHotkeyId = 0x5012;
    private const int ActiveWindowHotkeyId = 0x5013;
    private readonly ThreadMessageEventHandler _messageHandler;
    private readonly DispatcherTimer _keyStateTimer;
    private Dictionary<int, (GlobalAction Action, HotkeyGesture Gesture)> _registered = [];
    private readonly Dictionary<int, bool> _keyStates = [];
    private long _lastTrigger;
    private bool _suspended;

    public event EventHandler<CaptureModeEventArgs>? CaptureRequested;
    public event Action<GlobalAction>? PinActionRequested;

    public void Suspend()
    {
        if (_suspended) return;
        _suspended = true;
        foreach (var id in _registered.Keys) NativeMethods.UnregisterHotKey(nint.Zero, id);
    }
    public void Resume()
    {
        if (!_suspended) return;
        RegisterAll(_registered);
        _keyStates.Clear();
        foreach (var (id, registration) in _registered) _keyStates[id] = IsKeyDown(registration.Gesture.VirtualKey);
        _suspended = false;
    }

    public GlobalHotkeyHost()
    {
        _messageHandler = OnThreadMessage;
        ComponentDispatcher.ThreadFilterMessage += _messageHandler;
        _keyStateTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(25)
        };
        _keyStateTimer.Tick += PollPhysicalKeys;
        _keyStateTimer.Start();
    }

    public void Register(AppSettings settings)
    {
        var desired = CreateRegistrations(settings);
        var previous = _registered.ToDictionary(x => x.Key, x => x.Value);
        UnregisterAll();
        try
        {
            RegisterAll(desired);
            _registered = desired;
            _keyStates.Clear();
            WriteStatus("registered");
        }
        catch
        {
            UnregisterAll();
            if (previous.Count > 0)
            {
                RegisterAll(previous);
                _registered = previous;
            }
            throw;
        }
        finally
        {
            if (_suspended) foreach (var id in _registered.Keys) NativeMethods.UnregisterHotKey(nint.Zero, id);
        }
    }

    internal static Dictionary<int, (GlobalAction Action, HotkeyGesture Gesture)> CreateRegistrations(AppSettings settings)
    {
        var desired = new Dictionary<int, (GlobalAction, HotkeyGesture)>();
        var shortcuts = new[]
        {
            (RegionHotkeyId, GlobalAction.Region, settings.RegionHotkey, L.Get("RegionCapture")),
            (FullScreenHotkeyId, GlobalAction.FullScreen, settings.FullScreenHotkey, L.Get("FullScreenCapture")),
            (ActiveWindowHotkeyId, GlobalAction.ActiveWindow, settings.ActiveWindowHotkey, L.Get("ActiveWindowCapture")),
            (0x5020 + (int)GlobalAction.ClipboardPin, GlobalAction.ClipboardPin, settings.ClipboardPinHotkey, L.Get("PinClipboardContent")),
            (0x5020 + (int)GlobalAction.TogglePins, GlobalAction.TogglePins, settings.TogglePinsHotkey, L.Get("ShowHidePins")),
            (0x5020 + (int)GlobalAction.RestorePin, GlobalAction.RestorePin, settings.RestorePinHotkey, L.Get("RestorePin")),
            (0x5020 + (int)GlobalAction.RestoreInteraction, GlobalAction.RestoreInteraction, settings.RestorePinInteractionHotkey, L.Get("RestorePinInteraction"))
        };
        foreach (var (id, action, text, label) in shortcuts)
            if (!string.IsNullOrWhiteSpace(text)) desired[id] = (action, Parse(text, label));
        if (desired.Values.GroupBy(x => (x.Item2.Modifiers, x.Item2.VirtualKey)).Any(x => x.Count() > 1))
            throw new InvalidOperationException(L.Get("GlobalShortcutsMustBeUniqueIncludingCapture"));
        return desired;
    }

    private void RegisterAll(Dictionary<int, (GlobalAction Action, HotkeyGesture Gesture)> registrations)
    {
        var registeredIds = new List<int>();
        foreach (var (id, registration) in registrations)
        {
            if (!NativeMethods.RegisterHotKey(nint.Zero, id,
                    registration.Gesture.Modifiers, registration.Gesture.VirtualKey))
            {
                foreach (var registeredId in registeredIds)
                    NativeMethods.UnregisterHotKey(nint.Zero, registeredId);
                throw new Win32Exception(L.Format("CouldNotRegisterGlobalShortcut0Another", registration.Gesture.DisplayText));
            }
            registeredIds.Add(id);
        }
    }

    private static HotkeyGesture Parse(string text, string label)
    {
        if (HotkeyGesture.TryParse(text, out var gesture, out var error)) return gesture!;
        throw new InvalidOperationException(L.Format("Invalid0Shortcut1", label, error));
    }

    private void OnThreadMessage(ref MSG message, ref bool handled)
    {
        if (!_suspended && message.message == NativeMethods.WmHotKey &&
            _registered.TryGetValue(message.wParam.ToInt32(), out var registration))
        {
            handled = true;
            Trigger(registration.Action, "WM_HOTKEY");
        }
    }

    private void PollPhysicalKeys(object? sender, EventArgs e)
    {
        if (_suspended) return;
        foreach (var (id, registration) in _registered)
        {
            var down = IsKeyDown(registration.Gesture.VirtualKey) &&
                       AreModifiersDown(registration.Gesture.Modifiers);
            var wasDown = _keyStates.GetValueOrDefault(id);
            _keyStates[id] = down;
            if (down && !wasDown) Trigger(registration.Action, "physical fallback");
        }
    }

    private void Trigger(GlobalAction action, string source)
    {
        if (_suspended) return;
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastTrigger) < 250) return;
        Interlocked.Exchange(ref _lastTrigger, now);
        WriteStatus($"received {action} via {source}");
        if (action is GlobalAction.Region or GlobalAction.FullScreen or GlobalAction.ActiveWindow)
            CaptureRequested?.Invoke(this, new CaptureModeEventArgs(action switch
            { GlobalAction.Region => CaptureMode.Region, GlobalAction.FullScreen => CaptureMode.FullScreen, _ => CaptureMode.ActiveWindow }));
        else PinActionRequested?.Invoke(action);
    }

    internal static bool RunPinRoutingSelfTest()
    {
        using var host = new GlobalHotkeyHost();
        var captures = new List<CaptureMode>(); var pins = new List<GlobalAction>();
        host.CaptureRequested += (_, e) => captures.Add(e.Mode);
        host.PinActionRequested += pins.Add;
        foreach (var action in Enum.GetValues<GlobalAction>()) { host._lastTrigger = 0; host.Trigger(action, "self-test"); }
        if (!captures.SequenceEqual(new[] { CaptureMode.Region, CaptureMode.FullScreen, CaptureMode.ActiveWindow }) ||
            !pins.SequenceEqual(new[] { GlobalAction.ClipboardPin, GlobalAction.TogglePins, GlobalAction.RestorePin, GlobalAction.RestoreInteraction })) return false;
        host.Suspend(); host._lastTrigger = 0; host.Trigger(GlobalAction.ClipboardPin, "self-test-suspended");
        if (pins.Count != 4) return false;
        host.Resume(); host._lastTrigger = 0; host.Trigger(GlobalAction.ClipboardPin, "self-test-resumed");
        if (pins.Count != 5) return false;
        var settings = new AppSettings();
        if (CreateRegistrations(settings).Count != 5) return false;
        settings.ClipboardPinHotkey = "Shift+Ctrl+A";
        try { CreateRegistrations(settings); return false; } catch (InvalidOperationException) { }
        settings.ClipboardPinHotkey = ""; settings.TogglePinsHotkey = "";
        if (CreateRegistrations(settings).Count != 3) return false;
        settings.RegionHotkey = ""; settings.FullScreenHotkey = ""; settings.ActiveWindowHotkey = "";
        return CreateRegistrations(settings).Count == 0;
    }

    private static bool AreModifiersDown(uint modifiers) =>
        ((modifiers & NativeMethods.ModControl) != 0) == IsKeyDown(0x11) &&
        ((modifiers & NativeMethods.ModShift) != 0) == IsKeyDown(0x10) &&
        ((modifiers & NativeMethods.ModAlt) != 0) == IsKeyDown(0x12) &&
        ((modifiers & NativeMethods.ModWin) != 0) == (IsKeyDown(0x5B) || IsKeyDown(0x5C));

    private static bool IsKeyDown(uint virtualKey) =>
        (NativeMethods.GetAsyncKeyState((int)virtualKey) & 0x8000) != 0;

    private void UnregisterAll()
    {
        foreach (var id in _registered.Keys)
            NativeMethods.UnregisterHotKey(nint.Zero, id);
        _registered.Clear();
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
            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllText(Path.Combine(AppPaths.Root, "hotkey-status.log"),
                $"{DateTimeOffset.Now:O} {status}");
        }
        catch { }
    }
}

public enum GlobalAction { Region, FullScreen, ActiveWindow, ClipboardPin, TogglePins, RestorePin, RestoreInteraction }

public sealed class CaptureModeEventArgs(CaptureMode mode) : EventArgs
{
    public CaptureMode Mode { get; } = mode;
}
