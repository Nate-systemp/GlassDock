using System.Runtime.InteropServices;
using GlassDock.Core.Applications;

namespace GlassDock.Windows.Applications;

public sealed record ApplicationIndexSnapshot(
    IReadOnlyList<GlassSearchResult> Applications,
    bool IsIndexing,
    string? Warning);

/// <summary>
/// Retained metadata index for Glass Home.
///
/// Important memory policy:
/// - app metadata is indexed only when Start() is requested;
/// - icons are NOT extracted while building the installed-app index;
/// - only the currently requested search-result icons are loaded;
/// - the icon request queue and retained icon cache are both bounded.
/// </summary>
public sealed class WindowsApplicationIndex : IDisposable
{
    private const int MetadataPublishBatch = 64;
    private const int MaxRequestedIcons = 8;
    private const int MaxRetainedIcons = 24;

    private Thread? worker;
    private Thread? iconWorker;
    private volatile bool stopping;

    private ApplicationIndexSnapshot snapshot = new([], false, null);
    public ApplicationIndexSnapshot Snapshot => Volatile.Read(ref snapshot);

    public event EventHandler? Changed;

    private readonly object iconGate = new();
    private readonly AutoResetEvent iconSignal = new(false);
    private readonly Queue<GlassSearchResult> iconRequests = new();
    private readonly HashSet<string> queuedIcons =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, ApplicationIcon> loadedIcons =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Queue<string> loadedIconOrder = new();

    // Home only displays 32px search icons. A 64px source is already ample for
    // normal/high DPI rendering and is dramatically smaller than 256px RGBA.
    private readonly WindowsApplicationIconService iconService =
        new(
            targetIconSize: 64,
            cacheCapacity: MaxRetainedIcons,
            allowLargerIcons: false);

