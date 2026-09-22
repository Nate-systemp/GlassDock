using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using GlassDock.Windows.Desktop;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class SystemControlTests
{
    [Theory]
    [InlineData(false, "", true, true)]
    [InlineData(true, "saved", true, true)]
    [InlineData(true, "", true, false)]
    [InlineData(false, "", false, false)]
    public void Connections_require_a_supported_network_or_saved_credentials(bool secured, string profile, bool connectable, bool expected)
    {
        var network = new WifiNetwork(Guid.NewGuid(), "test", profile, [116, 101, 115, 116], 1, 70, false, secured, connectable);
        Assert.Equal(expected, network.CanConnect);
    }

    [Fact]
    public void Native_wifi_structures_match_the_x64_Windows_SDK_ABI()
    {
        Type Layout(string name) => typeof(WifiNetworkControl).GetNestedType(name, BindingFlags.NonPublic)!;
        Assert.Equal(532, Marshal.SizeOf(Layout("Interface")));
        Assert.Equal(36, Marshal.SizeOf(Layout("Ssid")));
        Assert.Equal(628, Marshal.SizeOf(Layout("Available")));
        Assert.Equal(40, Marshal.SizeOf(Layout("Connection")));
        Assert.Equal(604, Marshal.OffsetOf(Layout("Available"), "Signal").ToInt32());
        Assert.Equal(620, Marshal.OffsetOf(Layout("Available"), "Flags").ToInt32());
        Assert.Equal(8, Marshal.OffsetOf(Layout("Connection"), "Profile").ToInt32());
    }

    [Fact]
    public void Access_denial_is_explained_without_claiming_a_successful_scan()
    {
        Assert.Contains("location permission", WifiNetworkControl.ErrorMessage(new Win32Exception(5)));
        Assert.Contains("failed", WifiNetworkControl.ErrorMessage(new Win32Exception(87)));
    }

    [Fact]
    public async Task Unconfigured_secure_network_does_not_attempt_a_connection()
    {
        var network = new WifiNetwork(Guid.Empty, "secured", "", [1], 1, 70, false, true, true);
        var result = await WifiNetworkControl.ConnectAsync(network, CancellationToken.None);
        Assert.Contains("requires setup", result);
    }
}
