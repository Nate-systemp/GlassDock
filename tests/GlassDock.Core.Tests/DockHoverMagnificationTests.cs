using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class DockHoverMagnificationTests
{
    private static DockHoverMagnification<object>.Item[] Row(int count = 7) => Enumerable.Range(0, count)
        .Select(i => new DockHoverMagnification<object>.Item(new object(), i * 48, 0, 40, 40)).ToArray();

    private static IReadOnlyList<DockHoverMagnification<object>.Frame> Settle(
        DockHoverMagnification<object> hover,
        DockHoverMagnification<object>.Item[] row, int start = 0)
    {
        IReadOnlyList<DockHoverMagnification<object>.Frame> frames = [];
        for (var t = start; t <= start + 1000; t += 10)
            frames = hover.Step(row, t, true);
        return frames;
    }

    private static void AssertFixedPositions(
        DockHoverMagnification<object>.Item[] row,
        IReadOnlyList<DockHoverMagnification<object>.Frame> frames)
    {
        Assert.Equal(row.Length, frames.Count);
        for (var i = 0; i < row.Length; i++)
        {
            Assert.Same(row[i].Id, frames[i].Id);
            Assert.Equal(0, frames[i].OffsetX, 8);
            Assert.InRange(frames[i].Scale, 1, 1.5);
        }
    }

    [Theory]
    [InlineData(0, 1.5)]
    [InlineData(1, 1.25)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(20, 1)]
    public void Curve_magnifies_only_main_icon_and_direct_neighbors(double distance, double expected)
    {
        Assert.Equal(expected, DockHoverMagnification<object>.ScaleAtDistance(distance), 8);
        Assert.Equal(expected, DockHoverMagnification<object>.ScaleAtDistance(-distance), 8);
    }

    [Fact]
    public void Curve_is_continuous_monotonic_and_finite()
    {
        var previous = 1.5;
        for (var d = 0d; d < 4; d += .001)
        {
            var scale = DockHoverMagnification<object>.ScaleAtDistance(d);
            Assert.InRange(scale, 1, previous + 1e-9);
            Assert.InRange(previous - scale, 0, .001);
            previous = scale;
        }
        Assert.Equal(1, DockHoverMagnification<object>.ScaleAtDistance(double.NaN));
    }

    [Fact]
    public void Center_hover_keeps_every_icon_in_place_and_only_three_icons_grow()
    {
        var row = Row();
        var hover = new DockHoverMagnification<object>();
        hover.SetPointer(164, 20);
        var frames = Settle(hover, row);
        Assert.Equal(new[] { 1d, 1d, 1.25, 1.5, 1.25, 1d, 1d },
            frames.Select(frame => Math.Round(frame.Scale, 6)));
        AssertFixedPositions(row, frames);
        Assert.Equal(328, hover.ContentWidth);
        Assert.False(hover.IsAnimating);
    }

    [Fact]
    public void Spring_reaches_full_hover_scale_smoothly_without_bounce_or_restart()
    {
        var row = Row();
        var hover = new DockHoverMagnification<object>();
        hover.SetPointer(164, 20);
        var previous = 1d;
        for (var t = 0; t <= 220; t += 10)
        {
            var frames = hover.Step(row, t, true);
            Assert.InRange(frames[3].Scale, previous, 1.5);
            previous = frames[3].Scale;
            Assert.False(hover.SetPointer(164, 20));
            AssertFixedPositions(row, frames);
        }
        Assert.InRange(previous, 1.495, 1.5);
    }

    [Fact]
    public void Moving_within_the_same_icon_does_not_magnify_more_distant_icons()
    {
        var row = Row();
        var hover = new DockHoverMagnification<object>();
        foreach (var x in new[] { 147d, 155d, 164d, 175d, 187d })
        {
            hover.SetPointer(x, 20);
            var frames = Settle(hover, row);
            Assert.Equal(1.5, frames[3].Scale, 6);
            Assert.Equal(1.25, frames[2].Scale, 6);
            Assert.Equal(1.25, frames[4].Scale, 6);
            Assert.All(new[] { 0, 1, 5, 6 }, index => Assert.Equal(1, frames[index].Scale, 6));
            AssertFixedPositions(row, frames);
        }
    }

    [Fact]
    public void Moving_to_edge_icon_never_recenters_the_whole_row()
    {
        var row = Row(13);
        var hover = new DockHoverMagnification<object>();
        foreach (var index in new[] { 6, 12, 0, 10, 3 })
        {
            hover.SetPointer(row[index].Left + row[index].Width / 2, 20);
            var frames = Settle(hover, row);
            for (var i = 0; i < frames.Count; i++)
            {
                var expected = i == index ? 1.5 : Math.Abs(i - index) == 1 ? 1.25 : 1;
                Assert.Equal(expected, frames[i].Scale, 6);
            }
            AssertFixedPositions(row, frames);
            Assert.Equal(616, hover.ContentWidth);
        }
    }

    [Fact]
    public void Rapid_pointer_motion_and_mouse_exit_leave_original_spacing_untouched()
    {
        var row = Row();
        var hover = new DockHoverMagnification<object>();
        for (var t = 0; t < 2500; t += 8)
        {
            hover.SetPointer(164 + Math.Sin(t * .018) * 170, 20);
            var frames = hover.Step(row, t, true);
            AssertFixedPositions(row, frames);
            Assert.Equal(328, hover.ContentWidth);
        }
        hover.Leave();
        var settled = Settle(hover, row, 2500);
        Assert.All(settled, frame => { Assert.Equal(1, frame.Scale); Assert.Equal(0, frame.OffsetX); });
    }

    [Fact]
    public void Reduced_motion_applies_scale_immediately_and_drag_reset_clears_it()
    {
        var row = Row();
        var hover = new DockHoverMagnification<object>();
        hover.SetPointer(164, 20);
        var frames = hover.Step(row, 0, false);
        Assert.Equal(1.5, frames[3].Scale);
        Assert.Equal(1.25, frames[2].Scale);
        AssertFixedPositions(row, frames);
        Assert.False(hover.IsAnimating);
        hover.Leave();
        frames = hover.Step(row, 1, false);
        Assert.All(frames, frame => { Assert.Equal(1, frame.Scale); Assert.Equal(0, frame.OffsetX); });
        hover.SetPointer(164, 20);
        Settle(hover, row);
        hover.Reset();
        Assert.All(hover.Step(row, 1001, true), frame =>
        {
            Assert.Equal(1, frame.Scale);
            Assert.Equal(0, frame.OffsetX);
        });
    }

    [Fact]
    public void Utilities_never_magnify_and_do_not_shift_for_hover()
    {
        var row = Row(8);
        row[^1] = row[^1] with { Magnifiable = false, Width = 120 };
        var hover = new DockHoverMagnification<object>();
        hover.SetPointer(164, 20);
        var frames = Settle(hover, row);
        Assert.Equal(1, frames[^1].Scale);
        AssertFixedPositions(row, frames);
        Assert.Equal(456, hover.ContentWidth);
        hover.SetPointer(400, 20); // Hovering the utilities cannot magnify an app.
        frames = Settle(hover, row);
        Assert.All(frames, frame => Assert.Equal(1, frame.Scale));
        var remaining = row[..3];
        Assert.Equal(3, Settle(hover, remaining).Count);
        hover.Reset();
        Assert.Empty(hover.Step([], 2001, true));
    }

    [Fact]
    public void Pause_holds_scale_and_fixed_positions_for_context_menus()
    {
        var row = Row();
        var hover = new DockHoverMagnification<object>();
        hover.SetPointer(164, 20);
        hover.Step(row, 0, true);
        var before = hover.Step(row, 60, true);
        hover.Pause(true, 60);
        Assert.Equal(before, hover.Step(row, 500, true));
        hover.Pause(false, 500);
        var after = hover.Step(row, 510, true);
        Assert.True(after[3].Scale > before[3].Scale);
        AssertFixedPositions(row, after);
    }

    [Fact]
    public void Thin_alpha_artwork_cannot_affect_button_placement_or_hover_targets()
    {
        var row = Row(13).Select((item, index) => item with
        {
            ArtworkWidth = index % 2 == 0 ? 6 : 12
        }).ToArray();
        var hover = new DockHoverMagnification<object>();
        hover.SetPointer(row[6].Left + row[6].Width / 2, 20);
        var frames = Settle(hover, row);
        Assert.Equal(1.5, frames[6].Scale, 6);
        Assert.All(row, item => Assert.True(item.Footprint >= 28));
        AssertFixedPositions(row, frames);
        Assert.Equal(616, hover.ContentWidth);
        Assert.Equal(1, frames[10].Scale, 6);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(328, 6)]
    public void Edge_hover_magnifies_only_the_edge_and_its_single_neighbor(double x, int index)
    {
        var row = Row();
        var hover = new DockHoverMagnification<object>();
        hover.SetPointer(x, 20);
        var frames = Settle(hover, row);
        Assert.Equal(1.5, frames[index].Scale, 6);
        Assert.Equal(1.25, frames[index == 0 ? 1 : index - 1].Scale, 6);
        Assert.Equal(1, frames[index == 0 ? 2 : index - 2].Scale, 6);
        AssertFixedPositions(row, frames);
    }

    [Fact]
    public void Hover_does_not_change_collapse_geometry()
    {
        var row = Row();
        var hover = new DockHoverMagnification<object>();
        hover.SetPointer(164, 20);
        hover.Step(row, 0, true);
        foreach (var progress in new[] { 1d, .75, .5, .25, 0 })
        {
            hover.Step(row, 20 + (1 - progress) * 200, true);
            var width = 400d;
            var height = 68d;
            var geometry = DockTransitionGeometry.At(progress, width, height);
            Assert.Equal(progress,
                DockTransitionTiming.Progress(geometry.Width, geometry.Height, width, height), 8);
            if (progress == 0)
            {
                Assert.Equal(120, geometry.Width);
                Assert.Equal(5, geometry.Height);
            }
        }
    }
}
