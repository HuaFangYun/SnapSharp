using System.Windows;

namespace SnapSharp.Views;

public partial class WatermarkDialog : Window
{
    public WatermarkDialog() => InitializeComponent();
    public string Watermark => WatermarkText.Text;
    public double WatermarkFontSize => FontSizeSlider.Value;
    public double WatermarkOpacity => OpacitySlider.Value;
    public double WatermarkAngle => AngleSlider.Value;
    public bool IsTiled => TiledCheckBox.IsChecked == true;
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(Watermark)) DialogResult = true;
    }
}
