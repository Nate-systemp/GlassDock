namespace GlassDock.Core.Settings;

public readonly record struct DockBehaviorSettings(
    double BottomMargin,
    int AutoHideDelayMilliseconds,
    int PeekDelayMilliseconds)
{
    public TimeSpan AutoHideDelay => TimeSpan.FromMilliseconds(AutoHideDelayMilliseconds);
    public TimeSpan PeekDelay => TimeSpan.FromMilliseconds(PeekDelayMilliseconds);
}

public sealed class GlassDockSettingsChangedEventArgs(GlassDockSettings settings) : EventArgs
{
    public GlassDockSettings Settings { get; } = settings;
}

/// <summary>Owns the normalized in-memory settings snapshot for one app lifetime.</summary>
public sealed class GlassDockSettingsSession
{
    public GlassDockSettingsSession(GlassDockSettings settings)
    {
        Current = GlassDockSettings.Normalize(settings);
    }

    public GlassDockSettings Current { get; private set; }

    public DockBehaviorSettings DockBehavior => new(
        Current.BottomMargin,
        Current.AutoHideDelayMilliseconds,
        Current.PeekDelayMilliseconds);

    public DockDisplayMode DisplayMode => Current.DockDisplayMode;

    public DockAppearanceSettings Appearance => new(
        Current.GlassMaterialMode,
        Current.IconSize,
        Current.MagnificationScale,
        Current.IconSpacing,
        Current.GlassBlurAmount,
        Current.DockOpacity,
        Current.BorderThickness,
        Current.BorderOpacity);

    public event EventHandler<GlassDockSettingsChangedEventArgs>? Changed;

    public GlassDockSettings CreateDockBehaviorUpdate(
        double bottomMargin,
        int autoHideDelayMilliseconds,
        int peekDelayMilliseconds) =>
        GlassDockSettings.Normalize(Current with
        {
            BottomMargin = bottomMargin,
            AutoHideDelayMilliseconds = autoHideDelayMilliseconds,
            PeekDelayMilliseconds = peekDelayMilliseconds
        });

    public GlassDockSettings CreateDefaultDockBehavior() =>
        CreateDockBehaviorUpdate(
            GlassDockSettings.DefaultBottomMargin,
            GlassDockSettings.DefaultAutoHideDelayMilliseconds,
            GlassDockSettings.DefaultPeekDelayMilliseconds);

    public GlassDockSettings CreateDockSettingsUpdate(
        double bottomMargin,
        int autoHideDelayMilliseconds,
        int peekDelayMilliseconds,
        GlassMaterialMode glassMaterialMode,
        double iconSize,
        double magnificationScale,
        double iconSpacing,
        double glassBlurAmount,
        double dockOpacity,
        double borderThickness,
        double borderOpacity,
        DockDisplayMode? dockDisplayMode = null,
        bool? hoverWaveEnabled = null,
        bool? pinDock = null) =>
        GlassDockSettings.Normalize(Current with
        {
            BottomMargin = bottomMargin,
            AutoHideDelayMilliseconds = autoHideDelayMilliseconds,
            PeekDelayMilliseconds = peekDelayMilliseconds,
            GlassMaterialMode = glassMaterialMode,
            IconSize = iconSize,
            MagnificationScale = magnificationScale,
            IconSpacing = iconSpacing,
            GlassBlurAmount = glassBlurAmount,
            DockOpacity = dockOpacity,
            BorderThickness = borderThickness,
            BorderOpacity = borderOpacity,
            DockDisplayMode = dockDisplayMode ?? Current.DockDisplayMode,
            HoverWaveEnabled = hoverWaveEnabled ?? Current.HoverWaveEnabled,
            PinDock = pinDock ?? Current.PinDock
        });

    public GlassDockSettings CreateAppearanceUpdate(
        GlassMaterialMode glassMaterialMode,
        double iconSize,
        double magnificationScale,
        double iconSpacing,
        double glassBlurAmount,
        double dockOpacity,
        double borderThickness,
        double borderOpacity) =>
        CreateDockSettingsUpdate(
            Current.BottomMargin,
            Current.AutoHideDelayMilliseconds,
            Current.PeekDelayMilliseconds,
            glassMaterialMode,
            iconSize,
            magnificationScale,
            iconSpacing,
            glassBlurAmount,
            dockOpacity,
            borderThickness,
            borderOpacity);

    public GlassDockSettings CreateDockAppearanceUpdate(DockAppearanceMode mode) =>
        GlassDockSettings.Normalize(Current with { DockAppearanceMode = mode });

    public GlassDockSettings CreateDefaultEditableSettings() =>
        CreateDockSettingsUpdate(
            GlassDockSettings.DefaultBottomMargin,
            GlassDockSettings.DefaultAutoHideDelayMilliseconds,
            GlassDockSettings.DefaultPeekDelayMilliseconds,
            GlassDockSettings.DefaultGlassMaterialMode,
            GlassDockSettings.DefaultIconSize,
            GlassDockSettings.DefaultMagnificationScale,
            GlassDockSettings.DefaultIconSpacing,
            GlassDockSettings.DefaultGlassBlurAmount,
            GlassDockSettings.DefaultDockOpacity,
            GlassDockSettings.DefaultBorderThickness,
            GlassDockSettings.DefaultBorderOpacity,
            GlassDockSettings.DefaultDockDisplayMode,
            GlassDockSettings.DefaultHoverWaveEnabled,
            GlassDockSettings.DefaultPinDock) with
        {
            DockAppearanceMode = GlassDockSettings.DefaultDockAppearanceMode,
            LaunchAtStartup = Current.LaunchAtStartup,
            NotificationBadgesEnabled = GlassDockSettings.DefaultNotificationBadgesEnabled
        };

    public GlassDockSettings CreateLaunchAtStartupUpdate(bool enabled) =>
        GlassDockSettings.Normalize(Current with
        {
            LaunchAtStartup = enabled
        });

    public GlassDockSettings CreateNotificationBadgesUpdate(bool enabled) =>
        GlassDockSettings.Normalize(Current with
        {
            NotificationBadgesEnabled = enabled
        });

    public GlassDockSettings CreateDisplayModeUpdate(DockDisplayMode displayMode) =>
        GlassDockSettings.Normalize(Current with
        {
            DockDisplayMode = displayMode
        });

    public bool Replace(GlassDockSettings settings)
    {
        var normalized = GlassDockSettings.Normalize(settings);
        if (normalized == Current)
            return false;

        Current = normalized;
        Changed?.Invoke(this, new(Current));
        return true;
    }
}
