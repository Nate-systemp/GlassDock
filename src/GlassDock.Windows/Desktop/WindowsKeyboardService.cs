using GlassDock.Core.Desktop;
using GlassDock.Windows.Interop;
using System.Runtime.InteropServices;

namespace GlassDock.Windows.Desktop;

/// <summary>
/// App-lifetime bare Windows-key activation,
/// Win+Space launcher activation,
/// and independent registered recovery hotkey.
/// </summary>
public sealed class WindowsKeyboardService : IKeyboardService
{
    private readonly nint hwnd;

    private readonly NativeMethods.SubclassProc callback;
    private readonly NativeMethods.KeyboardProc keyboardCallback;

    private readonly WindowsKeyGesture gesture = new();

    private nint keyboardHook;
    private readonly bool dockShortcutsEnabled;

    private const uint ExpandMessage = 0x8000 + 71;
    private const uint LauncherMessage = 0x8000 + 72;

    private const nuint InjectionTag = 0x47444F43;

    private const int LeftWindows = 0x5B;
    private const int RightWindows = 0x5C;
    private const int Space = 0x20;

    private bool launcherChordActive;
    private bool spaceHeld;
    private bool suppressDockToggle;
    private bool suppressCurrentWindowsPress;
    private bool captureBareWindowsKey;
    private bool capturedWindowsDown;
    private int capturedWindowsKey;
    private uint toggleRevision;

    /// <summary>
    /// When Glass Home owns the foreground window, consume the physical Windows-key
    /// down event so Explorer cannot open Start before GlassDock decides whether the
    /// gesture is bare Win or a real Win+key chord. Real chords are re-forwarded.
    /// </summary>
    public bool CaptureBareWindowsKey
    {
        get => captureBareWindowsKey;
        set
        {
            captureBareWindowsKey = value;
            if (!value && capturedWindowsDown)
            {
                // The original Win-down was consumed. If Home disappears before the
                // matching key-up, dropping this capture is safe; the eventual Win-up
                // is only a release with no system-visible press.
                capturedWindowsDown = false;
                capturedWindowsKey = 0;
            }
        }
    }

    // Record suppression at key-down as well as dispatch time. A menu may be
    // dismissed between key-down, key-up and the posted toggle message.
    public bool SuppressDockToggle
    {
        get => suppressDockToggle;
        set
        {
            if (suppressDockToggle == value) return;
            suppressDockToggle = value;
            toggleRevision++;
            if (value) suppressCurrentWindowsPress = true;
        }
    }

    public bool IsRegistered { get; private set; }

    public event EventHandler? HomeRequested;
    public event EventHandler? LauncherRequested;
    public event EventHandler? RecoveryRequested;

    public WindowsKeyboardService(nint hwnd, bool enableDockShortcuts = true)
    {
        this.hwnd = hwnd;
        dockShortcutsEnabled = enableDockShortcuts;

        keyboardCallback = KeyboardMessage;
        callback = WindowMessage;

        if (!NativeMethods.SetWindowSubclass(
                hwnd,
                callback,
                1,
                0))
        {
            throw new InvalidOperationException(
                "Cannot attach development hotkeys to the overlay.");
        }

        //
        // Optional development shortcut.
        // Ctrl + Alt + Space
        //
        if (dockShortcutsEnabled)
        {
            NativeMethods.RegisterHotKey(
                hwnd,
                0x4701,
                0x4003,
                0x20);
        }

        //
        // IMPORTANT SAFETY HOTKEY.
        // Ctrl + Alt + F12
        //
        var recovery =
            NativeMethods.RegisterHotKey(
                hwnd,
                0x4702,
                0x4003,
                0x7B);

        // The recovery hotkey is a convenience fallback, not a startup requirement.
        // Another application or Windows itself may already own Ctrl+Alt+F12.
        // Keep GlassDock running and let the watchdog / Recovery UI remain available
        // when registration fails.
        IsRegistered = recovery;

        if (dockShortcutsEnabled)
        {
            //
            // Synchronize the gesture state with keys
            // already physically held when GlassDock starts.
            //
            for (var key = 8; key < 256; key++)
            {
                if (NativeMethods.GetAsyncKeyState(key) < 0)
                {
                    gesture.Process(
                        key,
                        true);
                }
            }

            //
            // Global low-level keyboard hook.
            //
            keyboardHook =
                NativeMethods.SetWindowsHookExW(
                    13,
                    keyboardCallback,
                    0,
                    0);

            if (keyboardHook == 0)
            {
                Dispose();

                throw new InvalidOperationException(
                    "Cannot register Windows keyboard handling.");
            }
        }
    }

