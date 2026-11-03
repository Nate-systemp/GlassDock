using GlassDock.Windows.Desktop;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class WifiNetworkCacheTests
{
    private static readonly IReadOnlyList<WifiNetwork> snapshot = new[]
    { new WifiNetwork(Guid.Empty, "Network", "", new byte[] { 65 }, 1, 70, false, true, true) };

    [Fact]
    public async Task Cached_list_survives_panel_cancellation_while_concurrent_panels_share_one_scan()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<WifiNetwork>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var scans = 0;
        using var wifi = new WifiUtilityService((scan, _) =>
        { if (scan) { Interlocked.Increment(ref scans); return completion.Task; } return Task.FromResult(snapshot); });
        await wifi.ReadNetworksAsync(false);
        Assert.Same(snapshot, wifi.CachedNetworks); Assert.True(wifi.HasNetworkSnapshot);
        using var panel = new CancellationTokenSource();
        var first = wifi.ReadNetworksAsync(true, panel.Token); var second = wifi.ReadNetworksAsync(true);
        panel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal(1, scans); Assert.Same(snapshot, wifi.CachedNetworks);
        completion.SetResult(snapshot);
        Assert.Same(snapshot, await second); Assert.Same(snapshot, wifi.CachedNetworks);
    }

    [Fact]
    public async Task Radio_off_invalidates_cache_and_late_scan_cannot_restore_stale_results()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<WifiNetwork>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var wifi = new WifiUtilityService((_, _) => completion.Task);
        var scan = wifi.ReadNetworksAsync(true); wifi.InvalidateNetworks(); completion.SetResult(snapshot);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan);
        Assert.Empty(wifi.CachedNetworks); Assert.False(wifi.HasNetworkSnapshot);
    }

    [Fact]
    public async Task Failed_scan_does_not_discard_previously_useful_content()
    {
        var scans = 0;
        using var wifi = new WifiUtilityService((scan, _) => scan && ++scans == 1
            ? Task.FromException<IReadOnlyList<WifiNetwork>>(new IOException("Scan failed")) : Task.FromResult(snapshot));
        await wifi.ReadNetworksAsync(false);
        await Assert.ThrowsAsync<IOException>(() => wifi.ReadNetworksAsync(true));
        Assert.Same(snapshot, wifi.CachedNetworks);
        Assert.Same(snapshot, await wifi.ReadNetworksAsync(true)); Assert.Equal(2, scans);
    }
}
