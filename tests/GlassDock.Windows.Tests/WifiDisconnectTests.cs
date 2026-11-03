using GlassDock.Windows.Desktop;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class WifiDisconnectTests
{
    private static WifiNetwork Network => new(Guid.Empty, "Selected", "Profile", new byte[] { 1, 2 }, 1, 80, true, true, true)
        { Authentication = 7, Cipher = 4 };

    [Theory]
    [InlineData(0u)] // Not ready
    [InlineData(3u)] // Disconnecting
    [InlineData(5u)] // Associating
    [InlineData(6u)] // Discovering
    [InlineData(7u)] // Authenticating
    public void Transitional_or_unavailable_states_never_confirm_disconnection(uint state)
    {
        Assert.False(WifiNetworkControl.DisconnectConfirmed(Network, state, Array.Empty<byte>()));
    }

    [Fact]
    public void Disconnected_interface_confirms_without_requiring_a_scan_entry()
    {
        Assert.True(WifiNetworkControl.DisconnectConfirmed(Network, 4, Array.Empty<byte>()));
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    public void Current_connection_must_match_ssid_before_disconnecting(uint state)
    {
        var network = Network;
        Assert.False(WifiNetworkControl.DisconnectConfirmed(network, state, Array.Empty<byte>()));
        Assert.False(WifiNetworkControl.DisconnectConfirmed(network, state, network.Ssid));
        Assert.True(WifiNetworkControl.DisconnectConfirmed(network, state, new byte[] { 3 }));
    }
}
