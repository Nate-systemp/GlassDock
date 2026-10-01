using GlassDock.Core.Applications;
using Xunit;
namespace GlassDock.Core.Tests;

public sealed class NotificationBadgeTests
{
    [Fact]
    public void IntroOnlyOnZeroToPositiveAndRemovalCanReverse()
    {
        var state = new NotificationBadgeState();
        Assert.Equal(BadgeTransition.None, state.SetCount(0));
        Assert.Equal(BadgeTransition.Appear, state.SetCount(1));
        Assert.Equal(BadgeTransition.Increase, state.SetCount(2));
        Assert.Equal(BadgeTransition.None, state.SetCount(2));
        Assert.Equal(BadgeTransition.Update, state.SetCount(1));
        Assert.Equal(BadgeTransition.Remove, state.SetCount(0));
        Assert.Equal(BadgeTransition.Appear, state.SetCount(3));
    }
    [Fact]
    public void NegativeCountsClearAndLargeCountsStayBoundedVisually()
    {
        var state = new NotificationBadgeState();
        state.SetCount(int.MaxValue);
        Assert.Equal("99+", state.Text);
        Assert.Equal(int.MaxValue, state.Count);
        Assert.Equal(BadgeTransition.Remove, state.SetCount(-1));
        Assert.Equal(0, state.Count);
    }
    [Fact]
    public void CountsUseExactAumidAndNeverGuessFromExecutableName()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["App.One"] = 3 };
        Assert.Equal(3, NotificationCounts.ForApplication(new("app.one", null), counts));
        Assert.Equal(0, NotificationCounts.ForApplication(new(null, "App.One.exe"), counts));
        Assert.Equal(0, NotificationCounts.ForApplication(new("App.Two", null), counts));
    }
}
