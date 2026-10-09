using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class InputSignalMailboxTests
{
    [Fact]
    public void Burst_delivers_every_distinct_release_in_order_without_coalescing()
    {
        var mailbox = new InputSignalMailbox();
        for (var i = 100; i < 200; i++) Assert.True(mailbox.Publish(new(false, 1, i)));
        for (var i = 100; i < 200; i++) Assert.Equal(new InputSignal(false, 1, i), mailbox.Take(200));
        Assert.Null(mailbox.Take(200));
        Assert.True(mailbox.Publish(new(false, 2, 1001)));
    }
    [Fact]
    public void Ownership_change_discards_pending_input()
    {
        var mailbox = new InputSignalMailbox();
        mailbox.Publish(new(false, 1, 100));
        mailbox.Publish(new(false, 1, 101));
        mailbox.Clear();
        Assert.Null(mailbox.Take(102));
    }
    [Fact]
    public void Stalled_ui_discards_old_input_instead_of_replaying_it()
    {
        var mailbox = new InputSignalMailbox();
        mailbox.Publish(new(false, 0, 10));
        Assert.Null(mailbox.Take(511));
        Assert.True(mailbox.Publish(new(false, 0, 512)));
        Assert.NotNull(mailbox.Take(512));
    }
    [Theory]
    [InlineData(0x5B)]
    [InlineData(0x5C)]
    public void Repeated_bare_keys_and_shortcuts_do_not_leave_stale_state(int win)
    {
        var gesture = new WindowsKeyGesture();
        for (var i = 0; i < 100; i++)
        {
            foreach (var key in new[] { 0x45, 0x52, 0x44, 0x4C, 0x49, 0x09, 0x25, 0x26, 0x20 })
            {
                gesture.Process(win, true);
                Assert.True(gesture.IsWindowsHeld);
                gesture.Process(key, true);
                gesture.Process(key, false);
                Assert.False(gesture.Process(win, false));
                Assert.False(gesture.IsWindowsHeld);
            }
            gesture.Process(win, true);
            Assert.True(gesture.Process(win, false));
            Assert.False(gesture.Process(win, false));
        }
    }

    [Fact]
    public void Lifecycle_gate_coalesces_resume_and_unlock_until_the_next_boundary()
    {
        var gate = new InputLifecycleRecoveryGate();
        gate.MarkBoundary();
        Assert.True(gate.RequestRecovery());
        Assert.False(gate.RequestRecovery());
        Assert.False(gate.RequestRecovery());

        gate.MarkBoundary();
        Assert.True(gate.RequestRecovery());
        Assert.False(gate.RequestRecovery());
    }

    [Fact]
    public void Lifecycle_gate_recovers_when_resume_arrives_without_a_suspend_event()
    {
        var gate = new InputLifecycleRecoveryGate();
        Assert.True(gate.RequestRecovery());
        Assert.False(gate.RequestRecovery());
        gate.MarkBoundary();
        Assert.True(gate.RequestRecovery());
    }

    [Fact]
    public void Gesture_reset_releases_a_key_stuck_down_across_lock_or_sleep()
    {
        var gesture = new WindowsKeyGesture();
        gesture.Process(0x5B, true);
        gesture.Process(0x45, true); // Win+E disqualifies the gesture.
        gesture.Reset();

        gesture.Process(0x5B, true);
        Assert.True(gesture.Process(0x5B, false));
        Assert.False(gesture.IsWindowsHeld);
    }

    [Fact]
    public void Sleep_wake_requests_one_toggle_recovery_then_accepts_the_next_cycle()
    {
        var gate = new InputLifecycleRecoveryGate();
        gate.MarkBoundary();
        Assert.True(gate.RequestRecovery());
        Assert.False(gate.RequestRecovery()); // paired resume notification

        gate.MarkBoundary();
        Assert.True(gate.RequestRecovery());
    }

    [Fact]
    public void Lock_unlock_and_display_wake_share_the_same_recovery_boundary()
    {
        var gate = new InputLifecycleRecoveryGate();
        gate.MarkBoundary(); // lock or display off
        Assert.True(gate.RequestRecovery()); // unlock or display on
        Assert.False(gate.RequestRecovery()); // second resume broadcast
    }

    [Fact]
    public void Shutdown_cancels_recovery_that_is_still_pending()
    {
        var gate = new InputLifecycleRecoveryGate();
        gate.MarkBoundary();
        Assert.True(gate.RequestRecovery());
        gate.Shutdown();
        Assert.False(gate.RequestRecovery());
        gate.MarkBoundary();
        Assert.False(gate.RequestRecovery());
    }
}
