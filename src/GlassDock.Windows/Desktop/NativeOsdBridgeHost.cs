using System.ComponentModel;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace GlassDock.Windows.Desktop;

internal sealed class NativeOsdBridgeHost : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task completion;
    private string directory;
    private readonly string mappingName;
    private readonly int ownerId, session;
    private readonly long ownerCreated;
    private readonly Task replacementReady;
    private WindowsChildProcessJob? runningJob;
    private int disposed;
    private volatile bool available = true;
    private volatile string status = "Native OSD bridge is starting.";

    internal bool Available => available;
    internal string Status => status;

    internal NativeOsdBridgeHost(string directory, string mappingName, int ownerId, long ownerCreated,
        int session, Task replacementReady)
    {
        this.directory = directory;
        this.mappingName = mappingName;
        this.ownerId = ownerId;
        this.ownerCreated = ownerCreated;
        this.session = session;
        this.replacementReady = replacementReady;
        completion = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        var token = lifetime.Token;
        var engineConfig = "";
        var modConfig = "";
        string? originalConfig = null;
        try
        {
            await replacementReady.WaitAsync(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
            using var ownership = await AcquireOwnershipAsync(token).ConfigureAwait(false);
            directory = PrepareRuntime(directory, token);
            engineConfig = Path.Combine(directory, "AppData", "Engine", "settings.ini");
            var mods = Path.Combine(directory, "AppData", "Engine", "Mods");
            modConfig = Path.Combine(mods, "doky-brightness-osd.ini");
            foreach (var path in Directory.EnumerateFiles(mods, "*.ini"))
                if (!path.Equals(modConfig, StringComparison.OrdinalIgnoreCase) &&
                    File.ReadLines(path).Any(line => line.Trim().Equals("Disabled=0", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Another Windhawk mod is enabled.");
            using var mapping = MemoryMappedFile.OpenExisting(mappingName, MemoryMappedFileRights.ReadWrite);
            using var view = mapping.CreateViewAccessor(0, 128, MemoryMappedFileAccess.ReadWrite);
            originalConfig = await File.ReadAllTextAsync(engineConfig, token).ConfigureAwait(false);
            var shellPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            await File.WriteAllTextAsync(engineConfig,
                $"[Settings]\nLoggingVerbosity=1\nInclude={shellPath}\nExclude=*\nInjectIntoCriticalProcesses=0\nInjectIntoIncompatiblePrograms=0\nInjectIntoGames=0\n", token).ConfigureAwait(false);
            var failures = 0;
            var recoveryWindow = Stopwatch.StartNew();
            while (!token.IsCancellationRequested)
            {
                if (recoveryWindow.Elapsed > TimeSpan.FromMinutes(1)) { failures = 0; recoveryWindow.Restart(); }
                if (failures++ >= 3) throw new InvalidOperationException("Native OSD recovery exceeded three attempts in one minute.");
                using var shell = await WaitForShellAsync(token).ConfigureAwait(false);
                RejectOtherEngines();
                using var job = new WindowsChildProcessJob();
                Volatile.Write(ref runningJob, job);
                Process? engine = null;
                try
                {
                    token.ThrowIfCancellationRequested();
                    view.Write(100, 0);
                    await File.WriteAllTextAsync(modConfig,
                        $"[Mod]\nLibraryFileName=doky-brightness-osd-0.1.0.dll\nDisabled=0\nLoggingEnabled=1\nDebugLoggingEnabled=0\nInclude={shellPath}\nArchitecture=x86-64\nVersion=0.1.0\n[Settings]\nenabled=1\nownerPid={ownerId}\nownerCreated={ownerCreated}\n", token).ConfigureAwait(false);
                    engine = job.Start(Path.Combine(directory, "windhawk.exe"), "-tray-only");
                    view.Write(92, engine.Id);
                    view.Write(96, shell.Id);
                    using (var startup = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        startup.CancelAfter(TimeSpan.FromSeconds(15));
                        while (view.ReadInt32(100) != 1)
                        {
                            if (engine.HasExited) throw new InvalidOperationException("Windhawk exited before native OSD activation.");
                            await Task.Delay(50, startup.Token).ConfigureAwait(false);
                        }
                    }
                    status = "Native brightness and volume suppression is active.";
                    view.Write(88, 1);
                    await WaitForExitAsync(engine, shell, token).ConfigureAwait(false);
                    status = "Native OSD bridge is recovering.";
                }
                finally
                {
                    view.Write(88, 0);
                    view.Write(92, 0);
                    if (engine is not null)
                    {
                        await StopEngineAsync(job, engine).ConfigureAwait(false);
                        engine.Dispose();
                    }
                    Volatile.Write(ref runningJob, null);
                }
                await Task.Delay(250, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            status = "Windows retains native OSD handling: " + error.Message;
        }
        finally
        {
            available = false;
            try
            {
                if (originalConfig is not null)
                {
                    await File.WriteAllTextAsync(modConfig,
                        "[Mod]\nLibraryFileName=doky-brightness-osd-0.1.0.dll\nDisabled=1\n[Settings]\nenabled=0\n").ConfigureAwait(false);
                    await File.WriteAllTextAsync(engineConfig, originalConfig).ConfigureAwait(false);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                status = "Native OSD shutdown could not restore its private configuration: " + error.Message;
            }
        }
    }

    private static string PrepareRuntime(string payload, CancellationToken token)
    {
        var files = Directory.GetFiles(payload, "*", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);
        using var identity = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var hashes = new List<byte[]>(files.Length);
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            using var source = File.OpenRead(file);
            var hash = SHA256.HashData(source);
            hashes.Add(hash);
            identity.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(payload, file)));
            identity.AppendData(hash);
        }
        var runtime = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GlassDock", "NativeOsd", Convert.ToHexString(identity.GetHashAndReset()));
        for (var index = 0; index < files.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var target = Path.Combine(runtime, Path.GetRelativePath(payload, files[index]));
            if (File.Exists(target))
            {
                using var existing = File.OpenRead(target);
                if (SHA256.HashData(existing).AsSpan().SequenceEqual(hashes[index])) continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(files[index], target, true);
        }
        return runtime;
    }

    private async Task<FileStream> AcquireOwnershipAsync(CancellationToken token)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GlassDock", $"native-osd-{session}.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var deadline = Environment.TickCount64 + 5000;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (Environment.TickCount64 < deadline)
            { await Task.Delay(100, token).ConfigureAwait(false); }
        }
    }

    private static void RejectOtherEngines()
    {
        foreach (var process in Process.GetProcessesByName("windhawk"))
            using (process)
                if (!process.HasExited) throw new InvalidOperationException("Another Windhawk instance is running.");
    }

    private static async Task<Process> WaitForShellAsync(CancellationToken token)
    {
        var deadline = Environment.TickCount64 + 30000;
        while (Environment.TickCount64 < deadline)
        {
            token.ThrowIfCancellationRequested();
            GetWindowThreadProcessId(GetShellWindow(), out var pid);
            if (pid != 0)
            {
                try { return Process.GetProcessById((int)pid); }
                catch (ArgumentException) { }
            }
            await Task.Delay(200, token).ConfigureAwait(false);
        }
        throw new TimeoutException("The Windows Shell did not become available.");
    }

    private static async Task WaitForExitAsync(Process engine, Process shell, CancellationToken token)
    {
        using var cycle = CancellationTokenSource.CreateLinkedTokenSource(token);
        var engineExit = engine.WaitForExitAsync(cycle.Token);
        var shellExit = shell.WaitForExitAsync(cycle.Token);
        try { await Task.WhenAny(engineExit, shellExit).WaitAsync(token).ConfigureAwait(false); }
        finally
        {
            cycle.Cancel();
            try { await Task.WhenAll(engineExit, shellExit).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        token.ThrowIfCancellationRequested();
    }

    private async Task StopEngineAsync(WindowsChildProcessJob job, Process engine)
    {
        if (engine.HasExited) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var command = job.Start(Path.Combine(directory, "windhawk.exe"), "-exit -wait -timeout 2500");
            await command.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await engine.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is OperationCanceledException or Win32Exception or ObjectDisposedException)
        { status = "Native OSD shutdown is closing its owned process job: " + error.Message; }
    }

    internal Task StopAsync()
    {
        if (Volatile.Read(ref disposed) == 0) lifetime.Cancel();
        return completion;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        Volatile.Read(ref runningJob)?.Dispose();
        _ = completion.ContinueWith(_ => lifetime.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
}
