using GlassDock.Core.Desktop;
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
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
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
            hook.Dispose(); // release ownership BEFORE the app is allowed to restore fallback
            lifetime.Cancel();
            pipe.Dispose();
            events.Writer.TryComplete();
            Marshal.FreeHGlobal(handles);
            // Workers are cancellable and outside the hook. Join before disposing their event.
            Task.WhenAll(readTask, writeTask).GetAwaiter().GetResult();
        }

        async Task ReadStateAsync()
        {
            try
            {
                while (await reader.ReadLineAsync(lifetime.Token) is { } line) ApplyState(line, hook);
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
                    await writer.WriteLineAsync($"EVENT|{++sequence}|{value.Timestamp}|{value.Revision}|{(value.Launcher ? "LAUNCHER" : "HOME")}".AsMemory(), lifetime.Token);
                }
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
            finally { disconnected.Set(); }
        }
    }
    private static void ApplyState(string line, WindowsKeyHook hook)
    {
        var parts = line.Split('|');
        if (parts.Length != 4 || parts[0] != "STATE" || !uint.TryParse(parts[3], out var revision)) return;
        hook.UpdateState(parts[1] == "1", parts[2] == "1", revision);
    }
}
