using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using SnapSharp.Models;
using SnapSharp.Services;
using L = SnapSharp.Localization.UiText;

namespace SnapSharp.Views;

public partial class SelectionWindow
{
    private readonly IElementDetectionService _elementDetectionService;
    private DispatcherTimer? _elementHoverTimer;
    private WindowCandidate? _elementWindow;
    private IReadOnlyList<ElementCandidate> _elementCandidates = [];
    private int _elementScope;
    private System.Drawing.Point? _elementLastQueryPoint;
    private long _elementLastQueryTime;
    private CancellationTokenSource? _elementQueryCancellation;
    private long _elementRequestId;
    private bool _elementQueryPending;
    private bool _elementClosed;

    private void InitializeElementDetection()
    {
        _elementHoverTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            { Interval = TimeSpan.FromMilliseconds(90) };
        _elementHoverTimer.Tick += (_, _) =>
        {
            if (_mode != OverlayMode.Element || _editing || _dragging || _elementClosed || !IsVisible || CapturePanel.IsMouseOver) return;
            if (!SnapSharp.Native.NativeMethods.GetCursorPos(out var cursor) || !_screen.Bounds.Contains(cursor.X, cursor.Y)) return;
            _ = RefreshElementSuggestionAsync();
        };
        Closed += (_, _) => { _elementClosed = true; _elementHoverTimer.Stop(); CancelElementQuery(); };
    }

    private void CancelElementQuery()
    {
        _elementRequestId++;
        _elementQueryCancellation?.Cancel();
        _elementQueryCancellation = null;
        _elementQueryPending = false;
        _elementLastQueryPoint = null;
    }

    private void ResetElementDetection()
    {
        _elementHoverTimer?.Stop();
        CancelElementQuery();
        _elementWindow = null;
        _elementCandidates = [];
        _elementScope = 0;
    }

    private Rect SuggestedElementAt(Point point)
    {
        var physical = ToScreenPoint(point);
        // EnumWindows supplied these in Z order before the overlay existed. Never hit-test our own HWND.
        var window = _windows.FirstOrDefault(candidate => candidate.Bounds.Contains(physical));
        if (window != _elementWindow || _elementCandidates.Count > 0 && !_elementCandidates[0].Bounds.Contains(physical))
        {
            CancelElementQuery();
            _elementWindow = window;
            _elementCandidates = [];
            _elementScope = 0;
        }
        if (window is null) return Rect.Empty;
        var bounds = _elementCandidates.Count > 0 ? _elementCandidates[_elementScope].Bounds : window.SelectionBounds(false);
        // The pointer may be on the title bar, outside the client area.
        if (!bounds.Contains(physical)) bounds = window.Bounds;
        return ElementRect(bounds);
    }

    private Rect ElementRect(Rectangle bounds)
    {
        var visible = Rectangle.Intersect(bounds, _screen.Bounds);
        return visible.Width > 0 && visible.Height > 0 ? FromScreenRect(visible) : Rect.Empty;
    }

    private async Task RefreshElementSuggestionAsync()
    {
        if (_mode != OverlayMode.Element || _editing || _dragging || _elementClosed || !IsVisible) return;
        SuggestedElementAt(_lastPointer);
        var window = _elementWindow;
        var point = ToScreenPoint(_lastPointer);
        if (window is null || _elementQueryPending) return;
        var hasControl = _elementCandidates.Count > 0 &&
            _elementCandidates[0].Bounds != window.SelectionBounds(false) && _elementCandidates[0].Bounds != window.Bounds;
        if (_elementLastQueryPoint == point &&
            (hasControl || Environment.TickCount64 - _elementLastQueryTime < 1200)) return;
        var id = ++_elementRequestId;
        using var cancellation = new CancellationTokenSource();
        _elementQueryCancellation = cancellation;
        _elementQueryPending = true;
        _elementLastQueryPoint = point;
        _elementLastQueryTime = Environment.TickCount64;
        try
        {
            // Bound even a custom detector; cancellation must never freeze selection or shutdown.
            var detected = await _elementDetectionService.FindElementsAsync(window, point, cancellation.Token)
                .WaitAsync(TimeSpan.FromMilliseconds(650), cancellation.Token);
            if (id != _elementRequestId || _elementClosed || _editing || _dragging || _mode != OverlayMode.Element ||
                !IsVisible || _elementWindow != window || ToScreenPoint(_lastPointer) != point) return;
            var previousScope = _elementScope > 0 ? _elementCandidates[_elementScope].Bounds : (Rectangle?)null;
            _elementCandidates = ElementDetectionService.BuildChain(detected, window, point);
            _elementScope = 0;
            if (previousScope is { } bounds)
                for (var i = 0; i < _elementCandidates.Count; i++)
                    if (_elementCandidates[i].Bounds == bounds) { _elementScope = i; break; }
            _selection = SuggestedElementAt(_lastPointer);
            UpdateSelection();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // A failed/slow provider leaves the window fallback selectable.
            System.Diagnostics.Debug.WriteLine($"Element preview: {ex.GetType().Name}");
        }
        finally
        {
            if (id == _elementRequestId) { _elementQueryPending = false; _elementQueryCancellation = null; }
        }
    }

    private void AdjustElementScope(Point point, int delta)
    {
        SuggestedElementAt(point);
        if (_elementCandidates.Count == 0 || delta == 0) return;
        _elementScope = Math.Clamp(_elementScope + (delta < 0 ? 1 : -1), 0, _elementCandidates.Count - 1);
        _selection = ElementRect(_elementCandidates[_elementScope].Bounds);
        UpdateSelection();
    }

    private string ElementDescription()
    {
        if (_elementCandidates.Count == 0) return L.Get("ElementWindowFallback");
        var candidate = _elementCandidates[_elementScope];
        var name = string.Join(" ", candidate.Name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length > 48) name = name[..48] + "…";
        return $"{candidate.ControlType}{(name.Length > 0 ? ": " + name : "")}  ({_elementScope + 1}/{_elementCandidates.Count})";
    }
}
