using SnapSharp.Models;
using SnapSharp.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using TabControl = System.Windows.Controls.TabControl;
using ContextMenu = System.Windows.Controls.ContextMenu;

namespace SnapSharp.Services;

internal static class WindowThemeSelfTest
{
    public static bool Run(BitmapSource source)
    {
        var settings = new SettingsService();
        var images = new SkiaImageService();
        var clipboard = new ClipboardService();
        using var hotkeys = new GlobalHotkeyHost();
        var region = new System.Drawing.Rectangle(0, 0, source.PixelWidth, source.PixelHeight);
        Func<Window>[] factories =
        [
            () => new ScrollCaptureWindow(region, source),
            () => new RecognitionWindow(source, settings),
            () => new RecordingWindow(region, settings),
            () => new RecordingToolbarWindow(),
            () => new RecordingSetupWindow(),
            () => new SaveImageWindow(source, images, settings),
            () => new SettingsWindow(settings, new StartupRegistrationService(), hotkeys),
            () => new HistoryWindow(new HistoryService(images, settings), images, settings, clipboard, _ => { }),
            () => new EditorWindow(new CaptureResult(source, region, region), _ => { }, clipboard),
            () => new CanvasExpandDialog(source.PixelWidth, source.PixelHeight),
            () => new TextInputDialog(),
            () => new WatermarkDialog(),
            () => PrivacyConsentDialog.Create(null, new Uri("https://example.com/translate"), "待确认的示例文字", null),
            () => new PinWindow(source, images, settings, clipboard),
            () => new SelectionWindow(new CapturedScreen(region, source), [], new ScreenCaptureService())
        ];
        var names = new List<string>();
        foreach (var factory in factories)
        {
            var window = factory();
            try
            {
                // Test before Loaded: delayed application hides startup white flashes.
                if (!ReferenceEquals(window.Style, Application.Current.FindResource("AppWindow")))
                    throw new InvalidOperationException(window.GetType().Name + " does not explicitly use the shared window theme.");
                if (window is RecordingToolbarWindow)
                {
                    if (window.Background is not SolidColorBrush { Color.A: 0 }) return false;
                    Render(window, 1); Render(window, 1.5); Render(window, 2);
                }
                else if (window is PinWindow)
                {
                    if (window.Background is not SolidColorBrush { Color.A: 0 }) return false;
                    RenderMenus(window);
                }
                else if (window is not SelectionWindow)
                {
                    if (window.Background is not SolidColorBrush bg || window.Foreground is not SolidColorBrush fg ||
                        bg.Color != ((SolidColorBrush)Application.Current.FindResource("WindowBackgroundBrush")).Color ||
                        Contrast(bg.Color, fg.Color) < 4.5)
                        throw new InvalidOperationException(window.GetType().Name + " has an unthemed surface or unreadable text.");
                    Render(window, 1);
                    if (window is ScrollCaptureWindow or RecognitionWindow)
                    {
                        Render(window, 1.5);
                        Render(window, 2);
                    }
                    if (window is RecognitionWindow)
                    {
                        var tabs = (TabControl)window.FindName("ResultTabs");
                        for (var index = 1; index < tabs.Items.Count; index++)
                        {
                            tabs.SelectedIndex = index;
                            Render(window, 1, "-tab" + index);
                        }
                        tabs.SelectedIndex = 0;
                    }
                }
                names.Add(window.GetType().Name);
            }
            finally { window.Close(); }
        }
        // Temporary windows must have the same surface and controls.
        var temporary = new Window();
        ThemeService.ApplyToWindow(temporary);
        if (!ReferenceEquals(temporary.Style, Application.Current.FindResource("AppWindow"))) return false;
        temporary.Close();
        foreach (var buttons in new[] { MessageBoxButton.OK, MessageBoxButton.OKCancel, MessageBoxButton.YesNo, MessageBoxButton.YesNoCancel })
        {
            var dialog = ThemedMessageBox.Create(null, "确认弹窗主题测试\n覆盖已有文件前必须确认。", "确认覆盖", buttons,
                MessageBoxImage.Warning, MessageBoxResult.No, out var result);
            try
            {
                Render(dialog, 1);
                var actions = Descendants(dialog.Content as DependencyObject).OfType<Button>().ToArray();
                if (actions.Count(b => b.IsDefault) != 1) return false;
                var choice = buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel ? "否" : "确定";
                var button = actions.Single(b => Equals(b.Content, choice));
                if (choice == "否" && !button.IsDefault) return false;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (result() != (choice == "否" ? MessageBoxResult.No : MessageBoxResult.OK)) return false;
            }
            finally { dialog.Close(); }
        }
        File.WriteAllLines(Path.Combine(AppPaths.DiagnosticsRoot, "window-theme-audit.txt"), names);
        return true;
    }

