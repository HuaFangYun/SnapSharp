using L = SnapSharp.Localization.UiText;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SnapSharp.Views;

public partial class CanvasExpandDialog : Window
{
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;
    public int CanvasWidth { get; private set; }
    public int CanvasHeight { get; private set; }
    public int OffsetX { get; private set; }
    public int OffsetY { get; private set; }
    public Color BackgroundColor { get; private set; } = Colors.White;

    public CanvasExpandDialog(int width, int height)
    {
        InitializeComponent();
        _sourceWidth = width;
        _sourceHeight = height;
        WidthBox.Text = width.ToString();
        HeightBox.Text = height.ToString();
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.ColorDialog
        {
            Color = System.Drawing.Color.FromArgb(BackgroundColor.A, BackgroundColor.R, BackgroundColor.G, BackgroundColor.B),
            AllowFullOpen = true
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        BackgroundColor = Color.FromArgb(dialog.Color.A, dialog.Color.R, dialog.Color.G, dialog.Color.B);
        ColorButton.Content = $"#{BackgroundColor.A:X2}{BackgroundColor.R:X2}{BackgroundColor.G:X2}{BackgroundColor.B:X2}";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(WidthBox.Text, out var width) || !int.TryParse(HeightBox.Text, out var height) ||
            width < _sourceWidth || height < _sourceHeight)
        {
            MessageBox.Show(this, L.Format("CanvasCannotBeSmallerThanTheOriginal", _sourceWidth, _sourceHeight), L.Get("ExpandCanvas"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        CanvasWidth = width;
        CanvasHeight = height;
        var tag = (PositionBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Center";
        OffsetX = tag.Contains("Left", StringComparison.Ordinal) ? 0 :
            tag.Contains("Right", StringComparison.Ordinal) ? width - _sourceWidth : (width - _sourceWidth) / 2;
        OffsetY = tag.Contains("Top", StringComparison.Ordinal) ? 0 :
            tag.Contains("Bottom", StringComparison.Ordinal) ? height - _sourceHeight : (height - _sourceHeight) / 2;
        DialogResult = true;
    }
}
