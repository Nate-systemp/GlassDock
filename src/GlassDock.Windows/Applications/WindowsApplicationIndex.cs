using GlassDock.Core.Applications;

namespace GlassDock.Windows.Applications;

public sealed record ApplicationIndexSnapshot(IReadOnlyList<GlassSearchResult> Applications, bool IsIndexing, string? Warning);

/// <summary>One retained in-memory index, built on one STA worker. Never scans in response to typing.</summary>
public sealed class WindowsApplicationIndex : IDisposable
{
    private Thread? worker;
    private volatile bool stopping;
    private ApplicationIndexSnapshot snapshot = new([], true, null);
    public ApplicationIndexSnapshot Snapshot => Volatile.Read(ref snapshot);
    public event EventHandler? Changed;

    // Start and Dispose are owned by Home's UI thread.
    public void Start()
    {
        if (worker is not null || stopping) return;
        worker = new Thread(Build) { IsBackground = true, Name = "GlassDock installed application index" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    private void Build()
    {
        var entries = new Dictionary<string, GlassSearchResult>(StringComparer.OrdinalIgnoreCase);
        string? warning = null;
        try
        {
            ShellApplicationMetadata.ReadAvailable(entry =>
            {
                entries.TryAdd(entry.StableId, entry);
                if (entries.Count % 32 == 0) Publish(entries.Values, true, warning);
            }, () => stopping, message => warning = message);
            if (stopping) return;
            // Publish searchable metadata before potentially slow icon extraction.
            Publish(entries.Values, false, warning);
            var icons = new WindowsApplicationIconService();
            var pending = 0;
            foreach (var entry in entries.Values.ToArray())
            {
                if (stopping) return;
                try { entries[entry.StableId] = entry with { Icon = icons.FromShell(entry.StableId, entry.LaunchTarget) }; }
                catch (Exception error) when (ShellApplicationMetadata.IsDiscoveryError(error)) { }
                if (++pending % 16 == 0) Publish(entries.Values, false, warning);
            }
        }
        catch (Exception error) when (ShellApplicationMetadata.IsDiscoveryError(error))
        {
            warning = "Application indexing was incomplete. Restart GlassDock to retry.";
        }
        finally { if (!stopping) Publish(entries.Values, false, warning); }
    }

    private void Publish(IEnumerable<GlassSearchResult> entries, bool indexing, string? warning)
    {
        if (stopping) return;
        Volatile.Write(ref snapshot, new(Array.AsReadOnly(entries.ToArray()), indexing, warning));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        stopping = true;
        Changed = null;
        // Never block shutdown on a third-party Shell extension. The background worker owns and releases COM resources.
    }
}
