namespace GlassDock.Core.Settings;

public enum DockDisplayMode
{
    Primary = 0,
    Pointer = 1,
    Foreground = 2
}

/// <summary>
/// Persistent GlassDock preferences. Runtime components do not consume these
/// values until their individual settings are explicitly wired.
/// </summary>
public sealed record GlassDockSettings
{
    public const int CurrentSchemaVersion = 1;
    public const double DefaultBottomMargin = 24;
    public const int DefaultAutoHideDelayMilliseconds = 1000;
    public const int DefaultPeekDelayMilliseconds = 2000;
    public const double DefaultIconSize = 28;
    public const double DefaultMagnificationScale = 1.24;
    public const double DefaultIconSpacing = 6;
    public const double DefaultGlassBlurAmount = 20;
    public const double DefaultDockOpacity = 0.78;
    public const double DefaultBorderThickness = 1.05;
    public const double DefaultBorderOpacity = 0.78;
    public const bool DefaultHoverWaveEnabled = true;
    public const bool DefaultPinDock = false;
    public const bool DefaultLaunchAtStartup = false;
    public const GlassMaterialMode DefaultGlassMaterialMode = GlassMaterialMode.Frosted;
    public const DockAppearanceMode DefaultDockAppearanceMode = DockAppearanceMode.Dark;
    public const DockDisplayMode DefaultDockDisplayMode = DockDisplayMode.Primary;

    public const double MinimumBottomMargin = 16;
    public const double MaximumBottomMargin = 100;
    public const int MinimumAutoHideDelayMilliseconds = 0;
    public const int MaximumAutoHideDelayMilliseconds = 10_000;
    public const int MinimumPeekDelayMilliseconds = 0;
    public const int MaximumPeekDelayMilliseconds = 30_000;
    public const double MinimumIconSize = 20;
    public const double MaximumIconSize = 40;
    public const double MinimumMagnificationScale = 1;
    public const double MaximumMagnificationScale = 1.6;
    public const double MinimumIconSpacing = 0;
    public const double MaximumIconSpacing = 20;
    public const double MinimumGlassBlurAmount = 0;
    public const double MaximumGlassBlurAmount = 60;
    public const double MinimumDockOpacity = 0.35;
    public const double MaximumDockOpacity = 1;
    public const double MinimumBorderThickness = 0;
    public const double MaximumBorderThickness = 2;
    public const double MinimumBorderOpacity = 0;
    public const double MaximumBorderOpacity = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public bool LaunchAtStartup { get; init; } = DefaultLaunchAtStartup;
    public bool SuppressWindowsTaskbar { get; init; } = true;
    public double BottomMargin { get; init; } = DefaultBottomMargin;
    public int AutoHideDelayMilliseconds { get; init; } = DefaultAutoHideDelayMilliseconds;
    public int PeekDelayMilliseconds { get; init; } = DefaultPeekDelayMilliseconds;
    public double IconSize { get; init; } = DefaultIconSize;
    public double MagnificationScale { get; init; } = DefaultMagnificationScale;
    public double IconSpacing { get; init; } = DefaultIconSpacing;
    public double GlassBlurAmount { get; init; } = DefaultGlassBlurAmount;
    public double DockOpacity { get; init; } = DefaultDockOpacity;
    public double BorderThickness { get; init; } = DefaultBorderThickness;
    public double BorderOpacity { get; init; } = DefaultBorderOpacity;
    public bool HoverWaveEnabled { get; init; } = DefaultHoverWaveEnabled;
    public bool PinDock { get; init; } = DefaultPinDock;
    public GlassMaterialMode GlassMaterialMode { get; init; } = DefaultGlassMaterialMode;
    public DockAppearanceMode DockAppearanceMode { get; init; } = DefaultDockAppearanceMode;
    public DockDisplayMode DockDisplayMode { get; init; } = DefaultDockDisplayMode;

    /// <summary>
    /// Returns a safe current-schema snapshot. Unknown and missing schema
    /// versions use the current schema until real migrations are introduced.
    /// </summary>
    public static GlassDockSettings Normalize(GlassDockSettings? settings)
    {
        settings ??= new();

        var bottomMargin = double.IsFinite(settings.BottomMargin)
            ? Math.Clamp(settings.BottomMargin, MinimumBottomMargin, MaximumBottomMargin)
            : DefaultBottomMargin;

        return settings with
        {
            SchemaVersion = CurrentSchemaVersion,
            BottomMargin = bottomMargin,
            AutoHideDelayMilliseconds = Math.Clamp(
                settings.AutoHideDelayMilliseconds,
                MinimumAutoHideDelayMilliseconds,
                MaximumAutoHideDelayMilliseconds),
            PeekDelayMilliseconds = Math.Clamp(
                settings.PeekDelayMilliseconds,
                MinimumPeekDelayMilliseconds,
                MaximumPeekDelayMilliseconds),
            IconSize = Bound(
                settings.IconSize,
                MinimumIconSize,
                MaximumIconSize,
                DefaultIconSize),
            MagnificationScale = Bound(
                settings.MagnificationScale,
                MinimumMagnificationScale,
                MaximumMagnificationScale,
                DefaultMagnificationScale),
            IconSpacing = Bound(
                settings.IconSpacing,
                MinimumIconSpacing,
                MaximumIconSpacing,
                DefaultIconSpacing),
            GlassBlurAmount = Bound(
                settings.GlassBlurAmount,
                MinimumGlassBlurAmount,
                MaximumGlassBlurAmount,
                DefaultGlassBlurAmount),
            DockOpacity = Bound(
                settings.DockOpacity,
                MinimumDockOpacity,
                MaximumDockOpacity,
                DefaultDockOpacity),
            BorderThickness = Bound(
                settings.BorderThickness,
                MinimumBorderThickness,
                MaximumBorderThickness,
                DefaultBorderThickness),
            BorderOpacity = Bound(
                settings.BorderOpacity,
                MinimumBorderOpacity,
                MaximumBorderOpacity,
                DefaultBorderOpacity),
            GlassMaterialMode = DockMaterialStylePresets.Normalize(settings.GlassMaterialMode),
            DockAppearanceMode = Enum.IsDefined(settings.DockAppearanceMode)
                ? settings.DockAppearanceMode
                : DefaultDockAppearanceMode,
            DockDisplayMode = Enum.IsDefined(settings.DockDisplayMode)
                ? settings.DockDisplayMode
                : DefaultDockDisplayMode
        };
    }

    private static double Bound(double value, double minimum, double maximum, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
}
