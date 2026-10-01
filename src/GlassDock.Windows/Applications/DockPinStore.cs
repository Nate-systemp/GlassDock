using System.Text.Json;
using GlassDock.Core.Applications;

namespace GlassDock.Windows.Applications;

/// <summary>GlassDock-only preferences. Imported Windows taskbar pins are never modified.</summary>
internal sealed class DockPinStore
{
    private readonly object gate = new();
    private readonly string path = Settings.DokyUserData.PinsPath;
    private readonly ShellApplicationIdentityResolver identityResolver = new();

    private sealed record SavedPin(ApplicationIdentity Identity, string Name, string Target);

    private sealed class Preferences
    {
        public List<SavedPin> Pins { get; init; } = [];
        public HashSet<string> Excluded { get; init; } = [];
        public List<string>? Order { get; init; }
    }

    private Preferences preferences = new();

    public DockPinStore(string? preferencesPath = null)
    {
        if (preferencesPath is not null) path = preferencesPath;
        try
        {
            if (!File.Exists(path))
                return;

            if (JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) is not { } saved)
                return;

            preferences = new Preferences
            {
                Pins = (saved.Pins ?? [])
                    .Where(pin =>
                        pin is { Identity: not null } &&
                        !string.IsNullOrWhiteSpace(pin.Target) &&
                        !string.IsNullOrWhiteSpace(pin.Name))
                    .DistinctBy(pin => pin.Identity.Key, StringComparer.Ordinal)
                    .ToList(),

                Excluded = saved.Excluded is null
                    ? new HashSet<string>(StringComparer.Ordinal)
                    : new HashSet<string>(saved.Excluded, StringComparer.Ordinal),

                Order = saved.Order?
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList()
            };
        }
        catch (Exception error) when (
            error is IOException or
            UnauthorizedAccessException or
            JsonException)
        {
        }
    }

    public IReadOnlyList<PinnedApplication> Apply(
        IReadOnlyList<PinnedApplication> imported,
        WindowsApplicationIconService icons)
    {
        lock (gate)
        {
            var normalized = NormalizeShortcutPins(preferences);
            if (!ReferenceEquals(normalized, preferences))
            {
                // Canonicalize only Doky's own saved shortcut identity. The exact
                // launch target remains unchanged, so custom arguments/profile
                // behavior is preserved. Persist best-effort so pin/unpin/order
                // operations use the canonical identity on later launches too.
                if (!Save(normalized))
                    preferences = normalized;
            }

            var result = imported
                .Where(pin => !preferences.Excluded.Contains(pin.Identity.Key))
                .ToList();

            foreach (var pin in preferences.Pins)
            {
                if (!result.Any(item => item.Identity.Key == pin.Identity.Key))
                {
                    result.Add(new(
                        pin.Identity,
                        pin.Name,
                        pin.Target,
                        icons.FromShell(pin.Identity.Key, pin.Target)));
                }
            }

            if (preferences.Order is not { Count: > 0 } order)
                return result;

            var orderIndex = order
                .Select((id, index) => (id, index))
                .ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);

            return result
                .Select((pin, originalIndex) => new
                {
                    Pin = pin,
                    OriginalIndex = originalIndex,
                    SavedIndex = orderIndex.GetValueOrDefault(
                        pin.Identity.Key,
                        int.MaxValue)
                })
                .OrderBy(item => item.SavedIndex)
                .ThenBy(item => item.OriginalIndex)
                .Select(item => item.Pin)
                .ToArray();
        }
    }

    private Preferences NormalizeShortcutPins(Preferences source)
    {
        Dictionary<string, string>? remap = null;
        var pins = new List<SavedPin>(source.Pins.Count);

        foreach (var pin in source.Pins)
        {
            var next = pin;
            if (string.IsNullOrWhiteSpace(pin.Identity.AppUserModelId) &&
                pin.Target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) &&
                identityResolver.ResolveShortcut(pin.Target) is { } canonical &&
                !string.Equals(canonical.Key, pin.Identity.Key, StringComparison.Ordinal))
            {
                next = pin with { Identity = canonical };
                (remap ??= new(StringComparer.Ordinal))[pin.Identity.Key] = canonical.Key;
            }

            // If two saved shortcuts resolve to the same application identity,
            // keep the first launch target/order entry instead of creating a
            // duplicate dock icon.
            if (!pins.Any(existing =>
                    string.Equals(existing.Identity.Key, next.Identity.Key, StringComparison.Ordinal)))
                pins.Add(next);
        }

        if (remap is null)
            return source;

        string Map(string id) => remap.TryGetValue(id, out var mapped) ? mapped : id;

        return new Preferences
        {
            Pins = pins,
            Excluded = source.Excluded
                .Select(Map)
                .ToHashSet(StringComparer.Ordinal),
            Order = source.Order?
                .Select(Map)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList()
        };
    }

    public bool Set(DockApplication application, bool pinned)
    {
        lock (gate)
        {
            var target = WindowsApplicationLauncher.Target(application);
            if (pinned && target is null)
                return false;

            var pins = preferences.Pins
                .Where(pin => pin.Identity.Key != application.Id)
                .ToList();

            var excluded = new HashSet<string>(
                preferences.Excluded,
                StringComparer.Ordinal);

            List<string>? order = preferences.Order is null
                ? null
                : preferences.Order
                    .Where(id => id != application.Id)
                    .ToList();

            if (pinned)
            {
                excluded.Remove(application.Id);
                pins.Add(new(
                    application.Identity,
                    application.Name,
                    target!));

                // Once a custom order exists, newly pinned apps belong at its end.
                order?.Add(application.Id);
            }
            else
            {
                excluded.Add(application.Id);
            }

            return Save(new Preferences
            {
                Pins = pins,
                Excluded = excluded,
                Order = order
            });
        }
    }

    public bool AddExternalTarget(string path)
    {
        lock (gate)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            path = Path.GetFullPath(path);

            if (!File.Exists(path) && !Directory.Exists(path))
                return false;

            // Keep the shell target exact (including .lnk files and folders).
            // The identity is GlassDock-local; LaunchTarget remains the authoritative
            // ShellExecute target.
            var identity = new ApplicationIdentity(null, path);
            var id = identity.Key;

            var name = Directory.Exists(path)
                ? (Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } folderName
                    ? folderName
                    : path)
                : Path.GetFileNameWithoutExtension(path);

            if (string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var description = System.Diagnostics.FileVersionInfo
                        .GetVersionInfo(path)
                        .FileDescription;

                    if (!string.IsNullOrWhiteSpace(description))
                        name = description;
                }
                catch
                {
                    // Filename remains a safe display name.
                }
            }

            if (string.IsNullOrWhiteSpace(name))
                name = path;

            var pins = preferences.Pins
                .Where(pin => pin.Identity.Key != id)
                .ToList();

            pins.Add(new(identity, name, path));

            var excluded = new HashSet<string>(
                preferences.Excluded,
                StringComparer.Ordinal);

            excluded.Remove(id);

            var order = preferences.Order?.Where(existing => existing != id).ToList();

            // If a custom order already exists, append the new item. If there is
            // no custom order yet, Apply() naturally appends GlassDock-only pins.
            order?.Add(id);

            return Save(new Preferences
            {
                Pins = pins,
                Excluded = excluded,
                Order = order
            });
        }
    }

    public IReadOnlyList<DockApplication> ApplyOrder(IReadOnlyList<DockApplication> applications)
    {
        lock (gate)
        {
            if (preferences.Order is not { Count: > 0 } order) return applications;
            var indices = order.Select((id, index) => (id, index))
                .ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);
            return applications.OrderBy(app => indices.GetValueOrDefault(app.Id, int.MaxValue)).ToArray();
        }
    }

    public bool Reorder(
        IReadOnlyList<string> currentIds,
        IReadOnlyList<string> orderedIds)
    {
        lock (gate)
        {
            var requested = orderedIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (requested.Length != currentIds.Count)
                return false;

            var currentSet = currentIds.ToHashSet(StringComparer.Ordinal);
            if (requested.Any(id => !currentSet.Contains(id)))
                return false;

            return Save(new Preferences
            {
                Pins = preferences.Pins.ToList(),
                Excluded = new HashSet<string>(
                    preferences.Excluded,
                    StringComparer.Ordinal),
                // Keep identities of temporarily absent apps without pinning them.
                Order = requested.Concat(preferences.Order ?? [])
                    .Distinct(StringComparer.Ordinal).ToList()
            });
        }
    }

    private bool Save(Preferences next)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var temporary = path + ".tmp";
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(next));

            File.Move(
                temporary,
                path,
                true);

            preferences = next;
            return true;
        }
        catch (Exception error) when (
            error is IOException or
            UnauthorizedAccessException)
        {
            return false;
        }
    }
}
