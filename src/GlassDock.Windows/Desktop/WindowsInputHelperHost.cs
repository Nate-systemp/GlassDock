using GlassDock.Core.Desktop;
using GlassDock.Windows.Applications;
using GlassDock.Windows.Interop;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Channels;

namespace GlassDock.Windows.Desktop;

public static class WindowsInputHelperHost
{
    public static int Run()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return 0;
        using var singleInstance = WindowsInputHelperProtocol.AcquireSingleInstance(Process.GetCurrentProcess().SessionId, out var created);
        if (!created) return 0;
        try
        {
            using var pipe = new NamedPipeClientStream(".", WindowsInputHelperProtocol.GetPipeName(),
                PipeDirection.InOut, PipeOptions.Asynchronous);
            // A bounded initial connection, before any hook exists. No polling/reconnect loop.
            pipe.Connect(3000);
            RunConnected(pipe);
        }
        catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException)
        { Debug.WriteLine($"Doky input helper unavailable: {e.Message}"); }
        return 0;
    }

    private static void RunConnected(NamedPipeClientStream pipe)
    {
        using var reader = new StreamReader(pipe, leaveOpen: true);
        // StreamWriter.Dispose flushes even when LeaveOpen is true. If Doky
        // closes the pipe during shutdown, that flush used to crash the helper.
        // Own disposal here so a normal disconnect is not an unhandled exception.
        var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        try
        {
            writer.WriteLine("HELLO2");
            var initial = reader.ReadLine();
            if (initial is null || reader.ReadLine() != "GO") return;

            using var disconnected = new ManualResetEvent(false);
            using var lifetime = new CancellationTokenSource();
            // Preserve distinct key releases in transport. The UI retargets on each
            // event; it does not queue completed animations. Stale events still expire.
            var events = Channel.CreateUnbounded<InputSignal>(new UnboundedChannelOptions
            { SingleReader = true, SingleWriter = true });
            using var hook = new WindowsKeyHook((launcher, revision) =>
                events.Writer.TryWrite(new InputSignal(launcher, revision, Environment.TickCount64)));
            ApplyState(initial, hook);
            writer.WriteLine("READY");
            // Older clients ignore this optional capability; keyboard protocol is unchanged.
            writer.WriteLine("CAPS|RESTORE1");
            writer.WriteLine("CAPS|RECOVER1");
            var hookThread = GetCurrentThreadId();
            // Force creation of this thread's message queue before the bridge
            // can post a lifecycle recovery command to it.
            NativeMethods.PeekMessage(out _, 0, 0, 0, 0);
            TaskCompletionSource? recovery = null;
            using var writeLock = new SemaphoreSlim(1, 1);
            var readTask = ReadStateAsync();
            var writeTask = WriteEventsAsync();
            var handles = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(handles, disconnected.SafeWaitHandle.DangerousGetHandle());
            try
            {
                // Native message delivery wakes this thread immediately; disconnect is an event.
                while (NativeMethods.MsgWaitForMultipleObjectsEx(1, handles, uint.MaxValue, 0x04FF, 0x0004) == 1)
                    while (NativeMethods.PeekMessage(out var message, 0, 0, 0, 1))
                    {
                        if (message.MessageId == 0x8051)
                        {
                            try { hook.RecoverAfterResume(); Volatile.Read(ref recovery)?.TrySetResult(); }
                            catch (Exception error) { Volatile.Read(ref recovery)?.TrySetException(error); }
                            continue;
                        }
                        NativeMethods.TranslateMessage(ref message);
                        NativeMethods.DispatchMessage(ref message);
                    }
            }
            finally
            {
                hook.Dispose(); // release ownership BEFORE the app can restore fallback
                lifetime.Cancel();
                events.Writer.TryComplete();
                pipe.Dispose(); // unblock pending pipe reads/writes on disconnect
                Marshal.FreeHGlobal(handles);
                // Join workers before disposing writer, so no thread uses it after cleanup.
                Task.WhenAll(readTask, writeTask).GetAwaiter().GetResult();
            }

            async Task ReadStateAsync()
            {
                try
                {
                    while (await reader.ReadLineAsync(lifetime.Token) is { } line)
                    {
                        if (WindowsInputHelperProtocol.ReadRecovery(line) is { } recoveryRequest)
                        {
                            if (recoveryRequest.State is { } recoveryState)
                                ApplyState(recoveryState, hook);
                            var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                            Volatile.Write(ref recovery, pending);
                            if (!PostThreadMessageW(hookThread, 0x8051, 0, 0)) throw new IOException("Cannot dispatch hook recovery.");
                            await pending.Task.WaitAsync(TimeSpan.FromSeconds(3), lifetime.Token);
                            await WriteLineAsync("RECOVERED|" + recoveryRequest.Id);
                        }
                        else if (line.StartsWith("RECOVER|", StringComparison.Ordinal)) continue;
                        else if (WindowsInputHelperProtocol.ReadRestore(line, Environment.TickCount64) is { } request)
                        {
                            var restored = RestoreWindow(request);
                            await WriteLineAsync($"RESTORED|{request.Id}|{(restored ? 1 : 0)}");
                        }
                        else ApplyState(line, hook);
                    }
                }
                catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException or TimeoutException or InvalidOperationException)
                { Debug.WriteLine($"Doky helper connection/recovery ended: {e}"); }
                finally { disconnected.Set(); }
            }
            async Task WriteEventsAsync()
            {
                long sequence = 0;
                try
                {
                    await foreach (var value in events.Reader.ReadAllAsync(lifetime.Token))
                    {
                        if (Environment.TickCount64 - value.Timestamp > 500) continue;
                        await WriteLineAsync($"EVENT|{++sequence}|{value.Timestamp}|{value.Revision}|{(value.Launcher ? "LAUNCHER" : "HOME")}");
                    }
                }
                catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
                finally { disconnected.Set(); }
            }
            async Task WriteLineAsync(string line)
            {
                await writeLock.WaitAsync(lifetime.Token);
                try { await writer.WriteLineAsync(line.AsMemory(), lifetime.Token); }
                finally { writeLock.Release(); }
            }
        }
        finally
        {
            try { writer.Dispose(); }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
                // The peer can close the pipe between the last write and the
                // final StreamWriter flush. That is expected, not a helper crash.
            }
        }
    }

    private static void ApplyState(string line, WindowsKeyHook hook)
    {
        var parts = line.Split('|');
        if (parts.Length != 4 || parts[0] != "STATE" || !uint.TryParse(parts[3], out var revision)) return;
        hook.UpdateState(parts[1] == "1", parts[2] == "1", revision);
    }

    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessageW(uint thread, uint message, nuint wParam, nint lParam);

    private static bool RestoreWindow(WindowsInputHelperProtocol.RestoreRequest request)
    {
        // Only normal restore/foreground of a live, identity-validated HWND in
        // this desktop session. No arbitrary messages, launch, close or commands.
        try
        {
            using var process = Process.GetProcessById(request.Window.ProcessId);
            if (process.SessionId != Process.GetCurrentProcess().SessionId ||
                !WindowsApplicationService.IsEligible(request.Window)) return false;
            var window = (nint)request.Window.Handle;
            return (!ApplicationNative.IsIconic(window) || ApplicationNative.ShowWindowAsync(window, 9)) &&
                ApplicationNative.SetForegroundWindow(window);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return false; }
    }
}
