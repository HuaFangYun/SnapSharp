using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NimCap.Screenshot.Services;
using NimCap.Screenshot.Views;
using Application = System.Windows.Application;
using TabControl = System.Windows.Controls.TabControl;

namespace NimCap.Screenshot.WindowChecks;

internal static partial class Program
{
    private static void WindowLayeringChecks()
    {
        var image = BitmapSource.Create(32, 32, 96, 96, PixelFormats.Bgra32, null, new byte[32 * 32 * 4], 32 * 4);
        image.Freeze();
        var settings = new SettingsService(Path.Combine(Root, "layering-settings.json"));
        using var hotkey = new GlobalHotkeyHost();
        var pin = new PinWindow(image, new SkiaImageService(), settings, new ClipboardService());
        var otherPin = new Window { Width = 150, Height = 100, Topmost = true };
        var normal = new Window { Width = 150, Height = 100 };
        // Exercise production controller wiring without starting the tray, loading user
        // preferences, registering hotkeys or running recognition/model downloads.
        var controller = (AppController)RuntimeHelpers.GetUninitializedObject(typeof(AppController));
        void Field(string name, object value) => typeof(AppController).GetField(name, PrivateInstance)!.SetValue(controller, value);
        Field("_settings", settings);
        Field("_startup", new StartupRegistrationService());
        Field("_hotkey", hotkey);
        Field("_openWindows", new List<Window>());
        RecognitionWindow? recognition = null;
        try
        {
            pin.Show(); otherPin.Show(); normal.Show();
            Invoke(controller, "OpenRecognitionCore", image, pin, "OCR", false);
            recognition = Application.Current.Windows.OfType<RecognitionWindow>().Single();
            Require(recognition.Owner == pin, "Recognition lost its pin owner.");
            Require(IsAbove(recognition, pin), "Recognition is behind its topmost pin.");

            InspectModal<SettingsWindow>(() => Invoke(recognition, "Settings_Click", recognition, new RoutedEventArgs()), dialog =>
            {
                Require(dialog.Owner == recognition, "OCR settings lost the recognition owner.");
                Require(((TabItem)((TabControl)dialog.FindName("Categories")).SelectedItem).Tag?.ToString() == "ScanText",
                    "OCR settings opened the wrong category.");
                Require(!pin.Topmost && !otherPin.Topmost && !normal.Topmost, "A pin can obstruct modal settings.");
                Require(IsWindowEnabled(Handle(dialog)) && !IsWindowEnabled(Handle(recognition)), "Settings are not the usable modal window.");
                Require(IsAbove(dialog, recognition) && IsAbove(dialog, pin) && IsAbove(dialog, otherPin), "Settings are obscured.");

                InspectModal<Window>(() => ThemedMessageBox.Show(dialog, "Nested confirmation"), confirm =>
                {
                    Require(confirm.Owner == dialog && IsAbove(confirm, dialog), "Nested confirmation lost its owner/layer.");
                    Require(IsWindowEnabled(Handle(confirm)) && !IsWindowEnabled(Handle(dialog)), "Nested confirmation is blocked.");
                    Require(!pin.Topmost && !otherPin.Topmost, "Nested dialog restored topmost pins too early.");
                });
                Require(!pin.Topmost && !otherPin.Topmost, "Closing a nested dialog restored pins before settings closed.");

                using var color = new ProbeFormsDialog(hwnd =>
                {
                    Require(hwnd == Handle(dialog), "Native color dialog did not receive its WPF owner's HWND.");
                    Require(!pin.Topmost && !otherPin.Topmost, "Native dialog is obstructed by pins.");
                });
                WindowDialogService.ShowDialog(color, dialog);
                var file = new ProbeWpfDialog(hwnd => Require(hwnd == Handle(dialog), "File/folder dialog lost its owner HWND."));
                WindowDialogService.ShowDialog(file, dialog);
            });
            Require(pin.Topmost && otherPin.Topmost && !normal.Topmost, "Topmost preferences were not restored.");
            Require(IsWindowEnabled(Handle(recognition)), "Recognition stayed disabled after closing settings.");

            Func<Window>[] modalWindows =
            [
                () => new SaveImageWindow(image, new SkiaImageService(), settings),
                () => new CanvasExpandDialog(image.PixelWidth, image.PixelHeight),
                () => new WatermarkDialog(),
                () => new TextInputDialog(),
                () => new RecordingSetupWindow(),
                () => PrivacyConsentDialog.Create(pin, new Uri("https://example.com/ocr"), "test", null)
            ];
            foreach (var create in modalWindows)
            {
                var modal = create();
                InspectModal<Window>(() => WindowDialogService.ShowDialog(modal, pin), dialog =>
                {
                    Require(dialog.Owner == pin && IsAbove(dialog, pin) && IsAbove(dialog, otherPin),
                        dialog.GetType().Name + " is behind a pin or lost its owner.");
                    Require(IsWindowEnabled(Handle(dialog)) && !IsWindowEnabled(Handle(pin)),
                        dialog.GetType().Name + " has incorrect modal enablement.");
                });
                Require(pin.Topmost && otherPin.Topmost, modal.GetType().Name + " did not restore pin preferences.");
            }

            // Tray entry has no explicit caller; it must still open a usable modal.
            normal.Activate();
            InspectModal<TextInputDialog>(() => WindowDialogService.ShowDialog(new TextInputDialog()), dialog =>
            {
                Require(dialog.Owner == normal, "Ownerless dialog did not use the active application window.");
                Require(IsAbove(dialog, pin) && IsAbove(dialog, otherPin), "Tray dialog is behind a pin.");
            });
            var hidden = new Window();
            try
            {
                InspectModal<TextInputDialog>(() => WindowDialogService.ShowDialog(new TextInputDialog(), hidden), dialog =>
                    Require(dialog.Owner != hidden, "Hidden window was used as a modal owner."));
            }
            finally { hidden.Close(); }

            using var failing = new ProbeFormsDialog(_ => throw new InvalidOperationException("simulated dialog failure"));
            try { WindowDialogService.ShowDialog(failing, recognition); throw new Exception("Expected native dialog failure."); }
            catch (InvalidOperationException ex) when (ex.Message == "simulated dialog failure") { }
            Require(pin.Topmost && otherPin.Topmost && !normal.Topmost, "Dialog failure did not restore topmost preferences.");
            InspectModal<TextInputDialog>(() => WindowDialogService.ShowDialog(new TextInputDialog(), normal), _ => otherPin.Close());
            Require(pin.Topmost && !normal.Topmost, "Closing a pin during a modal dialog broke restoration.");
            Console.WriteLine("PASS: pin -> OCR -> settings -> nested confirmation; native dialog owners; unrelated pins; tray/hidden owners; modal enablement; native Z-order; topmost restoration on close and failure.");
            Console.WriteLine("PASS: save image, canvas expansion, watermark, text input, recording setup and privacy confirmation windows; pin closure during a modal dialog.");
        }
        finally
        {
            recognition?.Close(); normal.Close(); otherPin.Close(); pin.Close();
        }
    }

