using GlassDock.Core.Desktop;
using GlassDock.Windows.Interop;

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
    private const uint SignalMessage = 0x8047, OwnershipMessage = 0x8049;

    public bool CaptureBareWindowsKey
    {
        get => captureBareWindowsKey;
        set { captureBareWindowsKey = value; UpdateState(); }
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
        hook?.UpdateState(suppressDockToggle, captureBareWindowsKey, revision);
        elevatedHelper?.UpdateState(suppressDockToggle, captureBareWindowsKey, revision);
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
    private nint WindowMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == OwnershipMessage)
        {
            if (!disposed && elevatedHelper is not null)
            {
                if (wParam == 1)
                {
                    hook?.Dispose(); hook = null;
                    signals.Take(Environment.TickCount64);
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
        elevatedHelper?.Dispose();
        hook?.Dispose(); hook = null;
        NativeMethods.UnregisterHotKey(hwnd, 0x4701);
        NativeMethods.UnregisterHotKey(hwnd, 0x4702);
        NativeMethods.RemoveWindowSubclass(hwnd, callback, 1);
        IsRegistered = false;
    }
}