    private nint KeyboardMessage(
        int code,
        nuint message,
        nint data)
    {
        if (code < 0)
        {
            return NativeMethods.CallNextHookEx(
                keyboardHook,
                code,
                message,
                data);
        }

        var key =
            Marshal.PtrToStructure<NativeMethods.KeyboardData>(
                data);

        //
        // Ignore events injected by GlassDock itself.
        //
        if (key.ExtraInfo == InjectionTag)
        {
            return NativeMethods.CallNextHookEx(
                keyboardHook,
                code,
                message,
                data);
        }

        //
        // Only handle keyboard down/up messages.
        //
        if (message is not (
            0x100 or // WM_KEYDOWN
            0x101 or // WM_KEYUP
            0x104 or // WM_SYSKEYDOWN
            0x105))  // WM_SYSKEYUP
        {
            return NativeMethods.CallNextHookEx(
                keyboardHook,
                code,
                message,
                data);
        }

        var down =
            message is 0x100 or 0x104;

        var keyCode =
            (int)key.Key;

        var isWindowsKey =
            keyCode == LeftWindows ||
            keyCode == RightWindows;

        var winHeld =
            NativeMethods.GetAsyncKeyState(
                LeftWindows) < 0 ||
            NativeMethods.GetAsyncKeyState(
                RightWindows) < 0;

        //
        // ==================================================
        // WINDOWS KEY DOWN
        // ==================================================
        //
        if (isWindowsKey && down)
        {
            if (!winHeld) suppressCurrentWindowsPress = suppressDockToggle;
            //
            // New Win press:
            // recover from any stale launcher state.
            //
            if (!spaceHeld)
            {
                launcherChordActive = false;
            }

            gesture.Process(
                keyCode,
                true);

            // Glass Home is a foreground, focusable HWND. Letting the physical
            // Win-down reach Explorer here can make Start win the race before the
            // bare-Win release is suppressed. Capture the down while Home is open;
            // if another key follows, OTHER KEYS below forwards a tagged Win-down
            // first so normal Win+ shortcuts still work.
            if (captureBareWindowsKey)
            {
                capturedWindowsDown = true;
                capturedWindowsKey = keyCode;
                return 1;
            }

            return NativeMethods.CallNextHookEx(
                keyboardHook,
                code,
                message,
                data);
        }

        //
        // ==================================================
        // WIN + SPACE DOWN
        // ==================================================
        //
        if (keyCode == Space &&
            down &&
            winHeld)
        {
            //
            // Ignore keyboard auto-repeat.
            //
            if (spaceHeld)
            {
                return 1;
            }

            spaceHeld = true;
            launcherChordActive = true;

            //
            // Mark this as a Win chord so it won't
            // later be recognized as bare Win.
            //
            gesture.Process(
                keyCode,
                true);

            NativeMethods.PostMessageW(
                hwnd,
                LauncherMessage,
                0,
                0);

            //
            // Consume Space so Windows' language/input
            // switcher does not receive Win+Space.
            //
            return 1;
        }

        //
        // ==================================================
        // SPACE UP
        // ==================================================
        //
        if (keyCode == Space &&
            !down)
        {
            var belongedToLauncher =
                launcherChordActive;

            spaceHeld = false;

            gesture.Process(
                keyCode,
                false);

            //
            // Consume Space-up when it belonged
            // to GlassDock's launcher shortcut.
            //
            if (belongedToLauncher)
            {
                return 1;
            }

            return NativeMethods.CallNextHookEx(
                keyboardHook,
                code,
                message,
                data);
        }

        //
        // ==================================================
        // WINDOWS KEY UP
        // ==================================================
        //
        if (isWindowsKey && !down)
        {
            var wasLauncherShortcut =
                launcherChordActive;

            //
            // ALWAYS reset these.
            //
            launcherChordActive = false;
            spaceHeld = false;

            var bareWindows =
                gesture.Process(
                    keyCode,
                    false);

            // If the matching Win-down was captured while Glass Home was visible,
            // Explorer never saw a Windows-key press. Consume the release as well.
            // Bare Win belongs to GlassDock; Win+Space belongs to Glass Home.
            if (capturedWindowsDown &&
                capturedWindowsKey == keyCode)
            {
                capturedWindowsDown = false;
                capturedWindowsKey = 0;

                if (wasLauncherShortcut)
                    return 1;

                if (bareWindows)
                {
                    if (!suppressCurrentWindowsPress && !suppressDockToggle)
                    {
                        NativeMethods.PostMessageW(
                            hwnd,
                            ExpandMessage,
                            toggleRevision,
                            0);
                    }

                    return 1;
                }
            }

            //
            // ----------------------------------------------
            // Win+Space was used
            // ----------------------------------------------
            //
            if (wasLauncherShortcut)
            {
                NativeMethods.Input[] inputs =
                [
                    new()
                    {
                        Type = 1,
                        Key = 0xE8,
                        ExtraInfo = InjectionTag
                    },

                    new()
                    {
                        Type = 1,
                        Key = 0xE8,
                        Flags = 2,
                        ExtraInfo = InjectionTag
                    },

                    new()
                    {
                        Type = 1,
                        Key = (ushort)key.Key,
                        Flags = 3,
                        ExtraInfo = InjectionTag
                    }
                ];

                if (NativeMethods.SendInput(
                        3,
                        inputs,
                        Marshal.SizeOf<NativeMethods.Input>())
                    == 3)
                {
                    return 1;
                }

                //
                // If SendInput fails, let the real Win-up
                // continue so the key never gets stranded.
                //
                return NativeMethods.CallNextHookEx(
                    keyboardHook,
                    code,
                    message,
                    data);
            }

            //
            // ----------------------------------------------
            // Bare Windows key
            // ----------------------------------------------
            //
            if (bareWindows)
            {
                NativeMethods.Input[] inputs =
                [
                    new()
                    {
                        Type = 1,
                        Key = 0xE8,
                        ExtraInfo = InjectionTag
                    },

                    new()
                    {
                        Type = 1,
                        Key = 0xE8,
                        Flags = 2,
                        ExtraInfo = InjectionTag
                    },

                    new()
                    {
                        Type = 1,
                        Key = (ushort)key.Key,
                        Flags = 3,
                        ExtraInfo = InjectionTag
                    }
                ];

                if (NativeMethods.SendInput(
                        3,
                        inputs,
                        Marshal.SizeOf<NativeMethods.Input>())
                    == 3)
                {
                    if (!suppressCurrentWindowsPress && !suppressDockToggle) NativeMethods.PostMessageW(
                        hwnd,
                        ExpandMessage,
                        toggleRevision,
                        0);

                    return 1;
                }
            }

            return NativeMethods.CallNextHookEx(
                keyboardHook,
                code,
                message,
                data);
        }

        //
        // ==================================================
        // OTHER KEYS
        // ==================================================
        //
        // Keep WindowsKeyGesture synchronized so regular
        // shortcuts such as Win+E, Win+R, Win+D remain chords.
        //
        // If Glass Home captured Win-down, a non-Space key turns the gesture into
        // a real Windows shortcut. Re-inject only that Win-down (tagged so this
        // hook ignores it), then allow the real chord key through normally.
        if (down &&
            capturedWindowsDown &&
            winHeld)
        {
            NativeMethods.Input[] inputs =
            [
                new()
                {
                    Type = 1,
                    Key = (ushort)capturedWindowsKey,
                    ExtraInfo = InjectionTag
                }
            ];

            if (NativeMethods.SendInput(
                    1,
                    inputs,
                    Marshal.SizeOf<NativeMethods.Input>())
                == 1)
            {
                capturedWindowsDown = false;
                capturedWindowsKey = 0;
            }
        }

        gesture.Process(
            keyCode,
            down);

        return NativeMethods.CallNextHookEx(
            keyboardHook,
            code,
            message,
            data);
    }

