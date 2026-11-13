using GlassDock.Core.Desktop;
using GlassDock.Windows.Interop;
using System.Runtime.InteropServices;

namespace GlassDock.Windows.Desktop;

internal sealed class WindowsKeyHook : IDisposable
{
    private readonly Action<bool, uint> signal;
    private readonly Action<uint>? snippingShortcut;
    private readonly Action<uint>? snippingCanceled;
    private readonly Action<int, uint>? shortcutRequested;
    private readonly Func<uint, bool>? volumeRequested;
    private readonly VolumeKeyCapture volumeKeys = new();
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
    private State state = new(false, true, 0);
    private uint pressRevision;
    private uint snippingRevision;
    public void RecoverAfterResume()
    {
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
        volumeKeys.Reset();
        suppressCurrentWindowsPress = false;
        capturedWindowsDown = false;
        capturedWindowsKey = 0;
        pressRevision = 0;
    }
    public void UpdateState(bool suppress, bool capture, uint revision) =>
        Volatile.Write(ref state, new(suppress, capture, revision));
    public WindowsKeyHook(Action<bool, uint> signal, Action<uint>? snippingShortcut = null, Action<uint>? snippingCanceled = null, Action<int, uint>? shortcutRequested = null,
        Func<uint, bool>? volumeRequested = null)
    {
        this.signal = signal;
        this.snippingShortcut = snippingShortcut;
        this.snippingCanceled = snippingCanceled;
        this.shortcutRequested = shortcutRequested;
        this.volumeRequested = volumeRequested;
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

        if (key.ExtraInfo == InjectionTag)
        {
            return NativeMethods.CallNextHookEx(
                keyboardHook,
                code,
                message,
                data);
        }

        if (message is not (
            0x100 or
            0x101 or
            0x104 or
            0x105))
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

        if (volumeRequested is not null && volumeKeys.Process(key.Key, down, true, volumeRequested)) return 1;

        var isWindowsKey =
            keyCode == LeftWindows ||
            keyCode == RightWindows;

        var winHeld = gesture.IsWindowsHeld;

        if (isWindowsKey && down)
        {
            if (!winHeld)
            {
                suppressCurrentWindowsPress = suppressDockToggle;
                pressRevision = current.Revision;
            }
            if (!spaceHeld)
            {
                launcherChordActive = false;
            }

            gesture.Process(
                keyCode,
                true);

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

        if (keyCode == Space &&
            down &&
            winHeld)
        {
            if (spaceHeld)
            {
                return 1;
            }

            spaceHeld = true;
            launcherChordActive = true;

            gesture.Process(
                keyCode,
                true);

            signal(true, pressRevision);

            return 1;
        }

        if (keyCode == Space &&
            !down)
        {
            var belongedToLauncher =
                launcherChordActive;

            spaceHeld = false;

            gesture.Process(
                keyCode,
                false);

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

        if (isWindowsKey && !down)
        {
            var wasLauncherShortcut =
                launcherChordActive;

            launcherChordActive = false;
            spaceHeld = false;

            var bareWindows =
                gesture.Process(
                    keyCode,
                    false);

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

                return 1;
            }

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

                return NativeMethods.CallNextHookEx(
                    keyboardHook,
                    code,
                    message,
                    data);
            }

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

        if (!isWindowsKey)
        {
            if (!down && consumedShortcutKeys.Remove(keyCode))
            {
                gesture.Process(keyCode, false);
                return 1;
            }
            if (down && consumedShortcutKeys.Contains(keyCode)) return 1;
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

        if (keyCode is 0x53 or 0x2C)
        {
            if (!down) snipKeyHeld = false;
            else if (!snipKeyHeld && (keyCode == 0x2C || winHeld && gesture.IsShiftHeld))
            {
                snipKeyHeld = true;
                snipSessionActive = true;
                snippingRevision = keyCode == 0x2C ? current.Revision : pressRevision;
                snippingShortcut?.Invoke(snippingRevision);
            }
        }
        else if (keyCode == 0x1B && down && snipSessionActive)
        {
            snipSessionActive = false;
            snippingCanceled?.Invoke(snippingRevision);
        }
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
