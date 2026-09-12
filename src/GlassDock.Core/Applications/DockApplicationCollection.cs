namespace GlassDock.Core.Applications;

public static class DockApplicationCollection
{
    public static IReadOnlyList<DockApplication> Combine(IEnumerable<PinnedApplication> pinned,
        IEnumerable<ApplicationWindow> running)
    {
        var pins = pinned.DistinctBy(pin => pin.Identity.Key, StringComparer.Ordinal).ToArray();
        var windows = running.DistinctBy(window => window.Handle).ToArray();
        var explicitIdentities = pins.Select(pin => pin.Identity).Concat(windows.Select(window => window.Identity))
            .Where(identity => !string.IsNullOrEmpty(identity.AppUserModelId) && !string.IsNullOrEmpty(identity.ExecutablePath))
            .GroupBy(identity => ApplicationIdentity.NormalizePath(identity.ExecutablePath!))
            .ToDictionary(group => group.Key, group => group.DistinctBy(identity => identity.Key).ToArray());
        var groups = new Dictionary<string, List<ApplicationWindow>>(StringComparer.Ordinal);
        foreach (var window in windows)
        {
            var exact = pins.FirstOrDefault(pin => pin.Identity.Key == window.Identity.Key);
            var match = exact;
            var path = ApplicationIdentity.NormalizePath(window.Identity.ExecutablePath ?? "");
            explicitIdentities.TryGetValue(path, out var appIds);
            if (match is null && !string.IsNullOrEmpty(window.Identity.ExecutablePath))
            {
                var candidates = pins.Where(pin => !string.IsNullOrEmpty(pin.Identity.ExecutablePath) &&
                    ApplicationIdentity.NormalizePath(pin.Identity.ExecutablePath) == ApplicationIdentity.NormalizePath(window.Identity.ExecutablePath)).ToArray();
                // Never collapse two explicit, different app IDs or guess between browser profiles.
                if (candidates.Length == 1 && (appIds?.Length ?? 0) <= 1 && (string.IsNullOrEmpty(window.Identity.AppUserModelId) ||
                    string.IsNullOrEmpty(candidates[0].Identity.AppUserModelId))) match = candidates[0];
            }
            var key = match?.Identity.Key ?? window.Identity.Key;
            if (match is null && string.IsNullOrEmpty(window.Identity.AppUserModelId) && appIds?.Length == 1)
                key = appIds[0].Key;
            if (!groups.TryGetValue(key, out var group)) groups[key] = group = [];
            group.Add(window);
        }
        var result = new List<DockApplication>();
        foreach (var pin in pins)
        {
            groups.Remove(pin.Identity.Key, out var group);
            result.Add(new(pin.Identity.Key, pin.Identity, pin.Name, pin.LaunchTarget, true,
                OrderWindows(group ?? []), pin.Icon ?? group?.FirstOrDefault()?.Icon));
        }
        foreach (var (key, group) in groups.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var ordered = OrderWindows(group);
            var first = ordered[0];
            result.Add(new(key, first.Identity, first.Name, null, false, ordered, first.Icon));
        }
        return result;
    }

    private static ApplicationWindow[] OrderWindows(IEnumerable<ApplicationWindow> windows) => windows
        .OrderByDescending(window => window.IsActive).ThenBy(window => window.ProcessStartTicks)
        .ThenBy(window => window.Handle).ToArray();
}
