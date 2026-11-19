using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Automation;
using SnapSharp.Models;
using SnapSharp.Native;
using L = SnapSharp.Localization.UiText;

namespace SnapSharp.Services;

/// <summary>
/// Uses the saved underlying HWND instead of desktop FromPoint, which would hit our overlay.
/// UIA calls run on one bounded MTA worker: a broken provider cannot block input or create
/// an unbounded number of stuck threads. Only immutable rectangles leave that thread.
/// </summary>
public sealed class ElementDetectionService : IElementDetectionService
{
    private sealed record Request(WindowCandidate Window, System.Drawing.Point Point,
        CancellationToken Token, TaskCompletionSource<IReadOnlyList<ElementCandidate>> Completion);

    private static readonly Lazy<BlockingCollection<Request>> Requests = new(StartWorker);

    public async Task<IReadOnlyList<ElementCandidate>> FindElementsAsync(
        WindowCandidate window, System.Drawing.Point screenPoint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsUnchanged(window)) return [];
        var (native, _) = FindNativeChildren(window, screenPoint);
        var fallback = BuildChain(native, window, screenPoint);
        using var workCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var completion = new TaskCompletionSource<IReadOnlyList<ElementCandidate>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new Request(window, screenPoint, workCancellation.Token, completion);
        var queue = Requests.Value;
        // Keep only the newest waiting query, including across monitors and capture sessions.
        while (!queue.TryAdd(request))
            if (queue.TryTake(out var superseded)) superseded.Completion.TrySetResult([]);
        try
        {
            var detected = await completion.Task.WaitAsync(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            return IsUnchanged(window) ? BuildChain(detected.Concat(native), window, screenPoint) : [];
        }
        catch (TimeoutException) { return IsUnchanged(window) ? fallback : []; }
        finally { workCancellation.Cancel(); }
    }

    private static BlockingCollection<Request> StartWorker()
    {
        var queue = new BlockingCollection<Request>(1);
        var worker = new Thread(() =>
        {
            foreach (var request in queue.GetConsumingEnumerable())
            {
                if (request.Token.IsCancellationRequested) { request.Completion.TrySetCanceled(request.Token); continue; }
                try { request.Completion.TrySetResult(FindAutomationElements(request)); }
                catch (Exception ex)
                {
                    // Accessibility providers are external code and may disappear or reject access.
                    Debug.WriteLine($"Element detection: {ex.GetType().Name}");
                    request.Completion.TrySetResult([]);
                }
            }
        }) { IsBackground = true, Name = "NimCap Screenshot element detection" };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
        return queue;
    }

    private static bool IsUnchanged(WindowCandidate window) =>
        ScreenCaptureService.TryGetWindowBounds(window.Handle, out var current) && current == window.Bounds;

