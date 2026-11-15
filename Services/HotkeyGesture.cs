using SnapSharp.Native;
using System.Windows.Input;

namespace SnapSharp.Services;

public sealed record HotkeyGesture(uint Modifiers, uint VirtualKey, string DisplayText)
{
    public static bool TryParse(string? text, out HotkeyGesture? gesture, out string error)
    {
        gesture = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "快捷键不能为空。";
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
                        error = "快捷键只能包含一个普通按键。";
                        return false;
                    }
                    try
                    {
                        key = (Key)new KeyConverter().ConvertFromInvariantString(rawPart)!;
                    }
                    catch
                    {
                        error = $"无法识别按键“{rawPart}”。";
                        return false;
                    }
                    break;
            }
        }

        if (key is null || key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            error = "快捷键必须包含一个普通按键。";
            return false;
        }
        if ((modifiers & ~NativeMethods.ModNoRepeat) == 0)
        {
            error = "快捷键至少需要 Ctrl、Shift、Alt 或 Win 中的一个修饰键。";
            return false;
        }

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key.Value);
        if (virtualKey == 0)
        {
            error = "该按键不能注册为全局快捷键。";
            return false;
        }
        normalized.Add(key.Value.ToString());
        gesture = new HotkeyGesture(modifiers, virtualKey, string.Join('+', normalized));
        return true;
    }
}
