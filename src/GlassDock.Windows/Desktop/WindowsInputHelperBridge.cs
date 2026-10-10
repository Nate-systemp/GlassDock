using GlassDock.Core.Desktop;
using GlassDock.Core.Applications;
using GlassDock.Windows.Interop;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Threading.Channels;
using System.Text;
using GlassDock.Windows.Applications;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace GlassDock.Windows.Desktop;

// Persistent connection. UI only posts ownership changes and replaces the latest state.
// No pipe operations run on a keyboard callback or the UI thread.
internal sealed class WindowsInputHelperBridge : IDisposable
{
    private readonly string helperPath;
    private readonly Action<bool> ownership;
    private readonly Action<InputSignal> signal;
    private readonly Action<uint> snippingShortcut;
    private readonly Action<uint> snippingCanceled;
    private readonly Action<int, uint> shortcutRequested;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Channel<string> states = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
    { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private TaskCompletionSource? grant;
    private string state = "STATE|0|1|0";
    private readonly Channel<string> commands = Channel.CreateBounded<string>(1);
    private sealed record RestorePending(string Id, TaskCompletionSource<bool> Completion);
    private RestorePending? restorePending;
    private int restoreHelperPid;
    private CancellationTokenSource? attempt;
    private Task runTask = Task.CompletedTask;
    private int recovering, supportsRecovery;
    private RestorePending? recoveryPending;

    public async Task RecoverAsync()
    {
        if (lifetime.IsCancellationRequested || Interlocked.Exchange(ref recovering, 1) != 0) return;
        try
        {
            if (Volatile.Read(ref supportsRecovery) != 0 && !runTask.IsCompleted)
            {
                var request = new RestorePending(Guid.NewGuid().ToString("N"),
                    new(TaskCreationOptions.RunContinuationsAsynchronously));
                Volatile.Write(ref recoveryPending, request);
                try
                {
                    var recoveryCommand = WindowsInputHelperProtocol.CreateRecoveryCommand(
                        request.Id, Volatile.Read(ref state));
                    if (commands.Writer.TryWrite(recoveryCommand) &&
                        await request.Completion.Task.WaitAsync(TimeSpan.FromSeconds(4), lifetime.Token))
                    { Log("Resume: existing helper hook recovered; process retained."); return; }
                }
                catch (TimeoutException) { Log("Resume: helper recovery acknowledgement timed out."); }
                finally { Volatile.Write(ref recoveryPending, null); }
            }
            // Old/unresponsive/disconnected helper: EOF releases its hook. Never
            // install a competing hook or launch another helper until it exits.
            attempt?.Cancel();
            await runTask.WaitAsync(TimeSpan.FromSeconds(8), lifetime.Token);
            if (lifetime.IsCancellationRequested) return;
            attempt?.Dispose();
            attempt = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            runTask = RunAsync(attempt.Token);
            Log("Resume: starting a bounded helper reconnect.");
        }
        catch (Exception error) when (error is OperationCanceledException or TimeoutException)
        { if (!lifetime.IsCancellationRequested) Log($"Recovery deferred: {error.GetType().Name}; old helper ownership retained until exit."); }
        finally { Interlocked.Exchange(ref recovering, 0); }
    }

    public async Task<bool> RestoreWindowAsync(ApplicationWindow window)
    {
        var pid = Volatile.Read(ref restoreHelperPid);
        if (pid == 0 || lifetime.IsCancellationRequested) return false;
        var request = new RestorePending(Guid.NewGuid().ToString("N"),
            new(TaskCreationOptions.RunContinuationsAsynchronously));
        // One in-flight user activation, never an accumulating click queue.
        if (Interlocked.CompareExchange(ref restorePending, request, null) is not null) return false;
        try
        {
            if (!AllowSetForegroundWindow((uint)pid) || !commands.Writer.TryWrite(
                $"RESTORE|{request.Id}|{window.Handle}|{window.ProcessId}|{window.ProcessStartTicks}|{Environment.TickCount64}")) return false;
            return await request.Completion.Task.WaitAsync(TimeSpan.FromSeconds(2), lifetime.Token);
        }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException) { return false; }
        finally { Interlocked.CompareExchange(ref restorePending, null, request); }
    }

    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);

    public WindowsInputHelperBridge(string helperPath, Action<bool> ownership, Action<InputSignal> signal,
        Action<uint> snippingShortcut, Action<uint> snippingCanceled, Action<int, uint> shortcutRequested)
    {
        this.helperPath = Path.GetFullPath(helperPath);
        this.ownership = ownership;
        this.signal = signal;
        this.snippingShortcut = snippingShortcut;
        this.snippingCanceled = snippingCanceled;
        this.shortcutRequested = shortcutRequested;
        attempt = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        runTask = RunAsync(attempt.Token);
    }
    public void UpdateState(bool suppress, bool capture, uint revision)
    {
        var value = $"STATE|{(suppress ? 1 : 0)}|{(capture ? 1 : 0)}|{revision}";
        Volatile.Write(ref state, value);
        states.Writer.TryWrite(value);
    }
    public void AllowHelper() => Volatile.Read(ref grant)?.TrySetResult();

    private async Task RunAsync(CancellationToken token)
    {
        // One bounded, asynchronous attempt per app start. If the installed
        // scheduled task is missing or points to an old helper, keep fallback
        // active and write a useful diagnostic instead of waiting forever.
        if (token.IsCancellationRequested) return;

        Process? helper = null;
        bool handedOff = false;
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(token);
        startup.CancelAfter(TimeSpan.FromSeconds(15));
        using var server = new NamedPipeServerStream(WindowsInputHelperProtocol.GetPipeName(),
            PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        Task? stateWriter = null;

        try
        {
            // Begin listening BEFORE launching the scheduled task. No pipe
            // activity or process launch occurs in the low-level keyboard hook.
            var pendingConnection = server.WaitForConnectionAsync(startup.Token);
            var launchResult = await RunRegisteredTaskAsync(startup.Token);
            if (launchResult != 0)
            {
                Log($"Scheduled task '{WindowsInputHelperRegistration.TaskName}' did not start (exit code {launchResult}). " +
                    "Repair its registration and restart Doky.");
                return;
            }

            await pendingConnection;
            using var reader = new StreamReader(server, leaveOpen: true);
            using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
            if (await reader.ReadLineAsync(startup.Token) != "HELLO2" ||
                !NativeMethods.GetNamedPipeClientProcessId(server.SafePipeHandle, out var pid))
            {
                Log("Input helper handshake failed; retaining the normal-keyboard fallback.");
                return;
            }

            helper = Process.GetProcessById((int)pid);
            if (helper.SessionId != Process.GetCurrentProcess().SessionId ||
                !IsExpectedExecutable(GetProcessPath(pid), helperPath))
            {
                Log("The registered helper is from another session or a DIFFERENT build. " +
                    $"Expected: {helperPath}. Repair the scheduled task, then restart Doky.");
                return;
            }

            var permission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref grant, permission);
            ownership(true); // Main window removes fallback hook first.
            handedOff = true;
            await permission.Task.WaitAsync(startup.Token);
            await writer.WriteLineAsync(Volatile.Read(ref state).AsMemory(), startup.Token);
            await writer.WriteLineAsync("GO".AsMemory(), startup.Token);
            if (await reader.ReadLineAsync(startup.Token) != "READY")
            {
                Log("Input helper did not confirm its keyboard hook was ready.");
                return;
            }

            stateWriter = WriteStatesAsync(writer, connection.Token);
            long sequence = 0;
            while (await reader.ReadLineAsync(token) is { } line)
            {
                if (line == "CAPS|RECOVER1") { Volatile.Write(ref supportsRecovery, 1); continue; }
                if (line == "CAPS|SNIP1") { Log("Input helper supports Doky screenshot mode."); continue; }
                if (Volatile.Read(ref recoveryPending) is { } recovery && line == "RECOVERED|" + recovery.Id)
                { recovery.Completion.TrySetResult(true); continue; }
                if (line == "CAPS|RESTORE1") { Volatile.Write(ref restoreHelperPid, helper.Id); continue; }
                var acknowledgement = line.Split('|');
                if (acknowledgement.Length == 3 && acknowledgement[0] == "RESTORED" &&
                    Volatile.Read(ref restorePending) is { } request && request.Id == acknowledgement[1])
                { request.Completion.TrySetResult(acknowledgement[2] == "1"); continue; }
                if (DockKeyboardShortcut.Read(line, ref sequence, Environment.TickCount64) is { } shortcut)
                    shortcutRequested(shortcut.Index, shortcut.Revision);
                else if (WindowsInputHelperProtocol.ReadEvent(line, ref sequence, Environment.TickCount64) is { } value)
                    signal(value);
                else if (WindowsInputHelperProtocol.ReadSnippingEvent(line, ref sequence, Environment.TickCount64) is { } snip)
                    snippingShortcut(snip.Revision);
                else if (WindowsInputHelperProtocol.ReadSnippingEnd(line, ref sequence, Environment.TickCount64) is { } end)
                    snippingCanceled(end.Revision);
            }
            if (!token.IsCancellationRequested)
                Log("Elevated helper disconnected. Restoring the normal-keyboard fallback.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            Log("Elevated helper did not connect and initialize within 15 seconds. " +
                "Check that its task points to the protected Program Files helper.");
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or Win32Exception or
                                  InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            if (!token.IsCancellationRequested) Log($"Elevated helper unavailable: {e.Message}");
        }
        finally
        {
            Volatile.Write(ref supportsRecovery, 0);
            Volatile.Read(ref recoveryPending)?.Completion.TrySetResult(false);
            Volatile.Write(ref restoreHelperPid, 0);
            Volatile.Read(ref restorePending)?.Completion.TrySetResult(false);
            connection.Cancel();
            server.Dispose(); // Helper releases its hook on disconnect before exiting.
            if (stateWriter is not null)
            {
                try { await stateWriter; }
                catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
            }
            if (handedOff && helper is not null)
            {
                try { await helper.WaitForExitAsync(lifetime.Token); }
                catch (OperationCanceledException) { }
                if (!lifetime.IsCancellationRequested) ownership(false);
            }
            helper?.Dispose();
            Volatile.Write(ref grant, null);
        }
    }

    private async Task WriteStatesAsync(StreamWriter writer, CancellationToken token)
    {
        Task<bool>? stateReady = null, commandReady = null;
        while (!token.IsCancellationRequested)
        {
            stateReady ??= states.Reader.WaitToReadAsync(token).AsTask();
            commandReady ??= commands.Reader.WaitToReadAsync(token).AsTask();
            var completed = await Task.WhenAny(stateReady, commandReady);
            if (!await completed) return;
            if (ReferenceEquals(completed, stateReady))
            {
                stateReady = null;
                if (states.Reader.TryRead(out var value)) await writer.WriteLineAsync(value.AsMemory(), token);
            }
            else
            {
                commandReady = null;
                if (commands.Reader.TryRead(out var value)) await writer.WriteLineAsync(value.AsMemory(), token);
            }
        }
    }
    private static string? GetProcessPath(uint pid)
    {
        using var process = ApplicationNative.OpenProcess(0x1000, false, pid);
        var path = new StringBuilder(32768);
        uint length = (uint)path.Capacity;
        return !process.IsInvalid && ApplicationNative.QueryFullProcessImageName(process, 0, path, ref length)
            ? path.ToString() : null;
    }
    private static async Task<int> RunRegisteredTaskAsync(CancellationToken token)
    {
        try
        {
            var start = new ProcessStartInfo(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"))
            { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "/Run", "/TN", WindowsInputHelperRegistration.TaskName })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start);
            if (process is null) return -1;
            await process.WaitForExitAsync(token);
            return process.ExitCode;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            if (!token.IsCancellationRequested)
                Log($"Could not launch elevated helper task: {e.Message}");
            return -1;
        }
    }

    // QueryFullProcessImageName and the task action can spell a path differently
    // when a junction is present. Compare the final on-disk targets rather than
    // silently rejecting a legitimate, protected helper.
    private static bool IsExpectedExecutable(string? actual, string expected)
    {
        if (actual is null) return false;
        if (string.Equals(Path.GetFullPath(actual), expected, StringComparison.OrdinalIgnoreCase))
            return true;
        var actualTarget = ResolveFinalPath(actual);
        var expectedTarget = ResolveFinalPath(expected);
        return actualTarget is not null && expectedTarget is not null &&
               string.Equals(actualTarget, expectedTarget, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveFinalPath(string file)
    {
        try
        {
            using SafeFileHandle handle = File.OpenHandle(file, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var resolved = new StringBuilder(32768);
            uint size = GetFinalPathNameByHandleW(handle, resolved, (uint)resolved.Capacity, 0);
            return size is > 0 and < 32768 ? resolved.ToString() : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW",
        CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle handle, StringBuilder buffer, uint bufferLength, uint flags);

    // Startup/connection diagnostics ONLY; no disk operations in keyboard hooks.
    private static void Log(string message)
    {
        Debug.WriteLine($"Doky input helper: {message}");
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Doky");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "input-helper.log"),
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
    }

    public void Dispose() { lifetime.Cancel(); states.Writer.TryComplete(); commands.Writer.TryComplete(); }
}

public static class WindowsInputHelperRegistration
{
    internal const string TaskName = "Doky Input Helper";

    public static void StopBestEffort()
    {
        RunBestEffort(
            "/End",
            "/TN",
            TaskName);
    }

    public static void RemoveBestEffort()
    {
        StopBestEffort();

        RunBestEffort(
            "/Delete",
            "/TN",
            TaskName,
            "/F");
    }

    private static void RunBestEffort(params string[] arguments)
    {
        try
        {
            var executable = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "schtasks.exe");

            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            process?.WaitForExit(3000);
        }
        catch (Exception error) when (
            error is InvalidOperationException or
            IOException or
            Win32Exception)
        {
        }
    }
}

internal static class WindowsInputHelperProtocol
{
    internal sealed record RestoreRequest(string Id, ApplicationWindow Window);
    internal sealed record RecoveryRequest(string Id, string? State);

    internal static string CreateRecoveryCommand(string id, string state)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid recovery id.", nameof(id));
        var parts = state.Split('|');
        return parts.Length == 4 && parts[0] == "STATE" && parts[1] is "0" or "1" &&
               parts[2] is "0" or "1" && uint.TryParse(parts[3], out _)
            ? $"RECOVER|{id}|{parts[1]}|{parts[2]}|{parts[3]}"
            : $"RECOVER|{id}";
    }

    internal static RecoveryRequest? ReadRecovery(string line)
    {
        var parts = line.Split('|');
        if (parts.Length is not (2 or 5) || parts[0] != "RECOVER" ||
            !Guid.TryParseExact(parts[1], "N", out _)) return null;
        if (parts.Length == 2) return new(parts[1], null);
        return parts[2] is ("0" or "1") && parts[3] is ("0" or "1") &&
               uint.TryParse(parts[4], out _) ? new(parts[1], $"STATE|{parts[2]}|{parts[3]}|{parts[4]}") : null;
    }

    internal static RestoreRequest? ReadRestore(string line, long now)
    {
        var parts = line.Split('|');
        if (parts.Length != 6 || parts[0] != "RESTORE" || !Guid.TryParseExact(parts[1], "N", out _) ||
            !long.TryParse(parts[2], out var hwnd) || hwnd <= 0 ||
            !int.TryParse(parts[3], out var pid) || pid <= 0 ||
            !long.TryParse(parts[4], out var ticks) || ticks <= 0 ||
            !long.TryParse(parts[5], out var stamp) || now - stamp is < 0 or > 1000) return null;
        return new(parts[1], new(new(null, Environment.ProcessPath), "", hwnd, pid, ticks, false, null));
    }
    private const string PipePrefix = "GlassDock.InputHelper.Session";

    internal static Mutex AcquireSingleInstance(int session, out bool created) =>
        new(true, $@"Local\GlassDock.InputHelper.Session{session}", out created);

    internal static InputSignal? ReadSnippingEvent(string line, ref long sequence, long now) =>
        ReadSnipMessage(line, "SNIP", ref sequence, now);

    internal static InputSignal? ReadSnippingEnd(string line, ref long sequence, long now) =>
        ReadSnipMessage(line, "SNIPEND", ref sequence, now);

    private static InputSignal? ReadSnipMessage(string line, string kind, ref long sequence, long now)
    {
        var parts = line.Split('|');
        if (parts.Length != 4 || parts[0] != kind ||
            !long.TryParse(parts[1], out var next) || next <= sequence ||
            !long.TryParse(parts[2], out var stamp) ||
            !uint.TryParse(parts[3], out var revision)) return null;
        sequence = next;
        return now - stamp is >= 0 and <= 500 ? new(false, revision, stamp) : null;
    }

    internal static InputSignal? ReadEvent(string line, ref long sequence, long now)
    {
        var parts = line.Split('|');
        if (parts.Length != 5 || parts[0] != "EVENT" ||
            !long.TryParse(parts[1], out var next) || next <= sequence ||
            !long.TryParse(parts[2], out var stamp) ||
            !uint.TryParse(parts[3], out var revision) ||
            parts[4] is not ("HOME" or "LAUNCHER")) return null;
        sequence = next;
        return now - stamp is >= 0 and <= 500 ? new(parts[4] == "LAUNCHER", revision, stamp) : null;
    }

    public static string GetPipeName()
    {
        return $"{PipePrefix}.{Process.GetCurrentProcess().SessionId}";
    }
}