    private static void Render(Window window, double scale, string suffix = "")
    {
        if (window.Content is not FrameworkElement content) throw new InvalidOperationException("Missing window content.");
        var width = double.IsNaN(window.Width) ? 600 : window.Width;
        var height = double.IsNaN(window.Height) ? 380 : window.Height;
        if (window is RecordingToolbarWindow)
        {
            content.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            width = content.DesiredSize.Width; height = content.DesiredSize.Height;
        }
        var size = new System.Windows.Size(width, height);
        content.Measure(size);
        content.Arrange(new Rect(size));
        content.UpdateLayout();
        if (window is EditorWindow)
        {
            foreach (var button in Descendants(content).OfType<Button>())
                if (button.Foreground is not SolidColorBrush brush || Contrast(brush.Color,
                    ((SolidColorBrush)Application.Current.FindResource("SurfaceBrush")).Color) < 4.5)
                    throw new InvalidOperationException("Editor toolbar has unreadable button text.");
        }
        if (window is RecognitionWindow && ((TabControl)window.FindName("ResultTabs")).SelectedIndex == 0)
        {
            var editor = (TextBox)window.FindName("TextEditor");
            if (editor.ActualHeight < 100) throw new InvalidOperationException($"Multiline OCR editor: {editor.RenderSize}; tabs={((TabControl)window.FindName("ResultTabs")).RenderSize}, selected={((TabControl)window.FindName("ResultTabs")).SelectedIndex}, height={editor.Height}.");
        }
        // Include the actual window background, which rendering Content alone misses.
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(window.Background, null, new Rect(size));
        }
        var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(AppPaths.DiagnosticsRoot, $"theme-{window.GetType().Name}{suffix}-{scale:0.0}x.png"));
        encoder.Save(stream);
    }

    private static void RenderMenus(Window pin)
    {
        var menu = (ContextMenu)pin.FindResource("PinMenu");
        // Populate the real dynamic group menu without opening a desktop popup.
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        menu.ApplyTemplate();
        menu.Measure(new System.Windows.Size(420, 1000));
        menu.Arrange(new Rect(menu.DesiredSize));
        menu.UpdateLayout();
        if (!menu.OverridesDefaultStyle || menu.Template.FindName("MenuChrome", menu) is not Border chrome ||
            chrome.Background != Application.Current.FindResource("SurfaceBrush"))
            throw new InvalidOperationException("Context menu still uses the system gutter template.");
        var items = menu.Items.OfType<System.Windows.Controls.MenuItem>().ToArray();
        if (items.Length < 10 || items.Any(item => item.ActualHeight < 32))
            throw new InvalidOperationException("Context menu items were not laid out with the shared spacing.");
        CheckMenuIcons(menu.Items);
        foreach (var separator in menu.Items.OfType<Separator>())
            if (!separator.OverridesDefaultStyle || separator.Background != Application.Current.FindResource("BorderBrush"))
                throw new InvalidOperationException("Menu separator still uses the system theme.");
        foreach (var scale in new[] { 1d, 1.5d, 2d })
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth * scale), (int)Math.Ceiling(menu.ActualHeight * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(menu);
            // A default light icon gutter at the left used to survive the dark item template.
            var pixel = new byte[4];
            bitmap.CopyPixels(new Int32Rect((int)(2 * scale), bitmap.PixelHeight / 2, 1, 1), pixel, 4, 0);
            var surface = ((SolidColorBrush)chrome.Background).Color;
            if (pixel[0] != surface.B || pixel[1] != surface.G || pixel[2] != surface.R || pixel[3] != 255)
                throw new InvalidOperationException("Context menu contains an unthemed left gutter.");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(AppPaths.DiagnosticsRoot, $"theme-PinMenu-{scale:0.0}x.png"));
            encoder.Save(output);
        }
        var group = items.Single(item => Equals(item.Tag, "Groups"));
        group.ApplyTemplate();
        if (group.Template.FindName("PART_Popup", group) is not System.Windows.Controls.Primitives.Popup { Child: Border submenu })
            throw new InvalidOperationException("Missing themed submenu.");
        submenu.Measure(new System.Windows.Size(420, 1000));
        submenu.Arrange(new Rect(submenu.DesiredSize)); submenu.UpdateLayout();
        var ungroup = group.Items.OfType<System.Windows.Controls.MenuItem>().Single(item => item.IsCheckable);
        ungroup.ApplyTemplate();
        if (!ungroup.IsChecked || ungroup.Template.FindName("Check", ungroup) is not TextBlock { Visibility: Visibility.Visible } ||
            submenu.Background != Application.Current.FindResource("SurfaceBrush"))
            throw new InvalidOperationException("Dynamic submenu/checkmark lost the shared theme.");
        var defaultMenu = new ContextMenu(); defaultMenu.Items.Add(new System.Windows.Controls.MenuItem { Header = "默认菜单" });
        defaultMenu.ApplyTemplate();
        if (!defaultMenu.OverridesDefaultStyle || defaultMenu.Template.FindName("MenuChrome", defaultMenu) is not Border)
            throw new InvalidOperationException("Implicit context menu style is missing.");
    }

    private static void CheckMenuIcons(System.Windows.Controls.ItemCollection items)
    {
        foreach (var item in items.OfType<System.Windows.Controls.MenuItem>())
        {
            item.ApplyTemplate();
            if (item.Icon is not MahApps.Metro.IconPacks.PackIconLucide { Width: 18, Height: 18 } icon ||
                icon.Kind != MenuIcons.Kind(item.Header?.ToString()))
                throw new InvalidOperationException("Missing semantic menu icon: " + item.Header);
            if (item.Template.FindName("IconHost", item) is not ContentPresenter { Visibility: Visibility.Visible })
                throw new InvalidOperationException("Menu icon hidden by check state: " + item.Header);
            CheckMenuIcons(item.Items);
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject? parent)
    {
        if (parent is null) yield break;
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }

    private static double Contrast(Color a, Color b)
    {
        static double Channel(byte c) { var s = c / 255d; return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4); }
        static double Luminance(Color c) => .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
        var x = Luminance(a); var y = Luminance(b);
        return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
}
