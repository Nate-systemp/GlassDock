namespace GlassDock.Core.Applications;

public enum BadgeKind
{
    None,
    Activity,
    Count
}

/// <summary>
/// Provider-owned badge signal keyed by canonical AppUserModelId. A signal never contains
/// notification text or other notification content.
/// </summary>
public readonly record struct BadgeSignal(BadgeKind Kind, int Count, DateTimeOffset UpdatedAt)
{
    public bool IsVisible => Kind != BadgeKind.None;

    public static BadgeSignal Activity(DateTimeOffset? updatedAt = null) =>
        new(BadgeKind.Activity, 0, updatedAt ?? DateTimeOffset.UtcNow);

    public static BadgeSignal Counted(int count, DateTimeOffset? updatedAt = null) =>
        count > 0
            ? new(BadgeKind.Count, count, updatedAt ?? DateTimeOffset.UtcNow)
            : new(BadgeKind.None, 0, updatedAt ?? DateTimeOffset.UtcNow);
}

/// <summary>The single presentation consumed by Doky's icon UI after provider arbitration.</summary>
public readonly record struct BadgeDisplayState(
    BadgeKind Kind,
    int Count,
    string? SourceId,
    int SourcePriority,
    DateTimeOffset UpdatedAt)
{
    public static BadgeDisplayState None => new(BadgeKind.None, 0, null, int.MinValue, DateTimeOffset.MinValue);
    public bool IsVisible => Kind != BadgeKind.None;
    public bool HasExactCount => Kind == BadgeKind.Count && Count > 0;
    public string Text => HasExactCount ? (Count > 99 ? "99+" : Count.ToString()) : string.Empty;
    public string AccessibilityText => Kind switch
    {
        BadgeKind.Count when Count == 1 => "1 notification",
        BadgeKind.Count => $"{Count} notifications",
        BadgeKind.Activity => "Unread activity",
        _ => string.Empty
    };

    public static BadgeDisplayState Counted(
        int count,
        string? sourceId = null,
        int sourcePriority = 0,
        DateTimeOffset? updatedAt = null) =>
        count > 0
            ? new(BadgeKind.Count, count, sourceId, sourcePriority, updatedAt ?? DateTimeOffset.UtcNow)
            : None;

    public static BadgeDisplayState Activity(
        string? sourceId = null,
        int sourcePriority = 0,
        DateTimeOffset? updatedAt = null) =>
        new(BadgeKind.Activity, 0, sourceId, sourcePriority, updatedAt ?? DateTimeOffset.UtcNow);
}

/// <summary>
/// Badge providers expose only app identity metadata plus a dot/count signal. They must not
/// expose notification body text through this contract.
/// </summary>
public interface IBadgeProvider : IDisposable
{
    string Id { get; }
    int Priority { get; }
    string Status { get; }
    IReadOnlyDictionary<string, BadgeSignal> Snapshot { get; }
    event EventHandler? Changed;
    event EventHandler? RefreshRequested;
    Task StartAsync(bool requestPermission = false);
    Task RefreshAsync();
    void Stop();
}

public static class BadgeProviderPriority
{
    // Future providers with a trustworthy native unread count should use a higher priority.
    public const int WindowsToast = 100;
    public const int ReliableNative = 300;
}

public sealed record BadgeProviderDiagnosticEntry(
    string ProviderId,
    int ProviderPriority,
    string AppUserModelId,
    BadgeKind Kind,
    int Count,
    DateTimeOffset UpdatedAt);

