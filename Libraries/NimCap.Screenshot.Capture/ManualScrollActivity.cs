namespace NimCap.Screenshot.Services;

/// <summary>Event wakeup with a settling period for delayed and inertial scrolling.</summary>
public sealed class ManualScrollActivity
{
    private readonly object _sync = new();
    private long _lastInput, _stableSince;
    private bool _active, _stable;

    public bool Active { get { lock (_sync) return _active; } }

    public void Input(long milliseconds)
    {
        lock (_sync)
        {
            _active = true;
            _lastInput = milliseconds;
            _stable = false;
        }
    }

    public void Observe(bool changed, long milliseconds)
    {
        lock (_sync)
        {
            if (!_active) return;
            if (changed) { _stable = false; return; }
            if (!_stable) { _stable = true; _stableSince = milliseconds; }
            // A wheel notification precedes target-window painting. Do not go
            // idle on the initial unchanged frame before the app has responded.
            if (milliseconds - _lastInput >= 350 && milliseconds - _stableSince >= 160)
                _active = false;
        }
    }
}
