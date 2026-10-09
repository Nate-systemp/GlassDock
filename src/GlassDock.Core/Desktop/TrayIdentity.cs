namespace GlassDock.Core.Desktop;

public static class TrayIdentity
{
    public static bool SameExecutable(string expected, string? actual) =>
        !string.IsNullOrWhiteSpace(actual) &&
        string.Equals(expected.Replace('/', '\\').Trim(), actual.Replace('/', '\\').Trim(), StringComparison.OrdinalIgnoreCase);

    public static string Key(bool shortcut, string name, string? path) =>
        shortcut ? "app:" + (path ?? "").Replace('/', '\\').Trim().ToUpperInvariant()
                 : "live:" + name.Trim().ToUpperInvariant();
}

/// <summary>No provider-specific double-click contract: absorb the second click.</summary>
public sealed class TrayActivationGate
{
    private long? lastAccepted;
    public bool TryAccept(long now, uint doubleClickMilliseconds)
    {
        if (lastAccepted is { } last && now - last <= doubleClickMilliseconds) return false;
        lastAccepted = now;
        return true;
    }
}

/// <summary>Retains absent items so an app returning later keeps its saved slot.</summary>
public sealed class TrayOrder
{
    private readonly List<string> keys;
    public TrayOrder(IEnumerable<string>? saved = null) => keys = (saved ?? [])
        .Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase).Take(512).ToList();
    public string[] Snapshot() => keys.ToArray();
    public string[] Apply(IEnumerable<string> available)
    {
        var present = available.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return keys.Where(k => present.Contains(k, StringComparer.OrdinalIgnoreCase))
            .Concat(present.Where(k => !keys.Contains(k, StringComparer.OrdinalIgnoreCase))).ToArray();
    }
    public bool Move(string source, string target, bool after, IEnumerable<string> available)
    {
        var visible = Apply(available).ToList();
        if (source == target || !visible.Contains(source) || !visible.Contains(target)) return false;
        visible.Remove(source);
        visible.Insert(visible.IndexOf(target) + (after ? 1 : 0), source);
        var missing = keys.Where(k => !visible.Contains(k, StringComparer.OrdinalIgnoreCase)).ToArray();
        keys.Clear(); keys.AddRange(visible); keys.AddRange(missing);
        return true;
    }
}
