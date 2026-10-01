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
            var events = Channel.CreateBounded<InputSignal>(new BoundedChannelOptions(1)
            { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
            using var hook = new WindowsKeyHook((launcher, revision) =>
                events.Writer.TryWrite(new InputSignal(launcher, revision, Environment.TickCount64)));
            ApplyState(initial, hook);
            writer.WriteLine("READY");
            // Older clients ignore this optional capability; keyboard protocol is unchanged.
            writer.WriteLine("CAPS|RESTORE1");
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
                        if (WindowsInputHelperProtocol.ReadRestore(line, Environment.TickCount64) is { } request)
                        {
                            var restored = RestoreWindow(request);
                            await WriteLineAsync($"RESTORED|{request.Id}|{(restored ? 1 : 0)}");
                        }
                        else ApplyState(line, hook);
                    }
                }
                catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
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