    private nint WindowMessage(
        nint window,
        uint message,
        nuint wParam,
        nint lParam,
        nuint id,
        nuint data)
    {
        //
        // MA_NOACTIVATE
        //
        if (message == 0x0021)
        {
            return 3;
        }

        //
        // Bare Windows key -> dock.
        //
        if (message == ExpandMessage)
        {
            if (!suppressDockToggle && wParam == toggleRevision) HomeRequested?.Invoke(
                this,
                EventArgs.Empty);

            return 0;
        }

        //
        // Win + Space -> Glass Home.
        //
        if (message == LauncherMessage)
        {
            LauncherRequested?.Invoke(
                this,
                EventArgs.Empty);

            return 0;
        }

        //
        // WM_HOTKEY
        //
        if (message == 0x0312)
        {
            //
            // Ctrl + Alt + Space
            //
            if (wParam == 0x4701 && !suppressDockToggle)
            {
                HomeRequested?.Invoke(
                    this,
                    EventArgs.Empty);
            }

            //
            // Ctrl + Alt + F12
            //
            if (wParam == 0x4702)
            {
                RecoveryRequested?.Invoke(
                    this,
                    EventArgs.Empty);
            }

            return 0;
        }

        return NativeMethods.DefSubclassProc(
            window,
            message,
            wParam,
            lParam);
    }

    public void Dispose()
    {
        if (keyboardHook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(
                keyboardHook);
        }

        keyboardHook = 0;

        NativeMethods.UnregisterHotKey(
            hwnd,
            0x4701);

        NativeMethods.UnregisterHotKey(
            hwnd,
            0x4702);

        NativeMethods.RemoveWindowSubclass(
            hwnd,
            callback,
            1);

        IsRegistered = false;

        GC.KeepAlive(
            keyboardCallback);
    }
}
