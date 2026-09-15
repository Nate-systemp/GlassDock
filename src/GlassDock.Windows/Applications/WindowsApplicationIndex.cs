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

        // Shell discovery can expose the same application more than once
        // (for example from per-user and all-users Start Menu locations).
        // StableId is source-specific, so StableId alone is not enough to
        // prevent duplicate visible applications.
        var deduplicated = DeduplicateApplications(entries);

        Volatile.Write(
            ref snapshot,
            new(
                Array.AsReadOnly(deduplicated),
                indexing,
                warning));

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static GlassSearchResult[] DeduplicateApplications(
        IEnumerable<GlassSearchResult> entries)
    {
        return entries
            .GroupBy(
                entry => NormalizeTitle(entry.Title),
                StringComparer.OrdinalIgnoreCase)
            .Select(MergeDuplicateGroup)
            .OrderBy(
                entry => entry.Title,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                entry => entry.StableId,
                StringComparer.Ordinal)
            .ToArray();
    }

    private static GlassSearchResult MergeDuplicateGroup(
        IGrouping<string, GlassSearchResult> group)
    {
        var candidates = group.ToArray();

        // Keep one deterministic "main" launch entry. Prefer a canonical
        // AppsFolder item, then a Start Menu shortcut, then a direct exe.
        // An entry that already has an extracted icon gets a small bonus.
        var preferred = candidates
            .OrderByDescending(EntryQuality)
            .ThenBy(
                entry => entry.StableId,
                StringComparer.Ordinal)
            .First();

        // Preserve useful aliases/keywords that may have come from another
        // duplicate source so deduplication does not make search worse.
        var keywords = candidates
            .SelectMany(entry => entry.Keywords)
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var icon =
            preferred.Icon ??
            candidates
                .Select(entry => entry.Icon)
                .FirstOrDefault(candidate => candidate is not null);

        return preferred with
        {
            Keywords = keywords,
            Icon = icon
        };
    }

    private static int EntryQuality(
        GlassSearchResult entry)
    {
        var target =
            entry.LaunchTarget?.Trim() ??
            string.Empty;

        var score = 0;

        if (target.StartsWith(
                "shell:AppsFolder\\",
                StringComparison.OrdinalIgnoreCase))
        {
            score += 300;
        }
        else if (target.EndsWith(
                     ".lnk",
                     StringComparison.OrdinalIgnoreCase))
        {
            score += 200;
        }
        else if (target.EndsWith(
                     ".exe",
                     StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        if (entry.Icon is not null)
            score += 10;

        return score;
    }

    private static string NormalizeTitle(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(
            " ",
            value.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
    }

    public void Dispose()
    {
        stopping = true;
        Changed = null;
        // Never block shutdown on a third-party Shell extension. The background worker owns and releases COM resources.
    }
}
