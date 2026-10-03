using System.Numerics;
using System.Collections.ObjectModel;
using GlassDock.App.ViewModels;
using GlassDock.Core.Applications;
using GlassDock.Windows.Applications;
using GlassDock.App.Controls;
using GlassDock.App.Rendering;
using GlassDock.Core.Desktop;
using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using GlassDock.Windows.Desktop;
using GlassDock.Windows.Settings;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using XamlPath = Microsoft.UI.Xaml.Shapes.Path;
using global::Windows.UI.ViewManagement;
using global::Windows.ApplicationModel.DataTransfer;

namespace GlassDock.App.Desktop;

public sealed partial class DesktopOverlayWindow : Window
{
    private readonly Grid root = new() { Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(1, 0, 0, 0)) };
    private readonly GlassSurface surface = new()
    {
        UseDesktopBackdrop = true, UsePlainSurface = true, Width = 120, Height = 5, Opacity = 0,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, 0, 16)
    };
    private readonly Border indicator = new()
    {
        Width = 120, Height = 5, CornerRadius = new CornerRadius(2.5),
        Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(220, 225, 225, 230)),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, 0, 16), IsHitTestVisible = false
    };
    private readonly StackPanel icons = new()
    {
        Orientation = Orientation.Horizontal, Spacing = 6, Height = 68, Opacity = 0,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, 0, 16), IsHitTestVisible = false
    };

    private readonly Border externalDropHighlight = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Bottom,
        CornerRadius = new CornerRadius(22),
        BorderThickness = new Thickness(1.5),
        BorderBrush = new SolidColorBrush(
            global::Windows.UI.Color.FromArgb(190, 225, 245, 255)),
        Background = new SolidColorBrush(
            global::Windows.UI.Color.FromArgb(34, 150, 210, 255)),
        Opacity = 0,
        IsHitTestVisible = false
    };

    // Geometry-only XAML paths. They remain available to build the native
    // hit-test polygon, but they never render. The visible dock edge is owned
    // exclusively by DesktopGlassBackdrop's compositor mask/inner-edge pass.
    // Keeping the old XAML rim visible on top of the compositor was the source
    // of the visibly stair-stepped/pixelated hover wave.
    private readonly XamlPath dockWaveGlow = new()
    {
        IsHitTestVisible = false,
        StrokeThickness = 0,
        Stroke = new SolidColorBrush(Colors.Transparent),
        Opacity = 0
    };

    private readonly XamlPath dockWaveRim = new()
    {
        IsHitTestVisible = false,
        StrokeThickness = 0,
        Stroke = new SolidColorBrush(Colors.Transparent),
        Opacity = 0
    };
    private readonly DockStateMachine state = new();
    private readonly WindowsApplicationService applicationService;
    private readonly BadgeCoordinator badges;
    private readonly bool ownsApplicationService;
    private readonly bool ownsBadges;
    private readonly bool ownsKeyboard;
    private readonly bool ownsGlobalServices;
    private readonly Action? requestApplicationShutdown;
    private readonly Action<DesktopOverlayWindow>? unexpectedClosed;
    private readonly Action? showSettingsOverride;
    private readonly Action? restoreTaskbarOverride;
    private readonly Action? resumeTaskbarOverride;
    private int notificationRefreshQueued;
    private readonly WindowsApplicationLauncher dropLauncher = new();
    private readonly DockApplicationsViewModel applications;
    private readonly WindowPreviewCoordinator previews;
    private readonly Dictionary<string, Button> applicationButtons = new(StringComparer.Ordinal);
    public ObservableCollection<DockApplicationItem> VisibleDockApplications => applications.VisibleDockApplications;
    private readonly DesktopGlassBackdrop desktopBackdrop = new()
    {
        UseInnerEdge = false,
        UseSolidSurface = true
    };
    private readonly WindowsOverlayManager windowManager;
    private readonly WindowsKeyboardService keyboard;
    private readonly DockAnimationController animation;
    private readonly GlassDockSettingsSession settingsSession;
    private readonly GlassDockSettingsStore settingsStore;
    private readonly ApplicationShutdownState shutdown;
    private readonly Action shutdownCompleted;
    private readonly bool safeMode;
    private readonly Action<bool>? prepareRestart;
    private bool taskbarSuppressionPaused;
    private readonly RetainedWindowSlot<SettingsWindow> settingsWindow = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer heartbeat;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer displayTimer;
    private long dockWaveLastFrame;
    private CancellationTokenSource? collapseDelay;
    private CancellationTokenSource? pinDockTransition;

    private CancellationTokenSource? pillHideDelay;
    private TaskbarDevelopmentSession? taskbarSession;
    private DevelopmentWindow? controls;
    private GlassHomeWindow? home;
    private Window? lab;
    private bool pointerInsideDock;
    private bool heldTransition;
    private bool closing;
    private bool startingTest;
    private bool desktopStartupQueued;
    private bool desktopStarted;
    private bool pinDockRequested;
    private bool notificationBadgesRequested;
    private bool pinnedPlacementActive;
    private bool pinDockTransitionActive;
    private Task? taskbarOperation;
    private Task? taskbarRestoreOperation;
    private Task? shutdownOperation;
    private int taskbarRevision;

    private bool dockWaveTimerRunning;
    private double dockWaveTargetX;
    private double dockWaveCurrentX;
    private double dockWaveTargetStrength;
    private double dockWaveCurrentStrength;

    // Pinned-app drag reorder state. Reordering is visual while the pointer is
    // down, then committed atomically to DockPinStore on release.
    private DockApplicationItem? reorderCandidate;
    private Button? reorderButton;
    private double reorderStartX;
    private double reorderDragStartRootX;
    private bool reorderDragging;
    private bool reorderCommitting;
    private int reorderSourceIndex = -1;
    private int reorderTargetIndex = -1;
    private List<Button> reorderPinnedButtons = [];
    private double[] reorderSlotCenters = [];
    private readonly Dictionary<string, DateTime> suppressClickUntil = new(StringComparer.Ordinal);

    private bool externalDragActive;
    private Button? externalDropTargetButton;

    // Compact system area shown at the right edge of the expanded dock.
    private readonly WindowsSystemControlService systemControls = new();
    private readonly StackPanel utilityCluster = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 4,
        VerticalAlignment = VerticalAlignment.Center,
        Tag = "NoMagnify"
    };
    private readonly TextBlock utilityClock = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer utilityTimer;
    private SystemQuickSettingsWindow? quickSettings;
    private SystemTrayWindow? trayWindow;
    private CalendarPopoverWindow? calendarWindow;
    private FrameworkElement? quickSettingsSource;
    private FrameworkElement? trayWindowSource;
    private FrameworkElement? calendarWindowSource;
    private Button? activeUtilityButton;
    private bool utilityTransitionPending;
    private bool quickSettingsOpen;
    private bool trayWindowOpen;
    private bool calendarWindowOpen;
    private bool SystemPopupOpen => utilityTransitionPending || quickSettingsOpen || trayWindowOpen || calendarWindowOpen || stackWindow?.IsOpen == true;
    private const double UtilityClusterWidth = 248;

    private const double DockWaveHalfWidth = 92;
    private const double DockWaveRise = 12;
    private const double ExpandedDockCornerRadius = 34;
    private const double PinnedDockClipDepth = ExpandedDockCornerRadius;
    private const int PinnedDockTransitionDurationMilliseconds = 280;
    private const double PillHideDurationMilliseconds = 210;

    public double BottomMargin { get; private set; }
    private DockAppearanceSettings Appearance => settingsSession.Appearance;
    private bool HoverWaveEnabled => !safeMode && settingsSession.Current.HoverWaveEnabled;
    private double ExpandedDockHeight => Math.Max(68, Appearance.ButtonHeight + 24);
    private double ExpandedContentTop => root.ActualHeight - icons.Margin.Bottom - ExpandedDockHeight;
    private bool DockPinLock => pinDockRequested || pinnedPlacementActive || pinDockTransitionActive;
    private const double PeekRestBottom = -2; // Three DIP remain visible above the physical screen edge.
    public string Status { get; private set; } = "Starting desktop recovery protection.";
    public string RenderingMode => desktopBackdrop.RenderingMode;
    public bool HotkeysAvailable => keyboard.IsRegistered;
    public bool IsShuttingDown => shutdown.IsRequested;
    public event EventHandler? StatusChanged;

    internal nint MonitorTarget => windowManager.FixedMonitor;
    internal WindowsKeyboardService KeyboardService => keyboard;
    internal bool OwnsGlobalServices => ownsGlobalServices;

    internal void RetargetMonitor(nint monitor)
    {
        if (closing) return;
        windowManager.SetFixedMonitor(monitor);
        windowManager.Position(0, settingsSession.DisplayMode);
        applications.RefreshFilter();
        UpdateBackdropBounds();
        if (state.State is DockState.Idle or DockState.Hovering)
            UpdatePeekInput();
        else
            UpdateDockWaveOutline();
        pointerInsideDock = windowManager.IsPointerInsideInput();
        previews.Reposition();
        RepositionSystemPopups();
    }

    public DesktopOverlayWindow(
        GlassDockSettingsSession settingsSession,
        GlassDockSettingsStore settingsStore,
        ApplicationShutdownState shutdown,
        Action shutdownCompleted,
        bool inspection = false,
        bool safeMode = false,
        Action<bool>? prepareRestart = null,
        WindowsApplicationService? sharedApplicationService = null,
        BadgeCoordinator? sharedBadges = null,
        nint monitorTarget = 0,
        bool ownsGlobalServices = true,
        WindowsKeyboardService? sharedKeyboard = null,
        Action? requestApplicationShutdown = null,
        Action<DesktopOverlayWindow>? unexpectedClosed = null,
        Action? showSettingsOverride = null,
        Action? restoreTaskbarOverride = null,
        Action? resumeTaskbarOverride = null)
    {
        this.settingsSession = settingsSession;
        this.settingsStore = settingsStore;
        this.shutdown = shutdown;
        this.shutdownCompleted = shutdownCompleted;
        this.safeMode = safeMode;
        this.prepareRestart = prepareRestart;
        this.ownsGlobalServices = ownsGlobalServices;
        this.requestApplicationShutdown = requestApplicationShutdown;
        this.unexpectedClosed = unexpectedClosed;
        this.showSettingsOverride = showSettingsOverride;
        this.restoreTaskbarOverride = restoreTaskbarOverride;
        this.resumeTaskbarOverride = resumeTaskbarOverride;
        ownsApplicationService = sharedApplicationService is null;
        applicationService = sharedApplicationService ?? new WindowsApplicationService();
        ownsBadges = sharedBadges is null;
        badges = sharedBadges ?? new BadgeCoordinator([new WindowsToastBadgeProvider()]);
        BottomMargin = settingsSession.DockBehavior.BottomMargin;
        pinDockRequested = settingsSession.Current.PinDock;
        notificationBadgesRequested = settingsSession.Current.NotificationBadgesEnabled;
        settingsSession.Changed += SettingsChanged;

        Title = "Doky — Floating Dock";
        WindowBranding.Apply(this);
        // The settings load in App.OnLaunched is asynchronous, so the UI dispatcher can
        // already be pumping while this window is constructed. Subscribe before any
        // native call can show the HWND; the queued callback runs after construction.
        root.Loaded += (_, _) => QueueDesktopStartup();
        Content = root;
        surface.Margin = indicator.Margin = icons.Margin = new Thickness(0, 0, 0, BottomMargin);
        SystemBackdrop = desktopBackdrop;
        AppWindow.IsShownInSwitchers = inspection;
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        windowManager = new WindowsOverlayManager(hwnd, monitorTarget);
        windowManager.Configure(inspection);
        root.Children.Add(surface);
        // Keep the wave path for native hit-test sampling; its XAML stroke is
        // no longer rendered over the compositor's independently sampled edge.
        root.Children.Add(indicator);
        root.Children.Add(icons);
        root.Children.Add(externalDropHighlight);

        root.AllowDrop = true;
        root.DragEnter += ExternalDragEnter;
        root.DragOver += ExternalDragOver;
        root.DragLeave += ExternalDragLeave;
        root.Drop += ExternalDrop;

        surface.RegisterPropertyChangedCallback(UIElement.OpacityProperty, (_, _) => UpdateBackdropBounds());
        surface.SizeChanged += (_, _) =>
        {
            UpdateBackdropBounds();
            UpdateDockWaveOutline();
            UpdateExternalDropHighlight();
        };

        root.SizeChanged += (_, _) =>
        {
            UpdateBackdropBounds();
            UpdateDockWaveOutline();
            UpdateExternalDropHighlight();
            RepositionSystemPopups();
        };
        desktopBackdrop.RenderingModeChanged += (_, _) => { UpdateBackdropBounds(); StatusChanged?.Invoke(this, EventArgs.Empty); };
        applications = new DockApplicationsViewModel(
            applicationService,
            DispatcherQueue,
            ownsService: ownsApplicationService,
            snapshotTransform: snapshot => WindowsMonitorService.FilterSnapshotForMonitor(snapshot, windowManager.FixedMonitor));
        badges.Changed += BadgesChanged;
        if (ownsGlobalServices)
            badges.RefreshRequested += BadgesRefreshRequested;
        applications.SnapshotApplied += (_, _) => QueueNotificationBadgeRefresh();
        previews = new(applications, root, hwnd, () => ExpandedContentTop, settingsSession,
            () => !closing && state.State == DockState.Expanded && reorderButton is null &&
                !externalDragActive && !SystemPopupOpen,
            window => keyboard?.RestoreElevatedWindowAsync(window) ?? Task.FromResult(false));
        previews.HoldChanged += (_, _) => OnInteractionHoldChanged();
        previews.ActionFailed += (_, message) => SetStatus(message);
        AppWindow.Changed += (_, _) =>
        {
            // Visual Studio/debugger activation and some shell z-order changes can
            // disturb a freshly shown overlay. Reassert the documented dock contract.
            if (!presenter.IsAlwaysOnTop) presenter.IsAlwaysOnTop = true;
            previews.Reposition();
            RepositionSystemPopups();
        };
        applications.VisibleDockApplications.CollectionChanged += (_, _) => SynchronizeItems();
        applications.WarningChanged += (_, _) => { if (applications.Warning is { } warning) SetStatus(warning); };
        animation = new DockAnimationController(surface, icons, indicator);

        BuildUtilityCluster();
        utilityTimer = DispatcherQueue.CreateTimer();
        utilityTimer.Interval = TimeSpan.FromSeconds(1);
        utilityTimer.Tick += (_, _) => RefreshUtilityStatus();

        ApplyAppearance(Appearance);
        animation.PlacementChanged += (_, _) =>
        {
            UpdateBackdropBounds();
            if (state.State is DockState.Idle or DockState.Hovering) UpdatePeekInput();
            else UpdateDockWaveOutline();
            if (!animation.IsPlacementAnimating && state.State == DockState.Hovering &&
                !previews.HoldsDock && !windowManager.IsPointerInsideInput())
                SchedulePillHide();
        };
        windowManager.PointerMovedOutsideInput += (_, _) => PointerDeparted();
        windowManager.PointerMovedInsideInput += (_, _) =>
        {
            pointerInsideDock = true;
            collapseDelay?.Cancel();
            CancelPillHide();
            if (previews.ContextMenuOpen) return;
            if (state.State == DockState.Idle) RaisePeek();
            if (state.State == DockState.Expanded && reorderButton is null)
                RefreshHoverVisuals();
        };
        displayTimer = DispatcherQueue.CreateTimer();
        displayTimer.Interval = TimeSpan.FromMilliseconds(200);
        displayTimer.Tick += (_, _) =>
        {
            // Monitor tracking must keep running even while the dock is expanded.
            // Otherwise a long auto-hide delay makes Follow pointer / Follow active
            // window appear broken until the dock collapses.
            if (closing || previews.HoldsDock)
                return;

            if (!windowManager.RepositionIfMonitorChanged(0, settingsSession.DisplayMode))
                return;

            UpdateBackdropBounds();

            if (state.State is DockState.Idle or DockState.Hovering)
                UpdatePeekInput();
            else
                UpdateDockWaveOutline();

            pointerInsideDock = windowManager.IsPointerInsideInput();
            previews.Reposition();
        };



        if (sharedKeyboard is null)
        {
            ownsKeyboard = true;
            keyboard = new WindowsKeyboardService(
                hwnd,
                enableDockShortcuts: !safeMode,
                elevatedHelperPath: safeMode
                    ? null
                    : Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        "Doky",
                        "InputHelper",
                        "GlassDock.InputHelper.exe"));

            keyboard.BareWindowsRequested += async (_, _) => await ToggleDockAsync();
            keyboard.HomeRequested += async (_, _) =>
            {
                // Ctrl+Alt+Space retains the explicit dock toggle.
                if (home is { IsVisible: true })
                    home.HideHome();

                await ToggleDockAsync();
            };
            keyboard.LauncherRequested += (_, _) => ShowHome();
            keyboard.RecoveryRequested += (_, _) => RestoreTaskbar();
        }
        else
        {
            ownsKeyboard = false;
            keyboard = sharedKeyboard;
        }

        root.PointerEntered += Entered;
        root.PointerMoved += Moved;
        root.PointerExited += Exited;
        root.Tapped += async (_, e) =>
        {
            if (state.State is not (DockState.Idle or DockState.Hovering)) return;
            e.Handled = true;
            await ExpandDockAsync();
        };
        surface.RenderingModeChanged += (_, _) => StatusChanged?.Invoke(this, EventArgs.Empty);
        var menu = new MenuFlyout();
        MenuItem(menu, "Dock Settings", ShowSettings);
        MenuItem(menu, "Enable notification badges", async () => await EnableNotificationBadgesAsync());
        MenuItem(menu, "Open Glass Home", ShowHome);
        menu.Items.Add(new MenuFlyoutSeparator());
        MenuItem(menu, "Restore Windows taskbar", RestoreTaskbar);
        MenuItem(menu, "Glass Material Laboratory", ShowLab);
        menu.Items.Add(new MenuFlyoutSeparator());
        MenuItem(menu, "Exit Doky", RequestShutdown);
        menu.Opening += (_, _) =>
        {
            previews.BeginContextMenu(menu);
        };
        menu.Closed += (_, _) => previews.EndContextMenu(menu);
        root.ContextFlyout = menu;
        heartbeat = DispatcherQueue.CreateTimer();
        heartbeat.Interval = TimeSpan.FromSeconds(1);
        heartbeat.Tick += async (_, _) =>
        {
            if (closing) return;
            if (taskbarSession is { IsActive: true } session) await session.HeartbeatAsync();
        };
        Closed += OnClosed;
        windowManager.Position(0, settingsSession.DisplayMode);
        surface.Margin = indicator.Margin = new Thickness(0, 0, 0, PeekRestBottom);
        indicator.Opacity = 1;
        windowManager.SetPeekInteraction(PeekRestBottom);
        // Also queue explicitly so desktop protection does not depend on Loaded timing.
        QueueDesktopStartup();
    }

    private async Task EnableNotificationBadgesAsync()
    {
        if (closing || shutdown.IsRequested) return;

        try
        {
            var edited = settingsSession.CreateNotificationBadgesUpdate(true);
            await settingsStore.SaveAsync(edited, shutdown.CancellationToken);
            if (closing || shutdown.IsRequested) return;

            await badges.StartAsync(requestPermission: true);
            settingsSession.Replace(edited);
            if (!closing) SetStatus(badges.Status);
        }
        catch (OperationCanceledException) when (shutdown.IsRequested)
        {
        }
        catch (Exception error)
        {
            if (!closing) SetStatus($"Notification badges could not be enabled: {error.Message}");
        }
    }

    private void QueueDesktopStartup()
    {
        if (desktopStartupQueued || desktopStarted || closing) return;
        desktopStartupQueued = true;
        if (!DispatcherQueue.TryEnqueue(async () => await StartDesktopAsync()))
            desktopStartupQueued = false;
    }

    private async Task StartDesktopAsync()
    {
        desktopStartupQueued = false;
        if (desktopStarted || closing) return;
        desktopStarted = true;
        ((OverlappedPresenter)AppWindow.Presenter).IsAlwaysOnTop = true;
        windowManager.Position(0, settingsSession.DisplayMode);
        displayTimer.Start();
        state.Show();
        animation.SetBottom(PeekRestBottom);
        indicator.Opacity = 1;
        ApplyMaterial(false);
        if (ownsApplicationService)
            applicationService.Start();
        if (ownsGlobalServices)
        {
            if (notificationBadgesRequested)
                _ = badges.StartAsync();
            else
                badges.Stop();
        }
        RefreshUtilityStatus();
        utilityTimer.Start();

        if (pinDockRequested)
            await ApplyPinDockModeAsync(pin: true, animate: true);

        if (ownsGlobalServices)
        {
            if (safeMode)
            {
                taskbarSuppressionPaused = true;
                TaskbarRecovery.RestoreNow();
                SetStatus("Safe Mode · Windows taskbar is restored · Win-key interception and Hover Wave are disabled.");
            }
            else if (settingsSession.Current.SuppressWindowsTaskbar)
            {
                await StartTaskbarTestAsync(whileAppActive: true);
            }
            else
            {
                taskbarSuppressionPaused = true;
                TaskbarRecovery.RestoreNow();
                SetStatus("Windows taskbar suppression is disabled.");
            }
        }
        else
        {
            SetStatus("Doky · per-monitor dock");
        }
    }

    private static void MenuItem(MenuFlyout menu, string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private void SynchronizeItems()
    {
        // Keep the right-click anchor and neighboring icon positions stable.
        // The coordinator dismisses menus whose app/window membership changed;
        // the latest collection is applied when the final menu hold releases.
        if (previews.ContextMenuOpen || reorderDragging || reorderCommitting || externalDragActive) return;
        if (stackWindow?.IsOpen == true)
        {
            var openStack = VisibleDockApplications.FirstOrDefault(app => app.Id == stackWindow.StackId);
            if (openStack?.Application.Stack is not null) { stackWindow.Update(openStack.Application); RepositionStackPopup(); } else stackWindow.Hide();
        }
        var present = VisibleDockApplications.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in applicationButtons.Keys.Where(id => !present.Contains(id)).ToArray())
        {
            previews.Detach(id);
            icons.Children.Remove(applicationButtons[id]);
            applicationButtons.Remove(id);
        }
        for (var index = 0; index < VisibleDockApplications.Count; index++)
        {
            var item = VisibleDockApplications[index];
            if (!applicationButtons.TryGetValue(item.Id, out var button))
            {
                button = CreateApplicationButton(item);
                applicationButtons.Add(item.Id, button);
            }
            var previous = icons.Children.IndexOf(button);
            if (previous == index) continue;
            if (previous >= 0) icons.Children.RemoveAt(previous);
            icons.Children.Insert(index, button);
        }
        if (state.State == DockState.Expanded)
        {
            var targetWidth = CalculateTargetDockWidth();
            surface.Width = targetWidth;
            UpdateBackdropBounds();
        }
    }
    private double CalculateTargetDockWidth()
    {
        var count = VisibleDockApplications.Count;

        // Do not use DockAppearanceSettings.TargetDockWidth here: that helper
        // intentionally caps the app-only dock at 560 DIP. Once the fixed
        // system/clock cluster was added, that legacy cap made the icon StackPanel
        // wider than the glass surface, producing the visible overflow near the
        // right edge. Compute the real content width from the configured button
        // geometry instead.
        var applicationWidth = count <= 0
            ? 100d
            : count * Appearance.ButtonWidth +
              Math.Max(0, count - 1) * Appearance.IconSpacing +
              36d; // breathing room around the app section

        // The overlay host is now monitor-aware and can grow well beyond the old
        // 960-DIP ceiling. Keep 16 DIP of invisible host margin on each side.
        var hostWidth = root.ActualWidth > 0
            ? root.ActualWidth
            : windowManager.CurrentHostWidthDips;
        var maximumWidth = Math.Max(120d, hostWidth - 32d);

        return Math.Min(maximumWidth, applicationWidth + UtilityClusterWidth);
    }

    private void BadgesChanged(object? sender, EventArgs e) => QueueNotificationBadgeRefresh();

    private void BadgesRefreshRequested(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref notificationRefreshQueued, 1) != 0) return;
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            Interlocked.Exchange(ref notificationRefreshQueued, 0);
            if (!closing) await badges.RefreshAsync();
        })) Interlocked.Exchange(ref notificationRefreshQueued, 0);
    }

    private void QueueNotificationBadgeRefresh()
    {
        if (closing) return;
        if (DispatcherQueue.HasThreadAccess)
            RefreshNotificationBadges();
        else
            DispatcherQueue.TryEnqueue(RefreshNotificationBadges);
    }

    private void RefreshNotificationBadges()
    {
        stackWindow?.RefreshBadges();
        if (closing) return;
        foreach (var item in applications.VisibleDockApplications)
        {
            if (applicationButtons.TryGetValue(item.Id, out var button) && button.Content is Grid content &&
                content.Children.FirstOrDefault() is AdaptiveAppIcon icon)
            {
                var badge = item.Application.Stack is null ? badges.ForApplication(item.Application.Identity) : StackBadge(item.Application);
                icon.SetNotificationBadge(badge);
                AutomationProperties.SetHelpText(button, badge.AccessibilityText);
            }
        }
    }

    private Button CreateApplicationButton(DockApplicationItem item)
    {
    var image = new AdaptiveAppIcon(Appearance.IconSize, Appearance.MagnificationScale, showTile: false);

    var running = new Border
    {
        Width = 4,
        Height = 3,
        CornerRadius = new CornerRadius(1.5),
        Background = DockForegroundBrush(),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, 0, 1)
    };

    var content = new Grid();

    content.Children.Add(image);
    content.Children.Add(running);

    var button = new Button
    {
        Padding = new Thickness(0),
        Margin = new Thickness(0),
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        CornerRadius = new CornerRadius(10),
        Background = new SolidColorBrush(Colors.Transparent),
        BorderBrush = new SolidColorBrush(Colors.Transparent),
        BorderThickness = new Thickness(0),
        Content = content
    };
        ApplyIconLayout(button, content, image, Appearance);
        button.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(Colors.Transparent);
        button.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(Colors.Transparent);
        button.Resources["ButtonBorderBrushPointerOver"] = new SolidColorBrush(Colors.Transparent);
        button.Resources["ButtonBorderBrushPressed"] = new SolidColorBrush(Colors.Transparent);

        ApplicationIcon? renderedIcon = null;
        void Update()
        {
            var badge = item.Application.Stack is null ? badges.ForApplication(item.Application.Identity) : StackBadge(item.Application);
            image.SetNotificationBadge(badge);
            AutomationProperties.SetName(button, item.Name);
            AutomationProperties.SetHelpText(button, badge.AccessibilityText);
            AutomationProperties.SetItemStatus(button, item.IsActive ? "Active" : item.IsRunning ? "Running" : "Pinned");
            ToolTipService.SetToolTip(button, WindowPreviewCoordinator.CreateTooltip(item.Name));
            running.Visibility = item.IsRunning ? Visibility.Visible : Visibility.Collapsed;
            running.Opacity = item.IsActive ? 1 : 0.55;
            running.Width = item.IsActive ? 10 : 4;
            if (item.Application.Stack is not null) image.SetStack(item.Application.StackApps);
            if (ReferenceEquals(renderedIcon, item.Application.Icon)) return;
            renderedIcon = item.Application.Icon;
            if (item.Application.Stack is null) image.SetIcon(renderedIcon);
        }
        Update();
        item.PropertyChanged += (_, _) => Update();

        button.AllowDrop = true;
        button.DragEnter += (_, e) => ApplicationDragEnter(button, item, e);
        button.DragOver += (_, e) => ApplicationDragOver(button, item, e);
        button.DragLeave += (_, e) => ApplicationDragLeave(button, e);
        button.Drop += async (_, e) => await ApplicationDropAsync(button, item, e);

        // ButtonBase handles pointer events internally for Click and can mark them
        // handled before ordinary += handlers see them. Register with
        // handledEventsToo=true so drag-reorder still receives the pointer stream.
        button.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler((_, e) => BeginReorderCandidate(button, item, e)),
            true);

        button.AddHandler(
            UIElement.PointerMovedEvent,
            new PointerEventHandler((_, e) => UpdateReorder(button, item, e)),
            true);

        button.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler((_, e) => FinishReorder(button, item, e)),
            true);

        button.AddHandler(
            UIElement.PointerCaptureLostEvent,
            new PointerEventHandler((_, _) =>
            {
                // ButtonBase may release capture as part of its own pointer-up
                // handling before our PointerReleased handler gets to commit.
                // Defer cancellation one dispatcher turn; a successful drop
                // clears reorderButton first, making this callback a no-op.
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (ReferenceEquals(reorderButton, button))
                        CancelReorder();
                });
            }),
            true);

        button.Click += (_, _) =>
        {
            // ButtonBase can raise Click before our handledEventsToo PointerReleased
            // callback commits the drag. Never activate an app while this button is
            // currently participating in a reorder gesture.
            if ((ReferenceEquals(reorderButton, button) && reorderDragging) ||
                (suppressClickUntil.Remove(item.Id, out var until) &&
                 DateTime.UtcNow <= until))
            {
                return;
            }

            if (item.Application.Stack is not null) { ToggleStack(item); return; }
            if (!applications.Activate(item))
                SetStatus($"Windows could not launch or focus {item.Name}.");
        };

        if (item.Application.Stack is null) previews.Attach(button, item);
        else button.RightTapped += (_, e) => { StackContext(button, item); e.Handled = true; };
        return button;
    }

    private void BuildUtilityCluster()
    {
        var divider = new Border
        {
            Width = 1,
            Height = 30,
            Margin = new Thickness(8, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(
                global::Windows.UI.Color.FromArgb(58, 235, 245, 255)),
            Tag = "NoMagnify"
        };

        // Fluent glyphs have different optical bounds, so use a shared button
        // box with small per-glyph size adjustments for a visually even row.
        var trayButton = CreateUtilityButton("\uE70E", "Hidden tray", ToggleSystemTray, 14.5);
        var networkButton = CreateUtilityButton("\uE701", "Network", ToggleQuickSettings, 16);
        var volumeButton = CreateUtilityButton("\uE767", "Volume", ToggleQuickSettings, 16);
        var batteryButton = CreateUtilityButton("\uE83F", "Battery", ToggleQuickSettings, 15);

        utilityClock.FontSize = 12.5;
        utilityClock.Foreground = new SolidColorBrush(Colors.White);
        utilityClock.VerticalAlignment = VerticalAlignment.Center;
        utilityClock.HorizontalAlignment = HorizontalAlignment.Center;
        utilityClock.TextAlignment = TextAlignment.Center;
        utilityClock.MinWidth = 62;
        utilityClock.Tag = "NoMagnify";

        var clockButton = new Button
        {
            Padding = new Thickness(4, 0, 4, 0),
            Margin = new Thickness(0),
            MinWidth = 68,
            Width = 68,
            Height = 38,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(9),
            Content = utilityClock,
            Tag = "NoMagnify",
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        SetUtilityButtonSelected(clockButton, selected: false);
        ToolTipService.SetToolTip(clockButton, "Clock and calendar");
        AutomationProperties.SetName(clockButton, "Clock and calendar");
        clockButton.Click += (_, _) => { utilitySource = clockButton; ToggleCalendar(); };

        utilityCluster.Children.Add(divider);
        utilityCluster.Children.Add(trayButton);
        utilityCluster.Children.Add(networkButton);
        utilityCluster.Children.Add(volumeButton);
        utilityCluster.Children.Add(batteryButton);
        utilityCluster.Children.Add(clockButton);

        // SynchronizeItems inserts application buttons by index, which naturally
        // keeps this utility cluster after all pinned/running applications.
        icons.Children.Add(utilityCluster);
    }

    private Button CreateUtilityButton(
        string glyph,
        string tooltip,
        Action click,
        double glyphSize)
    {
        // SystemControlStyle.Icon returns FontIcon, so size it directly.
        var icon = SystemControlStyle.Icon(glyph);
        icon.FontSize = glyphSize;
        icon.Width = 20;
        icon.Height = 20;
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;

        Button? source = null;
        var button = SystemControlStyle.Button(icon, tooltip, () => { utilitySource = source; click(); });
        source = button;
        button.Width = 34;
        button.MinWidth = 34;
        button.Height = 38;
        button.Padding = new Thickness(0);
        button.Margin = new Thickness(0);
        button.CornerRadius = new CornerRadius(DockControlPalette.ButtonRadius);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.Tag = "NoMagnify";
        SetUtilityButtonSelected(button, selected: false);
        return button;
    }

    private SolidColorBrush DockForegroundBrush() => new(
        DockControlPalette.Foreground(settingsSession.Current.DockAppearanceMode));

    private void SetUtilityButtonSelected(Button button, bool selected)
    {
        var mode = settingsSession.Current.DockAppearanceMode;
        var normal = new SolidColorBrush(DockControlPalette.Normal(mode, selected));
        var hover = new SolidColorBrush(DockControlPalette.Hover(mode, selected));
        var pressed = new SolidColorBrush(DockControlPalette.Pressed(mode, selected));

        button.Background = normal;
        button.Resources["ButtonBackground"] = normal;
        button.Resources["ButtonBackgroundPointerOver"] = hover;
        button.Resources["ButtonBackgroundPressed"] = pressed;
    }

    private void SetActiveUtilitySource(FrameworkElement? source)
    {
        var next = source as Button;
        if (ReferenceEquals(activeUtilityButton, next))
            return;

        if (activeUtilityButton is not null)
            SetUtilityButtonSelected(activeUtilityButton, selected: false);

        activeUtilityButton = next;

        if (activeUtilityButton is not null)
            SetUtilityButtonSelected(activeUtilityButton, selected: true);
    }

    private void RefreshUtilityStatus()
    {
        if (closing)
            return;

        var snapshot = systemControls.GetSnapshot();
        utilityClock.Text = DateTime.Now.ToString("t");
        ToolTipService.SetToolTip(utilityCluster.Children[2], snapshot.NetworkAvailable ? snapshot.NetworkName : "Offline");
        ToolTipService.SetToolTip(utilityCluster.Children[3], snapshot.Muted ? "Muted" : $"Volume {snapshot.VolumePercent}%");
        ToolTipService.SetToolTip(utilityCluster.Children[4], snapshot.HasBattery ? $"Battery {snapshot.BatteryPercent}%{(snapshot.PluggedIn ? " · Charging" : "")}" : "AC power");




    }

    private void PrepareSystemPopup()
    {
        collapseDelay?.Cancel();
        CancelPillHide();
        previews.Hide();
        animation.ResetMagnification();
        dockWaveCurrentStrength = dockWaveTargetStrength = 0;
        desktopBackdrop.ClearDockWave();
        UpdateDockWaveOutline();
        StopDockWaveTimer(clear: false);
    }

    private FrameworkElement? utilitySource;

    private global::Windows.Foundation.Point UtilityAnchor(FrameworkElement? source = null)
    {
        var anchorSource = source ?? utilitySource ?? utilityCluster;
        return anchorSource.TransformToVisual(root).TransformPoint(
            new global::Windows.Foundation.Point(
                anchorSource.ActualWidth / 2,
                anchorSource.ActualHeight / 2));
    }

    private void RepositionSystemPopups()
    {
        RepositionStackPopup();
        if (root.ActualWidth <= 0 || root.ActualHeight <= 0 ||
            (quickSettings is null && trayWindow is null && calendarWindow is null))
        {
            return;
        }

        var top = ExpandedContentTop;

        if (quickSettings is not null)
        {
            var anchor = UtilityAnchor(quickSettingsSource);
            quickSettings.PositionNear(AppWindow, windowManager.Scale, anchor.X, anchor.Y, top);
        }

        if (trayWindow is not null)
        {
            var anchor = UtilityAnchor(trayWindowSource);
            trayWindow.PositionNear(AppWindow, windowManager.Scale, anchor.X, anchor.Y, top);
        }

        if (calendarWindow is not null)
        {
            var anchor = UtilityAnchor(calendarWindowSource);
            calendarWindow.PositionNear(AppWindow, windowManager.Scale, anchor.X, anchor.Y, top);
        }
    }

    private void SystemPopupClosed()
    {
        if (closing)
            return;

        // A popup-to-popup switch deliberately finishes the outgoing collapse before
        // creating the next window. Keep the dock held during that short transition.
        if (utilityTransitionPending || SystemPopupOpen)
            return;

        SetActiveUtilitySource(null);
        pointerInsideDock = windowManager.IsPointerInsideInput();
        RefreshHoverVisuals();

        if (!pointerInsideDock &&
            !previews.HoldsDock &&
            state.State is DockState.Expanded or DockState.Expanding)
        {
            ScheduleCollapse();
        }
    }

    private void OpenQuickSettings(FrameworkElement? source)
    {
        if (closing) return;
        PrepareSystemPopup();
        quickSettingsSource = source;
        SetActiveUtilitySource(quickSettingsSource);
        var created = cachedSystemQuickSettingsWindow is null;
        try
        {
            quickSettings = cachedSystemQuickSettingsWindow ??= new SystemQuickSettingsWindow(systemControls, Appearance, settingsSession.Current.DockAppearanceMode, UtilityOwnsPointer);
        }
        catch (Exception error)
        {
            quickSettingsSource = null;
            SetActiveUtilitySource(null);
            SetStatus($"Quick Settings could not open: {error.Message}");
            SystemPopupClosed();
            return;
        }

        if (created) quickSettings.Hidden += (_, _) =>
        {
            quickSettingsOpen = false;
            quickSettings = null;
            quickSettingsSource = null;
            UtilityClosed();
        };

        if (created) quickSettings.Dismissed += (_, _) => utilityRequests.ClosingExternally();
        quickSettingsOpen = true;
        var anchor = UtilityAnchor(quickSettingsSource);
        quickSettings.PositionNear(AppWindow, windowManager.Scale, anchor.X, anchor.Y,
            ExpandedContentTop);
        quickSettings.AppWindow.Show();
        quickSettings.Activate();
        quickSettings.Present();

    }

    private void CloseQuickSettings(bool animate = false)
    {
        if (quickSettings is null)
        {
            quickSettingsOpen = false;
            return;
        }

        var window = quickSettings;
        if (animate)
        {
            window.Dismiss();
            return;
        }

        quickSettings = null;
        quickSettingsOpen = false;
        try { window.HideImmediately(); } catch (InvalidOperationException) { }
    }

    private void OpenSystemTray(FrameworkElement? source)
    {
        if (closing) return;
        PrepareSystemPopup();
        trayWindowSource = source;
        SetActiveUtilitySource(trayWindowSource);
        var created = cachedSystemTrayWindow is null;
        try
        {
            trayWindow = cachedSystemTrayWindow ??= new SystemTrayWindow(systemControls, applicationService, Appearance, settingsSession.Current.DockAppearanceMode, UtilityOwnsPointer);
        }
        catch (Exception error)
        {
            trayWindowSource = null;
            SetActiveUtilitySource(null);
            SetStatus($"Hidden tray could not open: {error.Message}");
            SystemPopupClosed();
            return;
        }

        if (created) trayWindow.Hidden += (_, _) =>
        {
            trayWindowOpen = false;
            trayWindow = null;
            trayWindowSource = null;
            UtilityClosed();
        };

        if (created) trayWindow.Dismissed += (_, _) => utilityRequests.ClosingExternally();
        trayWindowOpen = true;
        var anchor = UtilityAnchor(trayWindowSource);
        trayWindow.PositionNear(AppWindow, windowManager.Scale, anchor.X, anchor.Y,
            ExpandedContentTop);
        trayWindow.AppWindow.Show();
        trayWindow.Activate();
        trayWindow.Present();

    }

    private void CloseSystemTray(bool animate = false)
    {
        if (trayWindow is null)
        {
            trayWindowOpen = false;
            return;
        }

        var window = trayWindow;
        if (animate)
        {
            window.Dismiss();
            return;
        }

        trayWindow = null;
        trayWindowOpen = false;
        try { window.HideImmediately(); } catch (InvalidOperationException) { }
    }

    private void OpenCalendar(FrameworkElement? source)
    {
        if (closing) return;
        PrepareSystemPopup();
        calendarWindowSource = source;
        SetActiveUtilitySource(calendarWindowSource);
        var created = cachedCalendarPopoverWindow is null;
        try
        {
            calendarWindow = cachedCalendarPopoverWindow ??= new CalendarPopoverWindow(Appearance, settingsSession.Current.DockAppearanceMode, UtilityOwnsPointer);
        }
        catch (Exception error)
        {
            calendarWindowSource = null;
            SetActiveUtilitySource(null);
            SetStatus($"Calendar could not open: {error.Message}");
            SystemPopupClosed();
            return;
        }

        if (created) calendarWindow.Hidden += (_, _) =>
        {
            calendarWindowOpen = false;
            calendarWindow = null;
            calendarWindowSource = null;
            UtilityClosed();
        };

        if (created) calendarWindow.Dismissed += (_, _) => utilityRequests.ClosingExternally();
        calendarWindowOpen = true;
        var anchor = UtilityAnchor(calendarWindowSource);
        calendarWindow.PositionNear(AppWindow, windowManager.Scale, anchor.X, anchor.Y,
            ExpandedContentTop);
        calendarWindow.AppWindow.Show();
        calendarWindow.Activate();
        calendarWindow.Present();

    }

    private void CloseCalendar(bool animate = false)
    {
        if (calendarWindow is null)
        {
            calendarWindowOpen = false;
            return;
        }

        var window = calendarWindow;
        if (animate)
        {
            window.Dismiss();
            return;
        }

        calendarWindow = null;
        calendarWindowOpen = false;
        try { window.HideImmediately(); } catch (InvalidOperationException) { }
    }

    private SystemQuickSettingsWindow? cachedSystemQuickSettingsWindow;
    private SystemTrayWindow? cachedSystemTrayWindow;
    private CalendarPopoverWindow? cachedCalendarPopoverWindow;
    private readonly GlassDock.Core.Desktop.UtilityPopupRequests utilityRequests = new();
    private bool UtilityOwnsPointer()
    {
        if (!windowManager.TryGetPointerPosition(out var x, out var y)) return false;
        foreach (var child in utilityCluster.Children)
        {
            if (child is not Button button || !button.IsEnabled || button.Visibility != Visibility.Visible) continue;
            var bounds = button.TransformToVisual(root).TransformBounds(
                new global::Windows.Foundation.Rect(0, 0, button.ActualWidth, button.ActualHeight));
            if (bounds.Contains(new global::Windows.Foundation.Point(x, y))) return true;
        }
        return false;
    }

    private void ToggleQuickSettings() => RequestUtility();
    private void ToggleSystemTray() => RequestUtility();
    private void ToggleCalendar() => RequestUtility();

    private void RequestUtility()
    {
        if (closing || utilitySource is null) return;
        stackWindow?.Hide();
        var key = utilityCluster.Children.IndexOf(utilitySource);
        if (key < 1) return;
        utilityRequests.Click(key);
        utilityTransitionPending = utilityRequests.Pending.HasValue;
        if (quickSettings is null && trayWindow is null && calendarWindow is null)
        {
            OpenRequestedUtility();
            return;
        }
        if (utilityRequests.TargetOpen)
        {
            quickSettings?.Present(); trayWindow?.Present(); calendarWindow?.Present();
        }
        else
        {
            quickSettings?.RetargetClosed(); trayWindow?.RetargetClosed(); calendarWindow?.RetargetClosed();
        }
    }

    private void OpenRequestedUtility()
    {
        if (closing || utilityRequests.Active is not { } key) return;
        var source = utilityCluster.Children[key] as FrameworkElement;
        if (key == 1) OpenSystemTray(source);
        else if (key == 5) OpenCalendar(source);
        else OpenQuickSettings(source);
        if (quickSettings is null && trayWindow is null && calendarWindow is null)
            utilityRequests.Reset();
    }

    private void UtilityClosed()
    {
        utilityRequests.Closed();
        utilityTransitionPending = false;
        if (!closing) OpenRequestedUtility();
        SystemPopupClosed();
    }
    private void BeginReorderCandidate(
        Button button,
        DockApplicationItem item,
        PointerRoutedEventArgs e)
    {
        if (state.State != DockState.Expanded ||
            previews.ContextMenuOpen || externalDragActive ||
            reorderButton is not null)
        {
            return;
        }

        var point = e.GetCurrentPoint(icons);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        reorderCandidate = item;
        reorderButton = button;
        reorderStartX = point.Position.X;
        reorderDragStartRootX = 0;
        reorderDragging = false;
        reorderSourceIndex = -1;
        reorderTargetIndex = -1;
        reorderPinnedButtons.Clear();
        reorderSlotCenters = [];

        button.CapturePointer(e.Pointer);
    }

    private void UpdateReorder(
        Button button,
        DockApplicationItem item,
        PointerRoutedEventArgs e)
    {
        if (!ReferenceEquals(reorderButton, button) ||
            !ReferenceEquals(reorderCandidate, item))
        {
            return;
        }

        var point = e.GetCurrentPoint(icons);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        var pressDeltaX = point.Position.X - reorderStartX;

        if (!reorderDragging)
        {
            if (Math.Abs(pressDeltaX) < 6)
                return;

            var pinnedItems = VisibleDockApplications
                .ToArray();

            reorderSourceIndex = Array.FindIndex(
                pinnedItems,
                candidate => candidate.Id == item.Id);

            if (reorderSourceIndex < 0)
            {
                CancelReorder();
                return;
            }

            collapseDelay?.Cancel();
            CancelPillHide();
            previews.Hide();

            // Hover magnification changes rendered icon geometry. Remove it first,
            // then establish the drag coordinate system from the settled layout.
            animation.ResetMagnification();

            // Clear only the transient wave deformation. Keep the expanded dock rim
            // visible while reordering.
            dockWaveCurrentStrength = 0;
            dockWaveTargetStrength = 0;
            desktopBackdrop.ClearDockWave();
            UpdateDockWaveOutline();
            dockWaveGlow.Opacity = 0.22;
            dockWaveRim.Opacity = Appearance.BorderOpacity;
            StopDockWaveTimer(clear: false);

            icons.UpdateLayout();

            reorderPinnedButtons = pinnedItems
                .Select(candidate => applicationButtons.GetValueOrDefault(candidate.Id))
                .Where(candidate => candidate is not null)
                .Cast<Button>()
                .ToList();

            if (reorderPinnedButtons.Count != pinnedItems.Length)
            {
                CancelReorder();
                return;
            }

            reorderSlotCenters = reorderPinnedButtons
                .Select(candidate =>
                {
                    var origin = candidate
                        .TransformToVisual(icons)
                        .TransformPoint(new global::Windows.Foundation.Point(0, 0));

                    return origin.X + candidate.ActualWidth / 2;
                })
                .ToArray();

            // Re-read after hover magnification has been cancelled. From this point
            // onward the dragged button follows the physical pointer delta in ROOT
            // coordinates. Root does not move when neighbor icons shift, so this
            // cannot accumulate drift as the icon crosses reorder slots.
            point = e.GetCurrentPoint(icons);
            reorderDragStartRootX = e.GetCurrentPoint(root).Position.X;
            reorderTargetIndex = reorderSourceIndex;
            reorderDragging = true;

            // ButtonBase may emit Click before PointerReleased reaches us.
            suppressClickUntil[item.Id] = DateTime.UtcNow.AddMilliseconds(750);

            button.Opacity = 0.96;
            Canvas.SetZIndex(button, 100);
        }

        if (reorderSlotCenters.Length == 0)
            return;

        // Pointer position chooses the insertion slot.
        reorderTargetIndex = ResolveReorderTargetIndex(point.Position.X);

        // 1:1 physical pointer tracking. The source button keeps its original
        // layout slot while its RenderTransform follows only the pointer delta.
        // Neighbor insertion-gap transforms therefore cannot move the dragged icon
        // away from the mouse.
        var currentRootX = e.GetCurrentPoint(root).Position.X;
        var draggedX = currentRootX - reorderDragStartRootX;

        UpdateStackCandidate(point.Position.X, point.Position.Y);
        if (holdStackTarget || stackDrag.Mode is DockDragMode.StackCandidate or DockDragMode.StackMerge) reorderTargetIndex = reorderSourceIndex;
        ApplyReorderVisuals(draggedX);
        e.Handled = true;
    }

    private int ResolveReorderTargetIndex(double pointerX)
    {
        if (reorderSlotCenters.Length == 0)
            return -1;

        if (reorderSlotCenters.Length == 1)
            return 0;

        // Use the midpoint between adjacent slots as the insertion boundary.
        // The first/last slots naturally extend to infinity so the outer icons
        // remain easy to target even when the pointer is beyond the dock edge.
        for (var index = 0; index < reorderSlotCenters.Length - 1; index++)
        {
            var boundary =
                (reorderSlotCenters[index] + reorderSlotCenters[index + 1]) / 2;

            if (pointerX < boundary)
                return index;
        }

        return reorderSlotCenters.Length - 1;
    }

    private void ApplyReorderVisuals(double draggedX)
    {
        if (!reorderDragging ||
            reorderButton is null ||
            reorderPinnedButtons.Count == 0)
        {
            return;
        }

        var slot = Appearance.ButtonWidth + Appearance.IconSpacing;

        for (var index = 0; index < reorderPinnedButtons.Count; index++)
        {
            var candidate = reorderPinnedButtons[index];

            if (ReferenceEquals(candidate, reorderButton))
            {
                candidate.RenderTransform = new TranslateTransform
                {
                    X = draggedX,
                    Y = -3
                };
                continue;
            }

            double shift = 0;

            if (reorderTargetIndex > reorderSourceIndex &&
                index > reorderSourceIndex &&
                index <= reorderTargetIndex)
            {
                shift = -slot;
            }
            else if (reorderTargetIndex < reorderSourceIndex &&
                     index >= reorderTargetIndex &&
                     index < reorderSourceIndex)
            {
                shift = slot;
            }

            candidate.RenderTransform = new TranslateTransform { X = shift };
        }
    }

    private void FinishReorder(
        Button button,
        DockApplicationItem item,
        PointerRoutedEventArgs e)
    {
        if (!ReferenceEquals(reorderButton, button) ||
            !ReferenceEquals(reorderCandidate, item))
        {
            return;
        }

        // Do not release pointer capture yet. PointerCaptureLost can fire
        // synchronously and would cancel/clear the reorder state before we commit it.
        if (!reorderDragging)
        {
            ClearReorderState();
            button.ReleasePointerCapture(e.Pointer);
            return;
        }

        // PointerMoved can be coalesced/skipped near release, especially while
        // ButtonBase owns capture. Resolve the drop slot one final time from
        // the actual pointer-up position.
        if (FinishStackMerge(button, item, e)) return;
        var releasePoint = e.GetCurrentPoint(icons).Position.X;
        if (reorderSlotCenters.Length > 0)
            reorderTargetIndex = ResolveReorderTargetIndex(releasePoint);

        var orderedIds = VisibleDockApplications
            .Select(candidate => candidate.Id)
            .ToList();

        var committedWithAnimation = false;

        if (reorderSourceIndex >= 0 &&
            reorderSourceIndex < orderedIds.Count &&
            reorderTargetIndex >= 0 &&
            reorderTargetIndex < orderedIds.Count &&
            reorderSourceIndex != reorderTargetIndex)
        {
            var draggedId = orderedIds[reorderSourceIndex];
            orderedIds.RemoveAt(reorderSourceIndex);
            orderedIds.Insert(reorderTargetIndex, draggedId);

            if (applicationService.ReorderApplications(orderedIds))
            {
                // Keep the observable collection in the same order immediately.
                // That prevents the asynchronous application snapshot from
                // performing a second visible reorder a moment after the drop.
                reorderCommitting = true;
                try
                {
                    ApplyPinnedCollectionOrder(orderedIds);
                    ApplyPinnedVisualOrderSmooth(orderedIds);
                    committedWithAnimation = true;
                }
                finally
                {
                    reorderCommitting = false;
                }

                SetStatus("App order saved.");
            }
            else
            {
                SetStatus("Doky could not save the app order.");
                SynchronizeItems();
            }
        }

        suppressClickUntil[item.Id] = DateTime.UtcNow.AddMilliseconds(750);

        // A successful FLIP commit owns the transforms until its storyboards finish.
        // Resetting here would cancel that animation and cause a visible snap/glitch.
        if (!committedWithAnimation)
            ResetReorderVisuals();

        ClearReorderState();

        // Release only after the reorder state is cleared so the capture-lost
        // callback cannot undo the completed drop.
        button.ReleasePointerCapture(e.Pointer);

        pointerInsideDock = windowManager.IsPointerInsideInput();
        RefreshHoverVisuals();
        e.Handled = true;
    }

    private void ApplyPinnedCollectionOrder(IReadOnlyList<string> orderedIds)
    {
        for (var targetIndex = 0; targetIndex < orderedIds.Count; targetIndex++)
        {
            var item = VisibleDockApplications
                .FirstOrDefault(candidate => candidate.Id == orderedIds[targetIndex]);

            if (item is null)
                continue;

            var currentIndex = VisibleDockApplications.IndexOf(item);
            if (currentIndex >= 0 && currentIndex != targetIndex)
                VisibleDockApplications.Move(currentIndex, targetIndex);
        }
    }

    private void ApplyPinnedVisualOrderSmooth(IReadOnlyList<string> orderedIds)
    {
        var orderedButtons = orderedIds
            .Select(id => applicationButtons.GetValueOrDefault(id))
            .Where(button => button is not null)
            .Cast<Button>()
            .ToArray();

        // Capture where every icon is actually being drawn right now, including
        // the dragged icon and the temporary insertion-gap translations.
        var oldPositions = orderedButtons.ToDictionary(
            button => button,
            button => button
                .TransformToVisual(icons)
                .TransformPoint(new global::Windows.Foundation.Point(0, 0)));

        // Remove the drag transforms before changing the StackPanel's real order.
        foreach (var candidate in orderedButtons)
        {
            candidate.RenderTransform = null;
            candidate.Opacity = 1;
            Canvas.SetZIndex(candidate, 0);
        }

        for (var index = 0; index < orderedIds.Count; index++)
        {
            if (!applicationButtons.TryGetValue(orderedIds[index], out var candidate))
                continue;

            var currentIndex = icons.Children.IndexOf(candidate);
            if (currentIndex == index)
                continue;

            if (currentIndex >= 0)
                icons.Children.RemoveAt(currentIndex);

            icons.Children.Insert(index, candidate);
        }

        icons.UpdateLayout();

        // FLIP animation: after layout changes, temporarily translate each icon
        // back to its pre-drop visual position, then settle it into the new slot.
        foreach (var candidate in orderedButtons)
        {
            var newPosition = candidate
                .TransformToVisual(icons)
                .TransformPoint(new global::Windows.Foundation.Point(0, 0));

            var oldPosition = oldPositions[candidate];
            var deltaX = oldPosition.X - newPosition.X;
            var deltaY = oldPosition.Y - newPosition.Y;

            if (Math.Abs(deltaX) < 0.5 && Math.Abs(deltaY) < 0.5)
                continue;

            var transform = new TranslateTransform
            {
                X = deltaX,
                Y = deltaY
            };

            candidate.RenderTransform = transform;

            var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            var easing = new Microsoft.UI.Xaml.Media.Animation.CubicEase
            {
                EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut
            };

            var xAnimation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = deltaX,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(150)),
                EasingFunction = easing,
                EnableDependentAnimation = true
            };

            var yAnimation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = deltaY,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(150)),
                EasingFunction = easing,
                EnableDependentAnimation = true
            };

            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(
                xAnimation,
                transform);

            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(
                xAnimation,
                nameof(TranslateTransform.X));

            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(
                yAnimation,
                transform);

            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(
                yAnimation,
                nameof(TranslateTransform.Y));

            storyboard.Children.Add(xAnimation);
            storyboard.Children.Add(yAnimation);

            storyboard.Completed += (_, _) =>
            {
                if (ReferenceEquals(candidate.RenderTransform, transform))
                    candidate.RenderTransform = null;
            };

            storyboard.Begin();
        }
    }

    private void CancelReorder()
    {
        if (reorderButton is null)
            return;

        ResetReorderVisuals();
        ClearReorderState();
    }

    private void ResetReorderVisuals()
    {
        foreach (var button in reorderPinnedButtons)
        {
            button.RenderTransform = null;
            button.Opacity = 1;
            Canvas.SetZIndex(button, 0);
        }

        if (reorderButton is not null &&
            !reorderPinnedButtons.Contains(reorderButton))
        {
            reorderButton.RenderTransform = null;
            reorderButton.Opacity = 1;
            Canvas.SetZIndex(reorderButton, 0);
        }
    }

    private void ClearReorderState()
    {
        ClearStackDrag();
        reorderCandidate = null;
        reorderButton = null;
        reorderStartX = 0;
        reorderDragStartRootX = 0;
        reorderDragging = false;
        reorderSourceIndex = -1;
        reorderTargetIndex = -1;
        reorderPinnedButtons.Clear();
        reorderSlotCenters = [];
    }

    private void ApplicationDragEnter(Button button, DockApplicationItem item, DragEventArgs e)
    {
        if (StackItemDragOver(e)) return;
        if (closing || shutdown.IsRequested || !HasStorageItems(e))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            e.Handled = true;
            return;
        }

        // External shell drags are identified by their StorageItems payload, not by
        // internal reorder state. If this particular app cannot accept file
        // activation, leave the routed event available to the dock-level handler so
        // executable/shortcut drops can still use the existing "Pin to Doky" path.
        if (!WindowsApplicationLauncher.CanOpenWith(item.Application))
        {
            if (ReferenceEquals(externalDropTargetButton, button))
                SetApplicationDropTarget(button, false);

            e.AcceptedOperation = DataPackageOperation.Copy;
            e.Handled = false;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = $"Open with {item.Name}";
        e.DragUIOverride.IsCaptionVisible = true;
        SetApplicationDropTarget(button, true);
        e.Handled = true;
    }

    private void ApplicationDragOver(Button button, DockApplicationItem item, DragEventArgs e)
    {
        if (StackItemDragOver(e)) return;
        if (closing || shutdown.IsRequested || !HasStorageItems(e))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            e.Handled = true;
            return;
        }

        if (!WindowsApplicationLauncher.CanOpenWith(item.Application))
        {
            if (ReferenceEquals(externalDropTargetButton, button))
                SetApplicationDropTarget(button, false);

            e.AcceptedOperation = DataPackageOperation.Copy;
            e.Handled = false;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = $"Open with {item.Name}";
        e.DragUIOverride.IsCaptionVisible = true;
        collapseDelay?.Cancel();
        CancelPillHide();
        if (!ReferenceEquals(externalDropTargetButton, button))
            SetApplicationDropTarget(button, true);
        e.Handled = true;
    }

    private void ApplicationDragLeave(Button button, DragEventArgs e)
    {
        if (!ReferenceEquals(externalDropTargetButton, button))
            return;

        SetApplicationDropTarget(button, false);
        e.Handled = true;
    }

    private async Task ApplicationDropAsync(Button button, DockApplicationItem item, DragEventArgs e)
    {
        if (await StackItemDrop(e, item.Id)) return;
        if (closing || shutdown.IsRequested || !HasStorageItems(e))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            SetApplicationDropTarget(button, false);
            e.Handled = true;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.Handled = true;

        var deferral = e.GetDeferral();
        try
        {
            var storageItems = await e.DataView.GetStorageItemsAsync();
            var paths = storageItems
                .Select(storageItem => storageItem.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (paths.Length == 0)
            {
                SetStatus("No usable file path was dropped.");
                return;
            }

            // Application buttons also accept file drops ("Open with ..."). That
            // must not steal executable/shortcut drags that are intended to pin a
            // new application to Doky. Classify the dropped paths before checking
            // whether the hovered application supports file activation.
            if (paths.All(IsDockPinTarget))
            {
                var added = 0;

                foreach (var path in paths)
                {
                    if (applicationService.PinExternalTarget(path))
                        added++;
                }

                SetStatus(added switch
                {
                    0 => "Nothing was pinned.",
                    1 => "Pinned 1 item to Doky.",
                    _ => $"Pinned {added} items to Doky."
                });
                return;
            }

            if (!WindowsApplicationLauncher.CanOpenWith(item.Application))
            {
                e.AcceptedOperation = DataPackageOperation.None;
                SetStatus($"{item.Name} does not expose a supported file-drop launch target.");
                return;
            }

            if (dropLauncher.OpenWith(item.Application, paths))
                SetStatus(paths.Length == 1 ? $"Opened dropped item with {item.Name}." : $"Opened {paths.Length} dropped items with {item.Name}.");
            else
                SetStatus($"{item.Name} could not open the dropped item.");
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            SetStatus($"{item.Name} could not open the dropped item: {error.Message}");
        }
        finally
        {
            SetApplicationDropTarget(button, false);
            deferral.Complete();
        }
    }

    private void SetApplicationDropTarget(Button button, bool active)
    {
        if (active)
        {
            if (externalDropTargetButton is not null && !ReferenceEquals(externalDropTargetButton, button))
                RestoreApplicationDropTarget(externalDropTargetButton);

            externalDropTargetButton = button;
            externalDropHighlight.Opacity = 0;
            externalDragActive = true; stackDrag.Begin(external: true);
            collapseDelay?.Cancel();
            CancelPillHide();
            previews.Hide();
            animation.ResetMagnification();
            HideDockWave();
            button.BorderThickness = new Thickness(1.5);
            button.BorderBrush = new SolidColorBrush(global::Windows.UI.Color.FromArgb(220, 225, 245, 255));
            button.Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(54, 150, 210, 255));
            button.Opacity = 1;
            Canvas.SetZIndex(button, 120);
            return;
        }

        if (!ReferenceEquals(externalDropTargetButton, button)) return;
        RestoreApplicationDropTarget(button);
        externalDropTargetButton = null;
        externalDragActive = false; if (stackDrag.Mode == DockDragMode.ExternalFiles) stackDrag.Reset();

        if (!closing)
        {
            pointerInsideDock = windowManager.IsPointerInsideInput();
            RefreshHoverVisuals();
        }
    }

    private static bool IsDockPinTarget(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Directory.Exists(path))
            return false;

        var extension = Path.GetExtension(path);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".url", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".com", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bat", StringComparison.OrdinalIgnoreCase);
    }

    private static void RestoreApplicationDropTarget(Button button)
    {
        button.BorderThickness = new Thickness(0);
        button.BorderBrush = new SolidColorBrush(Colors.Transparent);
        button.Background = new SolidColorBrush(Colors.Transparent);
        button.Opacity = 1;
        Canvas.SetZIndex(button, 0);
    }

    private void UpdateExternalDropHighlight()
    {
        externalDropHighlight.Width = Math.Max(0, surface.ActualWidth > 0 ? surface.ActualWidth : surface.Width);
        externalDropHighlight.Height = Math.Max(
            0,
            surface.ActualHeight > 0 ? surface.ActualHeight : ExpandedDockHeight);
        externalDropHighlight.Margin = surface.Margin;
    }

    private void SetExternalDropVisual(bool active)
    {
        externalDragActive = active;
        if (active && stackDrag.Mode == DockDragMode.None) stackDrag.Begin(external: true);
        else if (!active && stackDrag.Mode == DockDragMode.ExternalFiles) stackDrag.Reset();
        UpdateExternalDropHighlight();

        if (!active && externalDropTargetButton is not null)
        {
            RestoreApplicationDropTarget(externalDropTargetButton);
            externalDropTargetButton = null;
        }

        externalDropHighlight.Opacity = active && externalDropTargetButton is null ? 1 : 0;

        if (active)
        {
            collapseDelay?.Cancel();
            CancelPillHide();
            previews.Hide();
            animation.ResetMagnification();
            HideDockWave();
        }
        else if (!closing)
        {
            pointerInsideDock = windowManager.IsPointerInsideInput();
            RefreshHoverVisuals();
        }
    }

    private static bool HasStorageItems(DragEventArgs e) =>
        e.DataView.Contains(StandardDataFormats.StorageItems);

    private async void ExternalDragEnter(object sender, DragEventArgs e)
    {
        if (StackItemDragOver(e)) return;
        if (closing || shutdown.IsRequested || !HasStorageItems(e))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;

        if (externalDropTargetButton is null)
        {
            e.DragUIOverride.Caption = "Pin to Doky";
            e.DragUIOverride.IsCaptionVisible = true;
            SetExternalDropVisual(true);
        }

        if (state.State is DockState.Idle or DockState.Hovering)
            await ExpandDockAsync();
    }

    private async void ExternalDragOver(object sender, DragEventArgs e)
    {
        if (StackItemDragOver(e)) return;
        if (closing || shutdown.IsRequested || !HasStorageItems(e))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;

        collapseDelay?.Cancel();
        CancelPillHide();

        if (externalDropTargetButton is null)
        {
            e.DragUIOverride.Caption = "Pin to Doky";
            e.DragUIOverride.IsCaptionVisible = true;
            SetExternalDropVisual(true);
        }

        if (state.State is DockState.Idle or DockState.Hovering)
            await ExpandDockAsync();
    }

    private void ExternalDragLeave(object sender, DragEventArgs e)
    {
        SetExternalDropVisual(false);

        if (!previews.HoldsDock &&
            state.State is DockState.Expanded or DockState.Expanding &&
            !windowManager.IsPointerInsideInput())
        {
            ScheduleCollapse();
        }
    }

    private async void ExternalDrop(object sender, DragEventArgs e)
    {
        if (await StackItemDrop(e)) return;
        if (closing || shutdown.IsRequested || !HasStorageItems(e))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            SetExternalDropVisual(false);
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;

        if (externalDropTargetButton is not null)
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items
                .Select(item => item.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var added = 0;

            foreach (var path in paths.Where(IsDockPinTarget))
            {
                if (applicationService.PinExternalTarget(path))
                    added++;
            }

            SetStatus(added switch
            {
                0 => "Nothing was pinned.",
                1 => "Pinned 1 item to Doky.",
                _ => $"Pinned {added} items to Doky."
            });
        }
        catch (Exception error) when (
            error is UnauthorizedAccessException or
            IOException or
            InvalidOperationException)
        {
            SetStatus($"Doky could not pin the dropped item: {error.Message}");
        }
        finally
        {
            SetExternalDropVisual(false);
            deferral.Complete();
        }
    }

    private void Entered(object sender, PointerRoutedEventArgs e)
    {
        // Moving geometry must not simulate a new physical hover.
        if (animation.IsPlacementAnimating || pinDockTransitionActive) return;
        if (!windowManager.IsPointerInsideInput()) return;
        pointerInsideDock = true;
        collapseDelay?.Cancel();
        CancelPillHide();
        if (previews.ContextMenuOpen) return;
        // Peek raising is driven by native physical-pointer movement only.
        // An animating pill passing under a stationary cursor is not reentry.
        if (state.State == DockState.Expanded) RefreshHoverVisuals();
    }

    private void RaisePeek()
    {
        if (DockPinLock) return;
        state.Enter();
        indicator.Opacity = 1;
        animation.AnimateBottom(BottomMargin, 180);
        UpdatePeekInput();
    }

    private void UpdatePeekInput() => windowManager.SetPeekInteraction(surface.Margin.Bottom);

    private void OnInteractionHoldChanged()
    {
        if (closing) return;
        // Menu ownership does not imply that the pointer is over the dock.
        pointerInsideDock = windowManager.IsPointerInsideInput();
        keyboard.SuppressDockToggle = previews.ContextMenuOpen;
        animation.HoldMagnification(previews.ContextMenuOpen);
        if (!previews.ContextMenuOpen)
        {
            RefreshHoverVisuals();
            DispatcherQueue.TryEnqueue(() =>
            {
                if (closing || previews.ContextMenuOpen) return;
                SynchronizeItems();
                pointerInsideDock = windowManager.IsPointerInsideInput();
                RefreshHoverVisuals();
            });
        }
        if (previews.HoldsDock)
        {
            collapseDelay?.Cancel();
            CancelPillHide();
            if (previews.ContextMenuOpen)
            {
                StopDockWaveTimer(clear: false);
                if (!heldTransition && state.State is DockState.Expanding or DockState.Collapsing)
                {
                    heldTransition = state.HoldTransition();
                    animation.FreezeCurrentTransitions();
                }
                if (state.State is DockState.Idle or DockState.Hovering) RaisePeek();
            }
            return;
        }
        ResumeAfterHold();
    }

    private void RefreshHoverVisuals()
    {
        if (pinDockTransitionActive ||
            previews.ContextMenuOpen ||
            externalDragActive ||
            reorderButton is not null ||
            SystemPopupOpen ||
            state.State != DockState.Expanded)
            return;
        if (pointerInsideDock && windowManager.TryGetPointerPosition(out var x, out _))
        {
            var origin = icons.TransformToVisual(root).TransformPoint(new(0, 0));
            animation.UpdateMagnification(x - origin.X);
            ShowDockWave(x);
        }
        else
        {
            animation.ResetMagnification();
            HideDockWave();
        }
    }

    private void ResumeAfterHold()
    {
        if (closing || previews.HoldsDock) return;
        pointerInsideDock = windowManager.IsPointerInsideInput();
        if (heldTransition)
        {
            heldTransition = false;
            state.ResumeHeldTransition(pointerInsideDock);
            if (pointerInsideDock) { _ = ExpandDockAsync(); return; }
        }
        RefreshHoverVisuals();
        if (pointerInsideDock) return;
        if (state.State is DockState.Expanded or DockState.Expanding) ScheduleCollapse();
        else if (state.State is DockState.Idle or DockState.Hovering) SchedulePillHide();
    }

    private void PointerDeparted()
    {
        pointerInsideDock = false;
        if (previews.ContextMenuOpen ||
            externalDragActive ||
            reorderButton is not null ||
            SystemPopupOpen)
            return;
        RefreshHoverVisuals();
        if (previews.HoldsDock) return;
        if (state.State is DockState.Idle or DockState.Hovering) SchedulePillHide();
        else if (state.State is DockState.Expanded or DockState.Expanding) ScheduleCollapse();
    }
    private void Moved(object sender, PointerRoutedEventArgs e)
    {
        if (!windowManager.IsPointerInsideInput()) { PointerDeparted(); return; }
        pointerInsideDock = true;
        collapseDelay?.Cancel();
        CancelPillHide();

        if (previews.ContextMenuOpen ||
            SystemPopupOpen ||
            reorderButton is not null ||
            state.State != DockState.Expanded)
            return;

        var pointerX =
            e.GetCurrentPoint(icons).Position.X;

        var rootX =
            e.GetCurrentPoint(root).Position.X;

        animation.UpdateMagnification(pointerX);
        ShowDockWave(rootX);
    }

    private void ShowDockWave(
        double rootX)
    {
        if (!HoverWaveEnabled ||
            !double.IsFinite(rootX) ||
            state.State != DockState.Expanded)
        {
            return;
        }

        dockWaveTargetX =
            ClampDockWaveCenter(
                rootX);

        dockWaveTargetStrength = 1;

        if (dockWaveCurrentX <= 0)
            dockWaveCurrentX = dockWaveTargetX;

        StartDockWaveTimer();
    }

    private void HideDockWave()
    {
        if (!HoverWaveEnabled)
        {
            ApplyHoverWaveSetting(false);
            return;
        }

        dockWaveTargetStrength = 0;
        StartDockWaveTimer();
    }

    private void TickDockWave(
        object? sender,
        object args)
    {
        if (!HoverWaveEnabled)
        {
            ApplyHoverWaveSetting(false);
            return;
        }

        if (previews.ContextMenuOpen || SystemPopupOpen)
        {
            StopDockWaveTimer(clear: false);
            return;
        }

        if (reorderButton is not null)
        {
            // During icon reorder, freeze the hover-wave animation but preserve the
            // expanded dock's rim/glow. clear:true would zero both opacities and make
            // the border disappear until the next collapse/expand cycle.
            dockWaveCurrentStrength = 0;
            dockWaveTargetStrength = 0;
            desktopBackdrop.ClearDockWave();
            UpdateDockWaveOutline();
            dockWaveGlow.Opacity = 0.22;
            dockWaveRim.Opacity = Appearance.BorderOpacity;
            StopDockWaveTimer(clear: false);
            return;
        }
        if (closing)
        {
            StopDockWaveTimer(
                clear: true);

            return;
        }

        dockWaveTargetX =
            ClampDockWaveCenter(
                dockWaveTargetX);

        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(dockWaveLastFrame, now).TotalSeconds;
        dockWaveLastFrame = now;
        dockWaveCurrentX = GlassDock.Core.Desktop.DockWaveGeometry.Follow(
            dockWaveCurrentX, dockWaveTargetX, elapsed, .0385);
        dockWaveCurrentStrength = GlassDock.Core.Desktop.DockWaveGeometry.Follow(
            dockWaveCurrentStrength, dockWaveTargetStrength, elapsed, .0644);
        desktopBackdrop.SetDockWave(
            dockWaveCurrentX,
            DockWaveHalfWidth,
            DockWaveRise,
            dockWaveCurrentStrength);

        UpdateDockWaveOutline();

        var xSettled =
            Math.Abs(
                dockWaveCurrentX -
                dockWaveTargetX) <
            0.001;

        var strengthSettled =
            Math.Abs(
                dockWaveCurrentStrength -
                dockWaveTargetStrength) <
            0.00001;

        if (!xSettled ||
            !strengthSettled)
        {
            return;
        }

        if (dockWaveTargetStrength <= 0)
        {
            dockWaveCurrentStrength = 0;
            desktopBackdrop.ClearDockWave();
            UpdateDockWaveOutline();
        }

        StopDockWaveTimer(
            clear: false);
    }

    private void UpdateDockWaveOutline()
    {
        if (state.State != DockState.Expanded &&
            state.State != DockState.Expanding && state.State != DockState.Collapsing)
        {
            return;
        }

        if (root.ActualWidth <= 0 ||
            root.ActualHeight <= 0 ||
            surface.ActualWidth <= 0 ||
            surface.ActualHeight <= 0)
        {
            return;
        }

        var centerX =
            dockWaveCurrentX > 0
                ? dockWaveCurrentX
                : root.ActualWidth / 2;

        // The input outline consumes the same continuous geometry as the glass mask.
        dockWaveRim.Data =
            CreateDockWaveGeometry(
                centerX,
                dockWaveCurrentStrength);

        // Use the same animated silhouette for native input as for the glass.
        // HTTRANSPARENT alone cannot forward input to another process/thread.
        var outline = new List<(double X, double Y)>();
        foreach (var figure in ((PathGeometry)dockWaveRim.Data).Figures)
        {
            var point = figure.StartPoint;
            outline.Add((point.X, point.Y));
            foreach (var segment in figure.Segments)
            {
                if (segment is LineSegment line)
                {
                    point = line.Point;
                    outline.Add((point.X, point.Y));
                }
                else if (segment is BezierSegment curve)
                {
                    var start = point;
                    for (var i = 1; i <= 24; i++)
                    {
                        var t = i / 24d;
                        var u = 1 - t;
                        outline.Add((
                            u*u*u*start.X + 3*u*u*t*curve.Point1.X + 3*u*t*t*curve.Point2.X + t*t*t*curve.Point3.X,
                            u*u*u*start.Y + 3*u*u*t*curve.Point1.Y + 3*u*t*t*curve.Point2.Y + t*t*t*curve.Point3.Y));
                    }
                    point = curve.Point3;
                }
            }
        }
        windowManager.SetInteractionPolygon(outline);
    }

    private Geometry CreateDockWaveGeometry(double centerX, double strength)
    {
        var outline = GlassDock.Core.Desktop.DockWaveGeometry.Create(
            (root.ActualWidth - surface.ActualWidth) / 2,
            root.ActualHeight - surface.Margin.Bottom - surface.ActualHeight,
            surface.ActualWidth, surface.ActualHeight, ExpandedDockCornerRadius,
            centerX, DockWaveHalfWidth, DockWaveRise, strength);
        static global::Windows.Foundation.Point Point(GlassDock.Core.Desktop.DockWaveGeometry.Point p) => new(p.X, p.Y);
        var figure = new PathFigure { StartPoint = Point(outline.Start), IsClosed = true };
        foreach (var segment in outline.Segments)
        {
            if (segment.IsLine) figure.Segments.Add(new LineSegment { Point = Point(segment.End) });
            else figure.Segments.Add(new BezierSegment
            {
                Point1 = Point(segment.Control1), Point2 = Point(segment.Control2), Point3 = Point(segment.End)
            });
        }
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
    private double ClampDockWaveCenter(
        double value)
    {
        var dockWidth =
            surface.ActualWidth > 0
                ? surface.ActualWidth
                : surface.Width;

        if (!double.IsFinite(dockWidth) ||
            dockWidth <= 0 ||
            root.ActualWidth <= 0)
        {
            return value;
        }

        var dockHeight =
            surface.ActualHeight > 0
                ? surface.ActualHeight
                : 68;

        var radius =
            Math.Max(
                0,
                Math.Min(
                    ExpandedDockCornerRadius,
                    Math.Min(
                        dockHeight / 2,
                        dockWidth / 2)));

        var left =
            (root.ActualWidth -
             dockWidth) /
            2;

        var topStart =
            left +
            radius;

        var topEnd =
            left +
            dockWidth -
            radius;

        // Only a tiny inset is needed now because the dock corner itself
        // becomes the outer half of the edge wave.
        const double crestInset = 4;

        var minimum =
            topStart +
            crestInset;

        var maximum =
            topEnd -
            crestInset;

        return minimum <= maximum
            ? Math.Clamp(
                value,
                minimum,
                maximum)
            : left +
              dockWidth / 2;
    }

    private static double Lerp(
        double current,
        double target,
        double amount) =>
        current +
        (target - current) *
        Math.Clamp(
            amount,
            0,
            1);

    private void StartDockWaveTimer()
    {
        if (!HoverWaveEnabled || dockWaveTimerRunning)
            return;

        dockWaveTimerRunning = true;
        dockWaveLastFrame = System.Diagnostics.Stopwatch.GetTimestamp();
        CompositionTarget.Rendering += TickDockWave;
    }

    private void StopDockWaveTimer(
        bool clear)
    {
        if (dockWaveTimerRunning)
        {
            dockWaveTimerRunning = false;
            CompositionTarget.Rendering -= TickDockWave;
        }

        if (!clear)
            return;

        dockWaveCurrentStrength = 0;
        dockWaveTargetStrength = 0;
        dockWaveGlow.Opacity = 0;
        dockWaveRim.Opacity = 0;
        desktopBackdrop.ClearDockWave();
    }

    private void ApplyHoverWaveSetting(bool enabled)
    {
        if (enabled)
        {
            // Do not synthesize a wave when enabling. The next real pointer move
            // supplies the anchor. Keep the current plain outline/input region valid.
            dockWaveCurrentStrength = 0;
            dockWaveTargetStrength = 0;
            desktopBackdrop.ClearDockWave();
            UpdateDockWaveOutline();
            return;
        }

        StopDockWaveTimer(clear: false);
        dockWaveCurrentStrength = 0;
        dockWaveTargetStrength = 0;
        desktopBackdrop.ClearDockWave();

        // The invisible XAML geometry still feeds the native hit-test polygon,
        // so disabling the visual deformation never disables pointer handling,
        // magnification, auto-hide, drag/reorder, or app activation.
        UpdateDockWaveOutline();
    }

    private Task ToggleDockAsync()
    {
        if (previews.ContextMenuOpen)
            return Task.CompletedTask;

        if (SystemPopupOpen)
        {
            stackWindow?.Hide();
            utilityRequests.Reset();
            utilityTransitionPending = false;
            CloseQuickSettings();
            CloseSystemTray();
            CloseCalendar();
            return Task.CompletedTask;
        }

        if (DockPinLock)
            return state.State is DockState.Expanded or DockState.Expanding
                ? Task.CompletedTask
                : ExpandDockAsync();

        return state.State is DockState.Expanded or DockState.Expanding
            ? CollapseDockAsync()
            : ExpandDockAsync();
    }

    private async Task ExpandDockAsync()
    {
        if (closing || state.State == DockState.Hidden || previews.ContextMenuOpen) return;
        collapseDelay?.Cancel();

        CancelPillHide();
        if (state.State is DockState.Expanded or DockState.Expanding) return;
        state.Enter();
        var revision = state.Expand();
        animation.AnimateBottom(BottomMargin, 320);
        UpdateDockWaveOutline();
        ApplyMaterial(true);
        var targetWidth = CalculateTargetDockWidth();
        if (await animation.AnimateAsync(true, targetWidth, ExpandedDockHeight))
        {
            state.Complete(revision);
            icons.IsHitTestVisible = state.State == DockState.Expanded;

            if (state.State == DockState.Expanded)
            {
                dockWaveGlow.Opacity = 0.22;
                dockWaveRim.Opacity = Appearance.BorderOpacity;
                UpdateDockWaveOutline();
                if (!previews.ContextMenuOpen) ResumeAfterHold();
            }
        }
    }

    private void Exited(object sender, PointerRoutedEventArgs e)
    {
        if (animation.IsPlacementAnimating || pinDockTransitionActive) return;
        if (!windowManager.IsPointerInsideInput()) PointerDeparted();
    }
    private async Task CollapseDockAsync()
    {
        if (DockPinLock || previews.HoldsDock || SystemPopupOpen) return;
        previews.Hide();

        CancelPillHide();
        animation.ResetMagnification();
        HideDockWave();

        dockWaveGlow.Opacity = 0;
        dockWaveRim.Opacity = 0;

        collapseDelay?.Cancel();
        if (closing || state.State is DockState.Hidden or DockState.Idle or DockState.Collapsing) return;
        var revision = state.Collapse();
        icons.IsHitTestVisible = false;
        if (await animation.AnimateAsync(false))
        {
            state.Complete(revision);
            if (state.State == DockState.Idle)
            {
                ApplyMaterial(false);
                // Keep the completed pill at its normal collapsed position
                // before starting the separate two-second hide countdown.
                animation.SetBottom(BottomMargin);
                indicator.Opacity = 1;
                UpdatePeekInput();
                SchedulePillHide();
            }
        }
    }

    private void CancelPillHide()
    {
        pillHideDelay?.Cancel();
        pillHideDelay = null;
        animation.StopIndicatorOpacityAnimation();
    }

    private async void SchedulePillHide()
    {
        if (DockPinLock || pillHideDelay is not null || closing || previews.HoldsDock ||
            state.State is not (DockState.Idle or DockState.Hovering) ||
            surface.Margin.Bottom <= PeekRestBottom + 0.01) return;
        var delay = new CancellationTokenSource();
        pillHideDelay = delay;
        try
        {
            await Task.Delay(settingsSession.DockBehavior.PeekDelay, delay.Token);
            if (delay.IsCancellationRequested || closing || previews.HoldsDock ||
                state.State is not (DockState.Idle or DockState.Hovering)) return;
            pointerInsideDock = windowManager.IsPointerInsideInput();
            if (pointerInsideDock) return;
            state.LeavePeek();
            // Separate from dock contraction: never fade out the remaining handle.
            animation.AnimateBottom(PeekRestBottom, PillHideDurationMilliseconds);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(pillHideDelay, delay)) pillHideDelay = null;
            delay.Dispose();
        }
    }
    private async void ScheduleCollapse()
    {
        if (DockPinLock || collapseDelay is { IsCancellationRequested: false } || closing || previews.HoldsDock) return;
        var delay = new CancellationTokenSource();
        collapseDelay = delay;
        try
        {
            await Task.Delay(settingsSession.DockBehavior.AutoHideDelay, delay.Token);
            if (delay.IsCancellationRequested || closing || previews.HoldsDock || windowManager.IsPointerInsideInput()) return;
            DockAnimationController.Trace($"Collapse delay elapsed state={state.State}");
            await CollapseDockAsync();
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(collapseDelay, delay)) collapseDelay = null; delay.Dispose(); }
    }

    private async Task ApplyPinDockModeAsync(bool pin, bool animate)
    {
        pinDockTransition?.Cancel();

        var transition = new CancellationTokenSource();
        pinDockTransition = transition;
        pinDockTransitionActive = true;
        collapseDelay?.Cancel();
        CancelPillHide();
        animation.ResetMagnification();
        HideDockWave();

        try
        {
            if (state.State is DockState.Expanding or DockState.Collapsing)
                await WaitForDockTransitionAsync(transition.Token);

            if (pin)
            {
                if (state.State != DockState.Expanded)
                    await ExpandDockAsync();

                if (state.State == DockState.Expanding)
                    await WaitForDockTransitionAsync(transition.Token);

                if (transition.IsCancellationRequested || closing || state.State != DockState.Expanded)
                    return;

                var workAreaReserved = windowManager.SetReservedBottomSpace(ExpandedDockHeight);

                await AnimatePinnedPlacementAsync(true, animate, transition.Token);
                transition.Token.ThrowIfCancellationRequested();
                pinnedPlacementActive = true;
                SetStatus(workAreaReserved
                    ? "Dock pinned to the bottom edge. Maximized windows now stay above Doky."
                    : "Dock pinned, but Windows could not reserve screen space for maximized windows.");
            }
            else
            {
                if (state.State == DockState.Expanded)
                    await AnimatePinnedPlacementAsync(false, animate, transition.Token);
                else
                    SetPinnedPlacementInstant(pin: false);

                transition.Token.ThrowIfCancellationRequested();
                pinnedPlacementActive = false;
                windowManager.ClearBottomWorkAreaReservation();
                SetStatus($"Dock unpinned · full Windows work area restored · bottom spacing {BottomMargin:0} DIP.");
            }
        }
        catch (OperationCanceledException) when (transition.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(pinDockTransition, transition))
            {
                pinDockTransition = null;
                pinDockTransitionActive = false;

                if (!closing && !shutdown.IsRequested)
                {
                    pointerInsideDock = windowManager.IsPointerInsideInput();
                    RefreshHoverVisuals();
                    if (!pinDockRequested && !pinnedPlacementActive && !pointerInsideDock &&
                        state.State is DockState.Expanded or DockState.Expanding)
                    {
                        ScheduleCollapse();
                    }
                }
            }

            transition.Dispose();
        }
    }

    private async Task WaitForDockTransitionAsync(CancellationToken cancellationToken)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        while (state.State is DockState.Expanding or DockState.Collapsing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds > 1200)
                break;
            await Task.Delay(16, cancellationToken);
        }
    }

    private async Task AnimatePinnedPlacementAsync(bool pin, bool animate, CancellationToken cancellationToken)
    {
        var startSurfaceBottom = surface.Margin.Bottom;
        var startContentBottom = icons.Margin.Bottom;
        var startIndicatorBottom = indicator.Margin.Bottom;
        var startSurfaceHeight = surface.ActualHeight > 0 ? surface.ActualHeight : surface.Height;

        var targetSurfaceBottom = pin ? -PinnedDockClipDepth : BottomMargin;
        var targetContentBottom = pin ? 0 : BottomMargin;
        var targetSurfaceHeight = ExpandedDockHeight + (pin ? PinnedDockClipDepth : 0);

        if (!animate)
        {
            SetPinnedPlacementFrame(
                targetSurfaceBottom,
                targetContentBottom,
                targetContentBottom,
                targetSurfaceHeight);
            return;
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var progress = Math.Clamp(elapsed / PinnedDockTransitionDurationMilliseconds, 0, 1);
            var eased = progress * progress * (3 - 2 * progress);

            SetPinnedPlacementFrame(
                Lerp(startSurfaceBottom, targetSurfaceBottom, eased),
                Lerp(startContentBottom, targetContentBottom, eased),
                Lerp(startIndicatorBottom, targetContentBottom, eased),
                Lerp(startSurfaceHeight, targetSurfaceHeight, eased));

            if (progress >= 1)
                break;

            await Task.Delay(16, cancellationToken);
        }
    }

    private void SetPinnedPlacementInstant(bool pin)
    {
        SetPinnedPlacementFrame(
            pin ? -PinnedDockClipDepth : BottomMargin,
            pin ? 0 : BottomMargin,
            pin ? 0 : BottomMargin,
            ExpandedDockHeight + (pin ? PinnedDockClipDepth : 0));
    }

    private void SetPinnedPlacementFrame(
        double surfaceBottom,
        double contentBottom,
        double indicatorBottom,
        double surfaceHeight)
    {
        surface.Height = Math.Max(ExpandedDockHeight, surfaceHeight);
        surface.Margin = new Thickness(0, 0, 0, surfaceBottom);
        icons.Margin = new Thickness(0, 0, 0, contentBottom);
        indicator.Margin = new Thickness(0, 0, 0, indicatorBottom);

        UpdateBackdropBounds();
        UpdateDockWaveOutline();
        UpdateExternalDropHighlight();
        if (SystemPopupOpen) RepositionSystemPopups();
        previews.Reposition();
    }

    private void ApplyMaterial(bool expanded, DockAppearanceMode? dockAppearance = null)
    {
        var mode = dockAppearance ?? settingsSession.Current.DockAppearanceMode;
        var cornerRadius = expanded ? ExpandedDockCornerRadius : 2.5;
        const double opacity = 1;

        // The main dock is a plain solid surface. DesktopGlassBackdrop still
        // owns the vector mask so the hover wave, DPI scaling, and native hit
        // geometry remain unchanged. Utility popups keep their glass material.
        surface.ApplyPlain(mode, opacity, cornerRadius);
        var glassStyle = mode.GlassStyle();
        var solid = glassStyle is null;
        var reconnect = desktopBackdrop.UseSolidSurface != solid;
        // Switch the compositor source without detaching the Window's WinUI
        // SystemBackdrop. The latter can invalidate a queued WinUI configuration
        // callback during solid <-> glass transitions ("target" argument error).
        desktopBackdrop.UseSolidSurface = solid;
        desktopBackdrop.UseInnerEdge = !solid;
        if (glassStyle is { } style)
        {
            // Settings already supplies absolute, editable preset values; apply them once.
            desktopBackdrop.ApplyMainDock(style, Appearance.ApplyTo(DockMaterialStylePresets.Create(style), true) with
            {
                CornerRadius = cornerRadius,
                BorderThickness = Appearance.BorderThickness,
                BorderOpacity = expanded ? Appearance.BorderOpacity : 0
            });
        }
        else
        {
            desktopBackdrop.SetSolidAppearance(mode, opacity, cornerRadius);
        }
        if (reconnect) desktopBackdrop.RebuildConnectedSurface();
        ApplyIndicatorAppearance(mode);
        UpdateBackdropBounds();
    }

    private void ApplyIndicatorAppearance(DockAppearanceMode mode)
    {
        indicator.Background = new SolidColorBrush(
            mode != DockAppearanceMode.Dark
                ? global::Windows.UI.Color.FromArgb(255, 243, 243, 243)
                : global::Windows.UI.Color.FromArgb(255, 36, 36, 36));
    }

    private void UpdateBackdropBounds()
    {
        desktopBackdrop.SetBounds(
            root.ActualWidth,
            root.ActualHeight,
            surface.ActualWidth,
            surface.ActualHeight,
            surface.Margin.Bottom,
            root.XamlRoot?.RasterizationScale ?? 1,
            surface.Opacity);

        if (state.State is DockState.Expanded or DockState.Expanding)
        {
            if (HoverWaveEnabled)
            {
                desktopBackdrop.SetDockWave(
                    dockWaveCurrentX > 0
                        ? dockWaveCurrentX
                        : root.ActualWidth / 2,
                    DockWaveHalfWidth,
                    DockWaveRise,
                    dockWaveCurrentStrength);
            }
            else
            {
                desktopBackdrop.ClearDockWave();
            }

            UpdateDockWaveOutline();
        }
    }

    private void SettingsChanged(
        object? sender,
        GlassDockSettingsChangedEventArgs eventArgs)
    {
        if (closing) return;

        var pinChanged = pinDockRequested != eventArgs.Settings.PinDock;
        pinDockRequested = eventArgs.Settings.PinDock;
        var notificationBadgesChanged = notificationBadgesRequested != eventArgs.Settings.NotificationBadgesEnabled;
        notificationBadgesRequested = eventArgs.Settings.NotificationBadgesEnabled;

        ApplyBottomMargin(eventArgs.Settings.BottomMargin);
        ApplyDisplayMode(eventArgs.Settings.DockDisplayMode);
        ApplyHoverWaveSetting(eventArgs.Settings.HoverWaveEnabled);
        ApplyAppearance(new DockAppearanceSettings(
            eventArgs.Settings.GlassMaterialMode,
            eventArgs.Settings.IconSize,
            eventArgs.Settings.MagnificationScale,
            eventArgs.Settings.IconSpacing,
            eventArgs.Settings.GlassBlurAmount,
            eventArgs.Settings.DockOpacity,
            eventArgs.Settings.BorderThickness,
            eventArgs.Settings.BorderOpacity),
            eventArgs.Settings.DockAppearanceMode);

        if (ownsGlobalServices && notificationBadgesChanged)
        {
            if (notificationBadgesRequested)
                _ = badges.StartAsync();
            else
                badges.Stop();
        }

        if (pinChanged || (pinDockRequested && !pinnedPlacementActive && !pinDockTransitionActive))
            _ = ApplyPinDockModeAsync(pinDockRequested, animate: true);
    }

    private void ApplyAppearance(DockAppearanceSettings appearance) =>
        ApplyAppearance(appearance, settingsSession.Current.DockAppearanceMode);

    private void ApplyAppearance(
        DockAppearanceSettings appearance,
        DockAppearanceMode dockAppearance)
    {
        stackWindow?.ApplyAppearance(appearance, dockAppearance);
        cachedSystemQuickSettingsWindow?.ApplyAppearance(appearance, dockAppearance);
        cachedSystemTrayWindow?.ApplyAppearance(appearance, dockAppearance);
        cachedCalendarPopoverWindow?.ApplyAppearance(appearance, dockAppearance);
        icons.Spacing = appearance.IconSpacing;
        icons.Height = Math.Max(68, appearance.ButtonHeight + 24);
        if (pinnedPlacementActive)
            windowManager.SetReservedBottomSpace(Math.Max(68, appearance.ButtonHeight + 24));
        animation.SetMaximumMagnificationScale(appearance.MagnificationScale);
        dockWaveRim.StrokeThickness = 0;
        dockWaveRim.Opacity = 0;
        var foreground = DockForegroundBrush();
        icons.RequestedTheme = dockAppearance == DockAppearanceMode.Light ? ElementTheme.Light : ElementTheme.Dark;
        utilityClock.Foreground = foreground;
        foreach (var child in utilityCluster.Children)
        {
            if (child is Border divider)
            {
                divider.Background = foreground;
                divider.Opacity = .2;
            }
            if (child is Button utilityButton)
            {
                if (utilityButton.Content is FontIcon glyph) glyph.Foreground = foreground;
                utilityButton.Foreground = foreground;
                utilityButton.Resources["ButtonForegroundPointerOver"] = foreground;
                utilityButton.Resources["ButtonForegroundPressed"] = foreground;
                SetUtilityButtonSelected(utilityButton, ReferenceEquals(utilityButton, activeUtilityButton));
            }
        }

        foreach (var button in applicationButtons.Values)
        {
            if (button.Content is not Grid content ||
                content.Children.FirstOrDefault() is not AdaptiveAppIcon icon)
                continue;

            ApplyIconLayout(button, content, icon, appearance);
            foreach (var running in content.Children.OfType<Border>()) running.Background = foreground;
        }

        if (state.State is DockState.Expanded)
        {
            surface.Width = CalculateTargetDockWidth();
            surface.Height = ExpandedDockHeight + (pinnedPlacementActive ? PinnedDockClipDepth : 0);
            ApplyMaterial(expanded: true, dockAppearance: dockAppearance);
        }
        else
        {
            ApplyMaterial(expanded: false, dockAppearance: dockAppearance);
        }

        UpdateBackdropBounds();
        previews.Reposition();
    }

    private static void ApplyIconLayout(
        Button button,
        Grid content,
        AdaptiveAppIcon icon,
        DockAppearanceSettings appearance)
    {
        button.Width = content.Width = appearance.ButtonWidth;
        button.Height = content.Height = appearance.ButtonHeight;
        icon.Configure(appearance.IconSize, appearance.MagnificationScale);
    }

    public void SetBottomMargin(double margin)
    {
        settingsSession.Replace(settingsSession.CreateDockBehaviorUpdate(
            margin,
            settingsSession.Current.AutoHideDelayMilliseconds,
            settingsSession.Current.PeekDelayMilliseconds));
    }

    private void ApplyBottomMargin(double margin)
    {
        BottomMargin = Math.Clamp(
            double.IsFinite(margin) ? margin : GlassDockSettings.DefaultBottomMargin,
            GlassDockSettings.MinimumBottomMargin,
            GlassDockSettings.MaximumBottomMargin);

        if (!DockPinLock)
        {
            icons.Margin = new Thickness(0, 0, 0, BottomMargin);
            if (state.State == DockState.Expanded) animation.SetBottom(BottomMargin);
            else if (state.State == DockState.Expanding) animation.AnimateBottom(BottomMargin, 180);
        }

        windowManager.Position(0, settingsSession.DisplayMode);
        if (state.State is DockState.Idle or DockState.Hovering) UpdatePeekInput();
        else UpdateDockWaveOutline();
        UpdateBackdropBounds();
        SetStatus(DockPinLock
            ? $"Pinned dock active · normal bottom spacing remains {BottomMargin:0} DIP."
            : $"Expanded dock bottom margin: {BottomMargin:0} DIP.");
        previews.Reposition();
    }

    private void ApplyDisplayMode(DockDisplayMode displayMode)
    {
        windowManager.Position(0, displayMode);

        if (state.State is DockState.Idle or DockState.Hovering)
            UpdatePeekInput();
        else
            UpdateDockWaveOutline();

        pointerInsideDock = windowManager.IsPointerInsideInput();
        UpdateBackdropBounds();
        previews.Reposition();

        var label = displayMode switch
        {
            DockDisplayMode.Pointer => "following the pointer",
            DockDisplayMode.Foreground => "following the active window",
            DockDisplayMode.AllDisplays => "on all displays",
            _ => "on the primary display"
        };
        SetStatus($"Dock display mode: {label}.");
    }

    public void ShowSettings()
    {
        if (showSettingsOverride is not null)
        {
            showSettingsOverride();
            return;
        }

        if (closing || shutdown.IsRequested || settingsWindow.IsShutdown) return;
        var window = settingsWindow.GetOrCreate(() =>
        {
            var created = new SettingsWindow(
                settingsSession,
                settingsStore,
                shutdown,
                safeMode,
                RestoreTaskbar,
                ResumeTaskbarSuppression,
                () => RestartGlassDock(inSafeMode: false),
                () => RestartGlassDock(inSafeMode: true),
                RequestShutdown,
                badges);
            created.Closed += (_, _) => settingsWindow.Release(created);
            return created;
        });
        window.Activate();
    }

    public void ShowControls()
    {
        if (closing || shutdown.IsRequested) return;
        if (controls is null)
        {
            controls = new DevelopmentWindow(this);
            controls.Closed += (_, _) => controls = null;
        }
        controls.Activate();
    }

    public void ShowHome()
    {
        if (closing || shutdown.IsRequested) return;
        if (home is null)
        {
            var window = new GlassHomeWindow();
            home = window;
            window.HomeVisibilityChanged += (_, _) =>
            {
                if (ReferenceEquals(home, window))
                    keyboard.CaptureBareWindowsKey = window.IsVisible;
            };
            window.Closed += (_, _) =>
            {
                keyboard.CaptureBareWindowsKey = false;
                if (ReferenceEquals(home, window))
                    home = null;
            };
        }

        home.Toggle();
        keyboard.CaptureBareWindowsKey = home.IsVisible;
    }
    public void ShowLab()
    {
        if (closing || shutdown.IsRequested) return;
        if (lab is null)
        {
            lab = new Window { Title = "Doky — Glass Material Laboratory", Content = new Views.GlassLabView() };
            WindowBranding.Apply(lab);
            lab.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1320, 900));
            lab.Closed += (_, _) => lab = null;
        }
        lab.Activate();
    }

    public Task StartTaskbarTestAsync(bool whileAppActive = false)
    {
        if (!ownsGlobalServices)
            return Task.CompletedTask;

        if (closing ||
            shutdown.IsRequested ||
            safeMode ||
            taskbarSuppressionPaused ||
            !settingsSession.Current.SuppressWindowsTaskbar)
        {
            return Task.CompletedTask;
        }
        if (taskbarOperation is { IsCompleted: false })
            return taskbarOperation;

        taskbarOperation = StartTaskbarTestCoreAsync(whileAppActive);
        return taskbarOperation;
    }

    private async Task StartTaskbarTestCoreAsync(bool whileAppActive)
    {
        if (startingTest || taskbarSession is { IsActive: true }) return;

        // The watchdog owns taskbar safety and has its own independent recovery path.
        // Do not prevent taskbar protection from starting just because the optional
        // keyboard recovery hotkey could not be registered.
        startingTest = true;
        var revision = ++taskbarRevision;
        try
        {
            if (taskbarSession is not null) await taskbarSession.DisposeAsync();
            var session = await TaskbarDevelopmentSession.StartAsync(Path.Combine(AppContext.BaseDirectory, "Recovery", "GlassDock.Watchdog.exe"), whileAppActive);
            if (closing || revision != taskbarRevision)
            {
                await session.DisposeAsync();
                return;
            }
            taskbarSession = session;
            session.Ended += (_, message) => DispatcherQueue.TryEnqueue(() =>
            {
                if (!ReferenceEquals(taskbarSession, session) || closing) return;
                heartbeat.Stop();
                SetStatus(message);
            });
            if (session.IsActive)
            {
                // Changing auto-hide updates the work area; the shell can move windows
                // during that change. Re-anchor to full monitor bounds afterwards.
                windowManager.Position(0, settingsSession.DisplayMode);
                heartbeat.Start();
                SetStatus(whileAppActive
                    ? "Taskbar suppressed while dock is active · Ctrl+Alt+F12 restores immediately."
                    : "Taskbar hidden · maximum 60 seconds · Ctrl+Alt+F12 restores immediately.");
            }
            else SetStatus("Taskbar test ended during initialization; recovery was requested.");
        }
        catch (Exception exception)
        {
            TaskbarRecovery.RestoreNow();
            SetStatus($"Taskbar suppression refused: {exception.Message}");
        }
        finally { startingTest = false; }
    }

    public void RestoreTaskbar()
    {
        if (!ownsGlobalServices && restoreTaskbarOverride is not null)
        {
            restoreTaskbarOverride();
            return;
        }

        if (closing || shutdown.IsRequested || taskbarRestoreOperation is { IsCompleted: false })
            return;

        taskbarSuppressionPaused = true;
        taskbarRestoreOperation = RestoreTaskbarAsync();
    }

    public void ResumeTaskbarSuppression()
    {
        if (!ownsGlobalServices && resumeTaskbarOverride is not null)
        {
            resumeTaskbarOverride();
            return;
        }

        if (closing || shutdown.IsRequested)
            return;

        if (safeMode)
        {
            SetStatus("Safe Mode keeps Windows taskbar suppression disabled.");
            return;
        }

        if (!settingsSession.Current.SuppressWindowsTaskbar)
        {
            SetStatus("Windows taskbar suppression is disabled in settings.");
            return;
        }

        taskbarSuppressionPaused = false;
        _ = StartTaskbarTestAsync(whileAppActive: true);
    }

    private void RestartGlassDock(bool inSafeMode)
    {
        if (closing || shutdown.IsRequested)
            return;

        if (prepareRestart is null)
        {
            SetStatus("Restart is unavailable in this build.");
            return;
        }

        prepareRestart(inSafeMode);
        RequestShutdown();
    }

    private async Task RestoreTaskbarAsync()
    {
        taskbarRevision++;
        heartbeat.Stop();
        var restored = TaskbarRecovery.RestoreNow();
        if (taskbarSession is { } session)
        {
            taskbarSession = null;
            await session.DisposeAsync();
        }
        if (!closing)
            SetStatus(restored ? "Windows taskbar restored." : "Restoration not verified; use the independent recovery command.");
    }

    private void SetStatus(string value) { Status = value; StatusChanged?.Invoke(this, EventArgs.Empty); }

    public void RequestShutdown()
    {
        if (requestApplicationShutdown is not null)
        {
            requestApplicationShutdown();
            return;
        }

        BeginShutdown(closeMainWindow: true);
    }

    private void OnClosed(object sender, WindowEventArgs e)
    {
        if (closing) return;
        if (unexpectedClosed is not null)
        {
            unexpectedClosed(this);
            return;
        }

        BeginShutdown(closeMainWindow: false);
    }

    internal Task ShutdownForCoordinatorAsync(bool closeMainWindow = true)
    {
        if (shutdownOperation is not null)
            return shutdownOperation;

        closing = true;
        shutdownOperation = ShutdownAsync(closeMainWindow, completeApplicationShutdown: false);
        return shutdownOperation;
    }

    internal Task CleanupAfterUnexpectedCloseAsync()
    {
        if (shutdownOperation is not null)
            return shutdownOperation;

        closing = true;
        shutdownOperation = ShutdownAsync(closeMainWindow: false, completeApplicationShutdown: false);
        return shutdownOperation;
    }

    private void BeginShutdown(bool closeMainWindow)
    {
        if (!shutdown.TryBegin()) return;
        closing = true;
        shutdownOperation = ShutdownAsync(closeMainWindow, completeApplicationShutdown: true);
    }

    private async Task ShutdownAsync(bool closeMainWindow, bool completeApplicationShutdown)
    {
        // Remove every GlassDock surface immediately; native/resource cleanup follows
        // while the taskbar recovery helper is still independently protecting exit.
        if (closeMainWindow) AppWindow.Hide();
        root.ContextFlyout?.Hide();

        ClearStackDrag(); stackWindow?.Close(); stackWindow = null;
        previews.Dispose();
        badges.Changed -= BadgesChanged;
        if (ownsGlobalServices)
            badges.RefreshRequested -= BadgesRefreshRequested;
        if (ownsBadges)
            badges.Dispose();
        applications.Dispose();
        taskbarRevision++;
        state.Hide();
        heldTransition = false;
        collapseDelay?.Cancel();
        pinDockTransition?.Cancel();

        CancelPillHide();
        heartbeat.Stop();
        displayTimer.Stop();
        utilityTimer.Stop();
        utilityRequests.Reset();
        utilityTransitionPending = false;
        CloseQuickSettings();
        CloseSystemTray();
        CloseCalendar();

        cachedSystemQuickSettingsWindow?.CloseImmediately();
        cachedSystemTrayWindow?.CloseImmediately();
        cachedCalendarPopoverWindow?.CloseImmediately();
        cachedSystemQuickSettingsWindow = null; cachedSystemTrayWindow = null; cachedCalendarPopoverWindow = null;
        StopDockWaveTimer(clear: true);
        CompositionTarget.Rendering -= TickDockWave;

        settingsSession.Changed -= SettingsChanged;
        settingsWindow.BeginShutdown()?.Close();

        var controlsToClose = controls;
        controls = null;
        controlsToClose?.Close();

        var homeToClose = home;
        home = null;
        homeToClose?.Close();

        var labToClose = lab;
        lab = null;
        labToClose?.Close();

        // Restore synchronously before stopping native services or closing the last window.
        if (ownsGlobalServices && (taskbarSession is not null || startingTest))
            TaskbarRecovery.RestoreNow();
        animation.Stop();
        if (ownsKeyboard)
            keyboard.Dispose();
        windowManager.Dispose();

        if (taskbarOperation is { } pendingTaskbar)
            await pendingTaskbar;

        if (taskbarRestoreOperation is { } pendingRestore)
            await pendingRestore;

        if (taskbarSession is { } session)
        {
            taskbarSession = null;
            await session.DisposeAsync();
        }

        if (closeMainWindow) Close();
        if (completeApplicationShutdown)
            shutdownCompleted();
    }
}
