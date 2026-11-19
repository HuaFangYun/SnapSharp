using SnapSharp.Models;
using SnapSharp.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Control = System.Windows.Controls.Control;
using Panel = System.Windows.Controls.Panel;
using L = SnapSharp.Localization.UiText;

namespace SnapSharp.Views;

public partial class SelectionWindow
{
    private bool _syncingSecondaryToolbar;
    private bool _changingSecondaryStyle;
    private Color _lastTextOutlineColor = Colors.White;
    private IReadOnlyList<System.Windows.Media.FontFamily>? _systemFonts;
    private bool _syncingFontList;

    private AnnotationTool EffectiveTool() => _editor?.EffectiveTool ?? AnnotationTool.Select;

    private void UpdateSecondaryToolbar()
    {
        if (_editor is null || SecondaryToolPanel is null) return;
        var tool = EffectiveTool();
        foreach (var group in ToolButtons.Children.OfType<ToolGroupButton>())
            group.SyncTool(tool, _editor.SelectedTool != AnnotationTool.Select && !_moving);
        var shape = tool is AnnotationTool.Rectangle or AnnotationTool.Ellipse or AnnotationTool.Line or AnnotationTool.Arrow;
        var stroke = shape || tool is AnnotationTool.Pen or AnnotationTool.Highlight or AnnotationTool.Polygon or AnnotationTool.Bubble or AnnotationTool.Number;
        var fill = tool is AnnotationTool.Rectangle or AnnotationTool.Ellipse or AnnotationTool.Polygon or AnnotationTool.Bubble;
        var color = stroke || tool is AnnotationTool.Text or AnnotationTool.Watermark or AnnotationTool.Number;
        var font = tool is AnnotationTool.Text or AnnotationTool.Watermark or AnnotationTool.Bubble;
        var effect = tool is AnnotationTool.Mosaic or AnnotationTool.Blur or AnnotationTool.Spotlight or AnnotationTool.Magnifier;
        var guidance = tool == AnnotationTool.Eraser;
        var numberOptions = tool == AnnotationTool.Number;
        var selected = _editor.SelectedAnnotation;

        FillOptions.Visibility = fill ? Visibility.Visible : Visibility.Collapsed;
        ArrowOptions.Visibility = tool == AnnotationTool.Arrow ? Visibility.Visible : Visibility.Collapsed;
        StrokeOptions.Visibility = stroke ? Visibility.Visible : Visibility.Collapsed;
        ColorOptions.Visibility = color ? Visibility.Visible : Visibility.Collapsed;
        FontOptions.Visibility = font ? Visibility.Visible : Visibility.Collapsed;
        FontFamilyControl.Visibility = tool is AnnotationTool.Text or AnnotationTool.Bubble
            ? Visibility.Visible : Visibility.Collapsed;
        TextOutlineControl.Visibility = tool is AnnotationTool.Text or AnnotationTool.Bubble
            ? Visibility.Visible : Visibility.Collapsed;
        EffectOptions.Visibility = effect ? Visibility.Visible : Visibility.Collapsed;
        GuidanceOptions.Visibility = guidance ? Visibility.Visible : Visibility.Collapsed;
        NumberOptions.Visibility = numberOptions ? Visibility.Visible : Visibility.Collapsed;
        SecondaryToolPanel.Visibility = tool == AnnotationTool.Select ? Visibility.Collapsed : Visibility.Visible;
        SecondaryToolTitle.Text = selected is not null ? L.Format("0Selected", ToolDisplayName(tool)) : ToolDisplayName(tool);
        GuidanceText.Text = tool == AnnotationTool.Eraser ? L.Get("ClickAnAnnotationToDeleteIt") : string.Empty;
        var supportsStrokeStyle = tool != AnnotationTool.Number;
        StrokeLabel.Visibility = supportsStrokeStyle ? Visibility.Visible : Visibility.Collapsed;
        StrokeStyleGroup.Visibility = supportsStrokeStyle ? Visibility.Visible : Visibility.Collapsed;
        ThicknessPopupLabel.Text = tool == AnnotationTool.Number ? L.Get("Size") : L.Get("Thickness");

        _syncingSecondaryToolbar = true;
        try
        {
            _changingSecondaryStyle = true;
            try { _editor.UseSelectedStyle(ThicknessSlider.Minimum, ThicknessSlider.Maximum); }
            finally { _changingSecondaryStyle = false; }
            var selectedColor = _editor.SelectedColor;
            var selectedThickness = Math.Clamp(_editor.StrokeThickness, ThicknessSlider.Minimum, ThicknessSlider.Maximum);
            var selectedFill = _editor.FillColor;
            var selectedFontSize = _editor.FontSize;
            var selectedFontFamily = _editor.FontFamilyName;
            var selectedOutlineColor = _editor.TextOutlineColor;
            var selectedOutlineThickness = _editor.TextOutlineThickness;
            var selectedStrokeStyle = _editor.StrokeStyle;
            var selectedArrowStyle = _editor.ArrowStyle;
            if (numberOptions)
            {
                var selectedNumber = selected as NumberAnnotation;
                NumberValueLabel.Text = selectedNumber is null ? L.Get("StartAt") : L.Get("Number");
                NumberValueBox.Text = $"{selectedNumber?.Number ?? _editor.NextNumber}";
                var numberStyle = selectedNumber?.NumberStyle ?? _editor.NumberStyle;
                foreach (var item in NumberStylePicker.Items.OfType<ComboBoxItem>())
                    if (item.Tag?.ToString() == numberStyle.ToString()) NumberStylePicker.SelectedItem = item;
                ContinuousNumberToggle.IsChecked = _editor.ContinuousNumbering;
                ContinuousNumberToggle.Visibility = selectedNumber is null ? Visibility.Visible : Visibility.Collapsed;
            }
            FillToggle.IsChecked = selectedFill is not null;
            var editingFillColor = fill && selectedFill is not null;
            ColorLabel.Text = editingFillColor ? L.Get("FillColor") : stroke ? L.Get("StrokeColor") : L.Get("Color");
            var fillOpacity = selectedFill is Color fillColor ? fillColor.A / 255d * 100 : tool == AnnotationTool.Bubble ? 95 : 38;
            FillOpacitySlider.Value = Math.Clamp(fillOpacity, FillOpacitySlider.Minimum, FillOpacitySlider.Maximum);
            FillOpacitySlider.IsEnabled = true;
            FillOpacityText.Text = $"{fillOpacity:0}%";
            FillOpacityPopupValue.Text = FillOpacityText.Text;
            UpdateStrokePreview(SelectedStrokePreview, selectedStrokeStyle);
            SelectedArrowPreview.ArrowStyle = selectedArrowStyle;
            UpdateStyleMenu(ArrowStyleMenu, selectedArrowStyle);
            UpdateStyleMenu(PrimaryArrowStyleMenu, selectedArrowStyle);
            UpdateStyleMenu(StrokeStyleMenu, selectedStrokeStyle);
            ThicknessSlider.Value = selectedThickness;
            ThicknessText.Text = $"{selectedThickness:0.#}";
            ThicknessPopupValue.Text = ThicknessText.Text;
            FontSizeSlider.Value = Math.Clamp(selectedFontSize, FontSizeSlider.Minimum, FontSizeSlider.Maximum);
            FontSizeText.Text = $"{selectedFontSize:0}";
            FontSizePopupValue.Text = FontSizeText.Text;
            FontFamilyText.Text = selectedFontFamily;
            FontFamilyText.FontFamily = new System.Windows.Media.FontFamily(selectedFontFamily);
            if (selectedOutlineColor is Color activeOutline) _lastTextOutlineColor = activeOutline;
            OutlineEnabledToggle.IsChecked = selectedOutlineColor is not null;
            TextOutlineSlider.Value = Math.Clamp(Math.Max(0.5, selectedOutlineThickness),
                TextOutlineSlider.Minimum, TextOutlineSlider.Maximum);
            TextOutlineValue.Text = $"{TextOutlineSlider.Value:0.#}";
            UpdatePalette(OutlineColorOptions, selectedOutlineColor ?? _lastTextOutlineColor);
            UpdatePalette(ColorOptions, editingFillColor && selectedFill is Color currentFill ? currentFill : selectedColor);

            if (tool is AnnotationTool.Mosaic or AnnotationTool.Blur)
            {
                var strength = selected is RegionEffectAnnotation selectedEffect ? selectedEffect.Strength : _editor.EffectStrength;
                EffectLabel.Text = L.Get("Intensity"); EffectCompactLabel.Text = L.Get("Intensity"); EffectSlider.Minimum = 2; EffectSlider.Maximum = 48;
                EffectSlider.Value = Math.Clamp(strength, 2, 48);
                EffectText.Text = $"{strength:0}";
                EffectPopupValue.Text = EffectText.Text;
            }
            else if (tool == AnnotationTool.Spotlight)
            {
                var darkness = selected is SpotlightAnnotation selectedSpotlight ? selectedSpotlight.Darkness : _editor.SpotlightDarkness;
                EffectLabel.Text = L.Get("Darkness"); EffectCompactLabel.Text = L.Get("Darkness"); EffectSlider.Minimum = 10; EffectSlider.Maximum = 90;
                EffectSlider.Value = Math.Clamp(darkness * 100, 10, 90);
                EffectText.Text = $"{darkness:P0}";
                EffectPopupValue.Text = EffectText.Text;
            }
            else if (tool == AnnotationTool.Magnifier)
            {
                var zoom = selected is MagnifierAnnotation selectedMagnifier ? selectedMagnifier.Zoom : _editor.MagnifierZoom;
                EffectLabel.Text = L.Get("Magnification"); EffectCompactLabel.Text = L.Get("Magnification"); EffectSlider.Minimum = 15; EffectSlider.Maximum = 80;
                EffectSlider.Value = Math.Clamp(zoom * 10, 15, 80);
                EffectText.Text = $"{zoom:0.0}x";
                EffectPopupValue.Text = EffectText.Text;
            }
        }
        finally { _syncingSecondaryToolbar = false; }
        if (_editing) PositionChrome();
    }

