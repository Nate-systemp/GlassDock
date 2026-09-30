using GlassDock.Core.Desktop;
using GlassDock.Windows.Interop;
using System.Runtime.InteropServices;

namespace GlassDock.Windows.Desktop;

// The sole native Win-key implementation, hosted by either helper or fallback.
// Construct/dispose on the owning message-pump thread. Signals must be nonblocking.
internal sealed class WindowsKeyHook : IDisposable
{
    private readonly Action<bool, uint> signal;
    private readonly NativeMethods.KeyboardProc keyboardCallback;
    private readonly WindowsKeyGesture gesture = new();
    private nint keyboardHook;
    private const nuint InjectionTag = 0x47444F43;
    private const int LeftWindows = 0x5B, RightWindows = 0x5C, Space = 0x20;
    private bool launcherChordActive, spaceHeld, suppressCurrentWindowsPress;
    private bool capturedWindowsDown;
    private int capturedWindowsKey;
    private sealed record State(bool Suppress, bool Capture, uint Revision);
    private State state = new(false, false, 0);
    private uint pressRevision;
    public void UpdateState(bool suppress, bool capture, uint revision) =>
        Volatile.Write(ref state, new(suppress, capture, revision));
    public WindowsKeyHook(Action<bool, uint> signal)
    {
        this.signal = signal;
        keyboardCallback = KeyboardMessage;
        for (var key = 8; key < 256; key++)
            if (NativeMethods.GetAsyncKeyState(key) < 0) gesture.Process(key, true);
        keyboardHook = NativeMethods.SetWindowsHookExW(13, keyboardCallback, 0, 0);
        if (keyboardHook == 0) throw new InvalidOperationException("Cannot register Doky keyboard handling.");
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

        var current = Volatile.Read(ref state);
        var suppressDockToggle = current.Suppress;
        var captureBareWindowsKey = current.Capture;
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

        var winHeld = gesture.IsWindowsHeld;

        //
        // ==================================================
        // WINDOWS KEY DOWN
        // ==================================================
        //
        if (isWindowsKey && down)
        {
            if (!winHeld)
            {
                suppressCurrentWindowsPress = suppressDockToggle;
                pressRevision = current.Revision;
            }
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

            signal(true, pressRevision);

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
                        signal(false, pressRevision);
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
                    if (!suppressCurrentWindowsPress && !suppressDockToggle) signal(false, pressRevision);

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

    public void Dispose()
    {
        if (keyboardHook != 0) NativeMethods.UnhookWindowsHookEx(keyboardHook);
        keyboardHook = 0;
        GC.KeepAlive(keyboardCallback);
    }
}