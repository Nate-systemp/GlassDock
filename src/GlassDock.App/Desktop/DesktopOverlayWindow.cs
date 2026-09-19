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
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer dockWaveTimer;
    private CancellationTokenSource? collapseDelay;
    private TaskbarDevelopmentSession? taskbarSession;
    private DevelopmentWindow? controls;
    private GlassHomeWindow? home;
    private Window? lab;
    private bool menuOpen;
    private bool closing;
    private bool startingTest;
    private int taskbarRevision;

    private bool dockWaveTimerRunning;
    private double dockWaveTargetX;
    private double dockWaveCurrentX;
    private double dockWaveTargetStrength;
    private double dockWaveCurrentStrength;

    private const double DockWaveHalfWidth = 92;
    private const double DockWaveRise = 12;

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
        previews = new(applications, root, hwnd, () => root.ActualHeight - BottomMargin - 68);
        previews.HoldChanged += (_, _) => { if (previews.HoldsDock) collapseDelay?.Cancel(); else ScheduleCollapse(); };
        previews.ActionFailed += (_, message) => SetStatus(message);
        AppWindow.Changed += (_, _) => previews.Reposition();
        applications.VisibleDockApplications.CollectionChanged += (_, _) => SynchronizeItems();
        applications.WarningChanged += (_, _) => { if (applications.Warning is { } warning) SetStatus(warning); };
        animation = new DockAnimationController(surface, icons, indicator);

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
    const double spacing = 6;
    const double horizontalPadding = 36;

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
        button.Click += (_, _) =>
        {
            if (!applications.Activate(item)) SetStatus($"Windows could not launch or focus {item.Name}.");
        };
        previews.Attach(button, item);
        return button;
    }

    private async void Entered(object sender, PointerRoutedEventArgs e)
    {
        DockAnimationController.Trace($"PointerEntered state={state.State}");

        // Capture the pointer before expansion so a stationary mouse already
        // influences the closest icon as soon as the dock finishes opening.
        var pointerX =
            e.GetCurrentPoint(icons).Position.X;

        var rootX =
            e.GetCurrentPoint(root).Position.X;

        await ExpandDockAsync();

        if (state.State == DockState.Expanded)
        {
            animation.UpdateMagnification(pointerX);
            ShowDockWave(rootX);
        }
    }

    private void Moved(object sender, PointerRoutedEventArgs e)
    {
        collapseDelay?.Cancel();

        if (state.State != DockState.Expanded)
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
            BottomMargin -
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
        UpdateDockWaveOutline();
        ApplyMaterial(true);
        var targetWidth = CalculateTargetDockWidth();
        if (await animation.AnimateAsync(true, targetWidth))
        {
            state.Complete(revision);
            icons.IsHitTestVisible = state.State == DockState.Expanded;

            if (state.State == DockState.Expanded)
            {
                dockWaveGlow.Opacity = 0.22;
                dockWaveRim.Opacity = 0.78;
                UpdateDockWaveOutline();
            }
        }
    }

    private void Exited(object sender, PointerRoutedEventArgs e)
    {
        DockAnimationController.Trace($"PointerExited state={state.State}");

        animation.ResetMagnification();
        HideDockWave();
        ScheduleCollapse();
    }

    private async Task CollapseDockAsync()
    {
        previews.Hide();
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
            BorderThickness = 1
        };

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
            BottomMargin,
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

    public void SetBottomMargin(double margin)
    {
        BottomMargin = Math.Clamp(double.IsFinite(margin) ? margin : 24, 16, 100);
        surface.Margin = indicator.Margin = icons.Margin = new Thickness(0, 0, 0, BottomMargin);
        windowManager.Position(0);
        if (state.State == DockState.Idle) windowManager.SetInteractionRegion(false, BottomMargin);
        else UpdateDockWaveOutline();
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
        if (closing) return;
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

        StopDockWaveTimer(clear: true);
        dockWaveTimer.Tick -= TickDockWave;

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
