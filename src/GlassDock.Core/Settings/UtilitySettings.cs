namespace GlassDock.Core.Settings;

public enum UtilityKind { Wifi, Bluetooth, AirplaneMode, Volume, Brightness, Battery, BatterySaver, Focus, NightLight }
public enum UtilityTheme { Dark, Light }
public sealed record UtilityDefinition(UtilityKind Kind, string Name, string Icon, uint Accent, bool Available = false)
{
    public static IReadOnlyList<UtilityDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        new UtilityDefinition(UtilityKind.Wifi, "Wi-Fi", "wifi-high", 0xFF0A84FF, true),
        new UtilityDefinition(UtilityKind.Bluetooth, "Bluetooth", "bluetooth", 0xFF007AFF, true),
        new UtilityDefinition(UtilityKind.AirplaneMode, "Airplane mode (experimental)", "airplane", 0xFFFF9F0A, true),
        new UtilityDefinition(UtilityKind.Volume, "Sound", "speaker-high", 0xFF0A84FF, true),
        new UtilityDefinition(UtilityKind.Brightness, "Brightness", "sun", 0xFFFFB340, true),
        new UtilityDefinition(UtilityKind.Battery, "Battery", "battery-full", 0xFF30D158),
        new UtilityDefinition(UtilityKind.BatterySaver, "Energy Saver (experimental)", "battery-charging", 0xFF30D158, true),
        new UtilityDefinition(UtilityKind.Focus, "Do Not Disturb (experimental)", "moon", 0xFFAF52DE, true),
        new UtilityDefinition(UtilityKind.NightLight, "Night Light (experimental)", "moon", 0xFFFF9F0A, true)
    });
    public static UtilityKind[] Normalize(IEnumerable<UtilityKind>? values) => (values ?? [])
        .Where(k => All.Any(d => d.Kind == k && d.Available)).Distinct().ToArray();
}
public static class UtilityColors
{
    public static uint Icon(UtilityTheme theme, bool active, bool disabled = false) => disabled
        ? theme == UtilityTheme.Light ? 0xFFC7C7CC : 0xFF48484A
        : active ? 0xFFFFFFFF : theme == UtilityTheme.Light ? 0xFF1C1C1E : 0xFFF5F5F7;
    public static uint Surface(UtilityTheme theme) => theme == UtilityTheme.Light ? 0xFFE5E5EA : 0xFF2C2C2E;
}
