namespace GlassDock.Core.Settings;

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

    public const double MinimumBottomMargin = 16;
    public const double MaximumBottomMargin = 100;
    public const int MinimumAutoHideDelayMilliseconds = 0;
    public const int MaximumAutoHideDelayMilliseconds = 10_000;
    public const int MinimumPeekDelayMilliseconds = 0;
    public const int MaximumPeekDelayMilliseconds = 30_000;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public bool LaunchAtStartup { get; init; }
    public bool SuppressWindowsTaskbar { get; init; } = true;
    public double BottomMargin { get; init; } = DefaultBottomMargin;
    public int AutoHideDelayMilliseconds { get; init; } = DefaultAutoHideDelayMilliseconds;
    public int PeekDelayMilliseconds { get; init; } = DefaultPeekDelayMilliseconds;

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
                MaximumPeekDelayMilliseconds)
        };
    }
}
