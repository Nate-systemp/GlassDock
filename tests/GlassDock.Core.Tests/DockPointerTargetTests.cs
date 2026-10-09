using GlassDock.Core.Applications;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class DockPointerTargetTests
{
    private static readonly DockDragBounds[] Slots = [new(0, 10, 40, 50), new(46, 10, 86, 50), new(92, 10, 132, 50)];

    [Theory]
    [InlineData(46, 30, DockPointerMode.Stack, 1)]
    [InlineData(86, 30, DockPointerMode.Stack, 1)]
    [InlineData(89, 30, DockPointerMode.Reorder, 1)]
    [InlineData(92, 30, DockPointerMode.Stack, 2)]
    [InlineData(20, 30, DockPointerMode.Outside, -1)]
    [InlineData(89, 60, DockPointerMode.Outside, -1)]
    [InlineData(170, 30, DockPointerMode.Outside, -1)]
    [InlineData(140, 30, DockPointerMode.Reorder, 2)]
    public void Pointer_alone_selects_one_target(double x, double y, DockPointerMode mode, int index) =>
        Assert.Equal(new DockPointerTarget(mode, index), DockPointerTarget.Resolve(x, y, Slots, 0, _ => true));

    [Fact]
    public void Ineligible_icon_never_becomes_a_reorder_zone() =>
        Assert.Equal(DockPointerMode.Outside, DockPointerTarget.Resolve(60, 30, Slots, 0, _ => false).Mode);

    [Fact]
    public void Boundary_hysteresis_never_steals_the_gap()
    {
        Assert.Equal(DockPointerMode.Stack, DockPointerTarget.Resolve(86, 30, Slots, 0, _ => true, 1).Mode);
        Assert.Equal(DockPointerMode.Reorder, DockPointerTarget.Resolve(86.01, 30, Slots, 0, _ => true, 1).Mode);
    }

    [Fact]
    public void Immediate_preview_switches_and_cancels_without_a_dwell_or_persistence()
    {
        var drag = new DockStackDrag(); drag.Begin();
        for (var i = 0; i < 20; i++)
        {
            drag.PreviewTarget("b"); Assert.Equal(DockDragMode.StackMerge, drag.Mode);
            drag.PreviewTarget(null); Assert.Equal(DockDragMode.Reorder, drag.Mode); Assert.Null(drag.TargetId);
        }
        drag.Reset(); drag.PreviewTarget("b"); Assert.Equal(DockDragMode.None, drag.Mode);
        drag.Begin(true); drag.PreviewTarget("b"); Assert.Equal(DockDragMode.ExternalFiles, drag.Mode);
    }

    [Theory]
    [InlineData(1)] [InlineData(1.25)] [InlineData(1.5)] [InlineData(2)]
    public void Consistent_coordinate_transforms_preserve_targets(double scale)
    {
        var transformed = Slots.Select(b => new DockDragBounds(200 + b.Left * scale, 100 + b.Top * scale,
            200 + b.Right * scale, 100 + b.Bottom * scale)).ToArray();
        Assert.Equal(new DockPointerTarget(DockPointerMode.Stack, 1),
            DockPointerTarget.Resolve(200 + 60 * scale, 100 + 30 * scale, transformed, 0, _ => true));
        Assert.Equal(new DockPointerTarget(DockPointerMode.Reorder, 1),
            DockPointerTarget.Resolve(200 + 89 * scale, 100 + 30 * scale, transformed, 0, _ => true));
    }

    [Fact]
    public void Gap_insertion_index_accounts_for_removing_source()
    {
        Assert.Equal(1, DockPointerTarget.Resolve(43, 30, Slots, 2, _ => true).Index);
        Assert.Equal(0, DockPointerTarget.Resolve(43, 30, Slots, 0, _ => true).Index);
    }
}
