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
