using System.Text.Json;
using GlassDock.Core.Applications;

namespace GlassDock.Windows.Applications;

/// <summary>GlassDock-only preferences. Imported Windows taskbar pins are never modified.</summary>
internal sealed class DockPinStore
{
    private readonly object gate = new();
    private readonly string path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GlassDock",
        "dock-pins.json");

    private sealed record SavedPin(ApplicationIdentity Identity, string Name, string Target);

    private sealed class Preferences
    {
        public List<SavedPin> Pins { get; init; } = [];
        public HashSet<string> Excluded { get; init; } = [];
        public List<string>? Order { get; init; }
    }

    private Preferences preferences = new();

    public DockPinStore()
    {
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

    public bool Reorder(
        IReadOnlyList<PinnedApplication> currentPins,
        IReadOnlyList<string> orderedIds)
    {
        lock (gate)
        {
            var currentIds = currentPins
                .Select(pin => pin.Identity.Key)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var requested = orderedIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (requested.Length != currentIds.Length)
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
                Order = requested.ToList()
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
