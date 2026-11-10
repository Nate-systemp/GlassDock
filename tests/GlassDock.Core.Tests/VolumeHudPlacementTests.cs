using GlassDock.Core.Applications;
using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class VolumeHudPlacementTests
{
    private static PreviewRect Body(PreviewRect window, double scale)
    {
        var local = VolumeHudPlacement.CapsuleBounds(VolumeHudPlacement.ExpandedHeight * scale, scale);
        return new(window.X + local.X, window.Y + local.Y, local.Width, local.Height);
    }

    [Fact]
    public void Compression_changes_top_but_keeps_bottom_and_width()
    {
        var expanded = VolumeHudPlacement.CapsuleBounds(36);
        var thin = VolumeHudPlacement.CapsuleBounds(6);
        Assert.Equal(expanded.Y + expanded.Height, thin.Y + thin.Height);
        Assert.Equal(expanded.Width, thin.Width);
        Assert.Equal(30, thin.Y - expanded.Y);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void Pinned_sound_is_preferred_without_moving_dock_or_other_buttons(double scale)
    {
        var work = new PreviewRect(0, 0, 1920 * scale, 1040 * scale);
        var dock = new PreviewRect(500 * scale, 930 * scale, 600 * scale, 64 * scale);
        var sound = new PreviewRect(1200 * scale, 930 * scale, 48 * scale, 48 * scale);
        var result = VolumeHudPlacement.Position(work, dock, new[] { sound }, sound, scale);
        Assert.NotNull(result);
        var body = Body(result.Value, scale);
        Assert.InRange(Math.Abs(body.X + body.Width / 2 - sound.X - sound.Width / 2), 0, 1);
        Assert.InRange(Math.Abs(sound.Y - body.Y - body.Height - 12 * scale), 0, 1);
        Assert.False(VolumeHudPlacement.Overlaps(body, sound));
        Assert.False(VolumeHudPlacement.Overlaps(body, dock));
    }

    [Fact]
    public void Top_edge_flips_or_shifts_without_overlapping_neighbors()
    {
        var work = new PreviewRect(-1280, 0, 1280, 1000);
        var dock = new PreviewRect(-1100, 20, 500, 64);
        var sound = new PreviewRect(-580, 20, 48, 48);
        var blocker = new PreviewRect(-650, 80, 200, 80);
        var result = VolumeHudPlacement.Position(work, dock, new[] { sound, blocker }, sound, 1);
        Assert.NotNull(result);
        var body = Body(result.Value, 1);
        foreach (var rect in new[] { dock, sound, blocker }) Assert.False(VolumeHudPlacement.Overlaps(body, rect));
        Assert.InRange(body.X, work.X, work.X + work.Width - body.Width);
        Assert.InRange(body.Y, work.Y, work.Y + work.Height - body.Height);
    }

    [Fact]
    public void Unpinned_hud_avoids_tall_utility_and_stays_out_of_taskbar_work_area()
    {
        var work = new PreviewRect(0, 0, 800, 560);
        var dock = new PreviewRect(200, 500, 400, 60);
        var tall = new PreviewRect(350, 430, 200, 70);
        var result = VolumeHudPlacement.Position(work, dock, new[] { tall }, null, 1);
        Assert.NotNull(result);
        var body = Body(result.Value, 1);
        Assert.False(VolumeHudPlacement.Overlaps(body, tall));
        Assert.False(VolumeHudPlacement.Overlaps(body, dock));
        Assert.True(body.Y + body.Height <= work.Y + work.Height);
    }

    [Fact]
    public void No_legal_space_hides_instead_of_overlapping()
    {
        var work = new PreviewRect(0, 0, 240, 76);
        Assert.Null(VolumeHudPlacement.Position(work, work, Array.Empty<PreviewRect>(), null, 1));
        Assert.Null(VolumeHudPlacement.Position(work, default, Array.Empty<PreviewRect>(), null, double.NaN));
    }
}
