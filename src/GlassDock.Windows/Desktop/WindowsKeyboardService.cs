using GlassDock.Core.Desktop;
using GlassDock.Windows.Interop;

namespace GlassDock.Windows.Desktop;

/// <summary>Supported development hotkeys on our HWND; no global keyboard hook.</summary>
public sealed class WindowsKeyboardService : IKeyboardService
{
    private readonly nint hwnd;
    private readonly NativeMethods.SubclassProc callback;
    public bool IsRegistered { get; private set; }
    public event EventHandler? HomeRequested;
    public event EventHandler? RecoveryRequested;

    public WindowsKeyboardService(nint hwnd)
    {
        this.hwnd = hwnd;
        callback = WindowMessage;
        if (!NativeMethods.SetWindowSubclass(hwnd, callback, 1, 0))
            throw new InvalidOperationException("Cannot attach development hotkeys to the overlay.");
        var home = NativeMethods.RegisterHotKey(hwnd, 0x4701, 0x4003, 0x20); // Ctrl+Alt+Space
        var recovery = NativeMethods.RegisterHotKey(hwnd, 0x4702, 0x4003, 0x7B); // Ctrl+Alt+F12
        IsRegistered = home && recovery;
        if (!IsRegistered)
        {
            NativeMethods.UnregisterHotKey(hwnd, 0x4701);
            NativeMethods.UnregisterHotKey(hwnd, 0x4702);
        }
    }

    private nint WindowMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == 0x0021) return 3; // MA_NOACTIVATE: hovering/clicking does not steal foreground focus.
        if (message == 0x0312)
        {
            if (wParam == 0x4701) HomeRequested?.Invoke(this, EventArgs.Empty);
            if (wParam == 0x4702) RecoveryRequested?.Invoke(this, EventArgs.Empty);
            return 0;
        }
        return NativeMethods.DefSubclassProc(window, message, wParam, lParam);
    }

    public void Dispose()
    {
        NativeMethods.UnregisterHotKey(hwnd, 0x4701);
        NativeMethods.UnregisterHotKey(hwnd, 0x4702);
        NativeMethods.RemoveWindowSubclass(hwnd, callback, 1);
        IsRegistered = false;
    }
}
