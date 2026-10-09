using GlassDock.Core.Desktop;
using GlassDock.Core.Applications;
using GlassDock.Windows.Interop;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GlassDock.Windows.Desktop;

public sealed class WindowsKeyboardService : IKeyboardService
{
    private readonly nint hwnd;
    private readonly NativeMethods.SubclassProc callback;
    private readonly bool dockShortcutsEnabled;
    private readonly WindowsInputHelperBridge? elevatedHelper;
    private WindowsKeyHook? hook;
    private bool suppressDockToggle, captureBareWindowsKey, disposed;
    private uint revision;
    private readonly InputSignalMailbox signals = new();
    private readonly InputLifecycleRecoveryGate lifecycleGate = new();
    private readonly CancellationTokenSource lifecycleLifetime = new();
    private bool sessionNotificationsRegistered;
    private nint displayPowerNotification;
    private const uint SignalMessage = 0x8047, OwnershipMessage = 0x8049;
    private const uint PowerBroadcastMessage = 0x0218, PowerSettingChange = 0x8013;
    private const uint PowerSuspend = 0x0004, PowerResumeCritical = 0x0006;
    private const uint PowerResumeSuspend = 0x0007, PowerResumeAutomatic = 0x0012;
    private const uint WtsSessionChange = 0x02B1, WtsNotifyForThisSession = 0;
    private const uint WtsSessionLock = 0x0007, WtsSessionUnlock = 0x0008;
    private const uint DisplayStateOn = 1;
    private static readonly Guid ConsoleDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");

    // Retained for existing Glass Home visibility callers. Physical Win-down
    // must now be withheld even while Home is hidden; visibility no longer
    // decides whether keyboard input is captured.
    public bool CaptureBareWindowsKey
    {
        get => captureBareWindowsKey;
        set => captureBareWindowsKey = value;
    }
    public bool SuppressDockToggle
    {
        get => suppressDockToggle;
        set
        {
            if (suppressDockToggle == value) return;
            suppressDockToggle = value;
            revision++;
            UpdateState();
        }
    }
    public bool IsRegistered { get; private set; }
    public Task<bool> RestoreElevatedWindowAsync(ApplicationWindow window) =>
        disposed || elevatedHelper is null ? Task.FromResult(false) : elevatedHelper.RestoreWindowAsync(window);
    public event EventHandler? HomeRequested;
    public event EventHandler? BareWindowsRequested;
    public event EventHandler? LauncherRequested;
    public event EventHandler? RecoveryRequested;

    public WindowsKeyboardService(nint hwnd, bool enableDockShortcuts = true, string? elevatedHelperPath = null)
    {
        this.hwnd = hwnd;
        dockShortcutsEnabled = enableDockShortcuts;
        callback = WindowMessage;
        if (!NativeMethods.SetWindowSubclass(hwnd, callback, 1, 0))
            throw new InvalidOperationException("Cannot attach Doky hotkeys.");
        if (enableDockShortcuts)
        {
            sessionNotificationsRegistered = NativeMethods.WTSRegisterSessionNotification(hwnd, WtsNotifyForThisSession);
            var displayState = ConsoleDisplayState;
            displayPowerNotification = NativeMethods.RegisterPowerSettingNotification(hwnd, ref displayState, 0);
        }
        if (enableDockShortcuts) NativeMethods.RegisterHotKey(hwnd, 0x4701, 0x4003, 0x20);
        IsRegistered = NativeMethods.RegisterHotKey(hwnd, 0x4702, 0x4003, 0x7B);
        StartFallback();
        if (enableDockShortcuts && !string.IsNullOrWhiteSpace(elevatedHelperPath) && File.Exists(elevatedHelperPath))
        {
            elevatedHelper = new WindowsInputHelperBridge(elevatedHelperPath,
                take => NativeMethods.PostMessageW(hwnd, OwnershipMessage, take ? 1u : 0u, 0),
                PostSignal);
            UpdateState();
        }
    }

    private void UpdateState()
    {
        // Always withhold physical Win-down while dock shortcuts are active.
        // Explorer can open Start on the initial Win-down, *before* we know
        // whether the gesture is bare Win or a Win+key shortcut. Capturing
        // only while Home is visible leaks Windows Start when Home is hidden.
        // WindowsKeyHook replays a tagged Win-down for genuine shortcuts.
        var captureWinDown = dockShortcutsEnabled;
        hook?.UpdateState(suppressDockToggle, captureWinDown, revision);
        elevatedHelper?.UpdateState(suppressDockToggle, captureWinDown, revision);
    }
    private void StartFallback()
    {
        if (!dockShortcutsEnabled || disposed || hook is not null) return;
        hook = new WindowsKeyHook((launcher, version) =>
            PostSignal(new InputSignal(launcher, version, Environment.TickCount64)));
        UpdateState();
    }
    private void PostSignal(InputSignal signal)
    {
        if (signals.Publish(signal)) NativeMethods.PostMessageW(hwnd, SignalMessage, 0, 0);
    }

