using GlassDock.Windows.Desktop;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class WifiNetworkSelectionTests
{
    [Fact]
    public void Saved_network_does_not_require_password_entry()
    {
        var network = Network(1, 80);
        Assert.True(network.RequiresPassword);
        Assert.False((network with { Profile = "Windows-saved-profile" }).RequiresPassword);
        Assert.False((network with { Connected = true }).RequiresPassword);
        Assert.False((network with { Secured = false }).RequiresPassword);
    }

    private static WifiNetwork Network(byte id, uint signal, bool connected = false) =>
        new(Guid.Empty, "Network " + id, "", new[] { id }, 1, signal, connected, true, true)
        { Authentication = 7, Cipher = 4 };

    [Fact]
    public void Discovery_retains_all_real_ssids_for_scroll_and_orders_connected_first()
    {
        var state = new WifiNetworkSelection();
        state.Update(new[] { Network(1, 10, true), Network(2, 70), Network(2, 80), Network(3, 90), Network(4, 60),
            Network(5, 100) with { Ssid = [] } });
        Assert.Equal(new[] { "Network 1", "Network 3", "Network 2", "Network 4" }, state.Networks.Select(n => n.Name));
        Assert.Equal(80u, state.Networks[2].Signal);
        state.Select(state.Networks[3].Key);
        Assert.Equal("Network 4", state.Selected!.Name);
    }

    [Fact]
    public void Selecting_another_network_collapses_previous_and_refresh_preserves_identity()
    {
        var state = new WifiNetworkSelection(); var first = Network(1, 80); var second = Network(2, 60);
        state.Update(new[] { first, second }); state.Select(first.Key);
        Assert.Equal(first, state.Selected);
        state.Select(second.Key); Assert.Equal(second, state.Selected);
        Assert.False(state.Update(new[] { second with { Signal = 99, Connected = true }, first }));
        Assert.True(state.Selected!.Connected);
        Assert.True(state.Update(new[] { first })); Assert.Null(state.Selected);
        state.Select(first.Key); state.Clear(); Assert.Null(state.Selected); Assert.Empty(state.Networks);
    }

    [Fact]
    public void Connection_form_retains_original_networks_until_back_or_actual_disappearance()
    {
        var state = new WifiNetworkSelection();
        var original = new[] { Network(1, 90), Network(2, 80), Network(3, 70) };
        state.Update(original); state.Select(original[2].Key);
        var refreshed = new[] { Network(4, 100), original[2] with { Signal = 20, Connected = true }, original[1], original[0] };
        Assert.False(state.Update(refreshed));
        Assert.Equal(original.Select(n => n.Key), state.Networks.Take(3).Select(n => n.Key));
        Assert.Equal(4, state.Networks.Count);
        Assert.True(state.Selected!.Connected);
        state.Select(null); state.Update(refreshed);
        Assert.Equal(new[] { "Network 3", "Network 4", "Network 1", "Network 2" }, state.Networks.Select(n => n.Name));
        state.Select(original[2].Key);
        Assert.True(state.Update(new[] { original[0], original[1] }));
        Assert.Null(state.Selected);
    }
}
