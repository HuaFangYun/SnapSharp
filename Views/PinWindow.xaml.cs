using SnapSharp.Models;
using SnapSharp.Native;
using SnapSharp.Services;
using SnapSharp.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using MenuItem = System.Windows.Controls.MenuItem;

namespace SnapSharp.Views;

public partial class PinWindow : Window
{
    private const double FrameChrome = 22;

    private readonly SkiaImageService _images;
    private readonly SettingsService _settings;
    private readonly IClipboardService _clipboard;
    private double _zoom = 1;
    private readonly PinWorkspace _workspace;
    private HwndSource? _source;
    private bool _editing;
    private bool _positioned;
    private EditorViewModel? _editViewModel;

    public PinWindow(BitmapSource image, SkiaImageService images, SettingsService settings,
        IClipboardService clipboard, PinWorkspace? workspace = null, PinContent? content = null)
    {
        InitializeComponent();
        _images = images;
        _settings = settings;
        _clipboard = clipboard;
        _workspace = workspace ?? new PinWorkspace();
        ContentInfo = content ?? new PinContent(image);
        Image = image;
        ImageView.Source = image;
        Opacity = settings.Current.DefaultPinOpacity;
        _workspace.Add(this);
        SourceInitialized += (_, _) => { _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle); _source?.AddHook(WindowHook); };
        Closing += (_, _) => { Image = OutputImage(); ContentInfo = ContentInfo with { Image = Image }; };
        Closed += (_, _) => { _source?.RemoveHook(WindowHook); CancelEditing(); };
        ColorCopyBar.Visibility = ContentInfo.Color is null ? Visibility.Collapsed : Visibility.Visible;

