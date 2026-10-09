namespace GlassDock.Core.Settings;

public sealed record SettingsPageEntry(string Id, string Title, string Glyph);
public sealed record SettingsSearchEntry(string Page, string Label, string Control, string Keywords)
{
    public override string ToString() => $"{SettingsCatalog.Pages.First(page => page.Id == Page).Title} · {Label}";
}

/// <summary>Navigation and searchable options that exist in Doky's settings session.</summary>
public static class SettingsCatalog
{
    public static IReadOnlyList<SettingsPageEntry> Pages { get; } = Array.AsReadOnly(new[]
    {
        new SettingsPageEntry("General", "General", "\uE80F"),
        new SettingsPageEntry("Appearance", "Appearance", "\uE790"),
        new SettingsPageEntry("Dock", "Dock", "\uE737"),
        new SettingsPageEntry("Apps", "Apps & Stacks", "\uE8F1"),
        new SettingsPageEntry("Behavior", "Behavior", "\uE713"),
        new SettingsPageEntry("Displays", "Displays", "\uE7F4"),
        new SettingsPageEntry("System", "System", "\uE80A"),
        new SettingsPageEntry("Advanced", "Advanced", "\uE90F"),
        new SettingsPageEntry("About", "About", "\uE946")
    });

    public static IReadOnlyList<SettingsSearchEntry> Options { get; } = Array.AsReadOnly(new[]
    {
        new SettingsSearchEntry("General", "Start with Windows", "LaunchAtStartupToggle", "startup login launch"),
        new SettingsSearchEntry("Appearance", "Appearance mode", "AppearanceTiles", "dark light frosted acrylic clear liquid glass theme"),
        new SettingsSearchEntry("Appearance", "Blur and opacity", "GlassControls", "diffusion tint transparency material"),
        new SettingsSearchEntry("Appearance", "Clear edge refraction", "ClearRefractionStrengthBox", "liquid glass distortion strength"),
        new SettingsSearchEntry("Appearance", "Highlight position", "SpecularHighlightAngleBox", "specular rim lighting angle"),
        new SettingsSearchEntry("Dock", "Icon size", "IconSizeBox", "size scale layout"),
        new SettingsSearchEntry("Dock", "Magnification", "MagnificationScaleBox", "hover zoom grow"),
        new SettingsSearchEntry("Dock", "Icon spacing", "IconSpacingBox", "gap layout"),
        new SettingsSearchEntry("Dock", "Pin dock", "PinDockToggle", "keep expanded visible appbar work area"),
        new SettingsSearchEntry("Dock", "Auto-hide delay", "AutoHideDelayBox", "collapse hide"),
        new SettingsSearchEntry("Dock", "Peek delay", "PeekDelayBox", "indicator hide"),
        new SettingsSearchEntry("Apps", "Stacks", "StackHelp", "stack unstack folder pinned reorder"),
        new SettingsSearchEntry("Apps", "Notification badges", "NotificationBadgesToggle", "unread notifications count access"),
        new SettingsSearchEntry("Behavior", "Hover wave", "HoverWaveToggle", "animation pointer"),
        new SettingsSearchEntry("Behavior", "Open dock on hover", "HoverToExpandOnlyToggle", "hover minimized indicator expand open"),
        new SettingsSearchEntry("Behavior", "Window previews and activation", "InteractionHelp", "click restore drag drop windows keyboard win space"),
        new SettingsSearchEntry("Displays", "Monitor mode", "DockDisplayModeBox", "primary pointer active window all displays monitor dpi"),
        new SettingsSearchEntry("Displays", "Bottom spacing", "BottomMarginBox", "position margin"),
        new SettingsSearchEntry("System", "Taskbar recovery", "ResumeTaskbarButton", "restore windows suppression safe mode"),
        new SettingsSearchEntry("System", "Input helper", "HelperStatusText", "elevated keyboard uac"),
        new SettingsSearchEntry("Advanced", "Rendering status", "RenderingStatusText", "diagnostics fallback capture"),
        new SettingsSearchEntry("Advanced", "Reset preferences", "ResetSection", "defaults placement appearance reset"),
        new SettingsSearchEntry("About", "Version and updates", "CheckUpdatesButton", "update version github release"),
    });

    public static IReadOnlyList<SettingsSearchEntry> Search(string? query)
    {
        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return [];
        return Options.Where(entry => words.All(word =>
            $"{entry.Page} {entry.Label} {entry.Keywords}".Contains(word, StringComparison.OrdinalIgnoreCase)))
            .Take(8).ToArray();
    }
}
