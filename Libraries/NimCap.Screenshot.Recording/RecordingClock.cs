using NimCap.Screenshot.Models;
using System.Diagnostics;

namespace NimCap.Screenshot.Services;

internal sealed class RecordingClock(Func<TimeSpan>? now = null)
{
    private readonly Func<TimeSpan> _now = now ?? (() => Stopwatch.GetElapsedTime(0));
    private TimeSpan _start, _accumulated;
    public RecordingState State { get; private set; } = RecordingState.Ready;
    public TimeSpan Elapsed => _accumulated + (State == RecordingState.Recording ? _now() - _start : TimeSpan.Zero);
    public void Start() { if (State != RecordingState.Ready) throw new InvalidOperationException(); _start = _now(); State = RecordingState.Recording; }
    public void Pause() { if (State != RecordingState.Recording) throw new InvalidOperationException(); _accumulated = Elapsed; State = RecordingState.Paused; }
    public void Resume() { if (State != RecordingState.Paused) throw new InvalidOperationException(); _start = _now(); State = RecordingState.Recording; }
    public void Stop() { _accumulated = Elapsed; State = RecordingState.Stopping; }
}
