namespace GlassDock.Core.Desktop;

/// <summary>One queued consumer; content invalidation survives coalesced frame notifications.</summary>
public sealed class CaptureDrawGate
{
    private int queued, invalidated;
    public bool Request(bool visualChanged)
    {
        if (visualChanged) Interlocked.Exchange(ref invalidated, 1);
        return Interlocked.Exchange(ref queued, 1) == 0;
    }
    public bool Begin()
    {
        Interlocked.Exchange(ref queued, 0);
        return Interlocked.Exchange(ref invalidated, 0) != 0;
    }
    public void EnqueueFailed() => Interlocked.Exchange(ref queued, 0);
}
