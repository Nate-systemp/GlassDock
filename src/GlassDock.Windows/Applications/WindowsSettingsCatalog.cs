using GlassDock.Core.Applications;

namespace GlassDock.Windows.Applications;

public sealed record WindowsSettingEntry(string Name, string Description, IReadOnlyList<string> Keywords, string Uri)
{
    public GlassSearchResult ToSearchResult() => new(Uri, Name, Description, Keywords,
        GlassSearchResultType.Setting, Uri);
}

public static class WindowsSettingsCatalog
{
    // https://learn.microsoft.com/windows/apps/develop/launch/launch-settings
    public static IReadOnlyList<WindowsSettingEntry> Entries { get; } = Array.AsReadOnly<WindowsSettingEntry>([
        new("Bluetooth & devices", "Settings · Bluetooth and connected devices", ["bluetooth", "devices", "pair"], "ms-settings:bluetooth"),
        new("Display settings", "Settings · Screen resolution and brightness", ["display", "screen", "monitor", "brightness"], "ms-settings:display"),
        new("Wi-Fi", "Settings · Network & internet", ["wifi", "wi-fi", "wireless", "network", "internet"], "ms-settings:network-wifi"),
        new("Sound settings", "Settings · Volume, output and input", ["sound", "audio", "volume", "microphone"], "ms-settings:sound"),
        new("Startup apps", "Settings · Apps that run when you sign in", ["startup", "login"], "ms-settings:startupapps"),
        new("Windows Update", "Settings · Updates and restart options", ["update", "updates"], "ms-settings:windowsupdate"),
        new("Storage settings", "Settings · Disk space", ["storage", "disk", "space"], "ms-settings:storagesense"),
        new("Power & battery", "Settings · Power, sleep and battery", ["battery", "power", "sleep", "energy"], "ms-settings:powersleep"),
        new("Installed apps", "Settings · Manage installed applications", ["apps", "applications", "uninstall"], "ms-settings:appsfeatures")
    ]);
}
