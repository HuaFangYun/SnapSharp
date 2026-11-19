using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using NimCap.Screenshot.Localization;
using NimCap.Screenshot.Services;
using NimCap.Screenshot.Views;
using TextBlock = System.Windows.Controls.TextBlock;
using Point = System.Windows.Point;

namespace NimCap.Screenshot.WindowChecks;

internal static partial class Program
{
    private static void RulerClipboardChecks()
    {
        var original = UiText.Culture.Name;
        LocalizationService.Apply("zh-CN");
        var path = Path.Combine(Root, "ruler-copy.json");
        var settings = new SettingsService(path); settings.Load();
        Require(!settings.Current.ScreenRulerAutoCopy, "Auto copy overwrites the clipboard by default.");
        var copied = new List<string>(); var busy = false;
        var ruler = new ScreenRulerWindow(settings, value =>
        {
            if (busy) throw new ExternalException("Clipboard busy");
            copied.Add(value);
        });
        try
        {
            ruler.Show(); ruler.UpdateLayout();
            var state = (RulerMeasurements)typeof(ScreenRulerWindow).GetField("_measurements", PrivateInstance)!.GetValue(ruler)!;
            var toggle = (ToggleButton)typeof(ScreenRulerWindow).GetField("_autoCopyButton", PrivateInstance)!.GetValue(ruler)!;
            var notice = (TextBlock)typeof(ScreenRulerWindow).GetField("_copyNotice", PrivateInstance)!.GetValue(ruler)!;
            var instructions = (TextBlock)typeof(ScreenRulerWindow).GetField("_interactionText", PrivateInstance)!.GetValue(ruler)!;
            Require(toggle.IsChecked == false, "Auto-copy button ignores the default preference.");
            Invoke(ruler, "CopyCurrentMeasurement");
            state.Press(new Point(200, 100)); state.Release(new Point(20, 20));
            Require(copied.Count == 0, "Disabled auto copy or an empty manual copy changes the clipboard.");
            Invoke(ruler, "CopyCurrentMeasurement");
            Require(copied.SequenceEqual(new[] { "180 × 80 px" }), "Manual copy does not use normalized box dimensions.");
            toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var saved = new SettingsService(path); saved.Load();
            Require(saved.Current.ScreenRulerAutoCopy && copied.Count == 1, "Enabling auto copy is not persisted or copies an old result.");
            state.Press(new Point(10, 10)); state.Update(new Point(100, 50));
            Invoke(ruler, "CopyCurrentMeasurement");
            Require(copied.Count == 1, "A draft or manual copy during a draft overwrites the clipboard.");
            state.Cancel(); state.Resize(new System.Windows.Size(600, 300));
            state.Guides.Add(new RulerGuide(true, 100)); Invoke(ruler, "ToggleReferenceGuides");
            Require(copied.Count == 1, "Cancel, resize or guide visibility triggers automatic copying.");
            state.SelectTool(RulerTool.Line); state.Press(new Point(10, 10)); state.Release(new Point(13, 14));
            Require(copied.Count == 2 && copied[^1] == "长度：5 px", "Linear completion is not copied exactly once.");
            state.SelectTool(RulerTool.Distance);
            state.Press(new Point(120, 100)); state.Release(new Point(120, 100)); state.Update(new Point(0, 10));
            Require(copied.Count == 2, "First point or preview triggers automatic copying.");
            state.Press(new Point(0, 10)); state.Release(new Point(0, 10));
            Require(copied.Count == 3 && copied[^1] == "Δx：-120 px，Δy：-90 px，距离：150 px", "Two-click measurement copies wrong deltas or copies twice.");
            state.SelectTool(RulerTool.Angle);
            foreach (var p in new[] { new Point(50, 50), new Point(100, 50) }) { state.Press(p); state.Release(p); }
            Require(copied.Count == 3, "An incomplete angle changes the clipboard.");
            state.Press(new Point(50, 100)); state.Release(new Point(50, 100));
            Require(copied.Count == 4 && copied[^1] == "90°", "Angle completion does not copy its final angle exactly once.");
            Invoke(ruler, "UpdateMeasurements");
            Require(notice.Text == "已复制：90°" && notice.Visibility == Visibility.Visible && instructions.Visibility == Visibility.Collapsed,
                "Pointer updates erase the nonmodal copied notification.");
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2100) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
            Require(notice.Visibility == Visibility.Collapsed && instructions.Visibility == Visibility.Visible, "Copy notification never restores normal instructions.");
            busy = true; Invoke(ruler, "CopyCurrentMeasurement");
            Require(notice.Text == UiText.Get("DesktopRulerCopyBusy") && state.Results.ContainsKey(RulerTool.Angle), "Busy clipboard crashes or destroys the completed measurement.");
            busy = false; Invoke(ruler, "CopyCurrentMeasurement");
            Require(copied.Count == 5 && copied[^1] == "90°", "Manual retry cannot recover from a busy clipboard.");
            state.Clear(); Invoke(ruler, "CopyCurrentMeasurement");
            Require(copied.Count == 5, "Clearing results copies stale data.");
        }
        finally { ruler.Close(); LocalizationService.Apply(original); }
        var reloaded = new SettingsService(path); reloaded.Load();
        var reopened = new ScreenRulerWindow(reloaded, _ => throw new InvalidOperationException("No copy expected"));
        try
        {
            var toggle = (ToggleButton)typeof(ScreenRulerWindow).GetField("_autoCopyButton", PrivateInstance)!.GetValue(reopened)!;
            Require(toggle.IsChecked == true, "Reopening the ruler loses enabled auto copy.");
            toggle.IsChecked = false; toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var disabled = new SettingsService(path); disabled.Load();
            Require(!disabled.Current.ScreenRulerAutoCopy, "Disabled auto copy is not persisted.");
        }
        finally { reopened.Close(); }
        Console.WriteLine("PASS: ruler auto copy defaults off and persists; manual copy; all four formats; commit-only triggers; busy clipboard recovery; transient notification.");
    }
}
