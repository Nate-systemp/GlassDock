using GlassDock.Core.Desktop;
using GlassDock.Core.Settings;
using GlassDock.Windows.Desktop;
using GlassDock.Windows.Settings;
using Microsoft.UI.Dispatching;

namespace GlassDock.App.Desktop;

/// <summary>
/// Owns the set of monitor-specific dock windows while keeping process-wide
/// discovery, badge, keyboard and taskbar services singular.
/// </summary>
internal sealed class MonitorDockCoordinator : IDisposable
{
    private readonly GlassDockSettingsSession settingsSession;
    private readonly GlassDockSettingsStore settingsStore;
    private readonly ApplicationShutdownState shutdown;
    private readonly Action shutdownCompleted;
    private readonly bool inspection;
    private readonly bool safeMode;
    private readonly Action<bool>? prepareRestart;
    private readonly DokySharedRuntime runtime = new();
    private readonly Dictionary<nint, DesktopOverlayWindow> secondaryDocks = [];
    private readonly DispatcherQueueTimer topologyTimer;
    private DockDisplayMode lastDisplayMode;
    private bool reconciling;
    private bool reconcilePending;
    private bool shuttingDown;
    private bool disposed;

    public DesktopOverlayWindow PrimaryWindow { get; }

    public MonitorDockCoordinator(
        GlassDockSettingsSession settingsSession,
        GlassDockSettingsStore settingsStore,
        ApplicationShutdownState shutdown,
        Action shutdownCompleted,
        bool inspection = false,
        bool safeMode = false,
        Action<bool>? prepareRestart = null)
    {
        this.settingsSession = settingsSession;
        this.settingsStore = settingsStore;
        this.shutdown = shutdown;
        this.shutdownCompleted = shutdownCompleted;
        this.inspection = inspection;
        this.safeMode = safeMode;
        this.prepareRestart = prepareRestart;
        lastDisplayMode = settingsSession.DisplayMode;

        nint initialMonitor = 0;
        if (settingsSession.DisplayMode == DockDisplayMode.AllDisplays)
        {
            try { initialMonitor = WindowsMonitorService.PrimaryMonitorHandle(); }
            catch { initialMonitor = 0; }
        }

        PrimaryWindow = CreateDock(
            initialMonitor,
            ownsGlobalServices: true,
            sharedKeyboard: null,
            showSettingsOverride: null,
            restoreTaskbarOverride: null,
            resumeTaskbarOverride: null);

        runtime.StartApplications();
        settingsSession.Changed += SettingsChanged;

        topologyTimer = PrimaryWindow.DispatcherQueue.CreateTimer();
        topologyTimer.Interval = TimeSpan.FromSeconds(1);
        topologyTimer.Tick += (_, _) =>
        {
            if (!shuttingDown && settingsSession.DisplayMode == DockDisplayMode.AllDisplays)
                _ = ReconcileMonitorsAsync();
        };
        topologyTimer.Start();

        if (settingsSession.DisplayMode == DockDisplayMode.AllDisplays)
            _ = ReconcileMonitorsAsync();
    }

    private DesktopOverlayWindow CreateDock(
        nint monitor,
        bool ownsGlobalServices,
        WindowsKeyboardService? sharedKeyboard,
        Action? showSettingsOverride,
        Action? restoreTaskbarOverride,
        Action? resumeTaskbarOverride) =>
        new(
            settingsSession,
            settingsStore,
            shutdown,
            shutdownCompleted,
            inspection,
            safeMode,
            prepareRestart,
            runtime.Applications,
            runtime.Badges,
            monitor,
            ownsGlobalServices,
            sharedKeyboard,
            RequestShutdown,
            UnexpectedWindowClosed,
            showSettingsOverride,
            restoreTaskbarOverride,
            resumeTaskbarOverride);

    private void SettingsChanged(object? sender, GlassDockSettingsChangedEventArgs e)
    {
        if (shuttingDown || disposed || e.Settings.DockDisplayMode == lastDisplayMode)
            return;

        lastDisplayMode = e.Settings.DockDisplayMode;
        _ = ReconcileMonitorsAsync();
    }

