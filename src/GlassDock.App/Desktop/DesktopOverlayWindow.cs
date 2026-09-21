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

namespace GlassDock.App.Desktop;

public sealed class DesktopOverlayWindow : Window
{
    private readonly Grid root = new() { Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(1, 0, 0, 0)) };
    private readonly GlassSurface surface = new()
    {
        UseDesktopBackdrop = true, Width = 120, Height = 5, Opacity = 0,
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

    // These paths are only the luminous outline. The glass BODY itself is
    // deformed by DesktopGlassBackdrop.SetDockWave().
    private readonly XamlPath dockWaveGlow = new()
    {
        IsHitTestVisible = false,
        StrokeThickness = 3.2,
        Stroke = new SolidColorBrush(
            global::Windows.UI.Color.FromArgb(
                42,
                80,
                175,
                255)),
        Opacity = 0
    };

    private readonly XamlPath dockWaveRim = new()
    {
        IsHitTestVisible = false,
        StrokeThickness = 1.05,
        Stroke = new LinearGradientBrush
        {
            StartPoint = new global::Windows.Foundation.Point(0, 0),
            EndPoint = new global::Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new GradientStop
                {
                    Offset = 0,
                    Color = global::Windows.UI.Color.FromArgb(
                        150,
                        255,
                        255,
                        255)
                },
                new GradientStop
                {
                    Offset = 0.52,
                    Color = global::Windows.UI.Color.FromArgb(
                        70,
                        185,
                        225,
                        255)
                },
                new GradientStop
                {
                    Offset = 1,
                    Color = global::Windows.UI.Color.FromArgb(
                        105,
                        235,
                        248,
                        255)
                }
            }
        },
        Opacity = 0
    };
    private readonly DockStateMachine state = new();
    private readonly WindowsApplicationService applicationService = new();
    private readonly DockApplicationsViewModel applications;
    private readonly WindowPreviewCoordinator previews;
    private readonly Dictionary<string, Button> applicationButtons = new(StringComparer.Ordinal);
    public ObservableCollection<DockApplicationItem> VisibleDockApplications => applications.VisibleDockApplications;
    private readonly DesktopGlassBackdrop desktopBackdrop = new();
    private readonly WindowsOverlayManager windowManager;
    private readonly WindowsKeyboardService keyboard;
    private readonly DockAnimationController animation;
    private readonly GlassDockSettingsSession settingsSession;
    private readonly GlassDockSettingsStore settingsStore;
    private readonly ApplicationShutdownState shutdown;
    private readonly Action shutdownCompleted;
    private readonly RetainedWindowSlot<SettingsWindow> settingsWindow = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer heartbeat;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer displayTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer dockWaveTimer;
    private CancellationTokenSource? collapseDelay;

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
    private Task? taskbarOperation;
    private Task? taskbarRestoreOperation;
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
    private bool reorderDragging;
    private bool reorderCommitting;
    private int reorderSourceIndex = -1;
    private int reorderTargetIndex = -1;
    private List<Button> reorderPinnedButtons = [];
    private double[] reorderSlotCenters = [];
    private readonly Dictionary<string, DateTime> suppressClickUntil = new(StringComparer.Ordinal);

    private const double DockWaveHalfWidth = 92;
    private const double DockWaveRise = 12;
    private const double PillHideDurationMilliseconds = 210;

    public double BottomMargin { get; private set; }
    private DockAppearanceSettings Appearance => settingsSession.Appearance;
    private double ExpandedDockHeight => Math.Max(68, Appearance.ButtonHeight + 24);
    private const double PeekRestBottom = -2; // Three DIP remain visible above the physical screen edge.
    public string Status { get; private set; } = "Starting desktop recovery protection.";
    public string RenderingMode => desktopBackdrop.RenderingMode;
    public bool HotkeysAvailable => keyboard.IsRegistered;
    public bool IsShuttingDown => shutdown.IsRequested;
    public event EventHandler? StatusChanged;

    public DesktopOverlayWindow(
        GlassDockSettingsSession settingsSession,
        GlassDockSettingsStore settingsStore,
        ApplicationShutdownState shutdown,
        Action shutdownCompleted,
        bool inspection = false)
    {
        this.settingsSession = settingsSession;
        this.settingsStore = settingsStore;
        this.shutdown = shutdown;
        this.shutdownCompleted = shutdownCompleted;
        BottomMargin = settingsSession.DockBehavior.BottomMargin;
        settingsSession.Changed += SettingsChanged;

        Title = "GlassDock — Floating Dock";
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
        windowManager = new WindowsOverlayManager(hwnd);
        windowManager.Configure(inspection);
        root.Children.Add(surface);
        root.Children.Add(dockWaveGlow);
        root.Children.Add(dockWaveRim);
        root.Children.Add(indicator);
        root.Children.Add(icons);
        surface.RegisterPropertyChangedCallback(UIElement.OpacityProperty, (_, _) => UpdateBackdropBounds());
        surface.SizeChanged += (_, _) =>
        {
            UpdateBackdropBounds();
            UpdateDockWaveOutline();
        };

        root.SizeChanged += (_, _) =>
        {
            UpdateBackdropBounds();
            UpdateDockWaveOutline();
        };
        desktopBackdrop.RenderingModeChanged += (_, _) => { UpdateBackdropBounds(); StatusChanged?.Invoke(this, EventArgs.Empty); };
        applications = new DockApplicationsViewModel(applicationService, DispatcherQueue);
        previews = new(applications, root, hwnd, () => root.ActualHeight - BottomMargin - ExpandedDockHeight);
        previews.HoldChanged += (_, _) => OnInteractionHoldChanged();
        previews.ActionFailed += (_, message) => SetStatus(message);
        AppWindow.Changed += (_, _) => previews.Reposition();
        applications.VisibleDockApplications.CollectionChanged += (_, _) => SynchronizeItems();
        applications.WarningChanged += (_, _) => { if (applications.Warning is { } warning) SetStatus(warning); };
        animation = new DockAnimationController(surface, icons, indicator);
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
            if (state.State == DockState.Expanded) RefreshHoverVisuals();
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

        dockWaveTimer = DispatcherQueue.CreateTimer();
        dockWaveTimer.Interval = TimeSpan.FromMilliseconds(16);
        dockWaveTimer.Tick += TickDockWave;

        keyboard = new WindowsKeyboardService(hwnd);

keyboard.HomeRequested +=
    async (_, _) =>
        await ToggleDockAsync();

keyboard.LauncherRequested +=
    (_, _) =>
        ShowHome();

keyboard.RecoveryRequested +=
    (_, _) =>
        RestoreTaskbar();

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
        MenuItem(menu, "Open Glass Home", ShowHome);
        menu.Items.Add(new MenuFlyoutSeparator());
        MenuItem(menu, "Restore Windows taskbar", RestoreTaskbar);
        MenuItem(menu, "Glass Material Laboratory", ShowLab);
        menu.Items.Add(new MenuFlyoutSeparator());
        MenuItem(menu, "Exit GlassDock", RequestShutdown);
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
        windowManager.Position(0, settingsSession.DisplayMode);
        displayTimer.Start();
        state.Show();
        animation.SetBottom(PeekRestBottom);
        indicator.Opacity = 1;
        ApplyMaterial(false);
        applicationService.Start();
        await StartTaskbarTestAsync(whileAppActive: true);
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
        if (previews.ContextMenuOpen || reorderDragging || reorderCommitting) return;
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
    private double CalculateTargetDockWidth() =>
        Appearance.TargetDockWidth(VisibleDockApplications.Count);

    private Button CreateApplicationButton(DockApplicationItem item)
    {
    var image = new AdaptiveAppIcon(Appearance.IconSize, Appearance.MagnificationScale);

    var running = new Border
    {
        Width = 4,
        Height = 3,
        CornerRadius = new CornerRadius(1.5),
        Background = new SolidColorBrush(Colors.White),
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
        button.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(global::Windows.UI.Color.FromArgb(32, 255, 255, 255));
        button.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(global::Windows.UI.Color.FromArgb(56, 255, 255, 255));
        button.Resources["ButtonBorderBrushPointerOver"] = new SolidColorBrush(Colors.Transparent);
        button.Resources["ButtonBorderBrushPressed"] = new SolidColorBrush(Colors.Transparent);

        ApplicationIcon? renderedIcon = null;
        void Update()
        {
            AutomationProperties.SetName(button, item.Name);
            AutomationProperties.SetItemStatus(button, item.IsActive ? "Active" : item.IsRunning ? "Running" : "Pinned");
            ToolTipService.SetToolTip(button, WindowPreviewCoordinator.CreateTooltip(item.Name));
            running.Visibility = item.IsRunning ? Visibility.Visible : Visibility.Collapsed;
            running.Opacity = item.IsActive ? 1 : 0.55;
            running.Width = item.IsActive ? 10 : 4;
            if (ReferenceEquals(renderedIcon, item.Application.Icon)) return;
            renderedIcon = item.Application.Icon;
            image.SetIcon(renderedIcon);
        }
        Update();
        item.PropertyChanged += (_, _) => Update();
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
            if (suppressClickUntil.Remove(item.Id, out var until) &&
                DateTime.UtcNow <= until)
            {
                return;
            }

            if (!applications.Activate(item))
                SetStatus($"Windows could not launch or focus {item.Name}.");
        };

        previews.Attach(button, item);
        return button;
    }

    private void BeginReorderCandidate(
        Button button,
        DockApplicationItem item,
        PointerRoutedEventArgs e)
    {
        if (!item.IsPinned ||
            state.State != DockState.Expanded ||
            previews.ContextMenuOpen ||
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

        var deltaX = point.Position.X - reorderStartX;

        if (!reorderDragging)
        {
            if (Math.Abs(deltaX) < 6)
                return;

            var pinnedItems = VisibleDockApplications
                .Where(candidate => candidate.IsPinned)
                .ToArray();

            reorderSourceIndex = Array.FindIndex(
                pinnedItems,
                candidate => candidate.Id == item.Id);

            if (reorderSourceIndex < 0)
            {
                CancelReorder();
                return;
            }

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

            reorderTargetIndex = reorderSourceIndex;
            reorderDragging = true;

            collapseDelay?.Cancel();
            CancelPillHide();
            previews.Hide();
            animation.ResetMagnification();
            HideDockWave();

            button.Opacity = 0.82;
            Canvas.SetZIndex(button, 100);
        }

        if (reorderSlotCenters.Length == 0)
            return;

        var nearestIndex = 0;
        var nearestDistance = double.MaxValue;

        for (var index = 0; index < reorderSlotCenters.Length; index++)
        {
            var distance = Math.Abs(point.Position.X - reorderSlotCenters[index]);
            if (distance >= nearestDistance)
                continue;

            nearestDistance = distance;
            nearestIndex = index;
        }

        reorderTargetIndex = Math.Clamp(
            nearestIndex,
            0,
            reorderPinnedButtons.Count - 1);

        ApplyReorderVisuals(deltaX);
        e.Handled = true;
    }

    private void ApplyReorderVisuals(double draggedDeltaX)
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
                    X = draggedDeltaX,
                    Y = -6
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
        var releasePoint = e.GetCurrentPoint(icons).Position.X;
        if (reorderSlotCenters.Length > 0)
        {
            var nearestIndex = 0;
            var nearestDistance = double.MaxValue;

            for (var index = 0; index < reorderSlotCenters.Length; index++)
            {
                var distance = Math.Abs(releasePoint - reorderSlotCenters[index]);
                if (distance >= nearestDistance)
                    continue;

                nearestDistance = distance;
                nearestIndex = index;
            }

            reorderTargetIndex = Math.Clamp(
                nearestIndex,
                0,
                reorderSlotCenters.Length - 1);
        }

        var orderedIds = VisibleDockApplications
            .Where(candidate => candidate.IsPinned)
            .Select(candidate => candidate.Id)
            .ToList();

        if (reorderSourceIndex >= 0 &&
            reorderSourceIndex < orderedIds.Count &&
            reorderTargetIndex >= 0 &&
            reorderTargetIndex < orderedIds.Count &&
            reorderSourceIndex != reorderTargetIndex)
        {
            var draggedId = orderedIds[reorderSourceIndex];
            orderedIds.RemoveAt(reorderSourceIndex);
            orderedIds.Insert(reorderTargetIndex, draggedId);

            if (applicationService.ReorderPinned(orderedIds))
            {
                // Keep the observable collection in the same order immediately.
                // That prevents the asynchronous application snapshot from
                // performing a second visible reorder a moment after the drop.
                reorderCommitting = true;
                try
                {
                    ApplyPinnedCollectionOrder(orderedIds);
                    ApplyPinnedVisualOrderSmooth(orderedIds);
                }
                finally
                {
                    reorderCommitting = false;
                }

                SetStatus("Pinned app order saved.");
            }
            else
            {
                SetStatus("GlassDock could not save the pinned app order.");
                SynchronizeItems();
            }
        }

        suppressClickUntil[item.Id] = DateTime.UtcNow.AddMilliseconds(350);
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
        reorderCandidate = null;
        reorderButton = null;
        reorderStartX = 0;
        reorderDragging = false;
        reorderSourceIndex = -1;
        reorderTargetIndex = -1;
        reorderPinnedButtons.Clear();
        reorderSlotCenters = [];
    }

    private void Entered(object sender, PointerRoutedEventArgs e)
    {
        // Moving geometry must not simulate a new physical hover.
        if (animation.IsPlacementAnimating) return;
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
        if (previews.ContextMenuOpen || state.State != DockState.Expanded) return;
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
        if (previews.ContextMenuOpen) return;
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

        if (previews.ContextMenuOpen || state.State != DockState.Expanded)
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
        if (!double.IsFinite(rootX) ||
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
        dockWaveTargetStrength = 0;
        StartDockWaveTimer();
    }

    private void TickDockWave(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        if (previews.ContextMenuOpen) { StopDockWaveTimer(clear: false); return; }
        if (closing)
        {
            StopDockWaveTimer(
                clear: true);

            return;
        }

        dockWaveTargetX =
            ClampDockWaveCenter(
                dockWaveTargetX);

        // Position responds quickly; height follows slightly slower.
        // That combination reads as a flexible surface instead of a pill
        // physically sliding over the dock.
        dockWaveCurrentX =
            Lerp(
                dockWaveCurrentX,
                dockWaveTargetX,
                0.34);

        dockWaveCurrentStrength =
            Lerp(
                dockWaveCurrentStrength,
                dockWaveTargetStrength,
                0.22);

        if (Math.Abs(
                dockWaveCurrentX -
                dockWaveTargetX) <
            0.08)
        {
            dockWaveCurrentX =
                dockWaveTargetX;
        }

        if (Math.Abs(
                dockWaveCurrentStrength -
                dockWaveTargetStrength) <
            0.003)
        {
            dockWaveCurrentStrength =
                dockWaveTargetStrength;
        }

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
            0.08;

        var strengthSettled =
            Math.Abs(
                dockWaveCurrentStrength -
                dockWaveTargetStrength) <
            0.003;

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

        // WinUI Geometry instances cannot be assigned to two Path.Data
        // properties at the same time. Give each stroke its own geometry
        // instance, built from the exact same values so they stay aligned.
        dockWaveGlow.Data =
            CreateDockWaveGeometry(
                centerX,
                dockWaveCurrentStrength);

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

    private Geometry CreateDockWaveGeometry(
        double centerX,
        double strength)
    {
        var dockWidth =
            surface.ActualWidth;

        var dockHeight =
            surface.ActualHeight;

        var left =
            (root.ActualWidth -
             dockWidth) /
            2;

        var top =
            root.ActualHeight -
            surface.Margin.Bottom -
            dockHeight;

        var right =
            left +
            dockWidth;

        var bottom =
            top +
            dockHeight;

        // True stadium / pill geometry:
        // the end radius is exactly half of the dock height.
        var radius =
            Math.Max(
                0,
                Math.Min(
                    dockHeight / 2,
                    dockWidth / 2));

        var eased =
            Math.Clamp(
                strength,
                0,
                1);

        eased =
            eased *
            eased *
            (3 - 2 * eased);

        var rise =
            DockWaveRise *
            eased;

        centerX =
            ClampDockWaveCenter(
                centerX);

        var availableTop =
            Math.Max(
                0,
                dockWidth -
                radius * 2);

        var requestedHalfWidth =
            Math.Min(
                DockWaveHalfWidth,
                Math.Max(
                    24,
                    availableTop * 0.46));

        var topStart =
            left +
            radius;

        var topEnd =
            right -
            radius;

        var leftRoom =
            Math.Max(
                0,
                centerX -
                topStart);

        var rightRoom =
            Math.Max(
                0,
                topEnd -
                centerX);

        // At an end, the crest stays where the first/last app is.
        // Instead of ending the wave early, its OUTER side becomes the
        // dock corner itself. That makes the corner and bump one curve.
        var edgeMergeThreshold =
            Math.Min(
                radius + 12,
                requestedHalfWidth * 0.68);

        var leftEdge =
            rise > 0.01 &&
            leftRoom <
            edgeMergeThreshold;

        var rightEdge =
            rise > 0.01 &&
            rightRoom <
            edgeMergeThreshold;

        if (leftEdge &&
            rightEdge)
        {
            // Very small docks cannot meaningfully merge both ends at once.
            leftEdge = false;
            rightEdge = false;
        }

        var leftSpan =
            Math.Min(
                requestedHalfWidth,
                leftRoom);

        var rightSpan =
            Math.Min(
                requestedHalfWidth,
                rightRoom);

        var waveStart =
            centerX -
            leftSpan;

        var waveEnd =
            centerX +
            rightSpan;

        const double kappa =
            0.55228475;

        // Merge a little below the normal top-right/top-left tangent.
        // The cubic reaches this point vertically, so the dock side stays
        // rounded with no visible kink.
        // Merge at the side midpoint so the bump and the capsule end
        // become one smooth continuous rounded profile.
        var cornerMergeY =
            top +
            radius;

        PathFigure figure;

        if (leftEdge)
        {
            figure =
                new PathFigure
                {
                    StartPoint =
                        new global::Windows.Foundation.Point(
                            left,
                            cornerMergeY),

                    IsClosed = true,
                    IsFilled = false
                };

            var outerDistance =
                Math.Max(
                    18,
                    centerX - left);

            // LEFT CORNER + BUMP are one continuous cubic.
            figure.Segments.Add(
                new BezierSegment
                {
                    Point1 =
                        new global::Windows.Foundation.Point(
                            left,
                            top +
                            radius * 0.08),

                    Point2 =
                        new global::Windows.Foundation.Point(
                            centerX -
                            outerDistance * 0.48,
                            top - rise),

                    Point3 =
                        new global::Windows.Foundation.Point(
                            centerX,
                            top - rise)
                });
        }
        else
        {
            figure =
                new PathFigure
                {
                    StartPoint =
                        new global::Windows.Foundation.Point(
                            left + radius,
                            top),

                    IsClosed = true,
                    IsFilled = false
                };

            if (rise > 0.01)
            {
                figure.Segments.Add(
                    new LineSegment
                    {
                        Point =
                            new global::Windows.Foundation.Point(
                                waveStart,
                                top)
                    });

                figure.Segments.Add(
                    new BezierSegment
                    {
                        Point1 =
                            new global::Windows.Foundation.Point(
                                waveStart +
                                leftSpan * 0.38,
                                top),

                        Point2 =
                            new global::Windows.Foundation.Point(
                                centerX -
                                leftSpan * 0.46,
                                top - rise),

                        Point3 =
                            new global::Windows.Foundation.Point(
                                centerX,
                                top - rise)
                    });
            }
        }

        //
        // CREST -> RIGHT SIDE
        //
        if (rise > 0.01)
        {
            if (rightEdge)
            {
                var outerDistance =
                    Math.Max(
                        18,
                        right - centerX);

                // RIGHT BUMP + CORNER are one continuous cubic.
                // Point1 keeps the crest tangent horizontal.
                // Point2/Point3 make the end tangent vertical into the side.
                figure.Segments.Add(
                    new BezierSegment
                    {
                        Point1 =
                            new global::Windows.Foundation.Point(
                                centerX +
                                outerDistance * 0.48,
                                top - rise),

                        Point2 =
                            new global::Windows.Foundation.Point(
                                right,
                                top +
                                radius * 0.18),

                        Point3 =
                            new global::Windows.Foundation.Point(
                                right,
                                cornerMergeY)
                    });
            }
            else
            {
                figure.Segments.Add(
                    new BezierSegment
                    {
                        Point1 =
                            new global::Windows.Foundation.Point(
                                centerX +
                                rightSpan * 0.46,
                                top - rise),

                        Point2 =
                            new global::Windows.Foundation.Point(
                                waveEnd -
                                rightSpan * 0.38,
                                top),

                        Point3 =
                            new global::Windows.Foundation.Point(
                                waveEnd,
                                top)
                    });

                figure.Segments.Add(
                    new LineSegment
                    {
                        Point =
                            new global::Windows.Foundation.Point(
                                right - radius,
                                top)
                    });

                // Normal top-right rounded corner when the wave is not
                // merging into this end.
                figure.Segments.Add(
                    new BezierSegment
                    {
                        Point1 =
                            new global::Windows.Foundation.Point(
                                right -
                                radius +
                                radius * kappa,
                                top),

                        Point2 =
                            new global::Windows.Foundation.Point(
                                right,
                                top +
                                radius -
                                radius * kappa),

                        Point3 =
                            new global::Windows.Foundation.Point(
                                right,
                                top + radius)
                    });
            }
        }
        else
        {
            figure.Segments.Add(
                new LineSegment
                {
                    Point =
                        new global::Windows.Foundation.Point(
                            right - radius,
                            top)
                });

            figure.Segments.Add(
                new BezierSegment
                {
                    Point1 =
                        new global::Windows.Foundation.Point(
                            right -
                            radius +
                            radius * kappa,
                            top),

                    Point2 =
                        new global::Windows.Foundation.Point(
                            right,
                            top +
                            radius -
                            radius * kappa),

                    Point3 =
                        new global::Windows.Foundation.Point(
                            right,
                            top + radius)
                });
        }

        //
        // RIGHT SIDE + BOTTOM-RIGHT
        //
        figure.Segments.Add(
            new LineSegment
            {
                Point =
                    new global::Windows.Foundation.Point(
                        right,
                        bottom - radius)
            });

        figure.Segments.Add(
            new BezierSegment
            {
                Point1 =
                    new global::Windows.Foundation.Point(
                        right,
                        bottom -
                        radius +
                        radius * kappa),

                Point2 =
                    new global::Windows.Foundation.Point(
                        right -
                        radius +
                        radius * kappa,
                        bottom),

                Point3 =
                    new global::Windows.Foundation.Point(
                        right - radius,
                        bottom)
            });

        //
        // BOTTOM + BOTTOM-LEFT
        //
        figure.Segments.Add(
            new LineSegment
            {
                Point =
                    new global::Windows.Foundation.Point(
                        left + radius,
                        bottom)
            });

        figure.Segments.Add(
            new BezierSegment
            {
                Point1 =
                    new global::Windows.Foundation.Point(
                        left +
                        radius -
                        radius * kappa,
                        bottom),

                Point2 =
                    new global::Windows.Foundation.Point(
                        left,
                        bottom -
                        radius +
                        radius * kappa),

                Point3 =
                    new global::Windows.Foundation.Point(
                        left,
                        bottom - radius)
            });

        //
        // LEFT SIDE + TOP-LEFT
        //
        if (leftEdge)
        {
            figure.Segments.Add(
                new LineSegment
                {
                    Point =
                        new global::Windows.Foundation.Point(
                            left,
                            cornerMergeY)
                });
        }
        else
        {
            figure.Segments.Add(
                new LineSegment
                {
                    Point =
                        new global::Windows.Foundation.Point(
                            left,
                            top + radius)
                });

            figure.Segments.Add(
                new BezierSegment
                {
                    Point1 =
                        new global::Windows.Foundation.Point(
                            left,
                            top +
                            radius -
                            radius * kappa),

                    Point2 =
                        new global::Windows.Foundation.Point(
                            left +
                            radius -
                            radius * kappa,
                            top),

                    Point3 =
                        new global::Windows.Foundation.Point(
                            left + radius,
                            top)
                });
        }

        var result =
            new PathGeometry();

        result.Figures.Add(
            figure);

        return result;
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
                    dockHeight / 2,
                    dockWidth / 2));

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
        if (dockWaveTimerRunning)
            return;

        dockWaveTimerRunning = true;
        dockWaveTimer.Start();
    }

    private void StopDockWaveTimer(
        bool clear)
    {
        if (dockWaveTimerRunning)
        {
            dockWaveTimerRunning = false;
            dockWaveTimer.Stop();
        }

        if (!clear)
            return;

        dockWaveCurrentStrength = 0;
        dockWaveTargetStrength = 0;
        dockWaveGlow.Opacity = 0;
        dockWaveRim.Opacity = 0;
        desktopBackdrop.ClearDockWave();
    }

    private Task ToggleDockAsync()
    {
        if (previews.ContextMenuOpen)
            return Task.CompletedTask;

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
        if (animation.IsPlacementAnimating) return;
        if (!windowManager.IsPointerInsideInput()) PointerDeparted();
    }
    private async Task CollapseDockAsync()
    {
        if (previews.HoldsDock) return;
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
        if (pillHideDelay is not null || closing || previews.HoldsDock ||
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
        if (collapseDelay is { IsCancellationRequested: false } || closing || previews.HoldsDock) return;
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

    private void ApplyMaterial(bool expanded)
    {
        var material = Appearance.ApplyTo(
            DockMaterialStylePresets.Create(Appearance.GlassMaterialMode) with
        {
            // Keep the dock's current frosted character, but round the
            // expanded shell so it visually belongs with Glass Home.
            BlurAmount = 20,
            Opacity = 0.78,
            CornerRadius = expanded ? 34 : 2.5,

            // Slightly softer depth and a restrained luminous edge.
            ShadowOpacity = expanded ? 0.24 : 0.28,
            ShadowBlur = expanded ? 26 : 20,
            ShadowOffset = expanded ? 7 : 6,
            // Expanded mode uses the continuously deformed outline below,
            // so disable GlassSurface's static rounded-rectangle rim.
            EdgeHighlight = expanded ? 0 : 0,
            BorderOpacity = expanded ? 0 : 0.18,
            BorderThickness = expanded ? Appearance.BorderThickness : 1
        },
            expanded);

        surface.Apply(material);
        desktopBackdrop.Apply(material);
        UpdateBackdropBounds();
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
            desktopBackdrop.SetDockWave(
                dockWaveCurrentX > 0
                    ? dockWaveCurrentX
                    : root.ActualWidth / 2,
                DockWaveHalfWidth,
                DockWaveRise,
                dockWaveCurrentStrength);

            UpdateDockWaveOutline();
        }
    }

    private void SettingsChanged(
        object? sender,
        GlassDockSettingsChangedEventArgs eventArgs)
    {
        ApplyBottomMargin(eventArgs.Settings.BottomMargin);
        ApplyDisplayMode(eventArgs.Settings.DockDisplayMode);
        ApplyAppearance(new DockAppearanceSettings(
            eventArgs.Settings.GlassMaterialMode,
            eventArgs.Settings.IconSize,
            eventArgs.Settings.MagnificationScale,
            eventArgs.Settings.IconSpacing,
            eventArgs.Settings.GlassBlurAmount,
            eventArgs.Settings.DockOpacity,
            eventArgs.Settings.BorderThickness,
            eventArgs.Settings.BorderOpacity));
    }

    private void ApplyAppearance(DockAppearanceSettings appearance)
    {
        icons.Spacing = appearance.IconSpacing;
        icons.Height = Math.Max(68, appearance.ButtonHeight + 24);
        animation.SetMaximumMagnificationScale(appearance.MagnificationScale);
        dockWaveRim.StrokeThickness = appearance.BorderThickness;
        if (state.State == DockState.Expanded)
            dockWaveRim.Opacity = appearance.BorderOpacity;

        foreach (var button in applicationButtons.Values)
        {
            if (button.Content is not Grid content ||
                content.Children.FirstOrDefault() is not AdaptiveAppIcon icon)
                continue;

            ApplyIconLayout(button, content, icon, appearance);
        }

        if (state.State is DockState.Expanded)
        {
            surface.Width = CalculateTargetDockWidth();
            surface.Height = ExpandedDockHeight;
            ApplyMaterial(expanded: true);
        }
        else
        {
            ApplyMaterial(expanded: false);
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
        icons.Margin = new Thickness(0, 0, 0, BottomMargin);
        if (state.State == DockState.Expanded) animation.SetBottom(BottomMargin);
        else if (state.State == DockState.Expanding) animation.AnimateBottom(BottomMargin, 180);
        windowManager.Position(0, settingsSession.DisplayMode);
        if (state.State is DockState.Idle or DockState.Hovering) UpdatePeekInput();
        else UpdateDockWaveOutline();
        UpdateBackdropBounds();
        SetStatus($"Expanded dock bottom margin: {BottomMargin:0} DIP.");
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
            _ => "on the primary display"
        };
        SetStatus($"Dock display mode: {label}.");
    }

    public void ShowSettings()
    {
        if (closing || shutdown.IsRequested || settingsWindow.IsShutdown) return;
        var window = settingsWindow.GetOrCreate(() =>
        {
            var created = new SettingsWindow(settingsSession, settingsStore, shutdown);
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
            window.Closed += (_, _) => { if (ReferenceEquals(home, window)) home = null; };
        }
        home.Toggle();
    }
    public void ShowLab()
    {
        if (closing || shutdown.IsRequested) return;
        if (lab is null)
        {
            lab = new Window { Title = "GlassDock — Glass Material Laboratory", Content = new Views.GlassLabView() };
            lab.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1320, 900));
            lab.Closed += (_, _) => lab = null;
        }
        lab.Activate();
    }

    public Task StartTaskbarTestAsync(bool whileAppActive = false)
    {
        if (closing || shutdown.IsRequested)
            return Task.CompletedTask;
        if (taskbarOperation is { IsCompleted: false })
            return taskbarOperation;

        taskbarOperation = StartTaskbarTestCoreAsync(whileAppActive);
        return taskbarOperation;
    }

    private async Task StartTaskbarTestCoreAsync(bool whileAppActive)
    {
        if (startingTest || taskbarSession is { IsActive: true }) return;
        if (!keyboard.IsRegistered) { SetStatus("Taskbar test refused: development/recovery hotkeys are unavailable."); return; }
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
        if (closing || shutdown.IsRequested || taskbarRestoreOperation is { IsCompleted: false })
            return;

        taskbarRestoreOperation = RestoreTaskbarAsync();
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

    public void RequestShutdown() => BeginShutdown(closeMainWindow: true);

    private void OnClosed(object sender, WindowEventArgs e) => BeginShutdown(closeMainWindow: false);

    private void BeginShutdown(bool closeMainWindow)
    {
        if (!shutdown.TryBegin()) return;
        closing = true;
        _ = ShutdownAsync(closeMainWindow);
    }

    private async Task ShutdownAsync(bool closeMainWindow)
    {
        // Remove every GlassDock surface immediately; native/resource cleanup follows
        // while the taskbar recovery helper is still independently protecting exit.
        if (closeMainWindow) AppWindow.Hide();
        root.ContextFlyout?.Hide();

        previews.Dispose();
        applications.Dispose();
        taskbarRevision++;
        state.Hide();
        heldTransition = false;
        collapseDelay?.Cancel();

        CancelPillHide();
        heartbeat.Stop();
        displayTimer.Stop();

        StopDockWaveTimer(clear: true);
        dockWaveTimer.Tick -= TickDockWave;

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
        if (taskbarSession is not null || startingTest) TaskbarRecovery.RestoreNow();
        animation.Stop();
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
        shutdownCompleted();
    }
}
