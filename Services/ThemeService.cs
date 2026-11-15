using SnapSharp.Native;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SnapSharp.Services;

public sealed class ThemeService : IDisposable
{
    private bool _disposed;

    public static bool IsDarkMode { get; private set; } = true;
    public static event EventHandler? ThemeChanged;

    public void Start()
    {
        ApplyCurrentTheme();
    }

    public static void ApplyToWindow(Window window)
    {
        // Implicit Window styles do not match derived window types.
        // Keep explicit styles (including transparent capture overlays) intact.
        if (window.ReadLocalValue(FrameworkElement.StyleProperty) == DependencyProperty.UnsetValue)
            window.SetResourceReference(FrameworkElement.StyleProperty, "AppWindow");
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero) return;
        var dark = IsDarkMode && !SystemParameters.HighContrast ? 1 : 0;
        if (NativeMethods.DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
            NativeMethods.DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
    }

    private static void ApplyCurrentTheme()
    {
        IsDarkMode = true;
        var resources = Application.Current.Resources;
        if (SystemParameters.HighContrast)
        {
            resources["WindowBackgroundBrush"] = System.Windows.SystemColors.WindowBrush;
            resources["SurfaceBrush"] = System.Windows.SystemColors.ControlBrush;
            resources["SurfaceAltBrush"] = System.Windows.SystemColors.ControlBrush;
            resources["ControlBrush"] = System.Windows.SystemColors.WindowBrush;
            resources["ControlHoverBrush"] = System.Windows.SystemColors.HighlightBrush;
            resources["TextBrush"] = System.Windows.SystemColors.WindowTextBrush;
            resources["TextSecondaryBrush"] = System.Windows.SystemColors.GrayTextBrush;
            resources["BorderBrush"] = System.Windows.SystemColors.ActiveBorderBrush;
            resources["OverlaySurfaceBrush"] = System.Windows.SystemColors.ControlBrush;
            resources["OverlayTextBrush"] = System.Windows.SystemColors.ControlTextBrush;
            resources["ToolbarBackgroundBrush"] = System.Windows.SystemColors.ControlBrush;
            resources["ToolbarBorderBrush"] = System.Windows.SystemColors.ActiveBorderBrush;
            resources["ToolbarSeparatorBrush"] = System.Windows.SystemColors.ActiveBorderBrush;
            resources["ToolbarActiveBrush"] = System.Windows.SystemColors.HighlightBrush;
            resources["ToolbarActiveHoverBrush"] = System.Windows.SystemColors.HotTrackBrush;
            resources["ToolbarShadowColor"] = System.Windows.Media.Colors.Transparent;
        }
        else
        {
            SetBrush(resources, "WindowBackgroundBrush", "#17181B");
            SetBrush(resources, "SurfaceBrush", "#242529");
            SetBrush(resources, "SurfaceAltBrush", "#2D2F34");
            SetBrush(resources, "ControlBrush", "#303238");
            SetBrush(resources, "ControlHoverBrush", "#3C3F46");
            SetBrush(resources, "TextBrush", "#F4F5F7");
            SetBrush(resources, "TextSecondaryBrush", "#A7ABB4");
            SetBrush(resources, "BorderBrush", "#444851");
            SetBrush(resources, "OverlaySurfaceBrush", "#F21E1F23");
            SetBrush(resources, "OverlayTextBrush", "#F6F7F9");
            SetBrush(resources, "ToolbarBackgroundBrush", "#FA232428");
            SetBrush(resources, "ToolbarBorderBrush", "#8050555F");
            SetBrush(resources, "ToolbarSeparatorBrush", "#3B3E45");
            SetBrush(resources, "ToolbarActiveBrush", "#398BFF");
            SetBrush(resources, "ToolbarActiveHoverBrush", "#4A96FF");
            resources["ToolbarShadowColor"] = (Color)System.Windows.Media.ColorConverter.ConvertFromString("#D9000000");
        }

        foreach (Window window in Application.Current.Windows) ApplyToWindow(window);
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void SetBrush(ResourceDictionary resources, string key, string value)
    {
        var brush = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(value));
        brush.Freeze();
        resources[key] = brush;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
