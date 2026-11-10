using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using GlassDock.Windows.Interop;

namespace GlassDock.Windows.Desktop;

/// <summary>Notifies the owning UI thread when its desktop becomes interactive again.</summary>
public sealed class InputDesktopMonitor : IDisposable
{
    private readonly NativeMethods.WinEventProc callback;
    private nint hook;
    public event EventHandler? Restored;

    public InputDesktopMonitor()
    {
        callback = (_, _, _, _, _, _, _) => { if (IsCurrent) Restored?.Invoke(this, EventArgs.Empty); };
        hook = NativeMethods.SetWinEventHook(0x0020, 0x0020, 0, callback, 0, 0, 0); // EVENT_SYSTEM_DESKTOPSWITCH, OUTOFCONTEXT
        if (hook == 0) throw new Win32Exception("Cannot monitor input desktop restoration.");
    }

    public bool IsCurrent => IsCallingDesktopCurrent();

    internal static bool IsCallingDesktopCurrent()
    {
        var input = OpenInputDesktop(0, false, 1); // DESKTOP_READOBJECTS only
        if (input == 0) return false;
        try
        {
            return CanResumeCapture(Name(input), Name(GetThreadDesktop(GetCurrentThreadId())));
        }
        finally { CloseDesktop(input); }
    }

    internal static bool CanResumeCapture(string? input, string? rendering) =>
        !string.IsNullOrEmpty(input) && !string.IsNullOrEmpty(rendering) &&
        string.Equals(input, rendering, StringComparison.OrdinalIgnoreCase);

    private static string? Name(nint desktop)
    {
        var name = new StringBuilder(256);
        return desktop != 0 && GetUserObjectInformationW(desktop, 2, name, name.Capacity * sizeof(char), out _)
            ? name.ToString() : null;
    }

    public void Dispose()
    {
        if (hook == 0) return;
        NativeMethods.UnhookWinEvent(hook);
        hook = 0;
        GC.KeepAlive(callback);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll")]
    private static extern nint GetThreadDesktop(uint thread);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformationW(nint handle, int index, StringBuilder value, int length, out int needed);
}
