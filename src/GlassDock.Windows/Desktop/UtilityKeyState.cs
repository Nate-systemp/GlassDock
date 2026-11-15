using GlassDock.Core.Settings;

namespace GlassDock.Windows.Desktop;

internal sealed class UtilityKeyState(Action<UtilityKind, bool> changed)
{
    private readonly Dictionary<(nint Device, int Report), HashSet<ushort>> sources = [];
    internal static ushort KeyboardUsage(ushort key) => key switch { 0xAD => 0xE2, 0xAE => 0xEA, 0xAF => 0xE9, _ => 0 };
    private static UtilityKind Kind(ushort usage) => usage is 0x6F or 0x70 ? UtilityKind.Brightness : UtilityKind.Volume;
    private bool Held(UtilityKind kind) => sources.Values.Any(keys => keys.Any(key => Kind(key) == kind));

    internal void Keyboard(nint device, ushort key, bool down)
    {
        var usage = KeyboardUsage(key);
        if (usage == 0) return;
        var keys = sources.GetValueOrDefault((device, -1))?.ToHashSet() ?? [];
        if (down) keys.Add(usage);
        else keys.Remove(usage);
        Update(device, -1, keys);
    }

    internal void Update(nint device, int report, IEnumerable<ushort> usages)
    {
        var keys = usages.Where(key => key is 0x6F or 0x70 or 0xE2 or 0xE9 or 0xEA).ToHashSet();
        var previous = sources.GetValueOrDefault((device, report)) ?? [];
        var volumeHeld = Held(UtilityKind.Volume);
        var brightnessHeld = Held(UtilityKind.Brightness);
        sources[(device, report)] = keys;
        foreach (var kind in keys.Except(previous).Select(Kind).Distinct()) changed(kind, true);
        if (volumeHeld && !Held(UtilityKind.Volume)) changed(UtilityKind.Volume, false);
        if (brightnessHeld && !Held(UtilityKind.Brightness)) changed(UtilityKind.Brightness, false);
    }

    internal void Remove(nint device)
    {
        foreach (var source in sources.Keys.Where(key => key.Device == device).ToArray())
        {
            Update(source.Device, source.Report, []);
            sources.Remove(source);
        }
    }

    internal void Reset()
    {
        var volumeHeld = Held(UtilityKind.Volume);
        var brightnessHeld = Held(UtilityKind.Brightness);
        sources.Clear();
        if (volumeHeld) changed(UtilityKind.Volume, false);
        if (brightnessHeld) changed(UtilityKind.Brightness, false);
    }
}
