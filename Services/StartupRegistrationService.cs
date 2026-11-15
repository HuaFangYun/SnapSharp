using Microsoft.Win32;

namespace SnapSharp.Services;

public sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SnapSharp";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value &&
               string.Equals(value, GetCommand(), StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开 Windows 启动项注册表项。");

        if (enabled)
        {
            key.SetValue(ValueName, GetCommand(), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string GetCommand()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            throw new InvalidOperationException("无法确定 SnapSharp 可执行文件路径。");
        return $"\"{executable}\"";
    }
}
