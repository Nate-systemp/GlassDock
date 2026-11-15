using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class UtilityHudBounceTests
{
    [Fact]
    public void Early_release_finishes_the_whole_bounce()
    {
        var bounce = new UtilityHudBounce(.6);
        bounce.Start(0, true);
        bounce.Release(20);
        Assert.Equal(.6, bounce.Sample(210), 6);
        Assert.Equal(1, bounce.Sample(350));
        Assert.False(bounce.Active);
    }

    [Fact]
    public void Hold_freezes_exact_peak_and_release_resumes_return()
    {
        var bounce = new UtilityHudBounce(.6);
        bounce.Start(0, true);
        Assert.Equal(.6, bounce.Sample(5000));
        Assert.True(bounce.Held);
        bounce.Release(5000);
        Assert.Equal(.8, bounce.Sample(5070), 6);
        Assert.Equal(1, bounce.Sample(5140));
        Assert.False(bounce.Active);
    }

    [Fact]
    public void New_tap_restarts_timeline_and_cancel_clears_hold()
    {
        var bounce = new UtilityHudBounce(.6);
        bounce.Start(0, false);
        Assert.Equal(.8, bounce.Sample(280), 6);
        bounce.Start(280, true);
        Assert.Equal(0, bounce.Sample(280));
        Assert.Equal(.6, bounce.Sample(1000));
        bounce.Cancel();
        Assert.False(bounce.Active);
        Assert.False(bounce.Held);
    }
}
