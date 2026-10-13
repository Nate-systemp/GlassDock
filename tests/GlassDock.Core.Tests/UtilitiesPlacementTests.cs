using System.Text.Json;
using GlassDock.Core.Desktop;
using GlassDock.Core.Settings;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class UtilitiesPlacementTests
{
    [Fact]
    public void Wide_pill_clamps_by_width_and_height_independently()
    {
        Assert.Equal((734d, 530d), FloatingUtilityPlacement.Clamp(999, 999, 0, 0, 1000, 600, 266, 70, false));
        Assert.Equal((-266d, 20d), FloatingUtilityPlacement.Clamp(-5, 20, -1000, 0, 1000, 600, 266, 70, false));
    }
    [Theory]
    [InlineData(5, 0, 1, false)]
    [InlineData(6, 0, 1, true)]
    [InlineData(11, 0, 2, false)]
    [InlineData(12, 0, 2, true)]
    [InlineData(5, 5, 1, true)]
    public void Physical_drag_threshold_tracks_display_scale(double x, double y, double scale, bool expected) =>
        Assert.Equal(expected, FloatingUtilityPlacement.IsDrag(x, y, scale));

    [Fact]
    public void Disconnected_monitor_and_oversized_host_remain_accessible()
    {
        Assert.Equal((736d, 100d), FloatingUtilityPlacement.Clamp(3000, 100, 0, 0, 800, 600, 64, false));
        Assert.Equal((0d, 0d), FloatingUtilityPlacement.Clamp(3000, 100, 0, 0, 40, 40, 64, false));
        Assert.Equal((736d, 536d), FloatingUtilityPlacement.Clamp(double.NaN, double.PositiveInfinity, 0, 0, 800, 600, 64, false));
    }

    [Fact]
    public void Existing_profiles_stay_inside_dock_and_new_preferences_roundtrip()
    {
        var legacy = JsonSerializer.Deserialize<GlassDockSettings>("{}")!;
        Assert.Equal(UtilitiesPlacement.InsideDock, legacy.UtilitiesPlacement);
        var value = legacy with { UtilitiesPlacement = UtilitiesPlacement.FloatingButton, UtilitiesX = -450, UtilitiesY = 120,
            UtilitiesShortcutKey = 0x55, UtilitiesSnapEdges = false };
        Assert.Equal(value, JsonSerializer.Deserialize<GlassDockSettings>(JsonSerializer.Serialize(value)));
    }

    [Fact]
    public void Invalid_preferences_recover()
    {
        var value = GlassDockSettings.Normalize(new() { UtilitiesPlacement = (UtilitiesPlacement)42,
            UtilitiesX = double.NaN, UtilitiesY = double.PositiveInfinity, UtilitiesShortcutKey = -1 });
        Assert.Equal(UtilitiesPlacement.InsideDock, value.UtilitiesPlacement);
        Assert.Null(value.UtilitiesX); Assert.Null(value.UtilitiesY);
        Assert.Equal(0x55, value.UtilitiesShortcutKey);
    }

    [Theory]
    [InlineData(-900, 90, -800, 90)]
    [InlineData(-10, 1200, -64, 536)]
    [InlineData(-700, -10, -700, 0)]
    public void Clamp_respects_negative_monitor_origins(double x, double y, double expectedX, double expectedY)
    {
        Assert.Equal((expectedX, expectedY), FloatingUtilityPlacement.Clamp(x, y, -800, 0, 800, 600, 64, false));
    }

    [Theory]
    [InlineData(5, 200, 0, 200)]
    [InlineData(730, 200, 736, 200)]
    [InlineData(400, 2, 400, 0)]
    [InlineData(400, 530, 400, 536)]
    public void Snap_chooses_nearest_edge(double x, double y, double expectedX, double expectedY) =>
        Assert.Equal((expectedX, expectedY), FloatingUtilityPlacement.Clamp(x, y, 0, 0, 800, 600, 64, true));
}
