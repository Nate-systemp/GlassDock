using GlassDock.Core.Desktop;
using GlassDock.Windows.Interop;
using System.Runtime.InteropServices;

namespace GlassDock.Windows.Desktop;

/// <summary>App-lifetime bare Windows-key activation and independent registered recovery hotkey.</summary>
public sealed class WindowsKeyboardService : IKeyboardService
{
    private readonly nint hwnd;
    private readonly NativeMethods.SubclassProc callback;
    private readonly NativeMethods.KeyboardProc keyboardCallback;
    private readonly WindowsKeyGesture gesture = new();
    private nint keyboardHook;
    private const uint ExpandMessage = 0x8000 + 71;
    private const nuint InjectionTag = 0x47444F43;
    public bool IsRegistered { get; private set; }
    public event EventHandler? HomeRequested;
    public event EventHandler? RecoveryRequested;

    public WindowsKeyboardService(nint hwnd)
    {
        this.hwnd = hwnd;
        keyboardCallback = KeyboardMessage;
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
        if (IsRegistered)
        {
            for (var key = 8; key < 256; key++)
                if (NativeMethods.GetAsyncKeyState(key) < 0) gesture.Process(key, true);
            keyboardHook = NativeMethods.SetWindowsHookExW(13, keyboardCallback, 0, 0);
            if (keyboardHook == 0) { Dispose(); throw new InvalidOperationException("Cannot register Windows-key activation."); }
        }
    }

    private nint KeyboardMessage(int code, nuint message, nint data)
    {
        if (code >= 0)
        {
            var key = Marshal.PtrToStructure<NativeMethods.KeyboardData>(data);
            if (key.ExtraInfo != InjectionTag && message is 0x100 or 0x101 or 0x104 or 0x105)
            {
                var down = message is 0x100 or 0x104;
                if (gesture.Process((int)key.Key, down))
                {
                    // Mask Start, then release Win in the same ordered input batch. Shortcut
                    // sequences never enter this branch and keep their original physical events.
                    NativeMethods.Input[] inputs =
                    [
                        new() { Type = 1, Key = 0xE8, ExtraInfo = InjectionTag },
                        new() { Type = 1, Key = 0xE8, Flags = 2, ExtraInfo = InjectionTag },
                        new() { Type = 1, Key = (ushort)key.Key, Flags = 3, ExtraInfo = InjectionTag }
                    ];
                    if (NativeMethods.SendInput(3, inputs, Marshal.SizeOf<NativeMethods.Input>()) == 3)
                    {
                        NativeMethods.PostMessageW(hwnd, ExpandMessage, 0, 0);
                        return 1;
                    }
                    // UIPI or insertion failure: let the real release through; never strand Win down.
                }
            }
        }
        return NativeMethods.CallNextHookEx(keyboardHook, code, message, data);
    }

    private nint WindowMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == 0x0021) return 3; // MA_NOACTIVATE: hovering/clicking does not steal foreground focus.
        if (message == ExpandMessage) { HomeRequested?.Invoke(this, EventArgs.Empty); return 0; }
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
        if (keyboardHook != 0) NativeMethods.UnhookWindowsHookEx(keyboardHook);
        keyboardHook = 0;
        NativeMethods.UnregisterHotKey(hwnd, 0x4701);
        NativeMethods.UnregisterHotKey(hwnd, 0x4702);
        NativeMethods.RemoveWindowSubclass(hwnd, callback, 1);
        IsRegistered = false;
        GC.KeepAlive(keyboardCallback);
    }
}
