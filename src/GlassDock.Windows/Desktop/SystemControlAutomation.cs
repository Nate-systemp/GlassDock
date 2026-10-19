using System.Diagnostics;
using System.Runtime.InteropServices;
using static GlassDock.Windows.Desktop.SystemControlUiaInterop;

namespace GlassDock.Windows.Desktop;

public enum WindowsQuickControl { Focus, Airplane, NightLight }
public sealed record WindowsControlObservation(string Name, string AutomationId, int ControlType, bool Enabled, bool Offscreen, bool? ToggleState);
public sealed record WindowsQuickControlState(bool? Focus, bool? Airplane, bool? NightLight);
public sealed record WindowsControlChangeResult(bool Succeeded, bool? State, string Message, bool CanReturnToCaller = false);

/// <summary>Uses the public accessibility surface of the real Windows Settings controls.
/// Providers and RCWs never leave their MTA operation. No injected input or registry writes.</summary>
public sealed class SystemControlAutomation
{
    private readonly SemaphoreSlim access = new(1, 1);
    private int commandPending;
    internal const string AirplaneId = "SystemSettings_Radio_IsAirplaneModeEnabled_SettingsApp_ToggleSwitch";
    internal const string NightLightId = "SystemSettings_Display_BlueLight_AutomaticOnScheduleWithTime_ToggleSwitch";
    internal const string FocusStartId = "focusStartButton";
    internal const string FocusStopId = "focusStopButton";

    internal static bool Matches(WindowsQuickControl control, WindowsControlObservation item) =>
        item.ControlType == 50000 && control switch
        {
            WindowsQuickControl.Airplane => item.AutomationId == AirplaneId,
            WindowsQuickControl.NightLight => item.AutomationId == NightLightId,
            WindowsQuickControl.Focus => item.AutomationId is FocusStartId or FocusStopId,
            _ => false
        };

    internal static bool? FocusState() => SystemRadioControls.FocusStatus() switch { "On" => true, "Off" => false, _ => null };
    internal static int SelectTarget(WindowsQuickControl kind, IReadOnlyList<WindowsControlObservation> items, bool requireVisible = true)
    {
        var found = -1;
        for (var i = 0; i < items.Count; i++)
        {
            if (!Matches(kind, items[i])) continue;
            if (found >= 0) return -1; // Ambiguity is never permission to invoke the first match.
            found = i;
        }
        return found >= 0 && items[found].Enabled && (!requireVisible || !items[found].Offscreen) ? found : -1;
    }
    internal static string Page(WindowsQuickControl control) => control switch
    {
        WindowsQuickControl.Focus => "ms-settings:quiethours",
        WindowsQuickControl.Airplane => "ms-settings:network-airplanemode",
        WindowsQuickControl.NightLight => "ms-settings:display",
        _ => throw new ArgumentOutOfRangeException(nameof(control))
    };

    public async Task<WindowsQuickControlState> ReadAsync(CancellationToken cancellation = default)
    {
        // A passive refresh never navigates Settings or queues behind an in-flight command.
        if (!await access.WaitAsync(0, cancellation).ConfigureAwait(false)) return new(FocusState(), null, null);
        try
        {
            return await Task.Run(() =>
            {
                using var session = new Session();
                using var items = session.Read();
                bool? State(WindowsQuickControl kind)
                {
                    var found = items.Items.Where(x => Matches(kind, x.Info)).ToArray();
                    return found.Length == 1 ? ReadToggle(found[0].Element) : null;
                }
                return new WindowsQuickControlState(FocusState(), State(WindowsQuickControl.Airplane), State(WindowsQuickControl.NightLight));
            }, cancellation).ConfigureAwait(false);
        }
        catch (Exception e) when (IsProviderFailure(e)) { return new(FocusState(), null, null); }
        finally { access.Release(); }
    }

    public Task<WindowsControlChangeResult> ToggleAsync(WindowsQuickControl control, CancellationToken cancellation = default) =>
        ExecuteAsync(() =>
        {
            using var session = new Session();
            return Change(() => session.Resolve(control), () =>
            {
                Process.Start(new ProcessStartInfo(Page(control)) { UseShellExecute = true });
            }, cancellation);
        }, cancellation);

    // Endpoint is an operation-scoped, testable contract: read real state, invoke exactly once, dispose ownership.
    internal sealed record Endpoint(Func<bool?> Read, Action<bool> Change, Action Dispose) : IDisposable
    { void IDisposable.Dispose() => Dispose(); }

