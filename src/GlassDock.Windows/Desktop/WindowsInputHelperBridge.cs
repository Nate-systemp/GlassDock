using GlassDock.Core.Desktop;
using GlassDock.Windows.Interop;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Threading.Channels;
using System.Text;
using GlassDock.Windows.Applications;

namespace GlassDock.Windows.Desktop;

// Persistent connection. UI only posts ownership changes and replaces the latest state.
// No pipe operations run on a keyboard callback or the UI thread.
internal sealed class WindowsInputHelperBridge : IDisposable
{
    private readonly string helperPath;
    private readonly Action<bool> ownership;
    private readonly Action<InputSignal> signal;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Channel<string> states = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
    { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private TaskCompletionSource? grant;
    private string state = "STATE|0|0|0";

    public WindowsInputHelperBridge(string helperPath, Action<bool> ownership, Action<InputSignal> signal)
    {
        this.helperPath = Path.GetFullPath(helperPath);
        this.ownership = ownership;
        this.signal = signal;
        _ = RunAsync(lifetime.Token);
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
        // Create the server before asking the pre-registered task to run. No UAC at startup.
        while (!token.IsCancellationRequested)
        {
            Process? helper = null;
            bool handedOff = false;
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
            using var server = new NamedPipeServerStream(WindowsInputHelperProtocol.GetPipeName(),
                PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            Task? stateWriter = null;
            try
            {
                _ = RunRegisteredTaskAsync(token);
                await server.WaitForConnectionAsync(token);
                using var reader = new StreamReader(server, leaveOpen: true);
                using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
                var hello = await reader.ReadLineAsync(token);
                if (hello != "HELLO2" ||
                    !NativeMethods.GetNamedPipeClientProcessId(server.SafePipeHandle, out var pid)) continue;
                helper = Process.GetProcessById((int)pid);
                if (helper.SessionId != Process.GetCurrentProcess().SessionId ||
                    !string.Equals(GetProcessPath(pid), helperPath, StringComparison.OrdinalIgnoreCase)) continue;
                var readyToOwn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Volatile.Write(ref grant, readyToOwn);
                ownership(true);
                handedOff = true;
                await readyToOwn.Task.WaitAsync(token);
                await writer.WriteLineAsync(Volatile.Read(ref state).AsMemory(), token);
                await writer.WriteLineAsync("GO".AsMemory(), token);
                if (await reader.ReadLineAsync(token) != "READY") continue;
                stateWriter = WriteStatesAsync(writer, connection.Token);
                long sequence = 0;
                while (await reader.ReadLineAsync(token) is { } line)
                {
                    if (WindowsInputHelperProtocol.ReadEvent(line, ref sequence, Environment.TickCount64) is { } value)
                        signal(value);
                }
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or Win32Exception or InvalidOperationException or UnauthorizedAccessException)
            { Debug.WriteLine($"Doky input helper: {e.Message}"); }
            finally
            {
                connection.Cancel();
                server.Dispose(); // wakes helper; it removes its hook before exiting
                if (stateWriter is not null)
                    try { await stateWriter; } catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
                if (handedOff && helper is not null)
                {
                    try { await helper.WaitForExitAsync(token); }
                    catch (OperationCanceledException) { }
                    if (!token.IsCancellationRequested) ownership(false);
                }
                helper?.Dispose();
                Volatile.Write(ref grant, null);
            }
            // One startup attempt. A failed helper leaves the local fallback active;
            // do not restart processes or poll indefinitely during the app lifetime.
            return;
        }
    }
    private async Task WriteStatesAsync(StreamWriter writer, CancellationToken token)
    {
        await foreach (var value in states.Reader.ReadAllAsync(token))
            await writer.WriteLineAsync(value.AsMemory(), token);
    }
    private static string? GetProcessPath(uint pid)
    {
        using var process = ApplicationNative.OpenProcess(0x1000, false, pid);
        var path = new StringBuilder(32768);
        uint length = (uint)path.Capacity;
        return !process.IsInvalid && ApplicationNative.QueryFullProcessImageName(process, 0, path, ref length)
            ? path.ToString() : null;
    }
    private static async Task RunRegisteredTaskAsync(CancellationToken token)
    {
        try
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"))
            { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "/Run", "/TN", WindowsInputHelperRegistration.TaskName }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start);
            if (process is not null) await process.WaitForExitAsync(token);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or OperationCanceledException)
        { Debug.WriteLine($"Doky input helper task unavailable: {e.Message}"); }
    }
    public void Dispose() { lifetime.Cancel(); states.Writer.TryComplete(); }
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
    private const string PipePrefix = "GlassDock.InputHelper.Session";

    internal static Mutex AcquireSingleInstance(int session, out bool created) =>
        new(true, $@"Local\GlassDock.InputHelper.Session{session}", out created);

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

