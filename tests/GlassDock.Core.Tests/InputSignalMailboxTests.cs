using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class InputSignalMailboxTests
{
    [Fact]
    public void Burst_posts_once_and_keeps_only_latest_intent()
    {
        var mailbox = new InputSignalMailbox();
        Assert.True(mailbox.Publish(new(false, 1, 100)));
        for (var i = 101; i < 1000; i++) Assert.False(mailbox.Publish(new(true, 2, i)));
        Assert.Equal(new InputSignal(true, 2, 999), mailbox.Take(1000));
        Assert.Null(mailbox.Take(1000));
        Assert.True(mailbox.Publish(new(false, 2, 1001)));
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
}
