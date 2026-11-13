using GlassDock.Core.Desktop;
using GlassDock.Core.Settings;
using GlassDock.Windows.Desktop;
using GlassDock.Windows.Settings;
using Microsoft.UI.Dispatching;

namespace GlassDock.App.Desktop;

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
    private readonly DispatcherQueueTimer screenshotTimer;
    private bool screenshotActive;
    private DockDisplayMode lastDisplayMode;
    private bool reconciling;
    private bool reconcilePending;
    private bool shuttingDown;
    private bool disposed;
    private UtilityHudWindow? volumeHud;
    private DesktopOverlayWindow? volumeOwner;
    private UtilityHudWindow? brightnessHud;
    private DesktopOverlayWindow? brightnessOwner;
    private string? selectedBrightnessDisplay, brightnessTarget;
    private readonly NativeVolumeOsdSuppression nativeVolumeOsd;
    private readonly NativeBrightnessOsdLease nativeBrightnessOsd = new();
    private readonly DispatcherQueueTimer? brightnessOsdTimer;
    private long lastBrightnessHudSignal;

    public DesktopOverlayWindow PrimaryWindow { get; }
    private bool QuickSettingsOpen => secondaryDocks.Values.Prepend(PrimaryWindow).Any(window => window.IsQuickSettingsOpen);

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

        nativeVolumeOsd = new(runtime.Volume);
        ConfigureVolumeSuppression();
        runtime.StartApplications();
        runtime.Volume.Changed += VolumeChanged;
        runtime.Volume.VolumeKeyHandled += VolumeKeyHandled;
        runtime.Volume.Start();
        runtime.Brightness.Changed += BrightnessChanged;
        runtime.Brightness.Start();
        if (nativeBrightnessOsd.Enabled)
        {
            brightnessOsdTimer = PrimaryWindow.DispatcherQueue.CreateTimer();
            brightnessOsdTimer.Interval = TimeSpan.FromMilliseconds(33);
            brightnessOsdTimer.Tick += (_, _) => PulseBrightnessSuppression();
            brightnessOsdTimer.Start();
        }
        settingsSession.Changed += SettingsChanged;
        PrimaryWindow.KeyboardService.SnippingRequested += SnippingRequested;
        PrimaryWindow.KeyboardService.SnippingCanceled += SnippingCanceled;
        PrimaryWindow.KeyboardService.ClipboardChanged += ClipboardChanged;
        screenshotTimer = PrimaryWindow.DispatcherQueue.CreateTimer();
        screenshotTimer.Interval = TimeSpan.FromSeconds(45);
        screenshotTimer.IsRepeating = false;
        screenshotTimer.Tick += (_, _) => EndScreenshotCapture();

        topologyTimer = PrimaryWindow.DispatcherQueue.CreateTimer();
        topologyTimer.Interval = TimeSpan.FromSeconds(1);
        topologyTimer.Tick += (_, _) =>
        {
            if (!shuttingDown && !disposed) UpdateBrightnessButtons();
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
        Action? resumeTaskbarOverride)
    {
        var window = new DesktopOverlayWindow(
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
            resumeTaskbarOverride,
            runtime.Wifi, runtime.Bluetooth, runtime.AirplaneMode, runtime.NightLight, runtime.EnergySaver, runtime.DoNotDisturb, runtime.Volume)
        {
            ShowHomeOverride = ownsGlobalServices ? null : () => PrimaryWindow.ShowHome(),
            ScreenshotRequested = () => SnippingRequested(this, EventArgs.Empty)
        };
        window.SoundRequested += (_, _) => ShowVolume(window, explicitOpen: true);
        window.ReadQuickSettingsBrightness = () => UtilityHudValue.Brightness(BrightnessFor(window),
            BrightnessFor(window) is { } display && runtime.Brightness.IsPending(display.Id));
        window.WriteQuickSettingsBrightness = level =>
        {
            if (BrightnessFor(window) is { Available: true } display) runtime.Brightness.SetBrightness(display.Id, level * 100);
        };
        window.QuickSettingsVisibilityChanged += (_, _) =>
        {
            if (!window.IsQuickSettingsOpen) return;
            volumeHud?.Hide(true); brightnessHud?.Hide(true);
        };
        window.BrightnessRequested += (_, _) => ShowBrightness(window, explicitOpen: true);
        window.BrightnessMonitorsRequested += (_, _) =>
        {
            brightnessHud?.Hide(true);
            window.ShowBrightnessMonitors(runtime.Brightness.Displays, selectedBrightnessDisplay, id =>
            {
                selectedBrightnessDisplay = id; UpdateBrightnessButtons(); ShowBrightness(window, explicitOpen: true);
            });
        };
        return window;
    }

    private void VolumeChanged(object? sender, MasterVolumeChangedEventArgs args) => PrimaryWindow.DispatcherQueue.TryEnqueue(() =>
    {
        if (shuttingDown || disposed) return;
        ConfigureVolumeSuppression();
        volumeHud?.Update(UtilityHudValue.Volume(runtime.Volume.State));
        if (!args.External || screenshotActive) return;
        ShowVolume(VolumeSource(), explicitOpen: false);
    });

    private DesktopOverlayWindow VolumeSource() => (volumeHud?.IsOpen == true ? volumeOwner : null) ??
        secondaryDocks.Values.Prepend(PrimaryWindow)
            .FirstOrDefault(window => window.SoundMonitor == WindowsMonitorService.ForegroundMonitorHandle()) ?? PrimaryWindow;

    private void VolumeKeyHandled(object? sender, EventArgs args) => PrimaryWindow.DispatcherQueue.TryEnqueue(() =>
    {
        if (!shuttingDown && !disposed) ShowVolume(VolumeSource(), explicitOpen: false);
    });

    private void ConfigureVolumeSuppression()
    {
        var enabled = settingsSession.Current.SuppressNativeVolumeOsd && !safeMode && !inspection;
        nativeVolumeOsd.SetEnabled(enabled);
        PrimaryWindow.KeyboardService.SetVolumeKeyHandler(enabled && runtime.Volume.State.Available
            ? runtime.Volume.TryHandleVolumeKey : null);
    }

    private bool ShowVolume(DesktopOverlayWindow source, bool explicitOpen)
    {
        if (shuttingDown || disposed || screenshotActive || QuickSettingsOpen || !runtime.Volume.State.Available) return false;
        if (!explicitOpen && brightnessHud?.IsInteracting == true) return false;
        brightnessHud?.Hide(true);
        volumeHud ??= new(UtilityKind.Volume, () => UtilityHudValue.Volume(runtime.Volume.State), runtime.Volume.SetVolume);
        volumeOwner = source;
        volumeHud.ApplyAppearance(settingsSession.Appearance, settingsSession.Current.DockAppearanceMode);
        volumeHud.Show(UtilityHudValue.Volume(runtime.Volume.State), () => source.SoundAnchor, explicitOpen);
        return volumeHud.IsOpen;
    }

    private BrightnessDisplay? BrightnessFor(DesktopOverlayWindow source) =>
        runtime.Brightness.Displays.FirstOrDefault(d => d.Id == selectedBrightnessDisplay) ?? runtime.Brightness.ForMonitor(source.SoundMonitor);

    private UtilityHudValue BrightnessValue() => UtilityHudValue.Brightness(runtime.Brightness.Displays.FirstOrDefault(d => d.Id == brightnessTarget),
        brightnessTarget is { } id && runtime.Brightness.IsPending(id));

    private void UpdateBrightnessButtons()
    {
        foreach (var dock in secondaryDocks.Values.Prepend(PrimaryWindow)) dock.SetBrightnessStatus(BrightnessFor(dock));
    }

    private void BrightnessChanged(object? sender, BrightnessChangedEventArgs args) => PrimaryWindow.DispatcherQueue.TryEnqueue(() =>
    {
        if (shuttingDown || disposed) return;
        if (selectedBrightnessDisplay is not null && !runtime.Brightness.Displays.Any(d => d.Id == selectedBrightnessDisplay)) selectedBrightnessDisplay = null;
        UpdateBrightnessButtons(); brightnessHud?.Update(BrightnessValue());
        if (screenshotActive || !args.External || args.Display is not { Available: true } changed) return;
        lastBrightnessHudSignal = Environment.TickCount64;
        var source = secondaryDocks.Values.Prepend(PrimaryWindow).FirstOrDefault(d => d.SoundMonitor == changed.Monitor) ?? PrimaryWindow;
        if (brightnessHud?.IsOpen == true && brightnessTarget == changed.Id && brightnessOwner is not null) source = brightnessOwner;
        ShowBrightness(source, explicitOpen: false, changed.Id);
    });

    private void PulseBrightnessSuppression()
    {
        var display = runtime.Brightness.Displays.FirstOrDefault(d => d.Available && d.Backend == "WMI");
        var ready = !disposed && !shuttingDown && !safeMode && !inspection && !screenshotActive &&
            display is not null && volumeHud?.IsInteracting != true;
        if (!nativeBrightnessOsd.Pulse(ready) || !ready || QuickSettingsOpen || display is null || Environment.TickCount64 - lastBrightnessHudSignal < 100) return;
        lastBrightnessHudSignal = Environment.TickCount64;
        var source = secondaryDocks.Values.Prepend(PrimaryWindow).FirstOrDefault(d => d.SoundMonitor == display.Monitor) ?? PrimaryWindow;
        ShowBrightness(source, explicitOpen: false, display.Id);
    }

    private bool ShowBrightness(DesktopOverlayWindow source, bool explicitOpen, string? changedDisplay = null)
    {
        if (shuttingDown || disposed || screenshotActive || QuickSettingsOpen) return false;
        var display = changedDisplay is null ? BrightnessFor(source) : runtime.Brightness.Displays.FirstOrDefault(d => d.Id == changedDisplay);
        if (display?.Available != true || !explicitOpen && volumeHud?.IsInteracting == true) return false;
        if (!explicitOpen && brightnessHud?.IsInteracting == true && brightnessTarget != display.Id) return false;
        volumeHud?.Hide(true);
        brightnessOwner = source; brightnessTarget = display.Id;
        brightnessHud ??= new(UtilityKind.Brightness, BrightnessValue, value =>
        {
            if (brightnessTarget is { } id) runtime.Brightness.SetBrightness(id, value * 100);
        });
        brightnessHud.ApplyAppearance(settingsSession.Appearance, settingsSession.Current.DockAppearanceMode);
        brightnessHud.Show(BrightnessValue(), () => brightnessOwner?.BrightnessAnchor, explicitOpen);
        return brightnessHud.IsOpen;
    }

    private void DisposeVolumeHud()
    {
        nativeVolumeOsd.Dispose();
        PrimaryWindow.KeyboardService.SetVolumeKeyHandler(null);
        volumeHud?.Dispose(); volumeHud = null; volumeOwner = null;
        brightnessHud?.Dispose(); brightnessHud = null; brightnessOwner = null;
    }

    private void SnippingRequested(object? sender, EventArgs e)
    {
        if (shuttingDown || disposed) return;
        if (screenshotActive) return;
        volumeHud?.BeginScreenshot();
        brightnessHud?.BeginScreenshot();
        PrimaryWindow.BeginSnippingCapture();
        foreach (var secondary in secondaryDocks.Values)
            secondary.BeginSnippingCapture();
        screenshotActive = true;
        screenshotTimer.Stop();
        screenshotTimer.Start();
    }

    private void SnippingCanceled(object? sender, EventArgs e) => EndScreenshotCapture();

    private void ClipboardChanged(object? sender, EventArgs e)
    {
        if (screenshotActive) EndScreenshotCapture();
    }

    private void EndScreenshotCapture()
    {
        screenshotTimer.Stop();
        if (!screenshotActive) return;
        try
        {
            PrimaryWindow.EndSnippingCapture();
            foreach (var secondary in secondaryDocks.Values) secondary.EndSnippingCapture();
            volumeHud?.EndScreenshot();
            brightnessHud?.EndScreenshot();
        }
        finally { screenshotActive = false; }
    }

    private void SettingsChanged(object? sender, GlassDockSettingsChangedEventArgs e)
    {
        if (!shuttingDown && !disposed) ConfigureVolumeSuppression();
        if (!shuttingDown && !disposed) volumeHud?.ApplyAppearance(settingsSession.Appearance, e.Settings.DockAppearanceMode);
        if (!shuttingDown && !disposed)
        {
            brightnessHud?.ApplyAppearance(settingsSession.Appearance, e.Settings.DockAppearanceMode);
            UpdateBrightnessButtons();
        }
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
        DisposeVolumeHud();
        EndScreenshotCapture();
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
        EndScreenshotCapture();
        topologyTimer.Stop();
        settingsSession.Changed -= SettingsChanged;
        _ = ShutdownAsync();
    }

    private async Task ShutdownAsync()
    {
        DisposeVolumeHud();
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
        brightnessOsdTimer?.Stop();
        nativeBrightnessOsd.Dispose();
        runtime.Volume.Changed -= VolumeChanged;
        runtime.Brightness.Changed -= BrightnessChanged;
        runtime.Volume.VolumeKeyHandled -= VolumeKeyHandled;
        DisposeVolumeHud();
        EndScreenshotCapture();
        screenshotTimer.Stop();
        PrimaryWindow.KeyboardService.SnippingRequested -= SnippingRequested;
        PrimaryWindow.KeyboardService.SnippingCanceled -= SnippingCanceled;
        PrimaryWindow.KeyboardService.ClipboardChanged -= ClipboardChanged;
        topologyTimer.Stop();
        settingsSession.Changed -= SettingsChanged;
        if (!shuttingDown)
            runtime.Dispose();
    }
}
