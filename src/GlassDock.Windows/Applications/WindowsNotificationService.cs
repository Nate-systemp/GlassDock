using GlassDock.Core.Applications;

namespace GlassDock.Windows.Applications;

public sealed class WindowsNotificationService : WindowsToastBadgeProvider
{
    public IReadOnlyDictionary<string, int> Counts => Snapshot
        .Where(pair => pair.Value.Kind == BadgeKind.Count && pair.Value.Count > 0)
        .ToDictionary(pair => pair.Key, pair => pair.Value.Count, StringComparer.OrdinalIgnoreCase);
}
