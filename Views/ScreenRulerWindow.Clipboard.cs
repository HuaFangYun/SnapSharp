using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using MahApps.Metro.IconPacks;
using SnapSharp.Services;
using L = SnapSharp.Localization.UiText;
using TextBlock = System.Windows.Controls.TextBlock;

namespace SnapSharp.Views;

public sealed partial class ScreenRulerWindow
{
    private readonly SettingsService? _rulerSettings;
    private readonly Action<string> _writeRulerClipboard = System.Windows.Clipboard.SetText;
    private ToggleButton _autoCopyButton = null!;
    private readonly DispatcherTimer _copyNoticeTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly TextBlock _copyNotice = new() { Visibility = Visibility.Collapsed, Foreground = Brushes.LightSkyBlue,
        FontSize = 12, Margin = new Thickness(10, 7, 10, 12), TextTrimming = TextTrimming.CharacterEllipsis };

    internal ScreenRulerWindow(SettingsService settings, Action<string> writeClipboard) : this(settings)
    {
        _writeRulerClipboard = writeClipboard;
    }

    private void InitializeClipboardTool(WrapPanel toolbar)
    {
        _autoCopyButton = MakeToggle("DesktopRulerAutoCopy", PackIconLucideKind.Copy, "", true);
        _autoCopyButton.IsChecked = _rulerSettings?.Current.ScreenRulerAutoCopy == true;
        _autoCopyButton.ToolTip = L.Get("DesktopRulerAutoCopyHint");
        _autoCopyButton.Click += (_, _) =>
        {
            if (_rulerSettings is null) return;
            var previous = _rulerSettings.Current.ScreenRulerAutoCopy;
            _rulerSettings.Current.ScreenRulerAutoCopy = _autoCopyButton.IsChecked == true;
            try { _rulerSettings.Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _rulerSettings.Current.ScreenRulerAutoCopy = previous;
                _autoCopyButton.IsChecked = previous;
                ShowCopyNotice(ex.Message);
            }
        };
        toolbar.Children.Add(_autoCopyButton);
        _measurements.Committed += (tool, points) =>
        {
            if (_autoCopyButton.IsChecked == true) CopyMeasurement(tool, points);
        };
        _copyNoticeTimer.Tick += (_, _) =>
        {
            _copyNoticeTimer.Stop(); _copyNotice.Visibility = Visibility.Collapsed;
            _interactionText.Visibility = Visibility.Visible;
        };
        Closed += (_, _) => _copyNoticeTimer.Stop();
    }

    private void CopyCurrentMeasurement()
    {
        var tool = _measurements.ActiveTool;
        if (!_measurements.HasPending && _measurements.VisibleTools.Contains(tool) && _measurements.Results.TryGetValue(tool, out var points))
            CopyMeasurement(tool, points);
    }

    internal static string FormatMeasurement(RulerTool tool, Point[] points)
    {
        var a = points[0]; var b = points[1];
        return tool switch
        {
            RulerTool.Rectangle => FormattableString.Invariant($"{Math.Abs(b.X - a.X):0} × {Math.Abs(b.Y - a.Y):0} px"),
            RulerTool.Distance => L.Format("DesktopRulerDistanceCopy", FormattableString.Invariant($"{b.X - a.X:0}"),
                FormattableString.Invariant($"{b.Y - a.Y:0}"), FormattableString.Invariant($"{RulerMeasurements.Distance(a, b):0.##}")),
            RulerTool.Angle => FormattableString.Invariant($"{RulerMeasurements.Angle(a, b, points[2]):0.##}°"),
            _ => L.Format("DesktopRulerLengthCopy", FormattableString.Invariant($"{RulerMeasurements.Distance(a, b):0.##}"))
        };
    }

    private void CopyMeasurement(RulerTool tool, Point[] points)
    {
        var value = FormatMeasurement(tool, points);
        try
        {
            _writeRulerClipboard(value);
            ShowCopyNotice(L.Format("DesktopRulerCopied", value));
        }
        catch (ExternalException)
        {
            ShowCopyNotice(L.Get("DesktopRulerCopyBusy"));
        }
    }

    private void ShowCopyNotice(string message)
    {
        _copyNoticeTimer.Stop(); _copyNotice.Text = message; _copyNotice.ToolTip = message;
        _copyNotice.Visibility = Visibility.Visible; _interactionText.Visibility = Visibility.Collapsed;
        _copyNoticeTimer.Start();
    }
}
