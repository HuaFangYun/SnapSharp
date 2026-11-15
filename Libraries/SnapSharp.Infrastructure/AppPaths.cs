namespace SnapSharp.Services;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapSharp");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string HistoryRoot => Path.Combine(Root, "History");
    public static string HistoryIndex => Path.Combine(HistoryRoot, "index.json");
    public static string DiagnosticsRoot => Path.Combine(Root, "Diagnostics");
    public static string RenderBenchmark => Path.Combine(DiagnosticsRoot, "render-benchmark.txt");
    public static string PluginsRoot => Path.Combine(Root, "Plugins");
}
