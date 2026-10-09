namespace GlassDock.Core.Desktop;

public sealed record InputSignal(bool Launcher, uint Revision, long Timestamp);

/// <summary>One wakeup per gesture. Transport only: callers retarget immediately, never await animations.</summary>
public sealed class InputSignalMailbox
{
    private readonly object gate = new();
    private readonly Queue<InputSignal> pending = new();
    public bool Publish(InputSignal signal)
    {
        lock (gate)
        {
            // Bound stale transport data, without coalescing distinct recent key releases.
            while (pending.TryPeek(out var old) && signal.Timestamp - old.Timestamp > 500) pending.Dequeue();
            pending.Enqueue(signal);
            return true;
        }
    }
    public InputSignal? Take(long now)
    {
        lock (gate)
        {
            var value = pending.TryDequeue(out var next) ? next : null;
            return value is not null && now - value.Timestamp is >= 0 and <= 500 ? value : null;
        }
    }
    public void Clear() { lock (gate) pending.Clear(); }
}
