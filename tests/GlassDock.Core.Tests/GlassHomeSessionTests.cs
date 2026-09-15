using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class GlassHomeSessionTests
{
    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(5, 0, false)]
    [InlineData(20, 20, false)]
    [InlineData(29, 0, false)]
    [InlineData(30, 0, true)]
    [InlineData(18, 24, true)]
    [InlineData(-31, 0, true)]
    public void MovementUsesDistanceFromOpeningPosition(double dx, double dy, bool expanded)
    {
        var home = new GlassHomeSession();
        home.Open(new(-1500, -400, 0, true));
        home.Observe(new(-1500 + dx, -400 + dy, 0, true));
        Assert.Equal(expanded ? GlassHomeState.Expanded : GlassHomeState.Compact, home.State);
    }

    [Fact]
    public void JitterDoesNotAccumulateAndExpansionIsLatched()
    {
        var home = new GlassHomeSession();
        home.Open(new(100, 100, 0, true));
        for (var i = 0; i < 100; i++) home.Observe(new(i % 2 == 0 ? 105 : 95, 100, 0, true));
        Assert.Equal(GlassHomeState.Compact, home.State);
        home.Observe(new(131, 100, 0, true));
        home.Observe(new(100, 100, 0, true));
        Assert.Equal(GlassHomeState.Expanded, home.State);
    }

    [Fact]
    public void OutsideClicksUseNewButtonEdgesAndInsideClicksDoNotHide()
    {
        var home = new GlassHomeSession();
        foreach (var button in new[] { 1, 2, 4 })
        {
            home.Open(new(100, 100, button, false));
            home.Observe(new(100, 100, button, false));
            Assert.Equal(GlassHomeState.Compact, home.State);
            home.Observe(new(100, 100, 0, true));
            home.Observe(new(100, 100, button, true));
            Assert.Equal(GlassHomeState.Compact, home.State);
            home.Observe(new(100, 100, 0, false));
            home.Observe(new(100, 100, button, false));
            Assert.Equal(GlassHomeState.Hidden, home.State);
        }
    }

    [Fact]
    public void TwentyRapidSessionsResetOriginAndInvalidateOldFocusRequests()
    {
        var home = new GlassHomeSession();
        for (var i = 0; i < 20; i++)
        {
            home.Open(new(i * 100, 0, 0, true));
            var revision = home.Revision;
            Assert.Equal(GlassHomeState.Compact, home.State);
            home.Observe(new(i * 100 + 35, 0, 0, true));
            Assert.Equal(GlassHomeState.Expanded, home.State);
            home.Hide(); // Same session exit used by Escape and shortcut toggle.
            Assert.NotEqual(revision, home.Revision);
            home.Observe(new(9999, 0, 0, true));
            Assert.Equal(GlassHomeState.Hidden, home.State);
        }
    }

    [Fact]
    public void MissingInitialCursorWaitsForFirstValidSample()
    {
        var home = new GlassHomeSession();
        home.Open(null);
        home.Observe(new(-2000, 300, 1, false)); // Baseline a button already held when sampling recovers.
        Assert.Equal(GlassHomeState.Compact, home.State);
        home.Observe(new(-2005, 300, 0, true));
        Assert.Equal(GlassHomeState.Compact, home.State);
    }
}
