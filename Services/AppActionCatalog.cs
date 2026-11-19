using NimCap.Screenshot.Models;
using L = NimCap.Screenshot.Localization.UiText;

namespace NimCap.Screenshot.Services;

internal sealed record AppActionDefinition(GlobalAction Action, string LabelKey, string Group = "", bool DefaultVisible = true, int Delay = 0, bool ShowInTray = true)
{
    public string Label => Delay > 0 ? L.Format(LabelKey, Delay) : L.Get(LabelKey);
}

internal static class AppActionCatalog
{
    internal static IReadOnlyList<AppActionDefinition> All { get; } = new AppActionDefinition[]
    {
        new(GlobalAction.Region, "RegionCapture"), new(GlobalAction.FullScreen, "FullScreenCapture"),
        new(GlobalAction.ActiveWindow, "ActiveWindowCapture"), new(GlobalAction.RecognizeClipboard, "RecognizeClipboardImage2", ShowInTray: false),
        new(GlobalAction.ScreenRuler, "ScreenRuler"), new(GlobalAction.ScreenWords, "ScreenTextCaptureThisSession"),
        new(GlobalAction.Delay3, "0Seconds", "DelayedCapture", Delay: 3), new(GlobalAction.Delay5, "0Seconds", "DelayedCapture", Delay: 5),
        new(GlobalAction.Delay10, "0Seconds", "DelayedCapture", Delay: 10), new(GlobalAction.CancelDelay, "CancelDelayedCapture", "DelayedCapture"),
        new(GlobalAction.History, "CaptureHistory"),
        new(GlobalAction.ScrollingStart, "SelectRegionAndStartScrollingCapture", "ScrollingCapture"),
        new(GlobalAction.ScrollingStop, "StopCaptureAndPreview", "ScrollingCapture"), new(GlobalAction.OpenScrollProject, "OpenStitchingProject", "ScrollingCapture"),
        new(GlobalAction.RecordingRegion, "SelectRegionThenClickTheVideoIcon", "Recording"),
        new(GlobalAction.RecordingMonitor, "RecordCurrentMonitorShowToolbar", "Recording"),
        new(GlobalAction.RecordingPause, "PauseResume", "Recording"), new(GlobalAction.RecordingStop, "StopAndSave", "Recording"),
        new(GlobalAction.ClipboardPin, "Pins"), new(GlobalAction.TogglePins, "HideShowAllPins2", DefaultVisible: false),
        new(GlobalAction.RestorePin, "RestoreLastClosedPin", DefaultVisible: false),
        new(GlobalAction.RestoreInteraction, "DisablePinClickThrough", DefaultVisible: false), new(GlobalAction.ClosePins, "CloseAllPins", DefaultVisible: false)
    };

    internal static string Hotkey(AppSettings settings, GlobalAction action) => action switch
    {
        GlobalAction.Region => settings.RegionHotkey, GlobalAction.FullScreen => settings.FullScreenHotkey,
        GlobalAction.ActiveWindow => settings.ActiveWindowHotkey, GlobalAction.ClipboardPin => settings.ClipboardPinHotkey,
        GlobalAction.TogglePins => settings.TogglePinsHotkey, GlobalAction.RestorePin => settings.RestorePinHotkey,
        GlobalAction.RestoreInteraction => settings.RestorePinInteractionHotkey, GlobalAction.ScreenRuler => settings.ScreenRulerHotkey,
        _ => settings.ActionHotkeys.GetValueOrDefault(action.ToString(), "")
    };

    internal static void SetHotkey(AppSettings settings, GlobalAction action, string value)
    {
        switch (action)
        {
            case GlobalAction.Region: settings.RegionHotkey = value; break;
            case GlobalAction.FullScreen: settings.FullScreenHotkey = value; break;
            case GlobalAction.ActiveWindow: settings.ActiveWindowHotkey = value; break;
            case GlobalAction.ClipboardPin: settings.ClipboardPinHotkey = value; break;
            case GlobalAction.TogglePins: settings.TogglePinsHotkey = value; break;
            case GlobalAction.RestorePin: settings.RestorePinHotkey = value; break;
            case GlobalAction.RestoreInteraction: settings.RestorePinInteractionHotkey = value; break;
            case GlobalAction.ScreenRuler: settings.ScreenRulerHotkey = value; break;
            default: settings.ActionHotkeys[action.ToString()] = value; break;
        }
    }

    internal static bool Visible(AppSettings settings, AppActionDefinition definition)
        => definition.ShowInTray && settings.TrayActions.GetValueOrDefault(definition.Action.ToString(), definition.DefaultVisible);
}
