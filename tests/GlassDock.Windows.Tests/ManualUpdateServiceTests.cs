using GlassDock.App.Updates;
using Velopack;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class ManualUpdateServiceTests
{
    [Fact]
    public async Task Development_build_does_not_request_updates()
    {
        var manager = new FakeManager { Installed = false };
        var service = new ManualUpdateService(manager);
        Assert.False(service.CanUpdate);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CheckAsync(default));
        Assert.Equal(0, manager.Checks);
    }

    [Fact]
    public async Task Manual_check_exposes_installed_and_available_versions()
    {
        var manager = new FakeManager();
        var service = new ManualUpdateService(manager);
        Assert.Equal("0.1.0", service.CurrentVersion);
        Assert.Equal(0, manager.Checks); // Constructing Settings never checks the network.
        await service.CheckAsync(default);
        Assert.Equal("0.1.1", service.AvailableVersion);
        var progress = 0;
        await service.DownloadAsync(value => progress = value, default);
        Assert.Equal(100, progress);
        Assert.Equal(1, manager.Downloads);
    }

    [Fact]
    public async Task No_update_and_errors_clear_previous_offer_and_allow_retry()
    {
        var manager = new FakeManager();
        var service = new ManualUpdateService(manager);
        await service.CheckAsync(default);
        manager.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => service.CheckAsync(default));
        Assert.Null(service.AvailableVersion);
        manager.Fail = false;
        manager.Result = null;
        await service.CheckAsync(default);
        Assert.Null(service.AvailableVersion);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadAsync(_ => { }, default));
    }

    [Fact]
    public async Task Closing_during_check_discards_result_and_prevents_overlap()
    {
        var completion = new TaskCompletionSource<UpdateInfo?>();
        var manager = new FakeManager { Pending = completion.Task };
        var service = new ManualUpdateService(manager);
        using var cancellation = new CancellationTokenSource();
        var check = service.CheckAsync(cancellation.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CheckAsync(default));
        cancellation.Cancel();
        completion.SetResult(manager.Result);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check);
        Assert.Null(service.AvailableVersion);
    }

    [Fact]
    public async Task Apply_requires_a_matching_downloaded_package()
    {
        var service = new ManualUpdateService(new FakeManager());
        Assert.Throws<InvalidOperationException>(() => service.ApplyAfterExit(false));
        await service.CheckAsync(default);
        Assert.Throws<InvalidOperationException>(() => service.ApplyAfterExit(false));
    }

    [Fact]
    public async Task Timeout_releases_caller_but_retries_share_the_unfinished_request()
    {
        var completion = new TaskCompletionSource<UpdateInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = new FakeManager { Pending = completion.Task };
        var log = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var service = new ManualUpdateService(manager, TimeSpan.FromMilliseconds(100), log.Enqueue);
        await Assert.ThrowsAsync<TimeoutException>(() => service.CheckAsync(default).WaitAsync(TimeSpan.FromSeconds(3)));
        await Assert.ThrowsAsync<TimeoutException>(() => service.CheckAsync(default).WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(1, manager.Checks);
        Assert.Null(service.AvailableVersion);
        Assert.Contains(log, line => line.StartsWith("Check timeout"));
        var retry = service.CheckAsync(default);
        completion.SetResult(manager.Result);
        await retry;
        Assert.Equal(1, manager.Checks);
        Assert.Equal("0.1.1", service.AvailableVersion);
    }

    [Fact]
    public async Task Cancellation_returns_without_waiting_for_GitHub_and_late_result_is_not_applied()
    {
        var completion = new TaskCompletionSource<UpdateInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = new FakeManager { Pending = completion.Task };
        var service = new ManualUpdateService(manager);
        using var cancellation = new CancellationTokenSource();
        var check = service.CheckAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check.WaitAsync(TimeSpan.FromSeconds(3)));
        completion.SetResult(manager.Result);
        Assert.Null(service.AvailableVersion);
        manager.Pending = null;
        await service.CheckAsync(default);
        Assert.Equal("0.1.1", service.AvailableVersion);
    }

    private sealed class FakeManager() : UpdateManager("https://example.invalid",
        locator: new Velopack.Locators.TestVelopackLocator(ManualUpdateService.PackageId, "0.1.0", Path.GetTempPath()))
    {
        public bool Installed = true;
        public bool Fail;
        public int Checks;
        public int Downloads;
        public Task<UpdateInfo?>? Pending;
        public UpdateInfo? Result = new(new VelopackAsset { Version = new SemanticVersion(0, 1, 1) }, false, null, []);
        public override bool IsInstalled => Installed;
        public override string AppId => ManualUpdateService.PackageId;
        public override SemanticVersion CurrentVersion => new(0, 1, 0);
        public override VelopackAsset? UpdatePendingRestart => null;
        public override Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            Checks++;
            if (Fail) throw new IOException("Test network failure");
            return Pending ?? Task.FromResult(Result);
        }
        public override Task DownloadUpdatesAsync(UpdateInfo updates, Action<int>? progress = null, CancellationToken cancelToken = default)
        {
            cancelToken.ThrowIfCancellationRequested();
            Downloads++;
            progress?.Invoke(100);
            return Task.CompletedTask;
        }
    }
}
