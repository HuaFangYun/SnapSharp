using System.Windows;
using System.Windows.Interop;

namespace SnapSharp.Services;

/// <summary>Keeps modal windows above their caller and temporarily clears obstructing app pins.</summary>
internal static class WindowDialogService
{
    internal static Window? ResolveOwner(Window? owner = null, Window? dialog = null)
    {
        static bool CanOwn(Window window, Window? child) =>
            window != child && window.IsVisible && window.ShowActivated;

        if (owner is not null && CanOwn(owner, dialog)) return owner;
        var windows = Application.Current?.Windows.Cast<Window>().Where(w => CanOwn(w, dialog)).ToArray() ?? [];
        return windows.FirstOrDefault(w => w.IsActive)
            ?? windows.FirstOrDefault(w => w.Topmost)
            ?? windows.LastOrDefault();
    }

    internal static void SetOwner(Window window, Window? owner = null)
    {
        owner = ResolveOwner(owner ?? window.Owner, window);
        if (owner is null) return;
        window.Owner = owner;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    public static bool? ShowDialog(Window dialog, Window? owner = null)
    {
        SetOwner(dialog, owner);
        using var scope = new ModalScope();
        return dialog.ShowDialog();
    }

    public static bool? ShowDialog(Microsoft.Win32.CommonDialog dialog, Window? owner = null)
    {
        owner = ResolveOwner(owner);
        using var scope = new ModalScope();
        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }

    public static System.Windows.Forms.DialogResult ShowDialog(System.Windows.Forms.CommonDialog dialog, Window? owner = null)
    {
        owner = ResolveOwner(owner);
        using var scope = new ModalScope();
        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(new NativeOwner(new WindowInteropHelper(owner).Handle));
    }

    public static bool? ShowDialog(System.Windows.Controls.PrintDialog dialog, Window? owner = null)
    {
        // WPF's print dialog uses the active native window; it has no owner overload.
        ResolveOwner(owner)?.Activate();
        using var scope = new ModalScope();
        return dialog.ShowDialog();
    }

    private sealed class NativeOwner(nint handle) : System.Windows.Forms.IWin32Window
    {
        public nint Handle => handle;
    }

    private sealed class ModalScope : IDisposable
    {
        private readonly Window[] _topmost = Application.Current?.Windows.Cast<Window>()
            .Where(w => w.IsVisible && w.Topmost).ToArray() ?? [];

        public ModalScope()
        {
            // ShowDialog disables other app windows. A disabled topmost pin must not
            // cover the only usable dialog. Nested scopes leave restoration to the outer one.
            foreach (var window in _topmost) window.SetCurrentValue(Window.TopmostProperty, false);
        }

        public void Dispose()
        {
            foreach (var window in _topmost)
                if (window.IsLoaded) window.SetCurrentValue(Window.TopmostProperty, true);
        }
    }
}