        Loaded += Window_Loaded;
        DpiChanged += Window_DpiChanged;
        LocationChanged += (_, _) => UpdateAnnotationLayout();
        ImageHost.SizeChanged += (_, _) => UpdateAnnotationLayout();
    }

    public BitmapSource Image { get; private set; }
    public PinContent ContentInfo { get; private set; }
    public string? GroupName { get; internal set; }
    public PinContent Snapshot() => ContentInfo with { Image = OutputImage() };
    public bool IsClickThrough { get; private set; }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_positioned) return;
        _positioned = true;
        var dpi = VisualTreeHelper.GetDpi(this);
        ApplyImagePixelSize(dpi);
        var workArea = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, nint.Zero, workArea.Left + 40, workArea.Top + 40, 0, 0, 0x0001 | 0x0004 | NativeMethods.SwpNoActivate);
        dpi = VisualTreeHelper.GetDpi(this);
        _zoom = Math.Min(1, Math.Min((workArea.Width - 60d) / Image.PixelWidth, (workArea.Height - 60d) / Image.PixelHeight));
        _zoom = Math.Clamp(_zoom, .1, 1);
        ApplyImagePixelSize(dpi);
        if (NativeMethods.GetWindowRect(hwnd, out var bounds)) NativeMethods.SetWindowPos(hwnd, nint.Zero,
            workArea.Left + (workArea.Width - (bounds.Right - bounds.Left)) / 2,
            workArea.Top + (workArea.Height - (bounds.Bottom - bounds.Top)) / 2, 0, 0, 0x0001 | 0x0004 | NativeMethods.SwpNoActivate);
    }

    private nint WindowHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0231) _workspace.BeginMove(this); // WM_ENTERSIZEMOVE
        else if (message == 0x0232) _workspace.EndMove();
        else if (message == 0x0216) // WM_MOVING: adjust the proposed physical rectangle before Windows applies it.
        {
            var bounds = Marshal.PtrToStructure<NativeMethods.Rect>(lParam);
            var rectangle = System.Drawing.Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
            if (_settings.Current.PinSnapToEdges)
            {
                var workArea = System.Windows.Forms.Screen.FromRectangle(rectangle).WorkingArea;
                rectangle = PinWorkspace.Snap(rectangle, workArea, VisualTreeHelper.GetDpi(this).DpiScaleX, _settings.Current.PinSnapDistance);
            }
            bounds.Left = rectangle.Left; bounds.Top = rectangle.Top; bounds.Right = rectangle.Right; bounds.Bottom = rectangle.Bottom;
            Marshal.StructureToPtr(bounds, lParam, false);
            _workspace.Move(this, bounds.Left, bounds.Top);
            handled = true;
            return new nint(1);
        }
        return nint.Zero;
    }

    private void Window_DpiChanged(object sender, System.Windows.DpiChangedEventArgs e)
    {
        ApplyImagePixelSize(e.NewDpi);
        Dispatcher.BeginInvoke(UpdateAnnotationLayout);
    }

    private void ApplyImagePixelSize(DpiScale dpi)
    {
        Width = Image.PixelWidth * _zoom / dpi.DpiScaleX + FrameChrome;
        Height = Image.PixelHeight * _zoom / dpi.DpiScaleY + FrameChrome;
    }

    private void Image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_editing || e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount >= 2)
        {
            Close();
            e.Handled = true;
            return;
        }

        DragMove();
        e.Handled = true;
    }

    private void Window_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var menu = (System.Windows.Controls.ContextMenu)FindResource("PinMenu");
        menu.PlacementTarget = this;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void BeginEdit_Click(object sender, RoutedEventArgs e) { if (!_editing) BeginEditing(); }

    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_editing) return;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            Opacity = Math.Clamp(Opacity + (e.Delta > 0 ? 0.05 : -0.05), 0.2, 1);
            return;
        }

        SetZoom(_zoom * Math.Pow(1.1, e.Delta / 120d));
    }

    internal void SetZoom(double zoom, DpiScale? dpi = null)
    {
        _zoom = Math.Clamp(zoom, .1, 10);
        ApplyImagePixelSize(dpi ?? VisualTreeHelper.GetDpi(this));
    }

    private void BeginEditing()
    {
        _editing = true;
        _editViewModel = new EditorViewModel(Image);
        _editViewModel.PropertyChanged += EditViewModel_PropertyChanged;
        AnnotationView.DataContext = _editViewModel;
        EditToolbar.DataContext = _editViewModel;
        AnnotationView.Visibility = Visibility.Visible;
        EditToolbar.Visibility = Visibility.Visible;
        UpdateSelectedTool(AnnotationTool.Rectangle);
        Dispatcher.BeginInvoke(UpdateAnnotationLayout);
    }

    private void CancelEditing()
    {
        _editing = false;
        if (_editViewModel is not null) _editViewModel.PropertyChanged -= EditViewModel_PropertyChanged;
        _editViewModel = null;
        AnnotationView.DataContext = null;
        EditToolbar.DataContext = null;
        AnnotationView.Visibility = Visibility.Collapsed;
        EditToolbar.Visibility = Visibility.Collapsed;
    }

    private void EditViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.SelectedTool) && _editViewModel is not null)
            UpdateSelectedTool(_editViewModel.SelectedTool);
    }

    private void ApplyEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_editViewModel is null) return;
        Image = _images.Render(Image, _editViewModel.Annotations);
        ImageView.Source = Image;
        ContentInfo = ContentInfo with { Image = Image };
        CancelEditing();
    }

    private void CancelEdit_Click(object sender, RoutedEventArgs e) => CancelEditing();

    private void EditTool_Click(object sender, RoutedEventArgs e)
    {
        if (_editViewModel is null || sender is not FrameworkElement element ||
            !Enum.TryParse<AnnotationTool>(element.Tag?.ToString(), out var tool)) return;

        _editViewModel.SelectedTool = tool;
        UpdateSelectedTool(tool);
    }

    private void UpdateSelectedTool(AnnotationTool selected)
    {
        foreach (var button in EditToolButtons.Children.OfType<System.Windows.Controls.Button>())
        {
            if (button.Tag is not string tag) continue;
            var isSelected = string.Equals(tag, selected.ToString(), StringComparison.Ordinal);
            button.Background = isSelected ? new SolidColorBrush(Color.FromRgb(42, 104, 218)) : Brushes.Transparent;
            button.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty,
                isSelected ? "AccentForegroundBrush" : "TextBrush");
        }
    }

    private void EditColor_Click(object sender, RoutedEventArgs e)
    {
        if (_editViewModel is null) return;
        var current = _editViewModel.SelectedColor;
        var dialog = new System.Windows.Forms.ColorDialog
        {
            Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B)
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _editViewModel.SelectedColor = Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
            if (_editViewModel.SelectedTool == AnnotationTool.Select) _editViewModel.ApplySelectedStyle();
        }
    }

    private void UpdateAnnotationLayout()
    {
        if (!_editing || !IsLoaded || ImageHost.ActualWidth <= 0 || ImageHost.ActualHeight <= 0) return;

        var scale = Math.Min(ImageHost.ActualWidth / Image.PixelWidth,
            ImageHost.ActualHeight / Image.PixelHeight);
        var renderedWidth = Image.PixelWidth * scale;
        var renderedHeight = Image.PixelHeight * scale;
        var offsetX = (ImageHost.ActualWidth - renderedWidth) / 2;
        var offsetY = (ImageHost.ActualHeight - renderedHeight) / 2;

        AnnotationView.Width = Image.PixelWidth;
        AnnotationView.Height = Image.PixelHeight;
        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(scale, scale));
        transform.Children.Add(new TranslateTransform(offsetX, offsetY));
        AnnotationView.RenderTransformOrigin = new Point(0, 0);
        AnnotationView.RenderTransform = transform;

        var screenTopLeft = ImageHost.PointToScreen(new Point(offsetX, offsetY));
        var dpi = VisualTreeHelper.GetDpi(ImageHost);
        AnnotationView.ScreenPixelBounds = new PixelRect(
            (int)Math.Round(screenTopLeft.X),
            (int)Math.Round(screenTopLeft.Y),
            Math.Max(1, (int)Math.Round(renderedWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Round(renderedHeight * dpi.DpiScaleY)));
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (FindResource("PinMenu") is not System.Windows.Controls.ContextMenu menu || sender is not System.Windows.Controls.Button button) return;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Top;
        menu.IsOpen = true;
    }

    private BitmapSource OutputImage() => _editViewModel is null ? Image : _images.Render(Image, _editViewModel.Annotations);
    private void Recognition_Click(object sender, RoutedEventArgs e) => RecognitionUi.Open(OutputImage(), this, ((MenuItem)sender).Tag.ToString()!);

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { CopyCurrentImage(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "复制失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    internal void CopyCurrentImage() => _clipboard.SetImage(OutputImage());
    private void ToggleTopmost_Click(object sender, RoutedEventArgs e) => Topmost = !Topmost;

    private void Opacity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string value } && double.TryParse(value,
            System.Globalization.CultureInfo.InvariantCulture, out var opacity)) Opacity = opacity;
    }

    private void Rotate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string value } || !double.TryParse(value, out var change)) return;
        RotatePixels(change > 0);
    }

    internal void RotatePixels(bool clockwise)
    {
        Image = _images.Rotate90(OutputImage(), clockwise);
        ContentInfo = ContentInfo with { Image = Image };
        CancelEditing();
        ImageView.Source = Image;
        ApplyImagePixelSize(VisualTreeHelper.GetDpi(this));
    }

    private void ClickThrough_Click(object sender, RoutedEventArgs e)
    {
        if (_editing) CancelEditing();
        SetClickThrough(true);
    }

    public void SetClickThrough(bool enabled)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero) return;
        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
        style = enabled ? style | NativeMethods.WsExTransparent : style & ~NativeMethods.WsExTransparent;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle, new nint(style));
        IsClickThrough = enabled;
    }

    private void ResetSize_Click(object sender, RoutedEventArgs e)
    {
        SetZoom(1);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveImageWindow.Open(OutputImage(), _images, _settings, this);
    }

    private void CopyHex_Click(object sender, RoutedEventArgs e) => CopyColor(false);
    private void CopyRgb_Click(object sender, RoutedEventArgs e) => CopyColor(true);
    private void CopyColor(bool rgb)
    {
        try { if (ContentInfo.Color is { } color) OutputActions.CopyText(rgb ? PinContentService.Rgb(color) : PinContentService.Hex(color)); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "复制颜色失败"); }
    }
    private void Menu_Opened(object sender, RoutedEventArgs e)
    {
        var menu = (System.Windows.Controls.ContextMenu)sender;
        var groups = (MenuItem)menu.Items.OfType<MenuItem>().First(x => Equals(x.Tag, "Groups"));
        groups.Items.Clear();
        var create = new MenuItem { Header = "新建分组…" };
        create.Click += (_, _) =>
        {
            var dialog = new TextInputDialog { Owner = this, Title = "新建贴图分组", InitialText = "新分组" };
            if (dialog.ShowDialog() != true) return;
            try { _workspace.Assign(this, dialog.ResultText); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "分组名称无效"); }
        };
        groups.Items.Add(create);
        var ungroup = new MenuItem { Header = "不分组", IsCheckable = true, IsChecked = GroupName is null };
        ungroup.Click += (_, _) => _workspace.Assign(this, null);
        groups.Items.Add(ungroup);
        foreach (var group in _workspace.Groups)
        {
            var item = new MenuItem { Header = group, IsCheckable = true, IsChecked = GroupName == group };
            item.Click += (_, _) => _workspace.Assign(this, group);
            groups.Items.Add(item);
        }
        foreach (var item in menu.Items.OfType<MenuItem>().Where(x => x.Tag is string tag && tag is "HideGroup" or "CloseGroup")) item.IsEnabled = GroupName is not null;
    }
    private void HideGroup_Click(object sender, RoutedEventArgs e) { if (GroupName is { } group) _workspace.SetGroupVisible(group, false); }
    private void CloseGroup_Click(object sender, RoutedEventArgs e) { if (GroupName is { } group) _workspace.CloseGroup(group); }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
