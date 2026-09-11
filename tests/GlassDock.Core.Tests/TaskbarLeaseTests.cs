using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class TaskbarLeaseTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Missing_heartbeat_expires_even_before_hide(bool active)
    {
        var lease = new TaskbarLease(active);
        Assert.False(lease.IsExpired(TimeSpan.FromSeconds(4.9)));
        Assert.True(lease.IsExpired(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Active_session_renews_beyond_manual_test_limit_but_not_after_ui_stalls()
    {
        var lease = new TaskbarLease(true);
        lease.Hidden(TimeSpan.Zero);
        for (var seconds = 1; seconds <= 120; seconds++)
        {
            lease.Heartbeat(TimeSpan.FromSeconds(seconds));
            Assert.False(lease.IsExpired(TimeSpan.FromSeconds(seconds)));
        }
        Assert.True(lease.IsExpired(TimeSpan.FromSeconds(125)));
    }

    [Fact]
    public void Manual_test_cannot_extend_cap_by_heartbeats_or_repeated_hide()
    {
        var lease = new TaskbarLease(false);
        lease.Hidden(TimeSpan.Zero);
        lease.Heartbeat(TimeSpan.FromSeconds(59));
        lease.Hidden(TimeSpan.FromSeconds(59));
        Assert.False(lease.IsExpired(TimeSpan.FromSeconds(59)));
        Assert.True(lease.IsExpired(TimeSpan.FromSeconds(60)));
    }
}
