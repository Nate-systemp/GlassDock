using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class DockReversalTests
{
    [Fact]
    public void Five_hundred_toggles_with_interleaved_completions_and_holds_finish_collapsed()
    {
        var state = new DockStateMachine();
        state.Show();
        var stale = new List<long>();
        for (var i = 0; i < 500; i++)
        {
            var opening = i % 2 == 0;
            var revision = opening ? state.Expand() : state.Collapse();
            stale.Add(revision);
            if (i % 7 == 0)
            {
                state.HoldTransition();
                state.Complete(revision);
                Assert.Equal(opening ? DockState.Expanding : DockState.Collapsing, state.State);
            }
            // A zero-duration (reduced-motion) completion may happen immediately.
            if (i % 3 == 0) state.Complete(state.Revision);
            var expected = state.State;
            foreach (var old in stale.Where(value => value != state.Revision)) state.Complete(old);
            Assert.Equal(expected, state.State);
        }
        state.Complete(state.Revision);
        Assert.Equal(DockState.Idle, state.State);
        state.Hide();
        foreach (var old in stale) state.Complete(old);
        Assert.Equal(DockState.Hidden, state.State);
    }

    [Fact]
    public void Reversal_is_direct_and_stale_completion_cannot_finalize_it()
    {
        var state = new DockStateMachine();
        state.Show();
        var opening = state.Expand();
        Assert.Equal(DockState.Expanding, state.State);
        var closing = state.Collapse();
        Assert.Equal(DockState.Collapsing, state.State);
        state.Complete(opening);
        Assert.Equal(DockState.Collapsing, state.State);
        var reopening = state.Expand();
        Assert.Equal(DockState.Expanding, state.State);
        state.Complete(closing);
        Assert.Equal(DockState.Expanding, state.State);
        state.Complete(reopening);
        Assert.Equal(DockState.Expanded, state.State);
        state.Complete(state.Collapse());
        Assert.Equal(DockState.Idle, state.State);
    }

    [Theory]
    [InlineData(50, DockState.Collapsing, DockState.Idle)]
    [InlineData(51, DockState.Expanding, DockState.Expanded)]
    [InlineData(1000, DockState.Collapsing, DockState.Idle)]
    public void Every_press_changes_target_without_waiting(int presses, DockState moving, DockState settled)
    {
        var state = new DockStateMachine(); state.Show();
        var old = new List<long>();
        for (var i = 0; i < presses; i++)
        {
            old.Add(state.Revision);
            if (state.State is DockState.Expanding or DockState.Expanded) state.Collapse();
            else state.Expand();
            Assert.Equal(i % 2 == 0 ? DockState.Expanding : DockState.Collapsing, state.State);
        }
        foreach (var revision in old) state.Complete(revision);
        Assert.Equal(moving, state.State);
        state.Complete(state.Revision);
        Assert.Equal(settled, state.State);
    }

    [Fact]
    public void Shutdown_invalidates_both_directions_and_refuses_reversal()
    {
        foreach (var closing in new[] { false, true })
        {
            var state = new DockStateMachine(); state.Show(); state.Expand();
            if (closing) state.Collapse();
            var revision = state.Revision;
            state.Hide(); state.Complete(revision); state.Expand(); state.Collapse();
            Assert.Equal(DockState.Hidden, state.State);
        }
    }

    [Theory]
    [InlineData(.1, false, 45)]
    [InlineData(.9, false, 270)]
    [InlineData(.5, true, 190)]
    [InlineData(0, true, 380)]
    [InlineData(1, false, 300)]
    public void Retarget_duration_scales_with_remaining_distance(double progress, bool expanded, double ms) =>
        Assert.Equal(ms, DockTransitionTiming.Duration(expanded, progress), 6);

    [Theory]
    [InlineData(0)] [InlineData(.1)] [InlineData(.4)] [InlineData(.65)] [InlineData(1)]
    public void Progress_uses_current_geometry(double progress) =>
        Assert.Equal(progress, DockTransitionTiming.Progress(120 + 780 * progress, 5 + 63 * progress, 900, 68), 6);
}
