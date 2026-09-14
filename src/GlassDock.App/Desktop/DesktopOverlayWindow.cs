using System.Numerics;
using System.Collections.ObjectModel;
using GlassDock.App.ViewModels;
using GlassDock.Core.Applications;
using GlassDock.Windows.Applications;
using GlassDock.App.Controls;
using GlassDock.App.Rendering;
using GlassDock.Core.Desktop;
using GlassDock.Core.Materials;
using GlassDock.Windows.Desktop;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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
    private readonly DockStateMachine state = new();
    private readonly IApplicationService applicationService = new WindowsApplicationService();
    private readonly DockApplicationsViewModel applications;
    private readonly WindowPreviewCoordinator previews;
    private readonly Dictionary<string, Button> applicationButtons = new(StringComparer.Ordinal);
    public ObservableCollection<DockApplicationItem> VisibleDockApplications => applications.VisibleDockApplications;
    private readonly DesktopGlassBackdrop desktopBackdrop = new();
    private readonly WindowsOverlayManager windowManager;
    private readonly WindowsKeyboardService keyboard;
    private readonly DockAnimationController animation;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer heartbeat;
    private CancellationTokenSource? collapseDelay;
    private TaskbarDevelopmentSession? taskbarSession;
    private DevelopmentWindow? controls;
    private Window? home;
    private Window? lab;
    private bool menuOpen;
    private bool closing;
    private bool startingTest;
    private int taskbarRevision;
    public double BottomMargin { get; private set; } = 24;
    public string Status { get; private set; } = "Starting desktop recovery protection.";
    public string RenderingMode => desktopBackdrop.RenderingMode;
    public bool HotkeysAvailable => keyboard.IsRegistered;
    public event EventHandler? StatusChanged;

    public DesktopOverlayWindow(bool inspection = false)
    {
        Title = "GlassDock — Floating Dock";
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
        root.Children.Add(indicator);
        root.Children.Add(icons);
        surface.RegisterPropertyChangedCallback(UIElement.OpacityProperty, (_, _) => UpdateBackdropBounds());
        surface.SizeChanged += (_, _) => UpdateBackdropBounds();
        root.SizeChanged += (_, _) => UpdateBackdropBounds();
        desktopBackdrop.RenderingModeChanged += (_, _) => { UpdateBackdropBounds(); StatusChanged?.Invoke(this, EventArgs.Empty); };
        applications = new DockApplicationsViewModel(applicationService, DispatcherQueue);
        previews = new(applications, root, hwnd, () => root.ActualHeight - BottomMargin - 68);
        previews.HoldChanged += (_, _) => { if (previews.HoldsDock) collapseDelay?.Cancel(); else ScheduleCollapse(); };
        previews.ActionFailed += (_, message) => SetStatus(message);
        AppWindow.Changed += (_, _) => previews.Reposition();
        applications.VisibleDockApplications.CollectionChanged += (_, _) => SynchronizeItems();
        applications.WarningChanged += (_, _) => { if (applications.Warning is { } warning) SetStatus(warning); };
        animation = new DockAnimationController(surface, icons, indicator);
        keyboard = new WindowsKeyboardService(hwnd);
        keyboard.HomeRequested += async (_, _) => await ToggleDockAsync();
        keyboard.RecoveryRequested += (_, _) => RestoreTaskbar();
        root.PointerEntered += Entered;
        root.PointerMoved += (_, _) => collapseDelay?.Cancel();
        root.PointerExited += Exited;
        root.Loaded += async (_, _) =>
        {
            windowManager.Position(0);
            windowManager.SetInteractionRegion(false);
            state.Show();
            ApplyMaterial(false);
            applicationService.Start();
            await StartTaskbarTestAsync(whileAppActive: true);
        };
        surface.RenderingModeChanged += (_, _) => StatusChanged?.Invoke(this, EventArgs.Empty);
        var menu = new MenuFlyout();
        MenuItem(menu, "Development controls", ShowControls);
        MenuItem(menu, "Restore Windows taskbar", RestoreTaskbar);
        MenuItem(menu, "Glass Material Laboratory", ShowLab);
        MenuItem(menu, "Exit GlassDock", Close);
        menu.Opening += (_, _) => { menuOpen = true; collapseDelay?.Cancel(); };
        menu.Closed += (_, _) => { menuOpen = false; ScheduleCollapse(); };
        root.ContextFlyout = menu;
        heartbeat = DispatcherQueue.CreateTimer();
        heartbeat.Interval = TimeSpan.FromSeconds(1);
        heartbeat.Tick += async (_, _) =>
        {
            if (taskbarSession is { IsActive: true } session) await session.HeartbeatAsync();
        };
        Closed += OnClosed;
        windowManager.Position(0);
        windowManager.SetInteractionRegion(false);
    }

    private static void MenuItem(MenuFlyout menu, string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private void SynchronizeItems()
    {
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

    if (count == 0)
        return 100;

    const double buttonWidth = 40;
    const double spacing = 4;
    const double horizontalPadding = 28;

    var target =
        count * buttonWidth +
        (count - 1) * spacing +
        horizontalPadding;

    return Math.Clamp(target, 100, 560);
}

private Button CreateApplicationButton(DockApplicationItem item)
{
    var image = new AdaptiveAppIcon(size: 28, maximumHoverScale: 1.24);

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

    var content = new Grid
    {
        Width = 40,
        Height = 44
    };

    content.Children.Add(image);
    content.Children.Add(running);

    var button = new Button
    {
        Width = 40,
        Height = 44,
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
        button.PointerEntered += (_, _) =>
        {
            Canvas.SetZIndex(button, 10);
            ScaleItem(button, 1.24f);
        };
        button.PointerExited += (_, _) =>
        {
            Canvas.SetZIndex(button, 0);
            ScaleItem(button, 1.0f);
        };
        button.Click += (_, _) =>
        {
            if (!applications.Activate(item)) SetStatus($"Windows could not launch or focus {item.Name}.");
        };
        previews.Attach(button, item);
        return button;
    }

    private static void ScaleItem(FrameworkElement item, float scale)
    {
        var visual = ElementCompositionPreview.GetElementVisual(item);
        visual.CenterPoint = new Vector3((float)item.ActualWidth / 2, (float)item.ActualHeight * 0.88f, 0);
        if (!new UISettings().AnimationsEnabled) { visual.Scale = new Vector3(scale, scale, 1); return; }
        var compositor = visual.Compositor;
        using var effect = compositor.CreateVector3KeyFrameAnimation();
        var easing = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.16f, 1.0f),
            new Vector2(0.30f, 1.0f));
        effect.InsertKeyFrame(1f, new Vector3(scale, scale, 1), easing);
        effect.Duration = TimeSpan.FromMilliseconds(180);
        visual.StartAnimation("Scale", effect);
    }

    private async void Entered(object sender, PointerRoutedEventArgs e)
    {
        DockAnimationController.Trace($"PointerEntered state={state.State}");
        await ExpandDockAsync();
    }

    private Task ToggleDockAsync() => state.State is DockState.Expanded or DockState.Expanding or DockState.Hovering
        ? CollapseDockAsync()
        : ExpandDockAsync();

    private async Task ExpandDockAsync()
    {
        if (closing || state.State == DockState.Hidden) return;
        collapseDelay?.Cancel();
        if (state.State is DockState.Expanded or DockState.Expanding) return;
        state.Enter();
        var revision = state.Expand();
        windowManager.SetInteractionRegion(true);
        ApplyMaterial(true);
        var targetWidth = CalculateTargetDockWidth();
        if (await animation.AnimateAsync(true, targetWidth))
        {
            state.Complete(revision);
            icons.IsHitTestVisible = state.State == DockState.Expanded;
        }
    }

    private void Exited(object sender, PointerRoutedEventArgs e)
    {
        DockAnimationController.Trace($"PointerExited state={state.State}");
        ScheduleCollapse();
    }

    private async Task CollapseDockAsync()
    {
        previews.Hide();
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
                windowManager.SetInteractionRegion(false, BottomMargin);
            }
        }
    }

    private async void ScheduleCollapse()
    {
        collapseDelay?.Cancel();
        var delay = new CancellationTokenSource();
        collapseDelay = delay;
        try
        {
            await Task.Delay(650, delay.Token);
            if (menuOpen || closing || previews.HoldsDock) return;
            DockAnimationController.Trace($"Collapse delay elapsed state={state.State}");
            await CollapseDockAsync();
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(collapseDelay, delay)) collapseDelay = null; delay.Dispose(); }
    }

    private void ApplyMaterial(bool expanded)
    {
        var material = GlassMaterialPresets.Create(GlassMaterialPreset.Frosted) with
        {
            BlurAmount = 20, Opacity = 0.78, CornerRadius = 12,
            ShadowOpacity = 0.28, ShadowBlur = 20,
            ShadowOffset = 6, EdgeHighlight = 0,
            BorderOpacity = 0.18
        };
        surface.Apply(material);
        desktopBackdrop.Apply(material);
        UpdateBackdropBounds();
    }

    private void UpdateBackdropBounds() => desktopBackdrop.SetBounds(root.ActualWidth, root.ActualHeight,
        surface.ActualWidth, surface.ActualHeight, BottomMargin, root.XamlRoot?.RasterizationScale ?? 1, surface.Opacity);

    public void SetBottomMargin(double margin)
    {
        BottomMargin = Math.Clamp(double.IsFinite(margin) ? margin : 24, 16, 100);
        surface.Margin = indicator.Margin = icons.Margin = new Thickness(0, 0, 0, BottomMargin);
        windowManager.Position(0);
        windowManager.SetInteractionRegion(state.State != DockState.Idle, BottomMargin);
        UpdateBackdropBounds();
        SetStatus($"Indicator bottom margin: {BottomMargin:0} DIP. Primary-monitor desktop bounds.");
        previews.Reposition();
    }

    public void ShowControls()
    {
        if (controls is null)
        {
            controls = new DevelopmentWindow(this);
            controls.Closed += (_, _) => controls = null;
        }
        controls.Activate();
    }

    public void ShowHome()
    {
        if (home is null)
        {
            home = new Window
            {
                Title = "GlassDock — Glass Home integration placeholder",
                Content = new StackPanel
                {
                    Padding = new Thickness(32), Spacing = 16,
                    Children =
                    {
                        new TextBlock { Text = "Glass Home", FontSize = 28 },
                        new TextBlock { Text = "Development event received.\nThe launcher is not implemented.\nBare Windows key remains handled by Windows.", TextWrapping = TextWrapping.Wrap }
                    }
                }
            };
            home.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(520, 260));
            home.Closed += (_, _) => home = null;
        }
        home.Activate();
    }

    public void ShowLab()
    {
        if (lab is null)
        {
            lab = new Window { Title = "GlassDock — Glass Material Laboratory", Content = new Views.GlassLabView() };
            lab.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1320, 900));
            lab.Closed += (_, _) => lab = null;
        }
        lab.Activate();
    }

    public async Task StartTaskbarTestAsync(bool whileAppActive = false)
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
                windowManager.Position(0);
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

    public async void RestoreTaskbar()
    {
        taskbarRevision++;
        heartbeat.Stop();
        var restored = TaskbarRecovery.RestoreNow();
        if (taskbarSession is not null)
        {
            await taskbarSession.DisposeAsync();
            taskbarSession = null;
        }
        SetStatus(restored ? "Windows taskbar restored." : "Restoration not verified; use the independent recovery command.");
    }

    private void SetStatus(string value) { Status = value; StatusChanged?.Invoke(this, EventArgs.Empty); }

    private async void OnClosed(object sender, WindowEventArgs e)
    {
        closing = true;
        previews.Dispose();
        applications.Dispose();
        taskbarRevision++;
        state.Hide();
        collapseDelay?.Cancel();
        heartbeat.Stop();
        // Restore synchronously before closing the last XAML window can end the process.
        if (taskbarSession is not null || startingTest) TaskbarRecovery.RestoreNow();
        animation.Stop();
        keyboard.Dispose();
        windowManager.Dispose();
        controls?.Close();
        home?.Close();
        lab?.Close();
        if (taskbarSession is not null) await taskbarSession.DisposeAsync();
    }
}
