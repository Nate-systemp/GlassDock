using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class TaskbarLeaseTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Only_an_established_app_lifetime_lease_can_renew_after_a_long_pause(bool active)
    {
        var lease = new TaskbarLease(active);
        Assert.False(lease.CanRenewAfterTimeout);
        lease.Heartbeat(TimeSpan.FromSeconds(1));
        Assert.False(lease.CanRenewAfterTimeout);
        lease.Hidden(TimeSpan.FromSeconds(1));
        Assert.Equal(active, lease.CanRenewAfterTimeout);

        var resumed = TimeSpan.FromHours(8);
        Assert.True(lease.IsExpired(resumed));
        lease.Heartbeat(resumed);
        Assert.Equal(!active, lease.IsExpired(resumed));
        Assert.True(lease.IsExpired(resumed + TimeSpan.FromSeconds(10)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Missing_heartbeat_expires_even_before_hide(bool active)
    {
        var lease = new TaskbarLease(active);
        Assert.False(lease.IsExpired(TimeSpan.FromSeconds(9.9)));
        Assert.True(lease.IsExpired(TimeSpan.FromSeconds(10)));
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
        Assert.True(lease.IsExpired(TimeSpan.FromSeconds(130)));
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
