using System.Text.Json;
using GlassDock.Core.Applications;

namespace GlassDock.Windows.Applications;

/// <summary>GlassDock-only preferences. Imported Windows taskbar pins are never modified.</summary>
internal sealed class DockPinStore
{
    private readonly object gate = new();
    private readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassDock", "dock-pins.json");
    private sealed record SavedPin(ApplicationIdentity Identity, string Name, string Target);
    private sealed record Preferences(List<SavedPin> Pins, HashSet<string> Excluded);
    private Preferences preferences = new([], []);

    public DockPinStore()
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) is { Pins: not null, Excluded: not null } saved)
                preferences = saved with
                {
                    Pins = saved.Pins.Where(pin => pin is { Identity: not null } &&
                        !string.IsNullOrWhiteSpace(pin.Target) && !string.IsNullOrWhiteSpace(pin.Name)).ToList()
                };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
    }

    public IReadOnlyList<PinnedApplication> Apply(IReadOnlyList<PinnedApplication> imported, WindowsApplicationIconService icons)
    {
        lock (gate)
        {
            var result = imported.Where(pin => !preferences.Excluded.Contains(pin.Identity.Key)).ToList();
            foreach (var pin in preferences.Pins)
                if (!result.Any(item => item.Identity.Key == pin.Identity.Key))
                    result.Add(new(pin.Identity, pin.Name, pin.Target, icons.FromShell(pin.Identity.Key, pin.Target)));
            return result;
        }
    }

    public bool Set(DockApplication application, bool pinned)
    {
        lock (gate)
        {
            var target = WindowsApplicationLauncher.Target(application);
            if (pinned && target is null) return false;
            var next = new Preferences(preferences.Pins.Where(pin => pin.Identity.Key != application.Id).ToList(), new(preferences.Excluded));
            if (pinned)
            {
                next.Excluded.Remove(application.Id);
                next.Pins.Add(new(application.Identity, application.Name, target!));
            }
            else next.Excluded.Add(application.Id);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(next));
                File.Move(temporary, path, true);
                preferences = next;
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
        }
    }
}
