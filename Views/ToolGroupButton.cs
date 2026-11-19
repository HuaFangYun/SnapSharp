using L = NimCap.Screenshot.Localization.UiText;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MahApps.Metro.IconPacks;
using NimCap.Screenshot.Models;
using Button = System.Windows.Controls.Button;
using Control = System.Windows.Controls.Control;
using Orientation = System.Windows.Controls.Orientation;

namespace NimCap.Screenshot.Views;

public sealed class ToolGroupButton : StackPanel
{
    private readonly Button _main = new();
    private readonly PackIconLucide _icon = new();
    private readonly ToggleButton _toggle = new();
    private readonly Popup _popup = new();
    private readonly StackPanel _options = new();

    public static readonly DependencyProperty GroupProperty = DependencyProperty.Register(
        nameof(Group), typeof(string), typeof(ToolGroupButton), new PropertyMetadata("Shapes", (d, _) => ((ToolGroupButton)d).BuildOptions()));

    public string Group { get => (string)GetValue(GroupProperty); set => SetValue(GroupProperty, value); }
    public AnnotationTool CurrentTool { get; private set; }
    public IReadOnlyList<AnnotationTool> Tools => Definitions.Select(item => item.Tool).ToArray();
    public event EventHandler? ToolSelected;

    private (AnnotationTool Tool, PackIconLucideKind Icon, string Label)[] Definitions => Group switch
    {
        "Shapes" => [(AnnotationTool.Rectangle, PackIconLucideKind.Square, L.Get("Rectangle")), (AnnotationTool.Ellipse, PackIconLucideKind.Circle, L.Get("Ellipse")), (AnnotationTool.Polygon, PackIconLucideKind.Pentagon, L.Get("Polygon"))],
        "Ink" => [(AnnotationTool.Pen, PackIconLucideKind.Pencil, L.Get("Pen")), (AnnotationTool.Highlight, PackIconLucideKind.Highlighter, L.Get("Highlighter"))],
        "Text" => [(AnnotationTool.Text, PackIconLucideKind.Type, L.Get("Text")), (AnnotationTool.Bubble, PackIconLucideKind.MessageCircle, L.Get("Callout")), (AnnotationTool.Watermark, PackIconLucideKind.Stamp, L.Get("Watermark"))],
        "Effects" => [(AnnotationTool.Mosaic, PackIconLucideKind.Grid3x3, L.Get("Pixelate")), (AnnotationTool.Blur, PackIconLucideKind.Droplets, L.Get("GaussianBlur")), (AnnotationTool.Spotlight, PackIconLucideKind.Focus, L.Get("Spotlight")), (AnnotationTool.Magnifier, PackIconLucideKind.ZoomIn, L.Get("MagnifierAnnotation"))],
        _ => throw new InvalidOperationException("Unknown tool group: " + Group)
    };

    public ToolGroupButton()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        _main.SetResourceReference(StyleProperty, "ToolbarButton");
        _icon.SetResourceReference(StyleProperty, "ToolbarIcon");
        _main.Content = _icon;
        _main.Click += (_, _) => ChooseTool(CurrentTool);
        _toggle.SetResourceReference(StyleProperty, "ToolbarFieldToggle");
        _toggle.Width = 14; _toggle.Height = 32; _toggle.BorderThickness = new Thickness(0);
        _toggle.Background = Brushes.Transparent;
        var chevron = new PackIconLucide { Kind = PackIconLucideKind.ChevronDown };
        chevron.SetResourceReference(StyleProperty, "ToolbarIcon"); chevron.Width = 14; chevron.Height = 14;
        _toggle.Content = chevron;
        _toggle.ToolTip = L.Get("ExpandRelatedTools");
        _toggle.Checked += (_, _) => _popup.IsOpen = true;
        _toggle.Unchecked += (_, _) => _popup.IsOpen = false;
        _popup.Closed += (_, _) => _toggle.IsChecked = false;
        _popup.PlacementTarget = this; _popup.Placement = PlacementMode.Bottom;
        _popup.VerticalOffset = 6; _popup.StaysOpen = false; _popup.AllowsTransparency = true;
        var border = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(4), BorderThickness = new Thickness(1), Child = _options };
        border.SetResourceReference(Border.BackgroundProperty, "ToolbarBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "ToolbarBorderBrush");
        _popup.Child = border;
        _options.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Escape) return;
            CloseDropDown(); _toggle.Focus(); e.Handled = true;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Escape || !_popup.IsOpen) return;
            CloseDropDown(); e.Handled = true;
        };
        Children.Add(_main); Children.Add(_toggle); Children.Add(_popup);
        BuildOptions();
    }

    private void BuildOptions()
    {
        _options.Children.Clear();
        CurrentTool = Definitions[0].Tool;
        foreach (var item in Definitions)
        {
            var button = new Button { Width = 148, Height = 34, Tag = item.Tool, Margin = new Thickness(0, 1, 0, 1), Padding = new Thickness(10, 0, 10, 0) };
            button.SetResourceReference(StyleProperty, "ToolbarButton");
            var content = new StackPanel { Orientation = Orientation.Horizontal, Width = 124 };
            var icon = new PackIconLucide { Kind = item.Icon, Margin = new Thickness(0, 0, 10, 0) };
            icon.SetResourceReference(StyleProperty, "ToolbarIcon");
            content.Children.Add(icon); content.Children.Add(new TextBlock { Text = item.Label, VerticalAlignment = VerticalAlignment.Center });
            button.Content = content; button.Click += (_, _) => ChooseTool(item.Tool);
            _options.Children.Add(button);
        }
        SyncTool(CurrentTool, false);
    }

    internal void ChooseTool(AnnotationTool tool)
    {
        if (!Tools.Contains(tool)) throw new ArgumentOutOfRangeException(nameof(tool));
        SyncTool(tool, true); CloseDropDown(); ToolSelected?.Invoke(this, EventArgs.Empty);
    }

    internal void SyncTool(AnnotationTool tool, bool active)
    {
        if (Tools.Contains(tool)) CurrentTool = tool;
        var definition = Definitions.First(item => item.Tool == CurrentTool);
        _icon.Kind = definition.Icon; _main.ToolTip = definition.Label + L.Get("ExpandRelatedToolsOnTheRight");
        if (active && Tools.Contains(tool)) _main.SetResourceReference(Control.BackgroundProperty, "ToolbarActiveBrush");
        else _main.Background = Brushes.Transparent;
        _main.SetResourceReference(Control.ForegroundProperty, active && Tools.Contains(tool) ? "AccentForegroundBrush" : "TextBrush");
        foreach (var option in _options.Children.OfType<Button>())
        {
            var selected = Equals(option.Tag, CurrentTool);
            if (selected) option.SetResourceReference(Control.BackgroundProperty, "ToolbarActiveBrush"); else option.Background = Brushes.Transparent;
            option.SetResourceReference(Control.ForegroundProperty, selected ? "AccentForegroundBrush" : "TextBrush");
        }
    }

    internal void CloseDropDown() { _toggle.IsChecked = false; _popup.IsOpen = false; }
    internal void ActivateCurrentTool() => _main.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