    private async Task ReconcileMonitorsAsync()
    {
        if (shuttingDown || disposed)
            return;
        if (reconciling)
        {
            reconcilePending = true;
            return;
        }

        reconciling = true;
        try
        {
            if (settingsSession.DisplayMode != DockDisplayMode.AllDisplays)
            {
                foreach (var monitor in secondaryDocks.Keys.ToArray())
                    await RemoveSecondaryAsync(monitor);

                PrimaryWindow.RetargetMonitor(0);
                return;
            }

            IReadOnlyList<DesktopMonitor> monitors;
            try
            {
                monitors = WindowsMonitorService.GetMonitors();
            }
            catch
            {
                // A display topology transition can temporarily fail while Windows is
                // rebuilding monitor handles. Keep the current docks and retry next tick.
                return;
            }

            if (monitors.Count == 0)
                return;

            var handles = monitors.Select(monitor => monitor.Handle).ToHashSet();
            var primaryTarget = PrimaryWindow.MonitorTarget;

            if (primaryTarget == 0 || !handles.Contains(primaryTarget))
            {
                var replacement = monitors.FirstOrDefault(monitor => monitor.IsPrimary);
                if (replacement.Handle == 0)
                    replacement = monitors[0];

                if (secondaryDocks.ContainsKey(replacement.Handle))
                    await RemoveSecondaryAsync(replacement.Handle);

                PrimaryWindow.RetargetMonitor(replacement.Handle);
                primaryTarget = replacement.Handle;
            }

            foreach (var stale in secondaryDocks.Keys
                .Where(handle => !handles.Contains(handle) || handle == primaryTarget)
                .ToArray())
            {
                await RemoveSecondaryAsync(stale);
            }

            foreach (var monitor in monitors)
            {
                if (monitor.Handle == primaryTarget || secondaryDocks.ContainsKey(monitor.Handle))
                    continue;

                secondaryDocks[monitor.Handle] = CreateDock(
                    monitor.Handle,
                    ownsGlobalServices: false,
                    sharedKeyboard: PrimaryWindow.KeyboardService,
                    showSettingsOverride: PrimaryWindow.ShowSettings,
                    restoreTaskbarOverride: PrimaryWindow.RestoreTaskbar,
                    resumeTaskbarOverride: PrimaryWindow.ResumeTaskbarSuppression);
            }
        }
        finally
        {
            reconciling = false;
            if (reconcilePending && !shuttingDown && !disposed)
            {
                reconcilePending = false;
                _ = ReconcileMonitorsAsync();
            }
        }
    }

    private async Task RemoveSecondaryAsync(nint monitor)
    {
        if (!secondaryDocks.Remove(monitor, out var window))
            return;

        await window.ShutdownForCoordinatorAsync();
    }

    private void UnexpectedWindowClosed(DesktopOverlayWindow window)
    {
        if (shuttingDown || disposed)
            return;

        if (ReferenceEquals(window, PrimaryWindow))
        {
            _ = ShutdownAfterUnexpectedPrimaryCloseAsync();
            return;
        }

        var entry = secondaryDocks.FirstOrDefault(pair => ReferenceEquals(pair.Value, window));
        if (entry.Value is null)
            return;

        secondaryDocks.Remove(entry.Key);
        _ = CleanupAndReconcileAsync(window);
    }

    private async Task CleanupAndReconcileAsync(DesktopOverlayWindow window)
    {
        await window.CleanupAfterUnexpectedCloseAsync();
        if (!shuttingDown && settingsSession.DisplayMode == DockDisplayMode.AllDisplays)
            await ReconcileMonitorsAsync();
    }

    private async Task ShutdownAfterUnexpectedPrimaryCloseAsync()
    {
        if (shuttingDown || !shutdown.TryBegin())
            return;

        shuttingDown = true;
        topologyTimer.Stop();
        settingsSession.Changed -= SettingsChanged;

        await PrimaryWindow.CleanupAfterUnexpectedCloseAsync();
        foreach (var window in secondaryDocks.Values.ToArray())
            await window.ShutdownForCoordinatorAsync();
        secondaryDocks.Clear();

        runtime.Dispose();
        shutdownCompleted();
    }

    public void RequestShutdown()
    {
        if (shuttingDown || disposed || !shutdown.TryBegin())
            return;

        shuttingDown = true;
        topologyTimer.Stop();
        settingsSession.Changed -= SettingsChanged;
        _ = ShutdownAsync();
    }

    private async Task ShutdownAsync()
    {
        foreach (var window in secondaryDocks.Values.ToArray())
            await window.ShutdownForCoordinatorAsync();
        secondaryDocks.Clear();

        await PrimaryWindow.ShutdownForCoordinatorAsync();
        runtime.Dispose();
        shutdownCompleted();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        topologyTimer.Stop();
        settingsSession.Changed -= SettingsChanged;
        if (!shuttingDown)
            runtime.Dispose();
    }
}
