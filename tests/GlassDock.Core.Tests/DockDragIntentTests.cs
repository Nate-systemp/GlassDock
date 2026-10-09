using GlassDock.Core.Applications;
using Xunit;
namespace GlassDock.Core.Tests;

public sealed class DockDragIntentTests
{
    private static readonly DockDragBounds[] Bounds = [new(0, 0, 40, 40), new(46, 0, 86, 40), new(92, 0, 132, 40)];
    [Fact]
    public void Normal_drag_never_stacks_even_on_an_eligible_icon()
    {
        var intent = new DockDragIntent(); intent.Begin(false);
        for (var x = 0; x < 150; x++)
            Assert.NotEqual(DockPointerMode.Stack, intent.Resolve(x, 20, Bounds, 0, _ => true).Mode);
    }
    [Fact]
    public void One_physical_right_press_toggles_once_across_duplicate_events()
    {
        var intent = new DockDragIntent(); intent.Begin(false);
        Assert.True(intent.Observe(true, true));
        for (var i = 0; i < 10; i++) Assert.False(intent.Observe(true, true));
        Assert.True(intent.StackMode);
        Assert.False(intent.Observe(true, false)); Assert.True(intent.StackMode);
        Assert.True(intent.Observe(true, true)); Assert.False(intent.StackMode);
    }
    [Fact]
    public void Stack_mode_only_accepts_eligible_pointer_targets_never_gaps()
    {
        var intent = new DockDragIntent(); intent.Observe(true, true);
        Assert.Equal(DockPointerMode.Stack, intent.Resolve(60, 20, Bounds, 0, _ => true).Mode);
        foreach (var x in new[] { 20, 43, 89, 140, 200 })
            Assert.Equal(DockPointerMode.Outside, intent.Resolve(x, 20, Bounds, 0, _ => true).Mode);
        Assert.Equal(DockPointerMode.Outside, intent.Resolve(60, 20, Bounds, 0, _ => false).Mode);
    }
    [Fact]
    public void Left_release_while_right_is_held_preserves_intent_until_commit_then_reset()
    {
        var intent = new DockDragIntent(); intent.Observe(true, true);
        Assert.False(intent.Observe(false, true)); Assert.True(intent.StackMode);
        intent.Reset(); Assert.False(intent.StackMode);
        Assert.False(intent.Observe(false, true)); Assert.False(intent.StackMode);
        intent.Begin(true); Assert.False(intent.Observe(true, true)); Assert.False(intent.StackMode);
        intent.Observe(true, false); Assert.True(intent.Observe(true, true));
    }
    [Fact]
    public void Cancelling_or_starting_next_drag_restores_normal_reorder()
    {
        var intent = new DockDragIntent(); intent.Observe(true, true); intent.Reset();
        Assert.Equal(DockPointerMode.Reorder, intent.Resolve(60, 20, Bounds, 0, _ => true).Mode);
        intent.Begin(false); Assert.False(intent.StackMode);
    }
}