public sealed record BadgeApplicationDiagnosticEntry(
    string ApplicationKey,
    string? AppUserModelId,
    BadgeKind Kind,
    int Count,
    string? SelectedSourceId,
    int SelectedSourcePriority,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Single source of truth for badge presentation. Providers are never summed: for a given
/// canonical AppUserModelId the strongest trustworthy provider wins, preventing duplicate
/// sources from double-counting the same unread state.
/// </summary>
public sealed class BadgeCoordinator : IDisposable
{
    private readonly IReadOnlyList<IBadgeProvider> providers;
    private bool disposed;

    public BadgeCoordinator(IEnumerable<IBadgeProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        this.providers = providers.ToArray();

        var duplicate = this.providers
            .GroupBy(provider => provider.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Duplicate badge provider id '{duplicate.Key}'.", nameof(providers));

        foreach (var provider in this.providers)
        {
            provider.Changed += ProviderChanged;
            provider.RefreshRequested += ProviderRefreshRequested;
        }
    }

    public event EventHandler? Changed;
    public event EventHandler? RefreshRequested;

    public string Status
    {
        get
        {
            var statuses = providers
                .Select(provider => provider.Status)
                .Where(status => !string.IsNullOrWhiteSpace(status))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return statuses.Length == 0 ? "Notification badges are unavailable." : string.Join(" ", statuses);
        }
    }

    public BadgeDisplayState ForApplication(ApplicationIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var appId = identity.AppUserModelId;
        if (string.IsNullOrWhiteSpace(appId))
            return BadgeDisplayState.None;

        BadgeDisplayState selected = BadgeDisplayState.None;
        foreach (var provider in providers)
        {
            if (!provider.Snapshot.TryGetValue(appId, out var signal) || !signal.IsVisible)
                continue;

            var candidate = new BadgeDisplayState(
                signal.Kind,
                signal.Kind == BadgeKind.Count ? Math.Max(0, signal.Count) : 0,
                provider.Id,
                provider.Priority,
                signal.UpdatedAt);

            if (IsBetter(candidate, selected))
                selected = candidate;
        }

        return selected;
    }

    public async Task StartAsync(bool requestPermission = false)
    {
        ThrowIfDisposed();
        foreach (var provider in providers)
            await provider.StartAsync(requestPermission);
    }

    public async Task RefreshAsync()
    {
        ThrowIfDisposed();
        foreach (var provider in providers)
            await provider.RefreshAsync();
    }

    public void Stop()
    {
        if (disposed) return;
        foreach (var provider in providers)
            provider.Stop();
    }

    /// <summary>Metadata-only raw provider snapshot for diagnostics. No toast content is included.</summary>
    public IReadOnlyList<BadgeProviderDiagnosticEntry> GetProviderDiagnostics() =>
        providers
            .SelectMany(provider => provider.Snapshot.Select(pair => new BadgeProviderDiagnosticEntry(
                provider.Id,
                provider.Priority,
                pair.Key,
                pair.Value.Kind,
                pair.Value.Kind == BadgeKind.Count ? Math.Max(0, pair.Value.Count) : 0,
                pair.Value.UpdatedAt)))
            .OrderBy(entry => entry.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.AppUserModelId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>Metadata-only selected badge state for known Doky applications.</summary>
    public IReadOnlyList<BadgeApplicationDiagnosticEntry> GetApplicationDiagnostics(
        IEnumerable<ApplicationIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);
        return identities.Select(identity =>
        {
            var selected = ForApplication(identity);
            return new BadgeApplicationDiagnosticEntry(
                identity.Key,
                identity.AppUserModelId,
                selected.Kind,
                selected.Count,
                selected.SourceId,
                selected.SourcePriority,
                selected.UpdatedAt);
        }).ToArray();
    }

    private static bool IsBetter(BadgeDisplayState candidate, BadgeDisplayState current)
    {
        if (!current.IsVisible) return true;

        // A trustworthy exact count carries more information than activity-only state.
        // Provider priority arbitrates between signals of the same kind; it never turns a
        // known count into a less-informative dot.
        if (candidate.Kind != current.Kind)
            return candidate.Kind == BadgeKind.Count;

        if (candidate.SourcePriority != current.SourcePriority)
            return candidate.SourcePriority > current.SourcePriority;

        // Stable deterministic tie-breaker. Never add counts from multiple sources.
        return candidate.UpdatedAt > current.UpdatedAt;
    }

    private void ProviderChanged(object? sender, EventArgs e)
    {
        if (!disposed) Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ProviderRefreshRequested(object? sender, EventArgs e)
    {
        if (!disposed) RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ThrowIfDisposed()
    {
        if (disposed) throw new ObjectDisposedException(nameof(BadgeCoordinator));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var provider in providers)
        {
            provider.Changed -= ProviderChanged;
            provider.RefreshRequested -= ProviderRefreshRequested;
            provider.Dispose();
        }
        Changed = null;
        RefreshRequested = null;
    }
}
