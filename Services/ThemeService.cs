using NimCap.Screenshot.Native;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace NimCap.Screenshot.Services;

public sealed class ThemeService : IDisposable
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;
    private const int DwmColorDefault = -1;

    public static readonly DependencyProperty UseThemedTitleBarProperty = DependencyProperty.RegisterAttached(
        "UseThemedTitleBar", typeof(bool), typeof(ThemeService), new PropertyMetadata(false, OnUseThemedTitleBarChanged));

    public static bool GetUseThemedTitleBar(Window window) => (bool)window.GetValue(UseThemedTitleBarProperty);
    public static void SetUseThemedTitleBar(Window window, bool value) => window.SetValue(UseThemedTitleBarProperty, value);

    private bool _disposed;

    public static bool IsDarkMode => true;
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
        ApplyTitleBar(window);
    }

    private static void OnUseThemedTitleBarChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not Window window) return;
        if ((bool)e.NewValue)
        {
            window.SourceInitialized += OnWindowSourceInitialized;
            ApplyTitleBar(window);
        }
        else window.SourceInitialized -= OnWindowSourceInitialized;
    }

    private static void OnWindowSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is Window window) ApplyTitleBar(window);
    }

    private static void ApplyTitleBar(Window window)
    {
        if (window.WindowStyle == WindowStyle.None || window.AllowsTransparency) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero) return;
        var dark = 1;
        if (NativeMethods.DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
            NativeMethods.DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));

        // Explicit colors keep active and inactive captions consistent with the app.
        // Older Windows versions retain the immersive dark mode fallback above.
        var caption = GetTitleBarColor(window, "WindowBackgroundBrush");
        var text = GetTitleBarColor(window, "TextBrush");
        NativeMethods.DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref caption, sizeof(int));
        NativeMethods.DwmSetWindowAttribute(handle, DwmwaTextColor, ref text, sizeof(int));
    }

    private static int GetTitleBarColor(Window window, string resourceKey) =>
        window.TryFindResource(resourceKey) is SolidColorBrush brush
            ? brush.Color.R | (brush.Color.G << 8) | (brush.Color.B << 16)
            : DwmColorDefault;

    private static void ApplyCurrentTheme()
    {
        var resources = Application.Current.Resources;
        // NimCap.Screenshot always uses its own dark palette, independent of Windows settings.
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
