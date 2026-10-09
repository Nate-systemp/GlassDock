using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class TrayOrderTests
{
    [Fact]
    public void Double_click_dispatches_one_default_action_without_inventing_a_double_click()
    {
        var gate = new TrayActivationGate();
        Assert.True(gate.TryAccept(1000, 500));
        Assert.False(gate.TryAccept(1100, 500));
        Assert.False(gate.TryAccept(1500, 500));
        Assert.True(gate.TryAccept(1501, 500));
    }
    [Fact]
    public void Reordering_keeps_every_icon_once_and_survives_absence_and_return()
    {
        var order = new TrayOrder(["a", "b", "c"]);
        Assert.True(order.Move("a", "c", true, ["a", "b", "c", "c"]));
        Assert.Equal(new[] { "b", "c", "a" }, order.Apply(["c", "a", "b"]));
        var restarted = new TrayOrder(order.Snapshot());
        Assert.Equal(new[] { "c", "a" }, restarted.Apply(["a", "c"]));
        Assert.Equal(new[] { "b", "c", "a", "new" }, restarted.Apply(["new", "a", "b", "c"]));
    }

    [Fact]
    public void Invalid_or_cancelled_drop_does_not_lose_icons()
    {
        var order = new TrayOrder(["a", "b"]);
        Assert.False(order.Move("missing", "b", true, ["a", "b"]));
        Assert.False(order.Move("a", "a", true, ["a", "b"]));
        Assert.Equal(new[] { "a", "b" }, order.Apply(["b", "a"]));
    }

    [Fact]
    public void Executable_identity_never_matches_on_basename_or_tooltip_alone()
    {
        Assert.True(TrayIdentity.SameExecutable(@"C:\Apps\App.exe", "c:/apps/app.exe"));
        Assert.False(TrayIdentity.SameExecutable(@"C:\Apps\App.exe", @"D:\Other\App.exe"));
        Assert.False(TrayIdentity.SameExecutable(@"C:\Apps\App.exe", null));
        Assert.Equal(TrayIdentity.Key(true, "old tooltip", @"C:\app.exe"), TrayIdentity.Key(true, "new tooltip", @"C:\app.exe"));
        Assert.NotEqual(TrayIdentity.Key(true, "App", @"C:\app.exe"), TrayIdentity.Key(false, "App", @"C:\app.exe"));
    }
}
