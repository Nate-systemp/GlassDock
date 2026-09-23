using System.ComponentModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using GlassDock.Core.Applications;
using GlassDock.Windows.Interop;

namespace GlassDock.Windows.Applications;

public sealed class WindowsApplicationService : IApplicationService
{
    private readonly AutoResetEvent refresh = new(false);
    private readonly ConcurrentDictionary<nint, long> activationTimes = new();
    private readonly WindowsApplicationLauncher launcher = new();
    private readonly DockPinStore pinStore = new();
    private readonly NativeMethods.WinEventProc callback;
    private readonly List<nint> hooks = [];
    private Thread? worker;
    private volatile bool stopping;
    private IReadOnlyList<PinnedApplication> currentPins = Array.Empty<PinnedApplication>();
    private int pinRevision;
    private IReadOnlyList<DockApplication> currentApplications = Array.Empty<DockApplication>();
    public event EventHandler<ApplicationSnapshot>? SnapshotChanged;

    public WindowsApplicationService() => callback = (_, eventId, window, objectId, childId, _, _) =>
    {
        if (eventId == 3) activationTimes[window] = Stopwatch.GetTimestamp();
        if (eventId == 0x8001 && objectId == 0) activationTimes.TryRemove(window, out _);
        if (eventId == 3 || (objectId == 0 && childId == 0)) RequestRefresh();
    };

