using System.Globalization;

namespace GlassDock.Core.Applications;

public enum BadgeTransition { None, Appear, Increase, Update, Remove }

public sealed class NotificationBadgeState
{
    public int Count { get; private set; }
    public string Text => Count > 99 ? "99+" : Count.ToString(CultureInfo.CurrentCulture);
    public BadgeTransition SetCount(int value)
    {
        value = Math.Max(0, value);
        var previous = Count;
        Count = value;
        return value == previous ? BadgeTransition.None : value == 0 ? BadgeTransition.Remove :
            previous == 0 ? BadgeTransition.Appear : value > previous ? BadgeTransition.Increase : BadgeTransition.Update;
    }
}

public static class NotificationCounts
{
    // Exact AUMID mapping only. Display names and process titles are not identities.
    public static int ForApplication(ApplicationIdentity identity, IReadOnlyDictionary<string, int> counts) =>
        identity.AppUserModelId is { Length: > 0 } id && counts.TryGetValue(id, out var count) ? Math.Max(0, count) : 0;
}
