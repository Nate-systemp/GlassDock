using GlassDock.Core.Settings;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class GlassDockSettingsTests
{
    [Fact]
    public void Defaults_match_current_dock_behavior()
    {
        var settings = new GlassDockSettings();

        Assert.Equal(1, settings.SchemaVersion);
        Assert.False(settings.LaunchAtStartup);
        Assert.True(settings.SuppressWindowsTaskbar);
        Assert.Equal(24, settings.BottomMargin);
        Assert.Equal(1000, settings.AutoHideDelayMilliseconds);
        Assert.Equal(2000, settings.PeekDelayMilliseconds);
    }

    [Fact]
    public void Normalize_clamps_numeric_values_and_uses_current_schema()
    {
        var low = GlassDockSettings.Normalize(new()
        {
            SchemaVersion = 0,
            BottomMargin = -500,
            AutoHideDelayMilliseconds = -1,
            PeekDelayMilliseconds = -1
        });
        var high = GlassDockSettings.Normalize(new()
        {
            SchemaVersion = int.MaxValue,
            BottomMargin = 100_000,
            AutoHideDelayMilliseconds = int.MaxValue,
            PeekDelayMilliseconds = int.MaxValue
        });
        var nonFinite = GlassDockSettings.Normalize(new() { BottomMargin = double.NaN });

        Assert.Equal(GlassDockSettings.CurrentSchemaVersion, low.SchemaVersion);
        Assert.Equal(GlassDockSettings.MinimumBottomMargin, low.BottomMargin);
        Assert.Equal(GlassDockSettings.MinimumAutoHideDelayMilliseconds, low.AutoHideDelayMilliseconds);
        Assert.Equal(GlassDockSettings.MinimumPeekDelayMilliseconds, low.PeekDelayMilliseconds);
        Assert.Equal(GlassDockSettings.CurrentSchemaVersion, high.SchemaVersion);
        Assert.Equal(GlassDockSettings.MaximumBottomMargin, high.BottomMargin);
        Assert.Equal(GlassDockSettings.MaximumAutoHideDelayMilliseconds, high.AutoHideDelayMilliseconds);
        Assert.Equal(GlassDockSettings.MaximumPeekDelayMilliseconds, high.PeekDelayMilliseconds);
        Assert.Equal(GlassDockSettings.DefaultBottomMargin, nonFinite.BottomMargin);
    }

    [Fact]
    public void Runtime_session_exposes_normalized_dock_behavior()
    {
        var session = new GlassDockSettingsSession(new()
        {
            BottomMargin = -20,
            AutoHideDelayMilliseconds = 50_000,
            PeekDelayMilliseconds = 3500
        });

        Assert.Equal(GlassDockSettings.MinimumBottomMargin, session.DockBehavior.BottomMargin);
        Assert.Equal(
            TimeSpan.FromMilliseconds(GlassDockSettings.MaximumAutoHideDelayMilliseconds),
            session.DockBehavior.AutoHideDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(3500), session.DockBehavior.PeekDelay);
    }

    [Fact]
    public void Safe_dock_update_propagates_and_preserves_unexposed_settings()
    {
        var session = new GlassDockSettingsSession(new()
        {
            LaunchAtStartup = true,
            SuppressWindowsTaskbar = false
        });
        var changes = 0;
        session.Changed += (_, _) => changes++;

        var edited = session.CreateDockBehaviorUpdate(48, 3200, 6400);
        Assert.True(session.Replace(edited));

        Assert.Equal(48, session.DockBehavior.BottomMargin);
        Assert.Equal(TimeSpan.FromMilliseconds(3200), session.DockBehavior.AutoHideDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(6400), session.DockBehavior.PeekDelay);
        Assert.True(session.Current.LaunchAtStartup);
        Assert.False(session.Current.SuppressWindowsTaskbar);
        Assert.Equal(1, changes);
        Assert.False(session.Replace(edited));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Reset_dock_behavior_preserves_unexposed_settings()
    {
        var session = new GlassDockSettingsSession(new()
        {
            LaunchAtStartup = true,
            SuppressWindowsTaskbar = false,
            BottomMargin = 80,
            AutoHideDelayMilliseconds = 9000,
            PeekDelayMilliseconds = 12_000
        });

        var reset = session.CreateDefaultDockBehavior();

        Assert.Equal(GlassDockSettings.DefaultBottomMargin, reset.BottomMargin);
        Assert.Equal(GlassDockSettings.DefaultAutoHideDelayMilliseconds, reset.AutoHideDelayMilliseconds);
        Assert.Equal(GlassDockSettings.DefaultPeekDelayMilliseconds, reset.PeekDelayMilliseconds);
        Assert.True(reset.LaunchAtStartup);
        Assert.False(reset.SuppressWindowsTaskbar);
    }
}
