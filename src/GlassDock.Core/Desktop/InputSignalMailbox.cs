namespace GlassDock.Core.Desktop;

public sealed record InputSignal(bool Launcher, uint Revision, long Timestamp);

/// <summary>One outstanding UI message, with latest intent only. No delayed replay after a UI stall.</summary>
public sealed class InputSignalMailbox
{
    private readonly object gate = new();
    private InputSignal? latest;
    private bool posted;
    public bool Publish(InputSignal signal)
    {
        lock (gate)
        {
            if (latest == signal) return false;
            latest = signal;
            if (posted) return false;
            return posted = true;
        }
    }
    public InputSignal? Take(long now)
    {
        lock (gate)
        {
            var value = latest;
            latest = null;
            posted = false;
            return value is not null && now - value.Timestamp is >= 0 and <= 500 ? value : null;
        }
    }
}