    internal static (List<ElementCandidate> Candidates, nint Seed) FindNativeChildren(
        WindowCandidate window, System.Drawing.Point point)
    {
        var candidates = new List<ElementCandidate>();
        var parent = window.Handle;
        var clip = window.Bounds;
        for (var depth = 0; depth < 32; depth++)
        {
            var local = new NativeMethods.PointL { X = point.X, Y = point.Y };
            if (!NativeMethods.ScreenToClient(parent, ref local)) break;
            // Disabled controls are still valid screenshot targets; skip hidden/transparent HWNDs.
            var child = NativeMethods.ChildWindowFromPointEx(parent, local, 0x0001 | 0x0004);
            if (child == 0 || child == parent || !NativeMethods.GetWindowRect(child, out var rect)) break;
            var bounds = Rectangle.Intersect(clip, Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
            if (bounds.Width <= 0 || bounds.Height <= 0 || !bounds.Contains(point)) break;
            candidates.Add(new ElementCandidate(bounds, L.Get("ElementControl")));
            clip = bounds;
            parent = child;
        }
        candidates.Reverse();
        return (candidates, parent);
    }

    private static IReadOnlyList<ElementCandidate> FindAutomationElements(Request request)
    {
        if (!IsUnchanged(request.Window)) return [];
        var clock = Stopwatch.StartNew();
        var cache = new CacheRequest { TreeScope = TreeScope.Element, TreeFilter = Automation.RawViewCondition };
        cache.Add(AutomationElement.BoundingRectangleProperty);
        cache.Add(AutomationElement.ControlTypeProperty);
        cache.Add(AutomationElement.LocalizedControlTypeProperty);
        cache.Add(AutomationElement.NameProperty);
        // Chromium's rendering HWND can expose an empty native pane while its logical
        // accessibility tree lives under the top-level HWND. Always start at that root.
        var root = AutomationElement.FromHandle(request.Window.Handle).GetUpdatedCache(cache);
        var path = new List<(ElementCandidate Candidate, ControlType Type)>();
        var bestPath = new List<(ElementCandidate Candidate, ControlType Type)>();
        var bestRank = -1;
        var bestArea = long.MaxValue;
        var walker = TreeWalker.RawViewWalker;
        var visited = 0;
        void Visit(AutomationElement element, Rectangle visibleBounds, int depth)
        {
            request.Token.ThrowIfCancellationRequested();
            if (visited++ >= 512 || depth >= 32 || clock.ElapsedMilliseconds >= 300) return;
            var data = element.Cached;
            var rawBounds = ToRectangle(data.BoundingRectangle);
            var bounds = Rectangle.Intersect(visibleBounds, rawBounds);
            var hasBounds = bounds.Width > 0 && bounds.Height > 0;
            if (rawBounds.Width > 0 && rawBounds.Height > 0 && !bounds.Contains(request.Point)) return;
            if (hasBounds)
            {
                path.Add((new ElementCandidate(bounds, data.LocalizedControlType, data.Name), data.ControlType));
                visibleBounds = bounds;
                var rank = IsInteractive(data.ControlType) ? 2
                    : data.ControlType == ControlType.Text || data.ControlType == ControlType.Image ? 1 : 0;
                var area = (long)bounds.Width * bounds.Height;
                if (rank > bestRank || rank == bestRank && (area < bestArea || area == bestArea && path.Count > bestPath.Count))
                { bestRank = rank; bestArea = area; bestPath = [.. path]; }
            }
            // Multiple overlapping provider branches can represent the same pixels (notably
            // Chromium's native surface and DOM). Search every containing branch within budget.
            // IsOffscreen can become true solely because our own overlay occludes the browser;
            // use clipped bounds against the saved window instead of rejecting that subtree.
            var child = visited < 512 && clock.ElapsedMilliseconds < 300 ? walker.GetFirstChild(element, cache) : null;
            while (child is not null && visited < 512 && clock.ElapsedMilliseconds < 300)
            {
                Visit(child, visibleBounds, depth + 1);
                child = walker.GetNextSibling(child, cache);
            }
            if (hasBounds) path.RemoveAt(path.Count - 1);
        }
        Visit(root, request.Window.Bounds, 0);
        bestPath.Reverse();
        return bestPath.Select(entry => entry.Candidate).ToArray();
    }

    private static bool IsInteractive(ControlType type) => type == ControlType.Button || type == ControlType.Edit ||
        type == ControlType.Hyperlink || type == ControlType.CheckBox || type == ControlType.RadioButton ||
        type == ControlType.ComboBox || type == ControlType.MenuItem || type == ControlType.TabItem;

    private static Rectangle ToRectangle(System.Windows.Rect rect)
    {
        if (rect.IsEmpty || !double.IsFinite(rect.X) || !double.IsFinite(rect.Y) ||
            !double.IsFinite(rect.Right) || !double.IsFinite(rect.Bottom) ||
            rect.Left < int.MinValue || rect.Top < int.MinValue || rect.Right > int.MaxValue || rect.Bottom > int.MaxValue)
            return Rectangle.Empty;
        return Rectangle.FromLTRB((int)Math.Floor(rect.Left), (int)Math.Floor(rect.Top),
            (int)Math.Ceiling(rect.Right), (int)Math.Ceiling(rect.Bottom));
    }

    internal static IReadOnlyList<ElementCandidate> BuildChain(IEnumerable<ElementCandidate> detected,
        WindowCandidate window, System.Drawing.Point point)
    {
        // Sorting and containment keep UIA/native duplicate bounds and overlapping siblings out of wheel navigation.
        var candidates = detected.Append(new ElementCandidate(window.SelectionBounds(false), L.Get("ElementClientArea")))
            .Append(new ElementCandidate(window.Bounds, L.Get("Window")))
            .Select(item => item with { Bounds = Rectangle.Intersect(item.Bounds, window.Bounds) })
            .Where(item => item.Bounds.Width > 0 && item.Bounds.Height > 0 && item.Bounds.Contains(point))
            .OrderBy(item => (long)item.Bounds.Width * item.Bounds.Height);
        var result = new List<ElementCandidate>();
        foreach (var candidate in candidates)
            if (result.Count == 0 || candidate.Bounds != result[^1].Bounds && candidate.Bounds.Contains(result[^1].Bounds))
                result.Add(candidate);
        return result;
    }
}
