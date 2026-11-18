using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SnapSharp.Models;
using SnapSharp.Services;
using SnapSharp.Views;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using CaptureMode = SnapSharp.Models.CaptureMode;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace SnapSharp.WindowChecks;

internal static partial class Program
{
    private static void GuidePreferencesChecks()
    {
        var file = Path.Combine(Root, "guide-preferences.json");
        File.WriteAllText(file, "{\"CaptureRulerHotkey\":\"F8\",\"CaptureCrosshairHotkey\":\"F9\"}");
        var settings = new SettingsService(file); settings.Load();
        Require(!settings.Current.ShowCaptureRulers && !settings.Current.ShowCaptureCrosshair,
            "Existing settings did not default both guides to off.");
        settings.Save();

        var image = PrecisionImage();
        var screen = new CapturedScreen(new System.Drawing.Rectangle(0, 0, image.PixelWidth, image.PixelHeight), image);
        var coordinator = new CaptureCoordinator(new GuideCapture(screen), settings);
        coordinator.Begin(CaptureMode.Region);
        var windows = System.Windows.Application.Current.Windows.OfType<SelectionWindow>().ToArray();
        try
        {
            Require(windows.Length == 2, "Multi-monitor fixture did not open both overlays.");
            Invoke(windows[0], "Ruler_Click", windows[0], new RoutedEventArgs());
            Invoke(windows[0], "Crosshair_Click", windows[0], new RoutedEventArgs());
            foreach (var window in windows)
            {
                Require(((ToggleButton)window.FindName("CaptureRulerButton")).IsChecked == true &&
                    ((ToggleButton)window.FindName("CaptureCrosshairButton")).IsChecked == true,
                    "Guide state did not synchronize between monitors.");
            }
        }
        finally { Invoke(coordinator, "OnCanceled", coordinator, EventArgs.Empty); }

        var reloaded = new SettingsService(file); reloaded.Load();
        Require(reloaded.Current.ShowCaptureRulers && reloaded.Current.ShowCaptureCrosshair, "Enabled guide states were not saved.");
        var overlay = new SelectionWindow(screen, [], new PrecisionCapture(), settings: reloaded);
        try
        {
            overlay.Show(); overlay.Activate(); overlay.Focus(); overlay.UpdateLayout();
            var ruler = (ToggleButton)overlay.FindName("CaptureRulerButton");
            var crosshair = (ToggleButton)overlay.FindName("CaptureCrosshairButton");
            Require(ruler.IsChecked == true && crosshair.IsChecked == true, "The next capture lost enabled guide states.");
            Require(((TextBlock)overlay.FindName("Hint")).Text.Contains("(R)") &&
                ((TextBlock)overlay.FindName("Hint")).Text.Contains("(X)"), "Top hint does not show fixed R/X shortcuts.");
            SendGuideKey(overlay, Key.F8); SendGuideKey(overlay, Key.F9);
            Require(ruler.IsChecked == true && crosshair.IsChecked == true, "Obsolete custom shortcuts still toggle guides.");
            SendGuideKey(overlay, Key.R); SendGuideKey(overlay, Key.X);
            Require(ruler.IsChecked == false && crosshair.IsChecked == false, "Fixed R/X shortcuts did not toggle guides off.");

            var mode = (ComboBox)overlay.FindName("CaptureModePicker");
            mode.SelectedIndex = 0; overlay.Focus(); Invoke(overlay, "UpdateMovementHint");
            var hint = (TextBlock)overlay.FindName("MovementHintText");
            Require(hint.Visibility == Visibility.Visible, "Movable pointer has no arrow-key hint.");
            mode.SelectedIndex = 3;
            Require(hint.Visibility == Visibility.Collapsed, "Fixed full-screen capture advertises movement.");
            mode.SelectedIndex = 0;
            var root = (Grid)overlay.Content;
            var input = new TextBox { Width = 100, Height = 25, VerticalAlignment = VerticalAlignment.Bottom };
            root.Children.Add(input); overlay.UpdateLayout(); input.Focus(); Invoke(overlay, "UpdateMovementHint");
            Require(hint.Visibility == Visibility.Collapsed, "An input field still advertises capture movement.");
            root.Children.Remove(input); overlay.Focus();
            mode.IsDropDownOpen = true; Invoke(overlay, "UpdateMovementHint");
            Require(hint.Visibility == Visibility.Collapsed, "An open picker still advertises capture movement.");
            mode.IsDropDownOpen = false; overlay.Focus();
            typeof(SelectionWindow).GetField("_selection", PrivateInstance)!.SetValue(overlay,
                new Rect(0, 0, overlay.ActualWidth, overlay.ActualHeight));
            Invoke(overlay, "BeginInlineEditing"); overlay.Focus(); Invoke(overlay, "UpdateMovementHint");
            Require(hint.Visibility == Visibility.Collapsed, "A full-monitor selection advertises movement.");
            Invoke(overlay, "NudgeCapture", -1, 0, true);
            Require(hint.Visibility == Visibility.Visible, "A newly movable selection did not regain its hint.");
            Invoke(overlay, "RestartRegionSelection");
            SavePreview((FrameworkElement)overlay.Content, Path.Combine(Root, "guide-toolbar-default-off.png"));
        }
        finally { overlay.Close(); }

        var disabled = new SettingsService(file); disabled.Load();
        Require(!disabled.Current.ShowCaptureRulers && !disabled.Current.ShowCaptureCrosshair, "Disabled guide states were not saved.");
        disabled.Current.ShowCaptureCrosshair = true;
        using var hotkeys = new GlobalHotkeyHost();
        var configuration = new SettingsWindow(disabled, new StartupRegistrationService(), hotkeys);
        try
        {
            Require(configuration.FindName("CaptureRulerHotkey") == null &&
                configuration.FindName("CaptureCrosshairHotkey") == null &&
                configuration.FindName("ShowCaptureRulers") == null &&
                configuration.FindName("ShowCaptureCrosshair") == null, "Removed guide settings are still present.");
            var candidate = configuration.BuildCandidate();
            Require(candidate.ShowCaptureCrosshair && !candidate.ShowCaptureRulers,
                "Saving settings would overwrite remembered toolbar states.");
            candidate.ShowCaptureCrosshair = false;
            Require(disabled.Current.ShowCaptureCrosshair, "Settings candidate changed current toolbar preferences.");
            var rulerShortcut = (TextBox)configuration.FindName("ScreenRulerHotkey");
            rulerShortcut.Text = "Ctrl+Shift+M";
            candidate = configuration.BuildCandidate();
            Require(candidate.ScreenRulerHotkey == "Ctrl+Shift+M" && disabled.Current.ScreenRulerHotkey == "" &&
                GlobalHotkeyHost.CreateRegistrations(candidate).Values.Any(x => x.Action == GlobalAction.ScreenRuler),
                "Standalone ruler hotkey is not configurable or registered.");
            disabled.Update(candidate);
            var shortcutReload = new SettingsService(file); shortcutReload.Load();
            Require(shortcutReload.Current.ScreenRulerHotkey == "Ctrl+Shift+M", "Standalone ruler shortcut was not persisted.");
            rulerShortcut.Text = candidate.RegionHotkey;
            try { configuration.BuildCandidate(); throw new Exception("Duplicate ruler shortcut accepted."); }
            catch (InvalidOperationException) { }
            rulerShortcut.Text = "";
            Require(!GlobalHotkeyHost.CreateRegistrations(configuration.BuildCandidate()).Values.Any(x => x.Action == GlobalAction.ScreenRuler),
                "Clearing the standalone ruler hotkey did not disable it.");
            Require(GlobalHotkeyHost.RunPinRoutingSelfTest(), "Standalone ruler shortcut routing affected capture/pin actions.");
        }
        finally { configuration.Close(); }
        Console.WriteLine("PASS: guides default off; independent state persistence/reload; multi-monitor synchronization; fixed R/X shortcuts/hints; auxiliary settings removed; movement hints only when movement is available.");
    }

    private static void SendGuideKey(SelectionWindow window, Key key)
    {
        var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, key)
            { RoutedEvent = Keyboard.KeyDownEvent, Source = window };
        window.RaiseEvent(e);
    }

    private sealed class GuideCapture(CapturedScreen screen) : ICaptureService
    {
        public IReadOnlyList<CapturedScreen> CaptureAllScreens() => [screen,
            screen with { Bounds = new System.Drawing.Rectangle(screen.Bounds.Right, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height) }];
        public IReadOnlyList<WindowCandidate> EnumerateTopLevelWindows() => [];
        public WindowCandidate? FindWindowAt(System.Drawing.Point point) => null;
        public System.Windows.Media.Imaging.BitmapSource CaptureRegion(System.Drawing.Rectangle bounds) => throw new NotSupportedException();
        public CapturedScreen CaptureVirtualScreen() => throw new NotSupportedException();
        public CapturedScreen? CaptureWindow(nint hwnd) => throw new NotSupportedException();
    }
}
