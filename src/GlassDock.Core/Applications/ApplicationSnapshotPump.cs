namespace GlassDock.Core.Applications;

/// <summary>Serializes UI application, retaining only the latest discovery snapshot.
/// enqueue must post asynchronously to the owning UI thread.</summary>
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
                // Keep scheduled=true throughout apply: nested Windows/XAML
                // message pumping must not enter another collection mutation.
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
