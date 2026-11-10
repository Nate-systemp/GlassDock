using System.ComponentModel;
using System.Runtime.InteropServices;
using GlassDock.Windows.Interop;

namespace GlassDock.Windows.Desktop;

/// <summary>Consumes volume keys before Shell receives them; never changes a foreign window.</summary>
public sealed class NativeVolumeOsdSuppression : IDisposable
{
    private readonly MasterVolumeService audio;
    private readonly NativeMethods.KeyboardProc callback;
    private readonly VolumeKeyCapture capture = new();
    private readonly ManualResetEventSlim ready = new();
    private Thread? thread;
    private nint hook;
    private uint threadId;
    private volatile bool enabled, disposed;
    public bool Enabled => enabled && hook != 0;
    public string Status { get; private set; } = "Windows volume keys are unchanged.";

    public NativeVolumeOsdSuppression(MasterVolumeService audio)
    {
        this.audio = audio;
        callback = KeyboardMessage;
    }

    // Configure/dispose from Doky's owning UI thread; native callbacks use their own message pump.
    public void SetEnabled(bool value)
    {
        if (disposed) return;
        enabled = value;
        if (!value) { Status = "Windows volume keys are unchanged."; return; }
        if (thread is null)
        {
            thread = new Thread(Run) { IsBackground = true, Name = "Doky volume keys" };
            thread.Start();
            if (!ready.Wait(TimeSpan.FromSeconds(3)))
            {
                enabled = false;
                Status = "Volume key hook did not initialize; Windows retains volume handling.";
            }
        }
    }

    private void Run()
    {
        try
        {
            threadId = GetCurrentThreadId();
            NativeMethods.PeekMessage(out _, 0, 0, 0, 0);
            hook = NativeMethods.SetWindowsHookExW(13, callback, 0, 0); // WH_KEYBOARD_LL
            if (hook == 0) throw new Win32Exception("Cannot register volume key handling.");
            Status = "Volume keys use Core Audio before Shell dispatch.";
            ready.Set();
            while (!disposed && GetMessageW(out var message, 0, 0, 0) > 0)
            {
                NativeMethods.TranslateMessage(ref message);
                NativeMethods.DispatchMessage(ref message);
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            enabled = false;
            Status = $"Windows retains volume handling: {error.Message}";
        }
        finally
        {
            if (hook != 0) NativeMethods.UnhookWindowsHookEx(hook);
            hook = 0;
            ready.Set();
            GC.KeepAlive(callback);
        }
    }

    private nint KeyboardMessage(int code, nuint message, nint data)
    {
        if (code >= 0 && message is 0x100 or 0x101 or 0x104 or 0x105)
        {
            try
            {
                var key = Marshal.PtrToStructure<NativeMethods.KeyboardData>(data).Key;
                if (capture.Process(key, message is 0x100 or 0x104, enabled && !disposed, audio.TryHandleVolumeKey)) return 1;
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                enabled = false;
                Status = $"Windows retains volume handling: {error.Message}";
            }
        }
        return NativeMethods.CallNextHookEx(hook, code, message, data);
    }

    public void Dispose()
    {
        if (disposed) return;
        enabled = false;
        disposed = true;
        if (threadId != 0) PostThreadMessageW(threadId, 0x12, 0, 0); // WM_QUIT
        // No Shell state exists to restore. Windows also removes this process's hook after a crash.
    }

    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern int GetMessageW(out NativeMethods.Message message, nint window, uint min, uint max);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessageW(uint thread, uint message, nuint w, nint l);
}

internal sealed class VolumeKeyCapture
{
    private readonly HashSet<uint> captured = [];
    internal void Reset() => captured.Clear();
    internal static bool IsVolumeKey(uint key) => key is 0xAD or 0xAE or 0xAF;

    internal bool Process(uint key, bool down, bool enabled, Func<uint, bool> handle)
    {
        if (!IsVolumeKey(key)) return false;
        if (!down) return captured.Remove(key);
        if (captured.Contains(key))
        {
            // Mute is a toggle, not a repeating step. Keep matching releases paired on disable.
            if (enabled && key != 0xAD) handle(key);
            return true;
        }
        if (!enabled || !handle(key)) return false;
        captured.Add(key);
        return true;
    }
}