    public void Start()
    {
        if (worker is not null || stopping) return;
        // Register on the caller's UI thread, which already pumps messages.
        foreach (var (first, last) in new (uint, uint)[] { (3, 3), (0x8000, 0x8003), (0x800C, 0x800C), (0x8017, 0x8018) })
        {
            var hook = NativeMethods.SetWinEventHook(first, last, 0, callback, 0, 0, 2); // OUTOFCONTEXT | SKIPOWNPROCESS
            if (hook != 0) hooks.Add(hook);
        }
        worker = new Thread(Reconcile) { IsBackground = true, Name = "GlassDock application discovery" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    public void RequestRefresh()
    {
        if (stopping) return;
        try { refresh.Set(); } catch (ObjectDisposedException) { }
    }

    private void Reconcile()
    {
        var icons = new WindowsApplicationIconService();
        IReadOnlyList<PinnedApplication> pins = [];
        string? pinWarning = null;
        var lastPins = DateTime.MinValue;
        var lastPinRevision = -1;
        try
        {
            while (!stopping)
            {
                try
                {
                    // Pin order has no supported notification API. Reconcile periodically,
                    // and immediately after GlassDock changes pin/order preferences.
                    var revision = Volatile.Read(ref pinRevision);
                    if (revision != lastPinRevision ||
                        DateTime.UtcNow - lastPins > TimeSpan.FromSeconds(3))
                    {
                        try
                        {
                            pins = pinStore.Apply(ShellApplicationMetadata.ReadPinned(icons), icons);
                            Volatile.Write(ref currentPins, pins);
                            lastPinRevision = revision;
                            pinWarning = null;
                        }
                        catch (Exception error) when (error is COMException or InvalidCastException or IOException or UnauthorizedAccessException)
                        {
                            pinWarning = $"Pinned taskbar enumeration unavailable (0x{error.HResult:X8}); preserving the last known pins.";
                        }
                        lastPins = DateTime.UtcNow;
                    }
                    var windows = ReadWindows(icons);
                    var applications = pinStore.ApplyOrder(DockApplicationCollection.Combine(pins, windows));
                    Volatile.Write(ref currentApplications, applications);
                    icons.Retain(pins.Select(pin => pin.Identity.Key).Concat(windows.Select(window => window.Identity.Key)));
                    if (!stopping) SnapshotChanged?.Invoke(this, new(applications, pinWarning));
                }
                catch (Exception error) when (error is COMException or Win32Exception or InvalidOperationException)
                {
                    // A disappearing window should not take down the dock or its recovery heartbeat.
                    Debug.WriteLine($"Application reconciliation deferred: {error.Message}");
                }
                refresh.WaitOne(TimeSpan.FromSeconds(3));
                if (!stopping) Thread.Sleep(150); // Coalesce show/name/foreground bursts.
                refresh.Reset();
            }
        }
        finally { refresh.Dispose(); }
    }

    private IReadOnlyList<ApplicationWindow> ReadWindows(WindowsApplicationIconService? icons)
    {
        var result = new List<ApplicationWindow>();
        using var currentProcess = Process.GetCurrentProcess();
        var sessionId = currentProcess.SessionId;
        var foreground = ApplicationNative.GetForegroundWindow();
        ApplicationNative.EnumWindows((window, _) =>
        {
            try
            {
                if (!ApplicationNative.IsWindowVisible(window)) return true;
                var style = ApplicationNative.GetWindowLongPtr(window, -20).ToInt64();
                var appWindow = (style & 0x40000) != 0;
                if (!appWindow && ((style & (0x80 | 0x08000000)) != 0 || ApplicationNative.GetWindow(window, 4) != 0)) return true;
                if (ApplicationNative.DwmGetWindowAttribute(window, 14, out var cloaked, sizeof(int)) >= 0 && cloaked != 0) return true;
                var title = new StringBuilder(1024);
                if (ApplicationNative.GetWindowText(window, title, title.Capacity) == 0) return true;
                var className = new StringBuilder(256);
                ApplicationNative.GetClassName(window, className, className.Capacity);
                if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
                ApplicationNative.GetWindowThreadProcessId(window, out var pid);
                if (pid == Environment.ProcessId) return true;
                var path = ProcessPath(pid);
                if (Path.GetFileName(path).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
                {
                    var hostPid = pid;
                    ApplicationNative.EnumChildWindows(window, (child, _) =>
                    {
                        ApplicationNative.GetWindowThreadProcessId(child, out var childPid);
                        if (childPid != hostPid && ApplicationNative.IsWindowVisible(child)) { pid = childPid; return false; }
                        return true;
                    }, 0);
                    path = ProcessPath(pid);
                }
                if (string.IsNullOrEmpty(path) || Path.GetFileName(path).ToUpperInvariant() is
                    "APPLICATIONFRAMEHOST.EXE" or "STARTMENUEXPERIENCEHOST.EXE" or "SHELLEXPERIENCEHOST.EXE" or
                    "SEARCHHOST.EXE" or "TEXTINPUTHOST.EXE" or "GLASSDOCK.APP.EXE" or "GLASSDOCK.WATCHDOG.EXE") return true;
                using var process = Process.GetProcessById((int)pid);
                if (process.SessionId != sessionId) return true;
                string? appId = null;
                var iid = typeof(ApplicationNative.IPropertyStore).GUID;
                if (ApplicationNative.SHGetPropertyStoreForWindow(window, in iid, out var properties) >= 0)
                {
                    try { appId = ShellApplicationMetadata.Property(properties, "System.AppUserModel.ID"); }
                    finally { Marshal.ReleaseComObject(properties); }
                }
                using (var handle = ApplicationNative.OpenProcess(0x1000, false, pid))
                {
                    uint length = 0;
                    if (appId is null && !handle.IsInvalid && ApplicationNative.GetApplicationUserModelId(handle.DangerousGetHandle(), ref length, null) == 122 && length < 32768)
                    {
                        var text = new StringBuilder((int)length);
                        if (ApplicationNative.GetApplicationUserModelId(handle.DangerousGetHandle(), ref length, text) == 0) appId = text.ToString();
                    }
                }
                // Explorer folder windows often omit their Shell-assigned taskbar identity.
                if (appId is null && className.ToString() == "CabinetWClass" &&
                    Path.GetFileName(path).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
                    appId = "Microsoft.Windows.Explorer";
                var identity = new ApplicationIdentity(appId, path);
                var name = FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(path);
                result.Add(new(identity, name, window, (int)pid, process.StartTime.ToUniversalTime().Ticks, window == foreground,
                    icons?.FromWindow(identity.Key, window, path, appId), title.ToString(), ApplicationNative.IsIconic(window),
                    activationTimes.GetValueOrDefault(window)));
            }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException or ArgumentException or IOException or COMException)
            { Debug.WriteLine($"Skipping unavailable application window: {error.HResult:X8}"); }
            return true;
        }, 0);
        return result;
    }

    private static string ProcessPath(uint pid)
    {
        using var process = ApplicationNative.OpenProcess(0x1000, false, pid);
        if (process.IsInvalid) return "";
        uint length = 32768;
        var path = new StringBuilder((int)length);
        return ApplicationNative.QueryFullProcessImageName(process, 0, path, ref length) ? path.ToString() : "";
    }

    public bool LaunchOrActivate(DockApplication application)
    {
        // Recheck before launching: an app can open between reconciliation and the user's click.
        var current = DockApplicationCollection.Combine(Volatile.Read(ref currentPins), ReadWindows(null))
            .FirstOrDefault(item => item.Id == application.Id);
        if (current is not null) application = current;
        // A pin removed since the snapshot must not be launched as a stale pinned item.
        else if (application.IsPinned) { RequestRefresh(); return false; }
        foreach (var existing in application.Windows)
        {
            if (!IsEligible(existing)) continue;
            return ActivateWindow(existing); // Never relaunch because Windows denied foreground focus.
        }
        return application.IsPinned && Launch(application);
    }

    public bool Launch(DockApplication application)
    {
        var launched = launcher.Launch(application);
        RequestRefresh();
        return launched;
    }

    public bool SetPinned(DockApplication application, bool pinned)
    {
        var saved = pinStore.Set(application, pinned);
        if (saved)
            Interlocked.Increment(ref pinRevision);
        RequestRefresh();
        return saved;
    }

    public bool PinExternalTarget(string path)
    {
        var saved = pinStore.AddExternalTarget(path);

        if (saved)
            Interlocked.Increment(ref pinRevision);

        RequestRefresh();
        return saved;
    }

    public bool ReorderApplications(IReadOnlyList<string> orderedIds)
    {
        var ids = Volatile.Read(ref currentApplications).Select(app => app.Id).ToArray();
        if (!pinStore.Reorder(ids, orderedIds))
            return false;

        Interlocked.Increment(ref pinRevision);
        RequestRefresh();
        return true;
    }

    public bool RunAsAdministrator(DockApplication application)
    {
        var launched = launcher.RunAsAdministrator(application);
        if (launched) RequestRefresh();
        return launched;
    }

    public bool OpenFileLocation(DockApplication application) => launcher.OpenFileLocation(application);

    internal static bool IsEligible(ApplicationWindow existing)
    {
        var window = (nint)existing.Handle;
        if (!ApplicationNative.IsWindow(window) || !ApplicationNative.IsWindowVisible(window)) return false;
        try
        {
            using var process = Process.GetProcessById(existing.ProcessId);
            if (process.StartTime.ToUniversalTime().Ticks != existing.ProcessStartTicks) return false;
            ApplicationNative.GetWindowThreadProcessId(window, out var owner);
            if (owner != existing.ProcessId && !Path.GetFileName(ProcessPath(owner)).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase)) return false;
            return ApplicationNative.DwmGetWindowAttribute(window, 14, out var cloaked, sizeof(int)) < 0 || cloaked == 0;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception) { return false; }
    }

    public bool ActivateWindow(ApplicationWindow existing)
    {
        if (!IsEligible(existing)) { RequestRefresh(); return false; }
        var window = (nint)existing.Handle;
        if (ApplicationNative.IsIconic(window)) ApplicationNative.ShowWindowAsync(window, 9);
        var focused = ApplicationNative.SetForegroundWindow(window);
        RequestRefresh();
        return focused;
    }

    public bool CloseWindow(ApplicationWindow existing)
    {
        if (!IsEligible(existing)) { RequestRefresh(); return false; }
        // The application handles WM_CLOSE, including any unsaved-document confirmation.
        var sent = NativeMethods.PostMessageW((nint)existing.Handle, 0x0010, 0, 0);
        RequestRefresh();
        return sent;
    }
    public void Dispose()
    {
        if (stopping) return;
        stopping = true;
        foreach (var hook in hooks) NativeMethods.UnhookWinEvent(hook);
        hooks.Clear();
        refresh.Set();
        if (worker is null) refresh.Dispose();
        // Do not block the UI/recovery heartbeat on a slow Shell extension.
        GC.KeepAlive(callback);
    }
}
