using L = NimCap.Screenshot.Localization.UiText;
using NimCap.Screenshot.Views;
using NimCap.Screenshot.Native;
using System.Windows;
using System.Windows.Interop;

namespace NimCap.Screenshot.Services;

public sealed class PinWorkspace
{
    private readonly List<PinWindow> _windows = [];
    private Dictionary<PinWindow, System.Drawing.Point>? _moveOrigins;
    private PinWindow? _moveLeader;
    public event EventHandler? Changed;
    public IEnumerable<string> Groups => _windows.Select(x => x.GroupName).OfType<string>().Distinct().Order();

    public void Add(PinWindow window)
    {
        _windows.Add(window);
        window.Closed += (_, _) => { _windows.Remove(window); if (_moveLeader == window) EndMove(); Changed?.Invoke(this, EventArgs.Empty); };
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Assign(PinWindow window, string? group)
    {
        group = group?.Trim();
        if (group is { Length: > 40 } || group?.Any(char.IsControl) == true) throw new ArgumentException(L.Get("GroupNamesMustBeAtMost40"));
        window.GroupName = string.IsNullOrWhiteSpace(group) ? null : group;
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public PinWindow[] Members(PinWindow window) => window.GroupName is null ? [window] : _windows.Where(x => x.GroupName == window.GroupName).ToArray();
    public void SetGroupVisible(string group, bool visible)
    {
        foreach (var window in _windows.Where(x => x.GroupName == group).ToArray()) { if (visible) window.Show(); else window.Hide(); }
    }
    public void CloseGroup(string group)
    {
        foreach (var window in _windows.Where(x => x.GroupName == group).ToArray()) window.Close();
    }
    public void BeginMove(PinWindow leader)
    {
        _moveLeader = leader;
        _moveOrigins = [];
        foreach (var window in Members(leader))
            if (NativeMethods.GetWindowRect(new WindowInteropHelper(window).Handle, out var bounds))
                _moveOrigins[window] = new System.Drawing.Point(bounds.Left, bounds.Top);
    }
    public void Move(PinWindow leader, int left, int top)
    {
        if (_moveLeader != leader || _moveOrigins is null || !_moveOrigins.TryGetValue(leader, out var origin)) return;
        foreach (var (window, position) in _moveOrigins)
        {
            if (window == leader || !_windows.Contains(window)) continue;
            NativeMethods.SetWindowPos(new WindowInteropHelper(window).Handle, nint.Zero,
                position.X + left - origin.X, position.Y + top - origin.Y, 0, 0, 0x0001 | 0x0004 | NativeMethods.SwpNoActivate);
        }
    }
    public void EndMove() { _moveLeader = null; _moveOrigins = null; }

    // All rectangles are physical screen pixels. Threshold and frame inset originate in DIPs.
    internal static System.Drawing.Rectangle Snap(System.Drawing.Rectangle bounds, System.Drawing.Rectangle workArea, double dpi, double distance)
    {
        var threshold = distance * dpi;
        var inset = (int)Math.Round(10 * dpi);
        var left = bounds.Left; var top = bounds.Top;
        if (Math.Abs(bounds.Left + inset - workArea.Left) <= threshold) left = workArea.Left - inset;
        else if (Math.Abs(bounds.Right - inset - workArea.Right) <= threshold) left = workArea.Right + inset - bounds.Width;
        if (Math.Abs(bounds.Top + inset - workArea.Top) <= threshold) top = workArea.Top - inset;
        else if (Math.Abs(bounds.Bottom - inset - workArea.Bottom) <= threshold) top = workArea.Bottom + inset - bounds.Height;
        return new System.Drawing.Rectangle(left, top, bounds.Width, bounds.Height);
    }
}