    private void MarkLifecycleBoundary(string reason)
    {
        if (disposed) return;
        lifecycleGate.MarkBoundary();
        revision++;
        signals.Clear();
        hook?.ResetInputState();
        UpdateState();
        Debug.WriteLine($"Doky input lifecycle boundary: {reason}; revision={revision}");
    }

    private void RequestLifecycleRecovery(string reason)
    {
        if (disposed || !lifecycleGate.RequestRecovery()) return;
        revision++;
        signals.Clear();
        UpdateState();

        // The fallback hook is owned by this window's UI/message thread. Re-arm
        // it synchronously before any new input can be accepted. The elevated
        // hook is revalidated through the existing IPC connection below.
        if (hook is not null)
        {
            try { hook.RecoverAfterResume(); }
            catch (Exception error) when (error is InvalidOperationException or Win32Exception)
            {
                // RecoverAfterResume retains the previous hook when a fresh
                // registration fails. Do not dispose that remaining protection.
                Debug.WriteLine($"Doky fallback hook recovery deferred (previous hook retained): {error.Message}");
            }
        }
        else if (elevatedHelper is null)
        {
            // No helper exists to own interception: restore the fallback.
            try { StartFallback(); }
            catch (Exception fallbackError) when (fallbackError is InvalidOperationException or Win32Exception)
            { Debug.WriteLine($"Doky fallback hook start failed: {fallbackError.Message}"); }
        }

        var helper = elevatedHelper;
        if (helper is null) return;
        _ = RecoverHelperAsync(helper, reason);
    }

    private async Task RecoverHelperAsync(WindowsInputHelperBridge helper, string reason)
    {
        try
        {
            await helper.RecoverAsync().WaitAsync(lifecycleLifetime.Token);
            Debug.WriteLine($"Doky input lifecycle recovery completed: {reason}; revision={revision}");
        }
        catch (OperationCanceledException) when (lifecycleLifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException or ObjectDisposedException)
        { Debug.WriteLine($"Doky input lifecycle recovery deferred: {error.Message}"); }
    }

    private static bool IsDisplayOn(nint lParam)
    {
        if (lParam == 0) return true;
        try
        {
            // POWERBROADCAST_SETTING is GUID + DataLength + data bytes. The
            // registered GUID means no second GUID comparison is necessary.
            return Marshal.ReadInt32(lParam, 20) == DisplayStateOn;
        }
        catch (AccessViolationException) { return true; }
    }

    private nint WindowMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == PowerBroadcastMessage)
        {
            switch ((uint)wParam)
            {
                case PowerSuspend:
                    MarkLifecycleBoundary("suspend");
                    return 1;
                case PowerResumeCritical:
                case PowerResumeSuspend:
                case PowerResumeAutomatic:
                    RequestLifecycleRecovery("resume");
                    return 1;
                case PowerSettingChange:
                    if (IsDisplayOn(lParam)) RequestLifecycleRecovery("display-on");
                    else MarkLifecycleBoundary("display-off");
                    return 1;
            }
        }
        if (message == WtsSessionChange)
        {
            switch ((uint)wParam)
            {
                case WtsSessionLock:
                    MarkLifecycleBoundary("session-lock");
                    break;
                case WtsSessionUnlock:
                    RequestLifecycleRecovery("session-unlock");
                    break;
            }
            return 0;
        }
        if (message == OwnershipMessage)
        {
            if (!disposed && elevatedHelper is not null)
            {
                if (wParam == 1)
                {
                    hook?.Dispose(); hook = null;
                    signals.Clear();
                    elevatedHelper.AllowHelper(); // GO is sent only after the local hook is removed.
                }
                else StartFallback(); // helper process has exited and its hook is gone
            }
            return 0;
        }
        if (message == SignalMessage)
        {
            var signal = signals.Take(Environment.TickCount64);
            if (!disposed && signal is not null && signal.Revision == revision && !suppressDockToggle)
            {
                if (signal.Launcher) LauncherRequested?.Invoke(this, EventArgs.Empty);
                else BareWindowsRequested?.Invoke(this, EventArgs.Empty);
            }
            return 0;
        }
        if (message == 0x0021) return 3;
        if (message == 0x0312)
        {
            if (wParam == 0x4701 && !suppressDockToggle) HomeRequested?.Invoke(this, EventArgs.Empty);
            if (wParam == 0x4702) RecoveryRequested?.Invoke(this, EventArgs.Empty);
            return 0;
        }
        return NativeMethods.DefSubclassProc(window, message, wParam, lParam);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifecycleLifetime.Cancel();
        lifecycleGate.Shutdown();
        signals.Clear();
        elevatedHelper?.Dispose();
        hook?.Dispose(); hook = null;
        if (sessionNotificationsRegistered) NativeMethods.WTSUnRegisterSessionNotification(hwnd);
        if (displayPowerNotification != 0) NativeMethods.UnregisterPowerSettingNotification(displayPowerNotification);
        NativeMethods.UnregisterHotKey(hwnd, 0x4701);
        NativeMethods.UnregisterHotKey(hwnd, 0x4702);
        NativeMethods.RemoveWindowSubclass(hwnd, callback, 1);
        IsRegistered = false;
    }
}
