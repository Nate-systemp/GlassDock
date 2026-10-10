using GlassDock.Core.Desktop;
using GlassDock.Windows.Interop;
using System.Runtime.InteropServices;

namespace GlassDock.Windows.Desktop;

// The sole native Win-key implementation, hosted by either helper or fallback.
// Construct/dispose on the owning message-pump thread. Signals must be nonblocking.
internal sealed class WindowsKeyHook : IDisposable
{
    private readonly Action<bool, uint> signal;
    private readonly Action<uint>? snippingShortcut;
    private readonly Action<uint>? snippingCanceled;
    private readonly Action<int, uint>? shortcutRequested;
    private readonly HashSet<int> consumedShortcutKeys = [];
    private readonly NativeMethods.KeyboardProc keyboardCallback;
    private readonly WindowsKeyGesture gesture = new();
    private nint keyboardHook;
    private const nuint InjectionTag = 0x47444F43;
    private const int LeftWindows = 0x5B, RightWindows = 0x5C, Space = 0x20;
    private bool launcherChordActive, spaceHeld, suppressCurrentWindowsPress, snipKeyHeld, snipSessionActive;
    private bool capturedWindowsDown;
    private int capturedWindowsKey;
    private sealed record State(bool Suppress, bool Capture, uint Revision);
    // Capture from the moment the native hook is registered, including the
    // brief fallback/helper handoff before the latest UI state arrives.
    private State state = new(false, true, 0);
    private uint pressRevision;
    public void RecoverAfterResume()
    {
        // Must run on the owning message-pump thread. Re-register because Windows
        // does not expose whether a low-level hook was silently removed.
        // Install the replacement before releasing the existing hook. If Windows
        // rejects re-registration during a resume transition, retaining the old
        // hook is safer than leaving Doky without any Win-key interception.
        // Both handles belong to this message-pump thread, so no keyboard event
        // can be dispatched between installation and the old hook's removal.
        var replacement = NativeMethods.SetWindowsHookExW(13, keyboardCallback, 0, 0);
        if (replacement == 0)
            throw new InvalidOperationException("Cannot recover Doky keyboard handling after resume; previous hook retained.");

        var previous = keyboardHook;
        keyboardHook = replacement;
        if (previous != 0) NativeMethods.UnhookWindowsHookEx(previous);
        ResetInputState();
        for (var key = 8; key < 256; key++)
            if (NativeMethods.GetAsyncKeyState(key) < 0) gesture.Process(key, true);
    }

    public void ResetInputState()
    {
        gesture.Reset();
        launcherChordActive = false;
        spaceHeld = false;
        snipKeyHeld = false;
        snipSessionActive = false;
        consumedShortcutKeys.Clear();
        suppressCurrentWindowsPress = false;
        capturedWindowsDown = false;
        capturedWindowsKey = 0;
        pressRevision = 0;
    }
    public void UpdateState(bool suppress, bool capture, uint revision) =>
        Volatile.Write(ref state, new(suppress, capture, revision));
    public WindowsKeyHook(Action<bool, uint> signal, Action<uint>? snippingShortcut = null, Action<uint>? snippingCanceled = null, Action<int, uint>? shortcutRequested = null)
    {
        this.signal = signal;
        this.snippingShortcut = snippingShortcut;
        this.snippingCanceled = snippingCanceled;
        this.shortcutRequested = shortcutRequested;
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

            // Letting the physical Win-down reach Explorer may activate Start
            // before the bare-Win release can be recognized. The owning service
            // enables capture for ALL active dock states (including hidden Home).
            // If another key follows, OTHER KEYS replays a tagged Win-down first
            // so genuine Win+ shortcuts can still reach Windows.
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

            // If the matching Win-down was captured, Explorer never received
            // that physical key. Consume the release as well. Bare Win belongs
            // to Doky; Win+Space still belongs to Glass Home.
            if (capturedWindowsDown &&
                capturedWindowsKey == keyCode)
            {
                capturedWindowsDown = false;
                capturedWindowsKey = 0;

                if (bareWindows && !wasLauncherShortcut &&
                    !suppressCurrentWindowsPress && !suppressDockToggle)
                {
                    signal(false, pressRevision);
                }

                // The physical Win-down never reached Windows. Do not leak an
                // unmatched Win-up for a suppressed gesture (for example when
                // Ctrl was already held); Windows may interpret it as Start.
                // Genuine Win+ shortcuts replay Win-down on the OTHER KEYS
                // path, which clears capturedWindowsDown before their Win-up.
                return 1;
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
        // DOCK TASKBAR SHORTCUTS (Win+1–9 / Win+T)
        // ==================================================
        // Do not replay the withheld Win-down for Doky-owned shortcuts.
        // The key-up is consumed as well, so Explorer receives no partial chord.
        // Ctrl/Alt/Shift combinations remain ordinary Windows shortcuts.
        if (!isWindowsKey)
        {
            if (!down && consumedShortcutKeys.Remove(keyCode))
            {
                gesture.Process(keyCode, false);
                return 1;
            }
            if (down && consumedShortcutKeys.Contains(keyCode)) return 1; // key repeat
            var shortcutIndex = DockKeyboardShortcut.FromVirtualKey(keyCode);
            if (down && winHeld && capturedWindowsDown &&
                shortcutIndex >= 0 && !gesture.IsShiftHeld &&
                !gesture.IsControlHeld && !gesture.IsAltHeld &&
                !suppressDockToggle && shortcutRequested is not null)
            {
                gesture.Process(keyCode, true);
                consumedShortcutKeys.Add(keyCode);
                shortcutRequested(shortcutIndex, pressRevision);
                return 1;
            }
        }

        //
        // ==================================================
        // OTHER KEYS
        // ==================================================
        //
        // Observe Win+Shift+S without intercepting or delaying the OS shortcut.
        // The callback merely prepares the already-rendered glass for Snipping.
        if (keyCode == 0x53) // S
        {
            if (!down) snipKeyHeld = false;
            else if (!snipKeyHeld && winHeld && gesture.IsShiftHeld)
            {
                snipKeyHeld = true;
                snipSessionActive = true;
                snippingShortcut?.Invoke(pressRevision);
            }
        }
        else if (keyCode == 0x1B && down && snipSessionActive) // Escape cancels a snip
        {
            snipSessionActive = false;
            snippingCanceled?.Invoke(pressRevision);
        }
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
