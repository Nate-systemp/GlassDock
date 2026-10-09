using System.Numerics;
using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class DragLensMotionTests
{
    [Theory]
    [InlineData(500)] [InlineData(-500)] [InlineData(0)]
    public void Release_settles_exactly_into_slot_without_a_handoff_jump(float x)
    {
        var motion = new DragLensMotion(); motion.Begin(new(40, 30), 54);
        var start = motion.Position; var destination = new Vector2(x, 50);
        motion.End();
        Assert.Equal(start, motion.Position);
        var distance = Vector2.Distance(start, destination);
        for (var i = 0; i < 10; i++)
        {
            motion.Step(.02, destination, 54, null);
            var next = Vector2.Distance(motion.Position, destination);
            Assert.True(next <= distance + .0001); distance = next;
        }
        Assert.True(motion.Finished); Assert.Equal(destination, motion.Position);
    }

    [Fact]
    public void Release_target_is_independent_of_the_source_slot()
    {
        var motion = new DragLensMotion(); motion.Begin(new(300, 40), 54);
        motion.End();
        for (var i = 0; i < 10; i++) motion.Step(.02, new(620, 40), 54, null);
        Assert.Equal(new Vector2(620, 40), motion.Position);
    }
    [Theory]
    [InlineData(0, 0)]
    [InlineData(8, 35)]
    [InlineData(48, 48)]
    public void Original_grab_point_stays_under_pointer_at_any_distance(float clickX, float clickY)
    {
        var size = new Vector2(48, 48);
        var grab = DragLensMotion.GrabOffset(size, new(clickX, clickY));
        var motion = new DragLensMotion();
        motion.Begin(DragLensMotion.PointerTarget(new(300, 400), grab), 54);
        foreach (var pointer in new[] { new Vector2(310, 400), new Vector2(920, 350), new Vector2(90, 600) })
        {
            var target = DragLensMotion.PointerTarget(pointer, grab);
            motion.Step(.016, target, 54, null, precisePointer: true);
            Assert.Equal(pointer, motion.Position - grab);
        }
    }

    [Fact]
    public void Release_uses_frozen_destination_even_if_layout_target_changes()
    {
        var motion = new DragLensMotion();
        var grab = DragLensMotion.GrabOffset(new(48, 48), new(9, 32));
        motion.Begin(DragLensMotion.PointerTarget(new(800, 90), grab), 54);
        motion.Step(.016, DragLensMotion.PointerTarget(new(850, 95), grab), 54, null, precisePointer: true);
        var releasePosition = motion.Position;
        var finalSlot = new Vector2(700, 90);
        motion.End(finalSlot);
        Assert.Equal(releasePosition, motion.Position);
        for (var i = 0; i < 10; i++)
            motion.Step(.02, new(-900, 400), 54, null, precisePointer: true);
        Assert.True(motion.Finished);
        Assert.Equal(finalSlot, motion.Position);
        motion.Begin(Vector2.Zero, 54);
        motion.Step(.016, new(30, 40), 54, null, precisePointer: true);
        Assert.Equal(new Vector2(30, 40), motion.Position);
    }

    [Fact]
    public void FollowingIsBoundedAndCatchesUpWithoutOvershoot()
    {
        var motion = new DragLensMotion(); motion.Begin(Vector2.Zero, 54);
        var target = new Vector2(100, 20);
        motion.Step(1d / 60, target, 54, null);
        Assert.InRange(motion.Position.X, 1, 99);
        for (var i = 0; i < 20; i++)
        { motion.Step(1d / 60, target, 54, null); Assert.InRange(motion.Position.X, 0, 100); }
        Assert.InRange(Vector2.Distance(motion.Position, target), 0, .01f);
    }

    [Fact]
    public void MergeEmphasisWidensWithoutChangingThePointerTarget()
    {
        var motion = new DragLensMotion(); motion.Begin(new(100, 50), 54);
        motion.Step(.016, new(100, 50), 54, new(150, 50), animate: false);
        Assert.Equal(new Vector2(100, 50), motion.Position);
        Assert.True(motion.Bounds.Z > 90);
        motion.Step(.016, new(100, 50), 54, null, animate: false);
        Assert.Equal(54, motion.Bounds.Z);
    }

    [Fact]
    public void CancelFinishesAndRepeatedGesturesResetTheOutro()
    {
        var motion = new DragLensMotion(); motion.Begin(new(100, 50), 54);
        motion.Step(.05, new(100, 50), 54, null);
        motion.End();
        for (var i = 0; i < 20; i++) motion.Step(.016, Vector2.Zero, 54, null);
        Assert.True(motion.Finished); Assert.Equal(0, motion.Opacity);
        motion.Begin(new(20, 30), 54);
        Assert.False(motion.Finished); Assert.Equal(new Vector2(20, 30), motion.Position);
        motion.Step(.016, new(20, 30), 54, null, animate: false);
        Assert.Equal(1, motion.Opacity);
        motion.End(); motion.Step(.016, Vector2.Zero, 54, null, animate: false);
        Assert.True(motion.Finished); Assert.Equal(0, motion.Opacity);
    }
}
