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
    internal static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);
    private readonly TimeSpan checkTimeout;
    private readonly Action<string>? log;
    private Task<UpdateInfo?>? pendingCheck;

    public ManualUpdateService() : this(new UpdateManager(
        new GithubSource(RepositoryUrl, null, false, new MetadataDownloader())), CheckTimeout, WriteLog) { }

    internal ManualUpdateService(UpdateManager manager, TimeSpan? checkTimeout = null, Action<string>? log = null)
    {
        this.manager = manager;
        this.checkTimeout = checkTimeout ?? CheckTimeout;
        this.log = log;
    }

    public string? CurrentVersion => manager.CurrentVersion?.ToString();
    public bool CanUpdate => manager.IsInstalled && manager.AppId == PackageId;
    public string? AvailableVersion => available?.TargetFullRelease.Version.ToString();

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        if (!CanUpdate) throw new InvalidOperationException("Updates require the installed GlassDock application.");
        if (busy) throw new InvalidOperationException("An update operation is already running.");
        busy = true;
        available = null;
        var started = System.Diagnostics.Stopwatch.StartNew();
        log?.Invoke($"Check start source={RepositoryUrl} current={CurrentVersion}");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 1.2.158 has no check cancellation overload. Keep at most one request:
            // retries join a still-running request rather than spawning abandoned work.
            if (pendingCheck is null || pendingCheck.IsCompleted)
            {
                pendingCheck = Task.Run(() => manager.CheckForUpdatesAsync());
                _ = pendingCheck.ContinueWith(task =>
                {
                    var error = task.Exception; // Observe failures even after timeout/close.
                    log?.Invoke($"Network exception type={error?.GetBaseException().GetType().Name} hresult={error?.GetBaseException().HResult:X8}");
                }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            else log?.Invoke("Joining existing check; no duplicate request started.");
            var result = await pendingCheck.WaitAsync(checkTimeout, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            available = result;
            log?.Invoke($"Check result available={AvailableVersion ?? "none"} elapsedMs={started.ElapsedMilliseconds}");
        }
        catch (TimeoutException)
        {
            log?.Invoke($"Check timeout elapsedMs={started.ElapsedMilliseconds}");
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            log?.Invoke($"Check cancelled elapsedMs={started.ElapsedMilliseconds}");
            throw;
        }
        catch (OperationCanceledException error)
        {
            log?.Invoke($"HTTP timeout elapsedMs={started.ElapsedMilliseconds} type={error.GetType().Name}");
            throw new TimeoutException("GitHub metadata request timed out.", error);
        }
        catch (Exception error)
        {
            log?.Invoke($"Check exception type={error.GetType().Name} hresult={error.HResult:X8} elapsedMs={started.ElapsedMilliseconds}");
            throw;
        }
        finally { busy = false; }
    }

    // Bound small API/feed requests, without shortening large package downloads.
    internal sealed class MetadataDownloader : HttpClientFileDownloader
    {
        // Velopack downloader timeout units are minutes, not seconds.
        private const double MetadataTimeoutMinutes = 10d / 60;
        public override Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30) =>
            base.DownloadString(url, headers, Math.Min(timeout > 0 ? timeout : MetadataTimeoutMinutes, MetadataTimeoutMinutes));
        public override Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30) =>
            base.DownloadBytes(url, headers, Math.Min(timeout > 0 ? timeout : MetadataTimeoutMinutes, MetadataTimeoutMinutes));
    }

    private static void WriteLog(string message)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassDock");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "updater.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
                File.Move(path, path + ".previous", overwrite: true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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
