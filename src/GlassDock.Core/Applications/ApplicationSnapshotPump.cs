namespace GlassDock.Core.Applications;

public sealed class ApplicationSnapshotPump(Func<Action, bool> enqueue, Action<ApplicationSnapshot> apply) : IDisposable
{
    private readonly object gate = new();
    private ApplicationSnapshot? latest, pending;
    private bool scheduled, disposed;

    public void Publish(ApplicationSnapshot snapshot)
    {
        lock (gate)
        {
            if (disposed) return;
            latest = pending = snapshot;
            Schedule();
        }
    }

    public void Reapply()
    {
        lock (gate)
        {
            if (disposed) return;
            pending = latest;
            Schedule();
        }
    }

    private void Schedule()
    {
        if (disposed || scheduled || pending is null) return;
        scheduled = true;
        if (!enqueue(Drain)) scheduled = false;
    }

    private void Drain()
    {
        ApplicationSnapshot? snapshot;
        lock (gate)
        {
            snapshot = disposed ? null : pending;
            pending = null;
        }
        try { if (snapshot is not null) apply(snapshot); }
        finally
        {
            lock (gate)
            {
                scheduled = false;
                Schedule();
            }
        }
    }

    public void Dispose()
    {
        lock (gate) { disposed = true; latest = pending = null; }
    }
}
