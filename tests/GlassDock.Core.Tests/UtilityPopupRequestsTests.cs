using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class UtilityPopupRequestsTests
{
    [Fact]
    public void Utility_deactivation_before_click_does_not_toggle_twice()
    {
        var requests = new UtilityPopupRequests();
        requests.Click(5);
        for (var i = 0; i < 20; i++)
        {
            if (UtilityPopupRequests.ShouldDismissOnDeactivation(true, i, i))
                requests.ClosingExternally();
            requests.Click(5);
            Assert.False(requests.TargetOpen);
            requests.Click(5);
            Assert.True(requests.TargetOpen);
        }
        Assert.False(UtilityPopupRequests.ShouldDismissOnDeactivation(false, 1, 2));
        Assert.True(UtilityPopupRequests.ShouldDismissOnDeactivation(false, 2, 2));
    }

    [Fact]
    public void Wifi_clock_clock_tray_tray_leaves_no_pending_popup()
    {
        var requests = new UtilityPopupRequests();
        requests.Click(2);
        requests.Click(5);
        requests.Click(5);
        Assert.Null(requests.Pending);
        requests.Click(1);
        requests.Click(1);
        requests.Closed();
        Assert.Null(requests.Active);
        Assert.False(requests.TargetOpen);
    }

    [Fact]
    public void Forced_close_discards_pending_switch()
    {
        var requests = new UtilityPopupRequests();
        requests.Click(2);
        requests.Click(5);
        requests.Reset();
        requests.Closed();
        Assert.Null(requests.Active);
        Assert.Null(requests.Pending);
        Assert.False(requests.TargetOpen);
    }

    [Fact]
    public void Twenty_toggle_cycles_reverse_one_active_popup_without_a_queue()
    {
        var requests = new UtilityPopupRequests();
        requests.Click(5);
        for (var i = 0; i < 20; i++)
        {
            requests.Click(5);
            Assert.False(requests.TargetOpen);
            requests.Click(5);
            Assert.True(requests.TargetOpen);
            Assert.Equal(5, requests.Active);
            Assert.Null(requests.Pending);
        }
    }

    [Fact]
    public void Switching_keeps_only_the_latest_target_and_can_reverse_to_original()
    {
        var requests = new UtilityPopupRequests();
        requests.Click(2);
        requests.Click(5);
        requests.Click(1);
        requests.Click(4);
        Assert.Equal(4, requests.Pending);
        Assert.Equal(2, requests.Active);
        requests.Click(2);
        Assert.True(requests.TargetOpen);
        Assert.Null(requests.Pending);
        requests.Click(5);
        requests.Closed();
        Assert.Equal(5, requests.Active);
        Assert.Null(requests.Pending);
        requests.ClosingExternally();
        requests.Closed();
        Assert.Null(requests.Active);
    }
}
