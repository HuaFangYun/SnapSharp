namespace NimCap.Screenshot.Services;

// Native event unsubscription does not join callbacks already in flight.
internal sealed class CaptureCallbackLifetime
{
    private readonly object _gate = new();
    private bool _closed;
    private int _active;

    internal bool TryEnter()
    {
        lock (_gate)
        {
            if (_closed) return false;
            _active++;
            return true;
        }
    }
    internal void Exit()
    {
        lock (_gate) { _active--; Monitor.PulseAll(_gate); }
    }
    internal void Close(Action unsubscribe)
    {
        lock (_gate) _closed = true;
        try { unsubscribe(); } // No lock may surround native unsubscription/teardown.
        finally { lock (_gate) { while (_active != 0) Monitor.Wait(_gate); } }
    }
}
