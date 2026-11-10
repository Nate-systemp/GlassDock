using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class VolumeHudStateTests
{
    [Fact]
    public void Burst_expands_once_then_stays_compact_until_hidden()
    {
        var hud = new VolumeHudState();
        hud.Change(); Assert.Equal(VolumeHudPhase.Expanded, hud.Phase);
        hud.Change(); hud.Change(); Assert.Equal(VolumeHudPhase.Compact, hud.Phase);
        Assert.True(hud.Hide());
        hud.Change(); Assert.Equal(VolumeHudPhase.Expanded, hud.Phase);
    }
    [Fact]
    public void Drag_prevents_compaction_and_idle_hide()
    {
        var hud = new VolumeHudState();
        hud.Change(); hud.Change(); hud.BeginInteraction();
        hud.Change(); Assert.Equal(VolumeHudPhase.Expanded, hud.Phase);
        Assert.False(hud.Hide());
        hud.EndInteraction(); Assert.True(hud.Hide());
        hud.Change(true); Assert.Equal(VolumeHudPhase.Expanded, hud.Phase);
    }
    [Theory]
    [InlineData(-20, 200, 0)]
    [InlineData(100, 200, .5)]
    [InlineData(220, 200, 1)]
    [InlineData(10, 0, 0)]
    public void Pointer_mapping_is_bounded(double x, double width, double expected)
        => Assert.Equal(expected, VolumeHudState.VolumeAt(x, width));
}
