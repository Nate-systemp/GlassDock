using System.Globalization;

namespace GlassDock.Core.Applications;

public enum BadgeTransition { None, Appear, Increase, Update, Remove }

public sealed class NotificationBadgeState
{
    public BadgeDisplayState Display { get; private set; } = BadgeDisplayState.None;
    public int Count => Display.Kind == BadgeKind.Count ? Display.Count : 0;
    public bool HasActivity => Display.Kind == BadgeKind.Activity;
    public string Text => Display.Kind == BadgeKind.Count
        ? (Count > 99 ? "99+" : Count.ToString(CultureInfo.CurrentCulture))
        : string.Empty;

    public BadgeTransition SetDisplay(BadgeDisplayState value)
    {
        value = Normalize(value);
        var previous = Display;
        Display = value;

        if (previous.Kind == value.Kind && previous.Count == value.Count)
            return BadgeTransition.None;

        if (!value.IsVisible)
            return previous.IsVisible ? BadgeTransition.Remove : BadgeTransition.None;
        if (!previous.IsVisible)
            return BadgeTransition.Appear;
        if (previous.Kind == BadgeKind.Count && value.Kind == BadgeKind.Count && value.Count > previous.Count)
            return BadgeTransition.Increase;
        return BadgeTransition.Update;
    }

    public BadgeTransition SetCount(int value) =>
        SetDisplay(BadgeDisplayState.Counted(Math.Max(0, value), "legacy-count"));

    private static BadgeDisplayState Normalize(BadgeDisplayState value) => value.Kind switch
    {
        BadgeKind.Count when value.Count > 0 => value with { Count = Math.Max(1, value.Count) },
        BadgeKind.Activity => value with { Count = 0 },
        _ => BadgeDisplayState.None
    };
}

public static class NotificationCounts
{
    public static int ForApplication(ApplicationIdentity identity, IReadOnlyDictionary<string, int> counts) =>
        identity.AppUserModelId is { Length: > 0 } id && counts.TryGetValue(id, out var count) ? Math.Max(0, count) : 0;
}
