using System.Diagnostics;
using System.Runtime.InteropServices;
using GlassDock.Windows.Interop;

namespace GlassDock.Windows.Desktop;

internal static class TaskbarAutoHide
{
    private static string Journal => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GlassDock", $"taskbar-state-{Process.GetCurrentProcess().SessionId}.txt");

    internal static uint Read()
    {
        var data = new NativeMethods.AppBarData { Size = (uint)Marshal.SizeOf<NativeMethods.AppBarData>() };
        return (uint)NativeMethods.SHAppBarMessage(4, ref data);
    }

    private static bool Set(nint window, uint state)
    {
        var data = new NativeMethods.AppBarData
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.AppBarData>(), Window = window, Parameter = (nint)state
        };
        NativeMethods.SHAppBarMessage(10, ref data);
        return (Read() & 1) == (state & 1);
    }

    internal static void Suspend(nint window)
    {
        WithLock(() =>
        {
        // The watchdog holds the session lease. Recover an interrupted older lease first.
        if (!RestoreCore(window)) throw new IOException("Cannot restore the previous taskbar setting.");
        var original = Read();
        Directory.CreateDirectory(Path.GetDirectoryName(Journal)!);
        using (var stream = new FileStream(Journal, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        {
            stream.Write(System.Text.Encoding.UTF8.GetBytes(original.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            stream.Flush(true); // Recovery information must be durable before changing the shell setting.
        }
        if (!Set(window, original & ~1u)) throw new InvalidOperationException("Windows did not suspend taskbar auto-hide.");
        return true;
        });
    }

    internal static bool Maintain(nint window) => WithLock(() => File.Exists(Journal) && ((Read() & 1) == 0 || Set(window, Read() & ~1u)));

    internal static bool Restore(nint window) => WithLock(() => RestoreCore(window));

    private static bool WithLock(Func<bool> action)
    {
        using var gate = new Mutex(false, $"Local\\GlassDock.TaskbarState.{Process.GetCurrentProcess().SessionId}");
        try { gate.WaitOne(); }
        catch (AbandonedMutexException) { }
        try { return action(); }
        finally { gate.ReleaseMutex(); }
    }

    private static bool RestoreCore(nint window)
    {
        try
        {
            if (!File.Exists(Journal)) return true;
            if (!uint.TryParse(File.ReadAllText(Journal), out var original) || original > 3) return false;
            if (!Set(window, original)) return false;
            File.Delete(Journal);
            return true;
        }
        catch (FileNotFoundException) { return true; } // The other recovery participant completed first.
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