    // Start and Dispose are owned by Home's UI thread.
    public void Start()
    {
        if (worker is not null || stopping)
            return;

        Volatile.Write(
            ref snapshot,
            Snapshot with { IsIndexing = true });

        worker = new Thread(Build)
        {
            IsBackground = true,
            Name = "GlassDock installed application index"
        };

        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    private void Build()
    {
        var entries =
            new Dictionary<string, GlassSearchResult>(
                StringComparer.OrdinalIgnoreCase);

        string? warning = null;

        try
        {
            ShellApplicationMetadata.ReadAvailable(
                entry =>
                {
                    if (stopping)
                        return;

                    // Do not keep Shell-extracted icon payloads in the metadata
                    // index even if a discovery source happens to provide one.
                    entries.TryAdd(
                        entry.StableId,
                        entry with { Icon = null });

                    if (entries.Count % MetadataPublishBatch == 0)
                        Publish(entries.Values, true, warning);
                },
                () => stopping,
                message => warning = message);

            if (stopping)
                return;
        }
        catch (Exception error)
            when (ShellApplicationMetadata.IsDiscoveryError(error))
        {
            warning =
                "Application indexing was incomplete. Restart Doky to retry.";
        }
        finally
        {
            if (!stopping)
                Publish(entries.Values, false, warning);
        }
    }

    /// <summary>
    /// Returns a small lazily-loaded Home search icon, if one is currently cached.
    /// </summary>
    public ApplicationIcon? GetIcon(string stableId)
    {
        lock (iconGate)
        {
            return loadedIcons.TryGetValue(stableId, out var icon)
                ? icon
                : null;
        }
    }

    /// <summary>
    /// Replaces the pending icon queue with the current visible result set.
    /// Old queries therefore cannot build an ever-growing backlog.
    /// </summary>
    public void RequestIcons(IEnumerable<GlassSearchResult> results)
    {
        if (stopping)
            return;

        EnsureIconWorker();

        lock (iconGate)
        {
            iconRequests.Clear();
            queuedIcons.Clear();

            foreach (var result in results.Take(MaxRequestedIcons))
            {
                if (result.ResultType != GlassSearchResultType.Application ||
                    string.IsNullOrWhiteSpace(result.StableId) ||
                    loadedIcons.ContainsKey(result.StableId) ||
                    !queuedIcons.Add(result.StableId))
                {
                    continue;
                }

                iconRequests.Enqueue(
                    result with { Icon = null });
            }
        }

        iconSignal.Set();
    }

    private void EnsureIconWorker()
    {
        if (iconWorker is not null || stopping)
            return;

        lock (iconGate)
        {
            if (iconWorker is not null || stopping)
                return;

            iconWorker = new Thread(LoadRequestedIcons)
            {
                IsBackground = true,
                Name = "GlassDock Home icon loader"
            };

            iconWorker.SetApartmentState(ApartmentState.STA);
            iconWorker.Start();
        }
    }

    private void LoadRequestedIcons()
    {
        try
        {
            while (!stopping)
            {
                iconSignal.WaitOne();

                if (stopping)
                    break;

                var changed = false;

                while (!stopping)
                {
                    GlassSearchResult? request;

                    lock (iconGate)
                    {
                        if (iconRequests.Count == 0)
                            break;

                        request = iconRequests.Dequeue();
                        queuedIcons.Remove(request.StableId);

                        if (loadedIcons.ContainsKey(request.StableId))
                            continue;
                    }

                    ApplicationIcon? icon = null;

                    try
                    {
                        icon = iconService.FromShell(
                            request.StableId,
                            request.LaunchTarget);
                    }
                    catch (Exception error)
                        when (ShellApplicationMetadata.IsDiscoveryError(error) ||
                              error is COMException ||
                              error is InvalidOperationException)
                    {
                        // A missing/uncooperative Shell icon must never break Home.
                    }

                    if (icon is null)
                        continue;

                    lock (iconGate)
                    {
                        if (stopping)
                            break;

                        if (!loadedIcons.ContainsKey(request.StableId))
                        {
                            loadedIcons[request.StableId] = icon;
                            loadedIconOrder.Enqueue(request.StableId);
                            TrimLoadedIcons();
                            changed = true;
                        }
                    }
                }

                // Publish once per drained batch, not once per icon.
                if (changed && !stopping)
                    Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            iconSignal.Dispose();
        }
    }

    private void TrimLoadedIcons()
    {
        while (loadedIcons.Count > MaxRetainedIcons &&
               loadedIconOrder.Count > 0)
        {
            var oldest = loadedIconOrder.Dequeue();
            loadedIcons.Remove(oldest);
        }
    }

    private void Publish(
        IEnumerable<GlassSearchResult> entries,
        bool indexing,
        string? warning)
    {
        if (stopping)
            return;

        // Shell discovery can expose the same application more than once
        // (for example from per-user and all-users Start Menu locations).
        var deduplicated =
            DeduplicateApplications(entries);

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
        var preferred = candidates
            .OrderByDescending(EntryQuality)
            .ThenBy(
                entry => entry.StableId,
                StringComparer.Ordinal)
            .First();

        var keywords = candidates
            .SelectMany(entry => entry.Keywords)
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return preferred with
        {
            Keywords = keywords,
            Icon = null
        };
    }

    private static int EntryQuality(
        GlassSearchResult entry)
    {
        var target =
            entry.LaunchTarget?.Trim() ??
            string.Empty;

        if (target.StartsWith(
                "shell:AppsFolder\\",
                StringComparison.OrdinalIgnoreCase))
        {
            return 300;
        }

        if (target.EndsWith(
                ".lnk",
                StringComparison.OrdinalIgnoreCase))
        {
            return 200;
        }

        if (target.EndsWith(
                ".exe",
                StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        return 0;
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
        if (stopping)
            return;

        stopping = true;
        Changed = null;

        lock (iconGate)
        {
            iconRequests.Clear();
            queuedIcons.Clear();
            loadedIcons.Clear();
            loadedIconOrder.Clear();
        }

        if (iconWorker is null)
            iconSignal.Dispose();
        else
            iconSignal.Set();

        // Do not block shutdown on third-party Shell extensions.
        // Background workers own and release their COM resources.
    }
}
