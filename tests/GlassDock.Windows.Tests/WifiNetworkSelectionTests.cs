using GlassDock.Windows.Desktop;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class WifiNetworkSelectionTests
{
    private static WifiNetwork Network(byte id, uint signal, bool connected = false) =>
        new(Guid.Empty, "Network " + id, "", new[] { id }, 1, signal, connected, true, true)
        { Authentication = 7, Cipher = 4 };

    [Fact]
    public void List_is_deduplicated_connected_first_and_limited_to_three_real_ssids()
    {
        var state = new WifiNetworkSelection();
        state.Update(new[] { Network(1, 10, true), Network(2, 70), Network(2, 80), Network(3, 90), Network(4, 60),
            Network(5, 100) with { Ssid = [] } });
        Assert.Equal(new[] { "Network 1", "Network 3", "Network 2" }, state.Networks.Select(n => n.Name));
        Assert.Equal(80u, state.Networks[2].Signal);
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
}
