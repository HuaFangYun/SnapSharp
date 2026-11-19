using L = NimCap.Screenshot.Localization.UiText;
using NimCap.Screenshot.Native;
using System.Windows.Input;

namespace NimCap.Screenshot.Services;

public sealed record HotkeyGesture(uint Modifiers, uint VirtualKey, string DisplayText)
{
    public static bool TryParse(string? text, out HotkeyGesture? gesture, out string error)
    {
        gesture = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = L.Get("ShortcutCannotBeEmpty");
            return false;
        }

        uint modifiers = NativeMethods.ModNoRepeat;
        Key? key = null;
        var normalized = new List<string>();
        foreach (var rawPart in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (rawPart.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= NativeMethods.ModControl;
                    if (!normalized.Contains("Ctrl")) normalized.Add("Ctrl");
                    break;
                case "SHIFT":
                    modifiers |= NativeMethods.ModShift;
                    if (!normalized.Contains("Shift")) normalized.Add("Shift");
                    break;
                case "ALT":
                    modifiers |= NativeMethods.ModAlt;
                    if (!normalized.Contains("Alt")) normalized.Add("Alt");
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= NativeMethods.ModWin;
                    if (!normalized.Contains("Win")) normalized.Add("Win");
                    break;
                default:
                    if (key is not null)
                    {
                        error = L.Get("AShortcutCanContainOnlyOneRegular");
                        return false;
                    }
                    try
                    {
                        key = (Key)new KeyConverter().ConvertFromInvariantString(rawPart)!;
                    }
                    catch
                    {
                        error = L.Format("UnrecognizedKey0", rawPart);
                        return false;
                    }
                    break;
            }
        }

        if (key is null || key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            error = L.Get("AShortcutMustContainARegularKey");
            return false;
        }
        if ((modifiers & ~NativeMethods.ModNoRepeat) == 0)
        {
            error = L.Get("AShortcutRequiresAtLeastOneModifier");
            return false;
        }

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key.Value);
        if (virtualKey == 0)
        {
            error = L.Get("ThisKeyCannotBeRegisteredAsA");
            return false;
        }
        normalized.Add(key.Value.ToString());
        gesture = new HotkeyGesture(modifiers, virtualKey, string.Join('+', normalized));
        return true;
    }
}
