using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace SnapSharp.Views;

internal static class PrivacyConsentDialog
{
    public static bool Confirm(Window owner, Uri endpoint, string? text, BitmapSource? image)
    {
        return Create(owner, endpoint, text, image).ShowDialog() == true;
    }

    internal static Window Create(Window? owner, Uri endpoint, string? text, BitmapSource? image)
    {
        var window = new Window { Title = "确认发送 · SnapSharp", Owner = owner, Width = 640, Height = 540,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SetResourceReference(FrameworkElement.StyleProperty, "AppWindow");
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/SnapSharp;component/Views/ToolbarStyles.xaml", UriKind.Relative) });
        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = $"以下内容将发送至：\n{endpoint.AbsoluteUri}\n请确认服务商可信，内容不含敏感信息。关闭此窗口不会发送。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        FrameworkElement preview = image is null ? new TextBox { Text = text ?? "", IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } : new ScrollViewer { Content = new System.Windows.Controls.Image { Source = image, Stretch = System.Windows.Media.Stretch.Uniform } };
        Grid.SetRow(preview, 1); grid.Children.Add(preview);
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 90, Margin = new Thickness(8) };
        var send = new Button { Content = "确认发送", MinWidth = 100, Margin = new Thickness(8) }; send.Click += (_, _) => window.DialogResult = true;
        cancel.Style = send.Style = (Style)window.FindResource("DialogButton");
        buttons.Children.Add(cancel); buttons.Children.Add(send); Grid.SetRow(buttons, 2); grid.Children.Add(buttons); window.Content = grid;
        return window;
    }
}