    private static string ToolDisplayName(AnnotationTool tool) => tool switch
    {
        AnnotationTool.Rectangle => L.Get("Rectangle"),
        AnnotationTool.Ellipse => L.Get("Ellipse"),
        AnnotationTool.Line => L.Get("Line"),
        AnnotationTool.Arrow => L.Get("Arrow"),
        AnnotationTool.Pen => L.Get("Pen"),
        AnnotationTool.Highlight => L.Get("Highlighter"),
        AnnotationTool.Text => L.Get("Text"),
        AnnotationTool.Watermark => L.Get("Watermark"),
        AnnotationTool.Number => L.Get("Number"),
        AnnotationTool.Mosaic => L.Get("Pixelate"),
        AnnotationTool.Blur => L.Get("Blur"),
        AnnotationTool.Spotlight => L.Get("Spotlight"),
        AnnotationTool.Bubble => L.Get("Callout"),
        AnnotationTool.Magnifier => L.Get("Magnifier"),
        AnnotationTool.Polygon => L.Get("Polygon"),
        AnnotationTool.Eraser => L.Get("DeleteAnnotation"),
        _ => L.Get("ToolOptions")
    };

    private void NumberStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string value } || !int.TryParse(value, out var step)) return;
        var current = int.TryParse(NumberValueBox.Text, out var parsed) ? parsed : 1;
        NumberValueBox.Text = $"{Math.Clamp(current + step, 1, 9999)}";
        CommitNumberValue();
    }

    private void NumberValueBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitNumberValue();

    private void NumberValueBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitNumberValue();
        AnnotationView.Focus();
        e.Handled = true;
    }

    private void CommitNumberValue()
    {
        if (_syncingSecondaryToolbar || _editor is null) return;
        var value = int.TryParse(NumberValueBox.Text, out var parsed) ? Math.Clamp(parsed, 1, 9999) : 1;
        NumberValueBox.Text = $"{value}";
        ApplySecondaryChange(() =>
        {
            if (_editor.SelectedAnnotation is NumberAnnotation selected)
                _editor.Replace(selected, selected with { Number = value });
            else
                _editor.NextNumber = value;
        }, refreshToolbar: true);
    }

    private void NumberStyle_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSecondaryToolbar || _editor is null || NumberStylePicker.SelectedItem is not ComboBoxItem item ||
            !Enum.TryParse<AnnotationNumberStyle>(item.Tag?.ToString(), out var style)) return;
        ApplySecondaryChange(() =>
        {
            if (_editor.SelectedAnnotation is NumberAnnotation selected)
                _editor.Replace(selected, selected with { NumberStyle = style });
            else
                _editor.NumberStyle = style;
        }, refreshToolbar: true);
    }

    private void ContinuousNumber_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingSecondaryToolbar || _editor is null) return;
        ApplySecondaryChange(() => _editor.ContinuousNumbering = ContinuousNumberToggle.IsChecked == true);
    }

    private void StrokeStyleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_syncingSecondaryToolbar || _editor is null || sender is not FrameworkElement element ||
            !Enum.TryParse<AnnotationStrokeStyle>(element.Tag?.ToString(), out var style)) return;
        ApplySecondaryChange(() =>
        {
            _editor.StrokeStyle = style;
            _editor.SetSelectedStrokeStyle(style);
        }, refreshToolbar: true);
        StrokeStyleDropDownToggle.IsChecked = false;
    }

    private void ArrowStyleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_syncingSecondaryToolbar || _editor is null || sender is not FrameworkElement element ||
            !Enum.TryParse<AnnotationArrowStyle>(element.Tag?.ToString(), out var style)) return;
        ApplySecondaryChange(() =>
        {
            _editor.ArrowStyle = style;
            _editor.SetSelectedArrowStyle(style);
        }, refreshToolbar: true);
        ArrowStyleDropDownToggle.IsChecked = false;
    }

    private void PrimaryArrowStyleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_editor is null || sender is not FrameworkElement element ||
            !Enum.TryParse<AnnotationArrowStyle>(element.Tag?.ToString(), out var style)) return;
        AnnotationView.CommitInlineEdit();
        _moving = false;
        MoveSelectionButton.Background = Brushes.Transparent;
        AnnotationView.IsHitTestVisible = true;
        Cursor = Cursors.Arrow;
        _editor.SelectedAnnotation = null;
        _editor.ArrowStyle = style;
        _editor.ActivateTool(AnnotationTool.Arrow);
        UpdateSelectedTool(AnnotationTool.Arrow);
        UpdateSecondaryToolbar();
        PrimaryArrowStyleDropDownToggle.IsChecked = false;
    }

    private static void UpdateStrokePreview(System.Windows.Shapes.Shape preview, AnnotationStrokeStyle style)
    {
        preview.StrokeDashArray = style switch
        {
            AnnotationStrokeStyle.Dash => new DoubleCollection([4, 2]),
            AnnotationStrokeStyle.Dot => new DoubleCollection([1, 2]),
            AnnotationStrokeStyle.LongDash => new DoubleCollection([8, 2]),
            AnnotationStrokeStyle.DashDot => new DoubleCollection([5, 2, 1, 2]),
            AnnotationStrokeStyle.DashDotDot => new DoubleCollection([5, 2, 1, 2, 1, 2]),
            _ => null
        };
        preview.StrokeDashCap = style == AnnotationStrokeStyle.Dot ? PenLineCap.Round : PenLineCap.Flat;
    }

    private void Thickness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ThicknessText is not null) ThicknessText.Text = $"{e.NewValue:0.#}";
        if (ThicknessPopupValue is not null) ThicknessPopupValue.Text = $"{e.NewValue:0.#}";
        if (_syncingSecondaryToolbar || _editor is null) return;
        ApplySecondaryChange(() =>
        {
            _editor.StrokeThickness = e.NewValue;
            _editor.SetSelectedStrokeThickness(e.NewValue);
        });
    }

    private void Thickness_MouseWheel(object sender, MouseWheelEventArgs e) => AdjustSlider(ThicknessSlider, 1, e);

    private void Fill_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingSecondaryToolbar || _editor is null) return;
        var enabled = FillToggle.IsChecked == true;
        FillOpacitySlider.IsEnabled = true;
        var alpha = (byte)Math.Clamp(Math.Round(FillOpacitySlider.Value / 100 * 255), 0, 255);
        ApplySecondaryChange(() =>
        {
            _editor.FillColor = enabled ? Color.FromArgb(alpha, _editor.SelectedColor.R, _editor.SelectedColor.G, _editor.SelectedColor.B) : null;
            _editor.SetSelectedFill(_editor.FillColor);
        }, refreshToolbar: true);
    }

    private void FillOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FillOpacityText is not null) FillOpacityText.Text = $"{e.NewValue:0}%";
        if (FillOpacityPopupValue is not null) FillOpacityPopupValue.Text = $"{e.NewValue:0}%";
        if (_syncingSecondaryToolbar || _editor is null) return;
        var alpha = (byte)Math.Clamp(Math.Round(e.NewValue / 100 * 255), 0, 255);
        ApplySecondaryChange(() =>
        {
            var fill = _editor.FillColor ?? _editor.SelectedColor;
            _editor.FillColor = Color.FromArgb(alpha, fill.R, fill.G, fill.B);
            FillToggle.IsChecked = true;
            FillOpacitySlider.IsEnabled = true;
            _editor.SetSelectedFill(_editor.FillColor);
        });
    }

    private void FillOpacity_MouseWheel(object sender, MouseWheelEventArgs e) => AdjustSlider(FillOpacitySlider, 5, e);

    private void FontFamilyDropDownToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (_editor is null) return;
        _systemFonts ??= Fonts.SystemFontFamilies
            .OrderBy(font => font.Source, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        _syncingFontList = true;
        try
        {
            FontSearchBox.Text = string.Empty;
            PopulateFontList(string.Empty);
        }
        finally { _syncingFontList = false; }
    }

    private void FontSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_systemFonts is null) return;
        PopulateFontList(FontSearchBox.Text);
    }

    private void PopulateFontList(string search)
    {
        if (_systemFonts is null || _editor is null) return;
        var fonts = string.IsNullOrWhiteSpace(search)
            ? _systemFonts
            : _systemFonts.Where(font => font.Source.Contains(search.Trim(),
                StringComparison.CurrentCultureIgnoreCase)).ToArray();
        _syncingFontList = true;
        try
        {
            FontFamilyList.ItemsSource = fonts;
            var selected = fonts.FirstOrDefault(font => string.Equals(font.Source,
                _editor.FontFamilyName, StringComparison.CurrentCultureIgnoreCase));
            FontFamilyList.SelectedItem = selected;
            if (selected is not null) FontFamilyList.ScrollIntoView(selected);
        }
        finally { _syncingFontList = false; }
    }

    private void FontFamilyList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFontList || _syncingSecondaryToolbar || _editor is null ||
            FontFamilyList.SelectedItem is not System.Windows.Media.FontFamily font) return;
        ApplySecondaryChange(() =>
        {
            _editor.FontFamilyName = font.Source;
            _editor.SetSelectedFontFamily(font.Source);
        }, refreshToolbar: true);
        FontFamilyDropDownToggle.IsChecked = false;
    }

    private void FontSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FontSizeText is not null) FontSizeText.Text = $"{e.NewValue:0}";
        if (FontSizePopupValue is not null) FontSizePopupValue.Text = $"{e.NewValue:0}";
        if (_syncingSecondaryToolbar || _editor is null) return;
        ApplySecondaryChange(() =>
        {
            _editor.FontSize = e.NewValue;
            _editor.SetSelectedFontSize(e.NewValue);
        });
    }

    private void FontSize_MouseWheel(object sender, MouseWheelEventArgs e) => AdjustSlider(FontSizeSlider, 2, e);

    private void OutlineEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingSecondaryToolbar || _editor is null) return;
        var color = OutlineEnabledToggle.IsChecked == true ? _lastTextOutlineColor : (Color?)null;
        ApplySecondaryChange(() =>
        {
            _editor.TextOutlineColor = color;
            _editor.TextOutlineThickness = TextOutlineSlider.Value;
            _editor.SetSelectedTextOutline(color, TextOutlineSlider.Value);
        }, refreshToolbar: true);
    }

    private void TextOutline_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TextOutlineValue is not null) TextOutlineValue.Text = $"{e.NewValue:0.#}";
        if (_syncingSecondaryToolbar || _editor is null) return;
        ApplySecondaryChange(() =>
        {
            _editor.TextOutlineThickness = e.NewValue;
            if (OutlineEnabledToggle.IsChecked == true)
            {
                _editor.TextOutlineColor ??= _lastTextOutlineColor;
                _editor.SetSelectedTextOutline(_editor.TextOutlineColor, e.NewValue);
            }
        });
    }

    private void TextOutline_MouseWheel(object sender, MouseWheelEventArgs e) => AdjustSlider(TextOutlineSlider, 0.5, e);

    private void OutlineColor_Click(object sender, RoutedEventArgs e)
    {
        if (_editor is null || sender is not FrameworkElement { Tag: string value }) return;
        var color = (Color)System.Windows.Media.ColorConverter.ConvertFromString(value);
        _lastTextOutlineColor = color;
        ApplySecondaryChange(() =>
        {
            OutlineEnabledToggle.IsChecked = true;
            _editor.TextOutlineColor = color;
            _editor.TextOutlineThickness = TextOutlineSlider.Value;
            _editor.SetSelectedTextOutline(color, TextOutlineSlider.Value);
        }, refreshToolbar: true);
    }

    private void OutlineCustomColor_Click(object sender, RoutedEventArgs e)
    {
        if (_editor is null) return;
        var current = _editor.TextOutlineColor ?? _lastTextOutlineColor;
        var dialog = new System.Windows.Forms.ColorDialog
        {
            Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B)
        };
        if (WindowDialogService.ShowDialog(dialog, this) != System.Windows.Forms.DialogResult.OK) return;
        var color = Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
        _lastTextOutlineColor = color;
        ApplySecondaryChange(() =>
        {
            OutlineEnabledToggle.IsChecked = true;
            _editor.TextOutlineColor = color;
            _editor.TextOutlineThickness = TextOutlineSlider.Value;
            _editor.SetSelectedTextOutline(color, TextOutlineSlider.Value);
        }, refreshToolbar: true);
    }

    private void PaletteColor_Click(object sender, RoutedEventArgs e)
    {
        if (_editor is null || sender is not FrameworkElement { Tag: string value }) return;
        ApplySecondaryChange(() =>
        {
            var color = (Color)System.Windows.Media.ColorConverter.ConvertFromString(value);
            if (FillOptions.Visibility == Visibility.Visible && FillToggle.IsChecked == true)
            {
                var alpha = _editor.FillColor?.A ?? (byte)Math.Clamp(
                    Math.Round(FillOpacitySlider.Value / 100 * 255), 0, 255);
                _editor.FillColor = Color.FromArgb(alpha, color.R, color.G, color.B);
                _editor.SetSelectedFill(_editor.FillColor);
            }
            else
            {
                _editor.SelectedColor = color;
                _editor.SetSelectedColor(color, recolorFill: false);
            }
        }, refreshToolbar: true);
    }

    private void Effect_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingSecondaryToolbar || _editor is null) return;
        ApplySecondaryChange(() =>
        {
            var tool = EffectiveTool();
            if (tool is AnnotationTool.Mosaic or AnnotationTool.Blur)
            {
                _editor.EffectStrength = e.NewValue;
                EffectText.Text = $"{e.NewValue:0}";
                EffectPopupValue.Text = EffectText.Text;
                if (_editor.SelectedAnnotation is RegionEffectAnnotation selected)
                    _editor.Replace(selected, selected with { Strength = e.NewValue });
            }
            else if (tool == AnnotationTool.Spotlight)
            {
                _editor.SpotlightDarkness = e.NewValue / 100;
                EffectText.Text = $"{_editor.SpotlightDarkness:P0}";
                EffectPopupValue.Text = EffectText.Text;
                if (_editor.SelectedAnnotation is SpotlightAnnotation selected)
                    _editor.Replace(selected, selected with { Darkness = _editor.SpotlightDarkness });
            }
            else if (tool == AnnotationTool.Magnifier)
            {
                _editor.MagnifierZoom = e.NewValue / 10;
                EffectText.Text = $"{_editor.MagnifierZoom:0.0}x";
                EffectPopupValue.Text = EffectText.Text;
                if (_editor.SelectedAnnotation is MagnifierAnnotation selected)
                    _editor.Replace(selected, selected with { Zoom = _editor.MagnifierZoom });
            }
        });
    }

    private void Effect_MouseWheel(object sender, MouseWheelEventArgs e) => AdjustSlider(EffectSlider, 1, e);

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (_editor is null) return;
        var editingFillColor = FillOptions.Visibility == Visibility.Visible && FillToggle.IsChecked == true;
        var current = editingFillColor && _editor.FillColor is Color fillColor ? fillColor : _editor.SelectedColor;
        var dialog = new System.Windows.Forms.ColorDialog { Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B) };
        if (WindowDialogService.ShowDialog(dialog, this) == System.Windows.Forms.DialogResult.OK)
        {
            ApplySecondaryChange(() =>
            {
                var color = Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
                if (editingFillColor)
                {
                    var alpha = _editor.FillColor?.A ?? (byte)Math.Clamp(
                        Math.Round(FillOpacitySlider.Value / 100 * 255), 0, 255);
                    _editor.FillColor = Color.FromArgb(alpha, color.R, color.G, color.B);
                    _editor.SetSelectedFill(_editor.FillColor);
                }
                else
                {
                    _editor.SelectedColor = color;
                    _editor.SetSelectedColor(color, recolorFill: false);
                }
            }, refreshToolbar: true);
        }
    }

    private void ApplySecondaryChange(Action change, bool refreshToolbar = false)
    {
        AnnotationView?.CommitInlineEdit();
        _changingSecondaryStyle = true;
        try { change(); }
        finally { _changingSecondaryStyle = false; }
        AnnotationView?.InvalidateVisual();
        if (AnnotationView is not null)
            _ = AnnotationView.Dispatcher.InvokeAsync(AnnotationView.InvalidateVisual,
                System.Windows.Threading.DispatcherPriority.Render);
        if (refreshToolbar) UpdateSecondaryToolbar();
    }

    private static void UpdateStyleMenu(Panel options, Enum style)
    {
        foreach (var button in options.Children.OfType<System.Windows.Controls.Button>())
        {
            var active = string.Equals(button.Tag?.ToString(), style.ToString(), StringComparison.Ordinal);
            if (active) button.SetResourceReference(Control.BackgroundProperty, "ToolbarActiveBrush");
            else button.Background = Brushes.Transparent;
            button.SetResourceReference(Control.ForegroundProperty, active ? "AccentForegroundBrush" : "TextBrush");
        }
    }

    private static void UpdatePalette(Panel options, Color displayedColor)
    {
        foreach (var button in options.Children.OfType<System.Windows.Controls.Button>())
        {
            if (button.Tag is not string value) continue;
            var paletteColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString(value);
            var active = paletteColor.R == displayedColor.R && paletteColor.G == displayedColor.G && paletteColor.B == displayedColor.B;
            var needsOutline = value is "#FFFFFF" or "#111111";
            button.BorderThickness = new Thickness(active ? 2 : needsOutline ? 1 : 0);
            if (active) button.SetResourceReference(Control.BorderBrushProperty, "TextBrush");
            else if (needsOutline) button.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
            else button.BorderBrush = Brushes.Transparent;
        }
    }

    private void AdjustSlider(Slider slider, double step, MouseWheelEventArgs e)
    {
        if (_syncingSecondaryToolbar || _editor is null || e.Delta == 0) return;
        slider.Value = Math.Clamp(slider.Value + (e.Delta > 0 ? step : -step), slider.Minimum, slider.Maximum);
        e.Handled = true;
    }
}
