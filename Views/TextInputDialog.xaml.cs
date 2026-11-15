using System.Windows;

namespace SnapSharp.Views;

public partial class TextInputDialog : Window
{
    public TextInputDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => Input.Focus();
    }

    public string ResultText => Input.Text;

    public string InitialText
    {
        get => Input.Text;
        set { Input.Text = value; Input.SelectAll(); }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
