using System.Diagnostics;

namespace GlassDock.Windows.Desktop;

/// <summary>A bounded child-owned visibility lease; the UI never performs the hide operation.</summary>
public sealed class TaskbarDevelopmentSession : IAsyncDisposable
{
    private readonly Process process;
    private readonly SemaphoreSlim writerLock = new(1);
    private Task? monitor;
    private bool stopping;
    public bool IsActive { get; private set; }
    public event EventHandler<string>? Ended;

    private TaskbarDevelopmentSession(Process process) => this.process = process;

    public static async Task<TaskbarDevelopmentSession> StartAsync(string helperPath)
    {
        if (!File.Exists(helperPath)) throw new FileNotFoundException("Build the solution to deploy the recovery helper.", helperPath);
        using var parent = Process.GetCurrentProcess();
        var info = new ProcessStartInfo(helperPath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true
        };
        info.ArgumentList.Add("--watch");
        info.ArgumentList.Add(parent.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        info.ArgumentList.Add(parent.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var child = Process.Start(info) ?? throw new InvalidOperationException("Cannot start recovery helper.");
        var session = new TaskbarDevelopmentSession(child);
        try
        {
            var ready = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (ready != "READY") throw new InvalidOperationException(ready ?? "Recovery helper exited before readiness.");
            await session.SendAsync("HIDE");
            var hidden = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (hidden != "HIDDEN") throw new InvalidOperationException(hidden ?? "Taskbar test was not started.");
            session.IsActive = true;
            session.monitor = session.MonitorAsync();
            return session;
        }
        catch
        {
            child.StandardInput.Close(); // EOF tells the child to restore even after a lost acknowledgement.
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(7));
            TaskbarRecovery.RestoreNow();
            child.Dispose();
            throw;
        }
    }

    public async Task HeartbeatAsync()
    {
        if (!IsActive || stopping) return;
        try { await SendAsync("PING"); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            TaskbarRecovery.RestoreNow();
            IsActive = false;
        }
    }

    private async Task SendAsync(string command)
    {
        await writerLock.WaitAsync();
        try
        {
            await process.StandardInput.WriteLineAsync(command);
            await process.StandardInput.FlushAsync();
        }
        finally { writerLock.Release(); }
    }

    private async Task MonitorAsync()
    {
        var status = "Recovery helper ended.";
        try
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line) status = line;
            await process.WaitForExitAsync();
        }
        finally
        {
            IsActive = false;
            // Independent fallback if the helper died before its finally block.
            if (!stopping && !status.StartsWith("RESTORED", StringComparison.Ordinal))
                TaskbarRecovery.RestoreNow();
            Ended?.Invoke(this, status);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (stopping) return;
        stopping = true;
        try
        {
            if (!process.HasExited) await SendAsync("RESTORE");
            if (monitor is not null) await monitor.WaitAsync(TimeSpan.FromSeconds(7));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            TaskbarRecovery.RestoreNow();
        }
        finally
        {
            IsActive = false;
            process.Dispose();
        }
    }
}