    internal async Task<WindowsControlChangeResult> ExecuteAsync(Func<WindowsControlChangeResult> operation, CancellationToken cancellation = default)
    {
        if (Interlocked.CompareExchange(ref commandPending, 1, 0) != 0)
            return new(false, null, "Another Windows control is changing. Please wait.");
        var entered = false;
        try
        {
            await access.WaitAsync(cancellation).ConfigureAwait(false);
            entered = true;
            return await Task.Run(() => { cancellation.ThrowIfCancellationRequested(); return operation(); }, cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return new(false, null, "Windows control change canceled."); }
        catch (Exception e) when (IsProviderFailure(e))
        { return new(false, null, "Windows could not complete or verify this change. Its control may be unavailable, restricted, or changed in this Windows version.", IsSettingsForeground()); }
        finally { if (entered) access.Release(); Volatile.Write(ref commandPending, 0); }
    }

    internal static WindowsControlChangeResult Change(Func<Endpoint?> resolve, Action openPage, CancellationToken cancellation,
        int attempts = 50, int delayMs = 120)
    {
        cancellation.ThrowIfCancellationRequested();
        Endpoint? endpoint = resolve();
        if (endpoint is null)
        {
            cancellation.ThrowIfCancellationRequested();
            openPage();
            for (var i = 0; i < attempts && endpoint is null; i++)
            {
                if (Pause(delayMs, cancellation)) cancellation.ThrowIfCancellationRequested();
                endpoint = resolve();
            }
        }
        if (endpoint is null) return new(false, null, "Windows did not expose the expected control. No toggle was sent.", IsSettingsForeground());
        using (endpoint)
        {
            var before = endpoint.Read();
            if (!before.HasValue) return new(false, null, "Windows cannot report the current state. No toggle was sent.", IsSettingsForeground());
            cancellation.ThrowIfCancellationRequested();
            var desired = !before.Value;
            // Change revalidates the live state before invoking. Never retry a possibly accepted command.
            endpoint.Change(desired);
            for (var i = 0; i < attempts; i++)
            {
                var actual = endpoint.Read();
                if (actual == desired) return new(true, actual, "Windows confirmed the change.", IsSettingsForeground());
                if (Pause(delayMs, cancellation))
                    return new(false, null, "The request was sent, but confirmation was canceled. Check Windows before trying again.");
            }
            return new(false, null, "Windows did not confirm the requested state. The command was not repeated.", IsSettingsForeground());
        }
    }

    private static bool Pause(int milliseconds, CancellationToken cancellation)
    {
        // The popup can cancel and dispose its CTS while this MTA operation finishes.
        // Do not access CancellationToken.WaitHandle, which throws after source disposal.
        if (!cancellation.IsCancellationRequested && milliseconds > 0) Thread.Sleep(milliseconds);
        return cancellation.IsCancellationRequested;
    }

    private static bool IsProviderFailure(Exception e) => e is COMException or UnauthorizedAccessException
        or InvalidOperationException or NotSupportedException or ArgumentException or System.ComponentModel.Win32Exception;

    // Read-only diagnostic: callers opt in; never written to telemetry or periodic logs.
    public Task<IReadOnlyList<WindowsControlObservation>> InspectAsync() => Task.Run<IReadOnlyList<WindowsControlObservation>>(() =>
    {
        using var session = new Session();
        using var controls = session.Read(diagnostic: true);
        return controls.Items.Select(x => x.Info).ToArray();
    });

    private sealed class Session : IDisposable
    {
        private readonly IAutomation automation;
        private readonly ICondition condition;
        private readonly HashSet<string> revealed = [];
        public Session()
        {
            automation = (IAutomation)Activator.CreateInstance(Type.GetTypeFromCLSID(new("e22ad333-b25f-460c-83d0-0581107395c9"), true)!)!;
            try
            {
                automation.put_AutoSetFocus(0);
                automation.put_ConnectionTimeout(750);
                automation.put_TransactionTimeout(750);
                // Query only observed action IDs, never every text/control on the page.
                automation.CreatePropertyCondition(30011, FocusStartId, out var combined);
                try
                {
                    foreach (var id in new[] { FocusStopId, AirplaneId, NightLightId })
                    {
                        automation.CreatePropertyCondition(30011, id, out var next);
                        try
                        {
                            automation.CreateOrCondition(combined, next, out var joined);
                            Release(combined); combined = joined;
                        }
                        finally { Release(next); }
                    }
                    condition = combined;
                }
                catch { Release(combined); throw; }
            }
            catch { Release(automation); throw; }
        }

        public Endpoint? Resolve(WindowsQuickControl kind)
        {
            var controls = Read();
            try
            {
                var index = SelectTarget(kind, controls.Items.Select(x => x.Info).ToArray(), requireVisible: false);
                if (index < 0)
                { controls.Dispose(); return null; }
                var target = controls.Items[index];
                if (target.Info.Offscreen)
                {
                    // Reveal only the unique, enabled, verified target during an explicit command.
                    // Focus is the native fallback for controls without ScrollItemPattern;
                    // neither path invokes or toggles anything. A subsequent read must see it.
                    if (revealed.Add(target.Info.AutomationId) && IsSettingsWindow(target.Window))
                    {
                        object? scroll = null;
                        try
                        {
                            try { target.Element.GetCurrentPattern(10017, out scroll); }
                            catch (COMException) { }
                            if (scroll is IScrollItem item) item.ScrollIntoView();
                            else target.Element.SetFocus();
                        }
                        finally { Release(scroll); }
                    }
                    controls.Dispose();
                    return null;
                }
                bool? Read() => kind == WindowsQuickControl.Focus ? FocusState() : ReadToggle(target.Element);
                void Change(bool desired)
                {
                    if (!IsSettingsWindow(target.Window) || !Property<bool>(target.Element, 30010) || Property<bool>(target.Element, 30022))
                        throw new InvalidOperationException("Windows control is no longer available.");
                    var actual = Read();
                    if (!actual.HasValue) throw new InvalidOperationException("Unknown Windows state.");
                    if (actual == desired) return; // A concurrent external change already reached the requested state.
                    if (kind == WindowsQuickControl.Focus && target.Info.AutomationId != (desired ? FocusStartId : FocusStopId))
                        throw new InvalidOperationException("Focus action changed before invocation.");
                    object? pattern = null;
                    try
                    {
                        target.Element.GetCurrentPattern(kind == WindowsQuickControl.Focus ? 10000 : 10015, out pattern);
                        if (kind == WindowsQuickControl.Focus && pattern is IInvoke invoke) invoke.Invoke();
                        else if (kind != WindowsQuickControl.Focus && pattern is IToggle toggle) toggle.Toggle();
                        else throw new NotSupportedException("Expected automation pattern is unavailable.");
                    }
                    finally { Release(pattern); }
                }
                return new(Read, Change, controls.Dispose);
            }
            catch { controls.Dispose(); throw; }
        }

        public Controls Read(bool diagnostic = false)
        {
            var result = new Controls();
            try
            {
                foreach (var hwnd in SettingsWindows())
                {
                    automation.ElementFromHandle(hwnd, out var root);
                    try
                    {
                        ICondition? all = null;
                        IElements elements;
                        try
                        {
                            if (diagnostic) automation.CreateTrueCondition(out all);
                            root.FindAll(4, all ?? condition, out elements);
                        }
                        finally { Release(all); }
                        try
                        {
                            elements.get_Length(out var count);
                            if (count > 1500) throw new InvalidOperationException("Windows Settings automation tree is too large.");
                            for (var i = 0; i < count; i++)
                            {
                                elements.GetElement(i, out var element);
                                try
                                {
                                    var id = Property<string>(element, 30011) ?? "";
                                    var type = Property<int>(element, 30003);
                                    if (type is not (50000 or 50002 or 50003 or 50020)) continue;
                                    var info = new WindowsControlObservation(diagnostic ? Property<string>(element, 30005) ?? "" : "",
                                        id, type, Property<bool>(element, 30010), Property<bool>(element, 30022), ReadToggle(element));
                                    result.Items.Add(new(element, info, hwnd));
                                    element = null!;
                                }
                                finally { Release(element); }
                            }
                        }
                        finally { Release(elements); }
                    }
                    finally { Release(root); }
                }
                return result;
            }
            catch { result.Dispose(); throw; }
        }
        public void Dispose() { Release(condition); Release(automation); }
    }
    private sealed record Control(IElement Element, WindowsControlObservation Info, nint Window);
    private sealed class Controls : IDisposable
    {
        public List<Control> Items { get; } = [];
        public void Dispose() { foreach (var item in Items) Release(item.Element); Items.Clear(); }
    }
    private static T? Property<T>(IElement element, int id)
    { element.GetCurrentPropertyValue(id, out var value); return value is T typed ? typed : default; }
    private static bool? ReadToggle(IElement element)
    {
        object? pattern = null;
        try
        {
            element.GetCurrentPattern(10015, out pattern);
            if (pattern is not IToggle toggle) return null;
            toggle.get_CurrentToggleState(out var state);
            return state switch { 0 => false, 1 => true, _ => null };
        }
        catch (COMException) { return null; }
        finally { Release(pattern); }
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    private static bool HasImage(nint hwnd, string path)
    {
        if (!IsWindow(hwnd)) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return string.Equals(process.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception) { return false; }
    }
    private static bool IsSettingsWindow(nint hwnd) => HasImage(hwnd, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "ImmersiveControlPanel", "SystemSettings.exe"));
    public static bool IsSettingsForeground()
    {
        var foreground = GetForegroundWindow();
        return foreground != 0 && SettingsWindows().Any(hwnd => hwnd == foreground || GetAncestor(hwnd, 2) == foreground);
    }
    private static List<nint> SettingsWindows()
    {
        var result = new List<nint>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            if (IsSettingsWindow(hwnd)) result.Add(hwnd);
            else if (HasImage(hwnd, Path.Combine(Environment.SystemDirectory, "ApplicationFrameHost.exe")))
                EnumChildWindows(hwnd, (child, _) =>
                {
                    // Select one verified Settings root per host, avoiding duplicate descendants.
                    if (IsWindowVisible(child) && IsSettingsWindow(child)) { result.Add(child); return false; }
                    return true;
                }, 0);
            return true;
        }, 0);
        return result;
    }
    private delegate bool EnumWindowsCallback(nint hwnd, nint parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumChildWindows(nint hwnd, EnumWindowsCallback callback, nint parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
}

