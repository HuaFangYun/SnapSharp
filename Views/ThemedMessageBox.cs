using L = NimCap.Screenshot.Localization.UiText;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NimCap.Screenshot.Services;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace NimCap.Screenshot.Views;

/// <summary>Application confirmations use the same theme as their owner.</summary>
internal static class ThemedMessageBox
{
    public static MessageBoxResult Show(string text, string caption = "NimCap.Screenshot",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None) =>
        Show(Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.IsActive), text, caption, buttons, icon, defaultResult);

    public static MessageBoxResult Show(Window? owner, string text, string caption = "NimCap.Screenshot",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var window = Create(owner, text, caption, buttons, icon, defaultResult, out var result);
        WindowDialogService.ShowDialog(window, owner);
        return result();
    }

    internal static Window Create(Window? owner, string text, string caption, MessageBoxButton buttons,
        MessageBoxImage icon, MessageBoxResult defaultResult, out Func<MessageBoxResult> result)
    {
        var choices = buttons switch
        {
            MessageBoxButton.OK => new[] { MessageBoxResult.OK },
            MessageBoxButton.OKCancel => new[] { MessageBoxResult.OK, MessageBoxResult.Cancel },
            MessageBoxButton.YesNo => new[] { MessageBoxResult.Yes, MessageBoxResult.No },
            MessageBoxButton.YesNoCancel => new[] { MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel },
            _ => throw new ArgumentOutOfRangeException(nameof(buttons))
        };
        var selected = MessageBoxResult.None;
        var cancel = choices.Contains(MessageBoxResult.Cancel) ? MessageBoxResult.Cancel :
            buttons == MessageBoxButton.OK ? MessageBoxResult.OK : MessageBoxResult.None;
        var window = new Window
        {
            Title = caption, Width = 520, SizeToContent = SizeToContent.Height,
            MaxWidth = SystemParameters.WorkArea.Width, MaxHeight = SystemParameters.WorkArea.Height,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = owner is null,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner
        };
        if (owner?.IsVisible == true) window.Owner = owner;
        window.SetResourceReference(FrameworkElement.StyleProperty, "AppWindow");
        var panel = new Grid { Margin = new Thickness(22) };
        panel.RowDefinitions.Add(new RowDefinition());
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var body = new StackPanel();
        var label = icon switch { MessageBoxImage.Error => L.Get("Error"), MessageBoxImage.Warning => L.Get("Warning"),
            MessageBoxImage.Question => L.Get("PleaseConfirm"), MessageBoxImage.Information => L.Get("Information"), _ => "" };
        if (label.Length > 0)
            body.Children.Add(new TextBlock { Text = label, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = Math.Max(80, SystemParameters.WorkArea.Height - 180) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var focus = choices.Contains(defaultResult) ? defaultResult : choices[0];
        foreach (var choice in choices)
        {
            var button = new Button
            {
                Content = choice switch { MessageBoxResult.OK => L.Get("OK"), MessageBoxResult.Cancel => L.Get("Cancel"), MessageBoxResult.Yes => L.Get("Yes"), _ => L.Get("No") },
                MinWidth = 80, Padding = new Thickness(16, 5, 16, 5),
                Margin = new Thickness(8, 0, 0, 0), IsDefault = choice == focus, IsCancel = choice == cancel
            };
            button.Click += (_, _) => { selected = choice; window.Close(); };
            if (choice == focus) window.ContentRendered += (_, _) => button.Focus();
            actions.Children.Add(button);
        }
        Grid.SetRow(actions, 1);
        panel.Children.Add(actions);
        window.Content = panel;
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            if (cancel != MessageBoxResult.None) { selected = cancel; window.Close(); }
        };
        window.Closing += (_, e) =>
        {
            // Yes/No cannot be dismissed without making a choice.
            if (selected == MessageBoxResult.None && cancel == MessageBoxResult.None) e.Cancel = true;
            else if (selected == MessageBoxResult.None) selected = cancel;
        };
        window.SourceInitialized += (_, _) => ThemeService.ApplyToWindow(window);
        result = () => selected;
        return window;
    }
}
