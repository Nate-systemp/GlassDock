using GlassDock.Core.Settings;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class PerMonitorSettingsTests
{
    [Fact]
    public void All_displays_is_a_valid_persisted_display_mode()
    {
        var normalized = GlassDockSettings.Normalize(new()
        {
            DockDisplayMode = DockDisplayMode.AllDisplays
        });
        Assert.Equal(DockDisplayMode.AllDisplays, normalized.DockDisplayMode);

        var session = new GlassDockSettingsSession(new());
        Assert.True(session.Replace(session.CreateDisplayModeUpdate(DockDisplayMode.AllDisplays)));
        Assert.Equal(DockDisplayMode.AllDisplays, session.DisplayMode);
    }

    [Fact]
    public void Unknown_display_mode_still_falls_back_to_primary()
    {
        var normalized = GlassDockSettings.Normalize(new()
        {
            DockDisplayMode = (DockDisplayMode)999
        });
        Assert.Equal(DockDisplayMode.Primary, normalized.DockDisplayMode);
    }
}
