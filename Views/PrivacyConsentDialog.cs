using L = SnapSharp.Localization.UiText;
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
        var window = new Window { Title = L.Get("ConfirmSendingSnapSharp"), Owner = owner, Width = 640, Height = 540,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SetResourceReference(FrameworkElement.StyleProperty, "AppWindow");
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/SnapSharp;component/Views/ToolbarStyles.xaml", UriKind.Relative) });
        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = L.Format("TheFollowingContentWillBeSentTo", endpoint.AbsoluteUri), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        FrameworkElement preview = image is null ? new TextBox { Text = text ?? "", IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } : new ScrollViewer { Content = new System.Windows.Controls.Image { Source = image, Stretch = System.Windows.Media.Stretch.Uniform } };
        Grid.SetRow(preview, 1); grid.Children.Add(preview);
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var cancel = new Button { Content = L.Get("Cancel"), IsCancel = true, MinWidth = 90, Margin = new Thickness(8) };
        var send = new Button { Content = L.Get("ConfirmAndSend"), MinWidth = 100, Margin = new Thickness(8) }; send.Click += (_, _) => window.DialogResult = true;
        cancel.Style = send.Style = (Style)window.FindResource("DialogButton");
        buttons.Children.Add(cancel); buttons.Children.Add(send); Grid.SetRow(buttons, 2); grid.Children.Add(buttons); window.Content = grid;
        return window;
    }
}