    private static void InspectModal<T>(Action open, Action<T> inspect) where T : Window
    {
        var existing = Application.Current.Windows.Cast<Window>().Where(w => w.IsVisible).ToHashSet();
        Exception? failure = null;
        var inspected = false;
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += (_, _) =>
        {
            var window = Application.Current.Windows.Cast<Window>().FirstOrDefault(w => !existing.Contains(w) && w.IsVisible);
            if (window is null && timeout.Elapsed < TimeSpan.FromSeconds(10)) return;
            timer.Stop();
            try
            {
                Require(window is T, "Expected modal window " + typeof(T).Name + " was not shown.");
                inspected = true;
                inspect((T)window!);
            }
            catch (Exception ex) { failure = ex; }
            finally { window?.Close(); }
        };
        timer.Start();
        try { open(); }
        finally { timer.Stop(); }
        if (failure is not null) throw new InvalidOperationException("Modal inspection failed.", failure);
        Require(inspected, "Modal dialog returned without being inspected.");
    }

    private sealed class ProbeFormsDialog(Action<nint> inspect) : System.Windows.Forms.CommonDialog
    {
        public override void Reset() { }
        protected override bool RunDialog(nint hwndOwner) { inspect(hwndOwner); return false; }
    }

    private sealed class ProbeWpfDialog(Action<nint> inspect) : Microsoft.Win32.CommonDialog
    {
        public override void Reset() { }
        protected override bool RunDialog(nint hwndOwner) { inspect(hwndOwner); return false; }
    }

    private static nint Handle(Window window) => new WindowInteropHelper(window).Handle;
    private static bool IsAbove(Window upper, Window lower)
    {
        var target = Handle(lower);
        for (var handle = GetWindow(Handle(upper), 2); handle != 0; handle = GetWindow(handle, 2))
            if (handle == target) return true;
        return false;
    }
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowEnabled(nint hwnd);
}
