using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class DockIconMotionTests
{
    [Fact]
    public void Removing_stack_slots_requires_updated_expanded_geometry_for_full_size_icons()
    {
        const double oldWidth = 900, newWidth = 732, height = 68;
        Assert.True(DockIconMotion.FromGeometry(newWidth, height, oldWidth, height, height).Scale < 1);
        foreach (var width in new[] { newWidth, 620d, 508d, oldWidth })
            Assert.Equal(new DockIconMotion(1, 0, 1), DockIconMotion.FromGeometry(width, height, width, height, height));
    }
    [Theory]
    [InlineData(560, 68)]
    [InlineData(900, 92)]
    public void Rapid_reversals_follow_current_dock_size_and_settle_together(double width, double height)
    {
        var p = 0d;
        for (var i = 0; i < 100; i++)
        {
            var target = i % 2 == 0 ? 1d : 0d;
            var before = DockIconMotion.FromGeometry(120 + (width - 120) * p,
                5 + (height - 5) * p, width, height, 68);
            p += (target - p) * .31;
            var after = DockIconMotion.FromGeometry(120 + (width - 120) * p,
                5 + (height - 5) * p, width, height, 68);
            Assert.True(target == 1 ? after.Scale >= before.Scale : after.Scale <= before.Scale);
            Assert.True(target == 1 ? after.OffsetY <= before.OffsetY : after.OffsetY >= before.OffsetY);
        }
        Assert.Equal(new DockIconMotion(1, 0, 1), DockIconMotion.FromGeometry(width, height, width, height, 68));
        Assert.Equal(0, DockIconMotion.FromGeometry(120, 5, width, height, 68).Opacity);
    }

    [Fact]
    public void Expanded_returns_identity_and_collapsed_is_invisible()
    {
        Assert.Equal(new DockIconMotion(1, 0, 1), DockIconMotion.At(1, 68));
        var closed = DockIconMotion.At(0, 68);
        Assert.Equal(0, closed.Scale);
        Assert.Equal(0, closed.OffsetY);
        Assert.Equal(0, closed.Opacity);
    }

    [Theory]
    [InlineData(.9)] [InlineData(.5)] [InlineData(.35)]
    public void Fade_and_scale_start_together(double progress)
    {
        var value = DockIconMotion.At(progress, 68);
        Assert.InRange(value.Opacity, .001, .999);
        Assert.Equal(0, value.OffsetY);
        Assert.True(value.Scale < 1);
    }

    [Fact]
    public void Reversal_retraces_identical_values_without_direction_state()
    {
        var path = Enumerable.Range(0, 101).Select(i => DockIconMotion.At(i / 100d, 68)).ToArray();
        for (var i = 100; i >= 0; i--) Assert.Equal(path[i], DockIconMotion.At(i / 100d, 68));
        for (var i = 1; i < path.Length; i++)
        {
            Assert.True(path[i].Scale >= path[i - 1].Scale);
            Assert.True(path[i].OffsetY <= path[i - 1].OffsetY);
            Assert.True(path[i].Opacity >= path[i - 1].Opacity);
        }
    }

    [Theory]
    [InlineData(48)] [InlineData(68)] [InlineData(120)]
    public void Bottom_anchor_does_not_push_icons_outside_surface(double height) =>
        Assert.Equal(0, DockIconMotion.At(.5, height).OffsetY);

    [Theory]
    [InlineData(0)] [InlineData(.25)] [InlineData(.5)] [InlineData(.75)] [InlineData(1)]
    public void Both_directions_share_geometry_and_icons_fit_at_each_checkpoint(double progress)
    {
        const double width = 900, height = 68;
        var frame = DockTransitionGeometry.At(progress, width, height);
        var reverse = DockTransitionGeometry.At(1 - (1 - progress), width, height);
        Assert.Equal(frame, reverse);
        Assert.Equal(progress, DockTransitionTiming.Progress(frame.Width, frame.Height, width, height), 8);
        var icons = DockIconMotion.FromGeometry(frame.Width, frame.Height, width, height, height);
        Assert.True(width * icons.Scale <= frame.Width + .000001);
        Assert.True(height * icons.Scale <= frame.Height + .000001);
        Assert.Equal(progress * progress * (3 - 2 * progress), icons.Opacity, 8);
        Assert.InRange(frame.CornerRadius, 2.5, 34);
        if (progress == 0) Assert.Equal(new DockTransitionGeometry(120, 5, 2.5, 0), frame);
    }
}
