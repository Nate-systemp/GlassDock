using Velopack;
using Velopack.Sources;

namespace GlassDock.App.Updates;

internal sealed class ManualUpdateService
{
    internal const string RepositoryUrl = "https://github.com/Nate-systemp/GlassDock";
    internal const string PackageId = "Natesystemp.GlassDock";
    private readonly UpdateManager manager;
    private UpdateInfo? available;
    private bool busy;

    public ManualUpdateService() : this(new UpdateManager(
        new GithubSource(RepositoryUrl, null, false))) { }

    internal ManualUpdateService(UpdateManager manager) => this.manager = manager;

    public string? CurrentVersion => manager.CurrentVersion?.ToString();
    public bool CanUpdate => manager.IsInstalled && manager.AppId == PackageId;
    public string? AvailableVersion => available?.TargetFullRelease.Version.ToString();

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        if (!CanUpdate) throw new InvalidOperationException("Updates require the installed GlassDock application.");
        if (busy) throw new InvalidOperationException("An update operation is already running.");
        busy = true;
        available = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // This API has no cancellation overload in Velopack 1.2.158.
            var result = await manager.CheckForUpdatesAsync();
            cancellationToken.ThrowIfCancellationRequested();
            available = result;
        }
        finally { busy = false; }
    }

    public async Task DownloadAsync(Action<int> progress, CancellationToken cancellationToken)
    {
        if (!CanUpdate || available is null) throw new InvalidOperationException("Check for an update first.");
        if (busy) throw new InvalidOperationException("An update operation is already running.");
        busy = true;
        try { await manager.DownloadUpdatesAsync(available, progress, cancellationToken); }
        finally { busy = false; }
    }

    public void ApplyAfterExit(bool safeMode)
    {
        if (busy || available is null || !CanUpdate)
            throw new InvalidOperationException("No update is ready.");
        var pending = manager.UpdatePendingRestart;
        if (pending is null || pending.Version != available.TargetFullRelease.Version)
            throw new InvalidOperationException("The update download is not ready. Please retry.");

        // Do NOT use ApplyUpdatesAndRestart: it exits immediately. The caller
        // invokes normal GlassDock shutdown after this successfully starts Update.exe.
        manager.WaitExitThenApplyUpdates(pending, silent: false, restart: true,
            restartArgs: safeMode ? ["--safe-mode"] : []);
    }
}
