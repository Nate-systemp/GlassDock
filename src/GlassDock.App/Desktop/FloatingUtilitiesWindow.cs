using System.Numerics;
using GlassDock.App.Controls;
using GlassDock.App.Rendering;
using GlassDock.Core.Desktop;
using GlassDock.Core.Settings;
using GlassDock.Windows.Desktop;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Hosting;
using Windows.Graphics;

namespace GlassDock.App.Desktop;

internal sealed class FloatingUtilitiesWindow : Window
{
    // The pill is sized from its content. Fixed side padding replaces the old fixed
    // 124 px width, which left roughly 19 px of empty space on each side.
    private const double Diameter = 34, Gutter = 4, EdgeInset = 12, VerticalInset = 4, SidePadding = 12, ClockWidth = 37, SplitClockWidth = 62, FallbackContentWidth = 92;
    private const double CompactWidth = 102, ExpandedGap = 8, AnimationMilliseconds = 200;
    // Align the Utilities pill just below the dock surface centerline.
    private const double SplitVerticalOffset = 4;
    // The pill follows the same appearance selection as the dock and its popups.
    private double pillWidth = FallbackContentWidth + SidePadding * 2;
    private readonly StackPanel status = new() { Orientation = Orientation.Horizontal, Spacing = 0, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly StackPanel systemIcons = new() { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly FontIcon tray = new() { Glyph = "\uE70E", FontSize = 14, FontFamily = new FontFamily("Segoe Fluent Icons") };
    private readonly FontIcon network = new() { Glyph = "\uE701", FontSize = 14, FontFamily = new FontFamily("Segoe Fluent Icons") };
    private readonly FontIcon sound = new() { Glyph = "\uE767", FontSize = 14, FontFamily = new FontFamily("Segoe Fluent Icons") };
    private readonly FontIcon battery = new() { Glyph = "\uE83F", FontSize = 16, FontFamily = new FontFamily("Segoe Fluent Icons") };
    private readonly TextBlock clock = new() { FontSize = 12, FontFamily = new FontFamily("Segoe UI Variable Text"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, MinWidth = ClockWidth, TextAlignment = TextAlignment.Center };
    private readonly Border focus = new() { Width = 21, Height = 21, CornerRadius = new(11), Visibility = Visibility.Collapsed };
    private readonly Grid root = new();
    private readonly GlassSurface glass = new() { UseDesktopBackdrop = true, IsHitTestVisible = false, Margin = new(Gutter) };
    private readonly Border hoverHighlight = new()
    {
        IsHitTestVisible = false,
        Margin = new(Gutter),
        Height = Diameter,
        CornerRadius = new(Diameter / 2),
        Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(14, 255, 255, 255)),
        BorderBrush = new LinearGradientBrush
        {
            StartPoint = new global::Windows.Foundation.Point(0, 0),
            EndPoint = new global::Windows.Foundation.Point(0.8, 1),
            GradientStops =
            {
                new GradientStop { Color = global::Windows.UI.Color.FromArgb(50, 255, 255, 255), Offset = 0 },
                new GradientStop { Color = global::Windows.UI.Color.FromArgb(16, 255, 255, 255), Offset = 0.5 },
                new GradientStop { Color = global::Windows.UI.Color.FromArgb(32, 255, 255, 255), Offset = 1 }
            }
        },
        BorderThickness = new Thickness(0.8)
    };
    private bool hoverHighlighted;
    private readonly DesktopGlassBackdrop backdrop = new();
    private readonly PopupLiquidGlassSurface liquid;
    private readonly UtilityPopupTheme theme = new();
    private readonly InteractiveGlassWindowHost host;
    private readonly WindowsUtilitiesLauncherHost native;
    private readonly Button button = new() { Padding = new(0), Margin = new(Gutter), CornerRadius = new(Diameter / 2), BorderThickness = new(0), MinWidth = 0, MinHeight = 0,
        HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    private GlassDockSettings settings = new();
    private bool down, dragged, disposed, shown, expanded;
    // Reuse the same clipping geometry instead of allocating one on every frame.
    private readonly RectangleGeometry iconRevealClip = new();
    private double appliedGeometryWidth = double.NaN, appliedGeometryScale = double.NaN;
    private double expandFrom, expandTo, controlWidth;
    // Stable native bounds: the transparent HWND always reserves the EXPANDED
    // envelope, while only the glass's LEFT side animates inside it.
    private int inputRegionWidth = -1, inputRegionHeight = -1, inputRegionVisible = -1;
    private double SurfaceGutter => Split ? UtilitiesPressScale.Padding(ExpandedWidth) : Gutter;
    private double SplitHostWidth => FloatingUtilityPlacement.SplitHostWidth(ExpandedWidth, SurfaceGutter);
    private readonly UtilitiesPressScale pressScale = new();
    private PopupCompositionTrack? pressTrack;
    private readonly System.Diagnostics.Stopwatch expandClock = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer expandTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer holdTimer;
    private readonly UtilitiesHoldGesture hold = new();
    private bool suppressClick;
    private UtilitiesFocusGroup trayGroup = null!, quickGroup = null!, clockGroup = null!;
    private UtilitiesFocusGroup[] focusGroups = [];
    private readonly MenuFlyout layoutMenu = new();
    private MenuFlyout? regularMenu;
    public event Action<UtilitiesLayout>? LayoutRequested;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer hoverWatchTimer;
    private AppWindow? dockAnchor;
    private double anchorDockHeight, anchorBottomMargin;
    private double ExpandedWidth => Math.Max(CompactWidth, controlWidth + SplitClockWidth + SidePadding * 2 + ExpandedGap);
    private RectInt32? initialBounds;
    private bool positioned;
    private (int X, int Y) start, origin;
    private double? savedX, savedY;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer snapTimer;
    private readonly System.Diagnostics.Stopwatch snapClock = new();
    private PointInt32 snapFrom, snapTo;
    public double Scale => native.Scale;
    public bool ShortcutAvailable { get; private set; } = true;
    // The detached top bar stays visible even when an app enters fullscreen.
    // Exclusive fullscreen surfaces can still bypass the Windows work area.
    public bool FullscreenSuppressed => false;
    public bool ContainsPointer()
    {
        if (!shown || disposed || native is null) return false;
        var p = native.Cursor(); var origin = AppWindow.Position; var size = AppWindow.Size;
        if (Split)
        {
            var activeWidth = (int)Math.Ceiling((pillWidth + SurfaceGutter * 2) * Scale);
            var windowRight = origin.X + size.Width;
            return p.X >= windowRight - activeWidth && p.X < windowRight &&
                   p.Y >= origin.Y && p.Y < origin.Y + size.Height;
        }
        return p.X >= origin.X && p.Y >= origin.Y && p.X < origin.X + size.Width && p.Y < origin.Y + size.Height;
    }

    public Func<bool>? HasActivePopup { get; set; }
    public bool IsSomethingOpened => (HasActivePopup?.Invoke() == true) || (button.ContextFlyout?.IsOpen == true) || layoutMenu.IsOpen;
    public void NotifyPopupStateChanged() => UpdateAutoCollapseState();

    private void UpdateAutoCollapseState(bool forcePointerOutside = false)
    {
        // Layout is user-owned. Pointer exit restores clarity, never collapses.
        if (forcePointerOutside || !ContainsPointer()) FocusGroup(null);
    }
    // The time retains its own hit area while the system controls animate to its left.
    public bool PointerOverClock()
    {
        if (!Split || !shown || disposed || native is null) return false;
        var p = native.Cursor(); var position = AppWindow.Position; var size = AppWindow.Size;
        return FloatingUtilityPlacement.IsInsideCompactHoverArea(p.X, p.Y,
            position.X + size.Width, position.Y, size.Height,
            (int)Math.Ceiling((CompactWidth + SurfaceGutter * 2) * Scale));
    }

    public bool PointerOverWindow(AppWindow window)
    {
        if (disposed || native is null) return false;
        var p = native.Cursor(); var origin = window.Position; var size = window.Size;
        return p.X >= origin.X && p.Y >= origin.Y &&
            p.X < origin.X + size.Width && p.Y < origin.Y + size.Height;
    }

    public void BeginScreenshot() { liquid.BeginScreenshotMode(); }
    public void EndScreenshot() { liquid.EndScreenshotMode(); }
    public event Action? ToggleRequested;
    public event Action? SettingsRequested;
    public event Action? ReturnRequested;
    public event Action? TrayRequested;
    public event Action? CalendarRequested;
    public event Action? Suppressed;
    public event Action? Moved;
    public event Action<double, double>? PositionSaved;

    public FloatingUtilitiesWindow()
    {
        Title = "Doky Utilities";
        WindowBranding.Apply(this);
        AppWindow.IsShownInSwitchers = false;
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        root.Children.Add(glass);
        focus.Child = new FontIcon { Glyph = "\uE708", FontSize = 12, FontFamily = new FontFamily("Segoe Fluent Icons"), Foreground = theme.AccentText };
        focus.Background = theme.AccentFill;
        tray.Foreground = theme.Primary;
        var quickContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        quickContent.Children.Add(focus);
        foreach (var icon in new[] { network, sound, battery }) { icon.Foreground = theme.Primary; quickContent.Children.Add(icon); }
        trayGroup = new(tray); quickGroup = new(quickContent); clockGroup = new(clock);
        focusGroups = [trayGroup, quickGroup, clockGroup];
        systemIcons.Children.Add(trayGroup); systemIcons.Children.Add(quickGroup);
        status.Children.Add(systemIcons);
        clock.Foreground = theme.Primary;
        status.Children.Add(clockGroup);        // Split mode keeps the clock in view at every animation frame.
        clock.Text = DateTime.Now.ToString("h:mm tt");
        systemIcons.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        controlWidth = Math.Max(110, systemIcons.DesiredSize.Width + 28);
        ToolTipService.SetToolTip(clock, "Doky Calendar");
        root.Children.Add(hoverHighlight);
        ElementCompositionPreview.GetElementVisual(hoverHighlight).Opacity = 0;
        button.Content = status;
        button.Width = pillWidth;
        button.Height = Diameter;
        button.HorizontalAlignment = HorizontalAlignment.Center;
        button.VerticalAlignment = VerticalAlignment.Center;
        button.CornerRadius = new(Diameter / 2);
        var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        button.Background = clear;
        button.BorderBrush = clear;
        button.BorderThickness = new Thickness(0);
        button.Resources["ButtonBackground"] = clear;
        button.Resources["ButtonBackgroundPointerOver"] = clear;
        button.Resources["ButtonBackgroundPressed"] = clear;
        button.Resources["ButtonBackgroundDisabled"] = clear;
        button.Resources["ButtonBorderBrush"] = clear;
        button.Resources["ButtonBorderBrushPointerOver"] = clear;
        button.Resources["ButtonBorderBrushPressed"] = clear;
        button.Resources["ButtonBorderBrushDisabled"] = clear;
        root.Children.Add(button);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, "Open Control Center");
        Content = root;
        FitPill();
        expandTimer = DispatcherQueue.CreateTimer();
        expandTimer.Interval = TimeSpan.FromMilliseconds(16);
        expandTimer.Tick += (_, _) => TickExpansion();
        holdTimer = DispatcherQueue.CreateTimer();
        holdTimer.Interval = TimeSpan.FromMilliseconds(UtilitiesHoldGesture.DurationMilliseconds);
        holdTimer.Tick += (_, _) =>
        {
            holdTimer.Stop();
            if (Split && shown && hold.Take(Environment.TickCount64))
            {
                suppressClick = true;
                FocusGroup(null);
                ShowLayoutMenu();
            }
        };        // Watch for pointer hovering/leaving while expanded.
        hoverWatchTimer = DispatcherQueue.CreateTimer();
        hoverWatchTimer.Interval = TimeSpan.FromMilliseconds(100);
        hoverWatchTimer.Tick += (_, _) =>
        {
            if (disposed || !Split || !expanded)
            {
                hoverWatchTimer.Stop();
                return;
            }
            if (!ContainsPointer())
            {
                FocusGroup(null);
            }
            UpdateAutoCollapseState();
        };
        snapTimer = DispatcherQueue.CreateTimer();
        snapTimer.Interval = TimeSpan.FromMilliseconds(16);
        snapTimer.Tick += (_, _) =>
        {
            var progress = Math.Clamp(snapClock.Elapsed.TotalMilliseconds / 160, 0, 1);
            var eased = progress * progress * (3 - 2 * progress);
            AppWindow.Move(new((int)Math.Round(snapFrom.X + (snapTo.X - snapFrom.X) * eased),
                (int)Math.Round(snapFrom.Y + (snapTo.Y - snapFrom.Y) * eased)));
            Moved?.Invoke();
            if (progress < 1) return;
            snapTimer.Stop();
            if (settings.UtilitiesRememberPosition) PositionSaved?.Invoke(snapTo.X, snapTo.Y);
        };
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        host = new(hwnd) { EnableHostBackdropBrush = true, UseDockLayeredTransparency = true };
        host.Configure();
        native = new(hwnd);
        native.Shortcut += () => { if (!settings.UtilitiesHideFullscreen || !native.Fullscreen()) ToggleRequested?.Invoke(); };
        SystemBackdrop = backdrop;
        liquid = new(this, root, backdrop);
        liquid.UseDockOptics = true;
        root.SizeChanged += (_, _) => UpdateBounds();
        root.Loaded += (_, _) =>
        {
            if (initialBounds is { } bounds)
            {
                AppWindow.MoveAndResize(bounds);
                initialBounds = null;
            }
            UpdateBounds();
        };
        button.Click += (_, _) =>
        {
            if (dragged || suppressClick) return;
            if (!Split) { ToggleRequested?.Invoke(); return; }
            var cursor = native.Cursor();
            var group = GroupAt(new((cursor.X - AppWindow.Position.X) / Scale, (cursor.Y - AppWindow.Position.Y) / Scale));
            if (group == trayGroup) TrayRequested?.Invoke();
            else if (group == quickGroup) ToggleRequested?.Invoke();
            else if (group == clockGroup) CalendarRequested?.Invoke();
        };
        button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Pressed), true);
        button.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(Move), true);
        button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Released), true);
        button.PointerCanceled += (_, _) => { down = false; CancelHold(); FocusGroup(null); };
        button.PointerCaptureLost += (_, _) => { down = false; CancelHold(); };
        var menu = new MenuFlyout();
        void Add(string text, Action action) { var item = new MenuFlyoutItem { Text = text }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("Open Utilities", () => ToggleRequested?.Invoke());
        Add("Hidden Tray", () => TrayRequested?.Invoke());
        Add("Calendar", () => CalendarRequested?.Invoke());
        Add("Utilities Settings", () => SettingsRequested?.Invoke());
        Add("Return Utilities to Dock", () => ReturnRequested?.Invoke());
        menu.Opened += (_, _) => UpdateAutoCollapseState();
        menu.Closed += (_, _) => UpdateAutoCollapseState();
        regularMenu = menu; button.ContextFlyout = menu;
        root.KeyDown += (_, e) => { if (e.Key == global::Windows.System.VirtualKey.Escape) { CancelHold(); layoutMenu.Hide(); FocusGroup(null); e.Handled = true; } };
        Activated += (_, e) => { if (e.WindowActivationState == WindowActivationState.Deactivated) { CancelHold(); FocusGroup(null); } };
        button.PointerEntered += (_, _) =>
        {
            if (Split) { FocusGroup(GroupAtCursor()); return; }
            SetHoverHighlight(true);
            if (!Split) return;
            if (expanded)
            {
                UpdateAutoCollapseState();
            }
        };
        button.PointerExited += (_, _) =>
        {
            CancelHold(); FocusGroup(null);
            SetHoverHighlight(false);

            if (expanded)
            {
                UpdateAutoCollapseState(forcePointerOutside: true);
            }
        };
        Closed += (_, _) => { disposed = true; snapTimer.Stop(); expandTimer.Stop(); holdTimer.Stop(); hoverWatchTimer.Stop(); CancelHold(); layoutMenu.Hide(); trayGroup.Dispose(); quickGroup.Dispose(); clockGroup.Dispose(); pressTrack?.Dispose(); native.Dispose(); host.Dispose(); SystemBackdrop = null; };
    }

    private bool Split => settings.UtilitiesPlacement == UtilitiesPlacement.SplitDock;

    private UtilitiesFocusGroup? GroupAt(global::Windows.Foundation.Point point)
    {
        foreach (var group in focusGroups)
        {
            if (!expanded && group != clockGroup) continue;
            var bounds = group.TransformToVisual(root).TransformBounds(new(0, 0, group.ActualWidth, group.ActualHeight));
            // The padded group hit area is shared by click and long-press exclusion.
            bounds = new(bounds.X - 3, bounds.Y - 4, bounds.Width + 6, bounds.Height + 8);
            if (bounds.Contains(point)) return group;
        }
        return null;
    }
    private UtilitiesFocusGroup? GroupAtCursor()
    {
        var p = native.Cursor();
        return GroupAt(new((p.X - AppWindow.Position.X) / Scale, (p.Y - AppWindow.Position.Y) / Scale));
    }
    private void FocusGroup(UtilitiesFocusGroup? active)
    {
        foreach (var group in focusGroups)
            group?.SetFrosted(Split && expanded && active is not null && group != active);
    }
    private void CancelHold() { hold.Cancel(); holdTimer?.Stop(); AnimatePress(false); }
    private void AnimatePress(bool held)
    {
        if (disposed || !root.IsLoaded) return;
        var now = Environment.TickCount64;
        var animate = new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        if (!pressScale.Set(held, now, animate)) return;
        var center = new Vector3((float)(root.ActualWidth - SurfaceGutter - pillWidth / 2),
            (float)(root.ActualHeight / 2), 0);
        var frames = new PopupCompositionFrame[31];
        for (var i = 0; i < frames.Length; i++)
        {
            var scale = (float)pressScale.At(now + (long)Math.Round(pressScale.Duration * i / 30d));
            frames[i] = new(Matrix4x4.CreateTranslation(-center) * Matrix4x4.CreateScale(scale, scale, 1)
                * Matrix4x4.CreateTranslation(center), 1);
        }
        pressTrack ??= new(ElementCompositionPreview.GetElementVisual(root), animateOpacity: false);
        pressTrack.Bind();
        var duration = TimeSpan.FromMilliseconds(animate ? pressScale.Duration : 1);
        pressTrack.Start(frames, duration);
        backdrop.AnimatePresentation(frames, duration, press: true);
    }

    private void ShowLayoutMenu()
    {
        if (disposed || !Split) return;
        layoutMenu.Items.Clear();
        layoutMenu.Items.Add(new MenuFlyoutItem { Text = "Utilities Layout", IsEnabled = false });
        foreach (var value in Enum.GetValues<UtilitiesLayout>())
        {
            var item = new ToggleMenuFlyoutItem { Text = value.ToString(), IsChecked = settings.UtilitiesLayout == value };
            item.Click += (_, _) => { layoutMenu.Hide(); LayoutRequested?.Invoke(value); };
            layoutMenu.Items.Add(item);
        }
        var style = new Style(typeof(MenuFlyoutPresenter));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(12)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new AcrylicBrush
        {
            TintColor = global::Windows.UI.Color.FromArgb(255, 26, 34, 47), TintOpacity = .75,
            FallbackColor = global::Windows.UI.Color.FromArgb(255, 26, 34, 47)
        }));
        layoutMenu.MenuFlyoutPresenterStyle = style;
        layoutMenu.AreOpenCloseAnimationsEnabled = new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        layoutMenu.ShowAt(button, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
        { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Top });
    }

    public void AttachToDock(AppWindow dock, double dockWidth, double dockHeight, double bottomMargin)
    {
        dockAnchor = dock;
        // Retain dockWidth in this API for existing callers; Split positioning
        // no longer depends on the app dock's horizontal size.
        anchorDockHeight = dockHeight;
        anchorBottomMargin = bottomMargin;
    }

    private void SetExpanded(bool value)
    {
        if (!Split || disposed || expanded == value) return;
        expanded = value;
        expandFrom = pillWidth;
        expandTo = value ? ExpandedWidth : CompactWidth;
        if (value)
        {
            UpdateSplitInputRegion(true);
            hoverWatchTimer.Start();
            UpdateAutoCollapseState();
        }
        else
        {
            holdTimer.Stop();
            hoverWatchTimer.Stop();
        }
        if (Math.Abs(expandFrom - expandTo) < .5)
        {
            if (!value) UpdateSplitInputRegion(false);
            return;
        }
        expandClock.Restart();
        expandTimer.Start();
    }

    private void TickExpansion()
    {
        if (disposed) { expandTimer.Stop(); return; }
        var animate = new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        var t = animate ? Math.Clamp(expandClock.Elapsed.TotalMilliseconds / AnimationMilliseconds, 0, 1) : 1;
        // Smoothstep has zero velocity at BOTH ends, unlike the previous
        // ease-out curve which started abruptly and looked like a jump.
        var ease = FloatingUtilityPlacement.EaseSplitExpansion(t);
        // Align XAML width, glass mask and native HWND to the same physical
        // pixel boundary; fractional rounding disagreement caused shimmer.
        var nextWidth = expandFrom + (expandTo - expandFrom) * ease;
        pillWidth = Math.Round(nextWidth * Scale) / Scale;
        // The clock/button, native window AND hit-test origin never move here.
        // Only the right-aligned glass shape and the left controls are revealed.
        var reveal = Math.Clamp((pillWidth - CompactWidth) / Math.Max(1, ExpandedWidth - CompactWidth), 0, 1);
        var clip = FloatingUtilityPlacement.SplitRevealClip(controlWidth, reveal);
        var bleed = UtilitiesFocusGroup.Bleed;
        var horizontalBleed = reveal >= 1 ? bleed : 0;
        iconRevealClip.Rect = new global::Windows.Foundation.Rect(
            clip.Left - horizontalBleed, -bleed, clip.Width + horizontalBleed * 2, Diameter + bleed * 2);
        systemIcons.Opacity = reveal;
        systemIcons.IsHitTestVisible = reveal > .95;
        glass.Width = pillWidth;
        button.Width = pillWidth;
        hoverHighlight.Width = pillWidth;
        UpdateBounds();
        if (t < 1) return;
        expandTimer.Stop();
        pillWidth = expandTo;
        glass.Width = pillWidth;
        button.Width = pillWidth;
        hoverHighlight.Width = pillWidth;
        UpdateBounds();
        if (!expanded)
        {
            if (!ContainsPointer()) SetHoverHighlight(false);
            systemIcons.Opacity = expanded ? 1 : 0;
            UpdateSplitInputRegion(false);
        }
    }

    private Microsoft.UI.WindowId lastPlacedAnchorId;
    private PointInt32 lastPlacedAnchorPos;
    private SizeInt32 lastPlacedAnchorSize;
    private double lastPlacedAnchorHeight = double.NaN;
    private double lastPlacedBottomMargin = double.NaN;
    private double lastPlacedScale = double.NaN;
    private double lastPlacedHostWidth = double.NaN;
    private RectInt32 lastPlacedBounds;
    private bool hasLastPlacedBounds;

    private void PlaceNearDock()
    {
        if (!Split || dockAnchor is null || disposed || down) return;
        var scale = Scale;
        var hostWidth = SplitHostWidth;
        var anchorPos = dockAnchor.Position;
        var anchorSize = dockAnchor.Size;
        if (!hasLastPlacedBounds ||
            dockAnchor.Id != lastPlacedAnchorId ||
            anchorPos.X != lastPlacedAnchorPos.X ||
            anchorPos.Y != lastPlacedAnchorPos.Y ||
            anchorSize.Width != lastPlacedAnchorSize.Width ||
            anchorSize.Height != lastPlacedAnchorSize.Height ||
            Math.Abs(anchorDockHeight - lastPlacedAnchorHeight) > 0.01 ||
            Math.Abs(anchorBottomMargin - lastPlacedBottomMargin) > 0.01 ||
            Math.Abs(scale - lastPlacedScale) > 0.0001 ||
            Math.Abs(hostWidth - lastPlacedHostWidth) > 0.01)
        {
            // The main dock owns the bottom work-area reservation when pinned.
            // This launcher does not register a competing AppBar.
            var area = DisplayArea.GetFromWindowId(dockAnchor.Id, DisplayAreaFallback.Nearest).OuterBounds;
            // Expanded envelope is allocated ONCE: no WinUI/native resizing in
            // TickExpansion. The time pill stays on the same right-hand pixels.
            var width = (int)Math.Ceiling(hostWidth * scale);
            var height = (int)Math.Ceiling((Diameter + SurfaceGutter * 2) * scale);
            // Independent from the centered app dock: anchor the OUTER/right edge
            // to the monitor itself. Expansion grows left, keeping the clock at
            // exactly the same screen position in both states.
            var x = (int)Math.Round(FloatingUtilityPlacement.RightEdgeX(
                area.X, area.X + area.Width, width, Math.Max(0, EdgeInset + Gutter - SurfaceGutter) * scale));
            var dockSurfaceTop = anchorPos.Y + anchorSize.Height
                - anchorBottomMargin * scale - anchorDockHeight * scale;
            var y = (int)Math.Round(FloatingUtilityPlacement.AlignToDockCenterY(
                dockSurfaceTop, anchorDockHeight * scale, height, SplitVerticalOffset * scale,
                area.Y, area.Y + area.Height));
            lastPlacedBounds = new RectInt32(x, y, width, height);
            lastPlacedAnchorId = dockAnchor.Id;
            lastPlacedAnchorPos = anchorPos;
            lastPlacedAnchorSize = anchorSize;
            lastPlacedAnchorHeight = anchorDockHeight;
            lastPlacedBottomMargin = anchorBottomMargin;
            lastPlacedScale = scale;
            lastPlacedHostWidth = hostWidth;
            hasLastPlacedBounds = true;
        }
        var desired = lastPlacedBounds;
        if (AppWindow.Position.X != desired.X || AppWindow.Position.Y != desired.Y ||
            AppWindow.Size.Width != desired.Width || AppWindow.Size.Height != desired.Height)
        {
            AppWindow.MoveAndResize(desired);
            Moved?.Invoke();
        }
        UpdateBounds();
        if (!expandTimer.IsRunning) UpdateSplitInputRegion(expanded);
    }

    private void UpdateSplitInputRegion(bool showControls)
    {
        if (!Split || !shown || disposed) return;
        var width = AppWindow.Size.Width;
        var height = AppWindow.Size.Height;
        if (width <= 0 || height <= 0) return;
        var visible = showControls ? width :
            (int)Math.Ceiling((CompactWidth + SurfaceGutter * 2) * Scale);
        if (width == inputRegionWidth && height == inputRegionHeight && visible == inputRegionVisible) return;
        if (!native.SetSplitInputRegion(width, height, visible)) return;
        inputRegionWidth = width;
        inputRegionHeight = height;
        inputRegionVisible = visible;
    }

    private void ClearSplitWindowRegion()
    {
        if (inputRegionWidth < 0) return;
        native.ClearSplitInputRegion();
        inputRegionWidth = inputRegionHeight = inputRegionVisible = -1;
    }

    private void SetHoverHighlight(bool active)
    {
        if (hoverHighlighted == active || disposed) return;
        hoverHighlighted = active;
        var visual = ElementCompositionPreview.GetElementVisual(hoverHighlight);
        if (!new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        {
            visual.StopAnimation("Opacity");
            visual.Opacity = active ? 1f : 0f;
            return;
        }
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1, active ? 1f : 0f);
        animation.Duration = TimeSpan.FromMilliseconds(active ? 140 : 180);
        visual.StartAnimation("Opacity", animation);
    }

    private void AnimateHover(float scale)
    {
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(button);
        visual.CenterPoint = new((float)button.ActualWidth / 2, (float)button.ActualHeight / 2, 0);
        if (!new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        { visual.Scale = new(scale, scale, 1); return; }
        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(120);
        animation.InsertKeyFrame(1, new(scale, scale, 1));
        visual.StartAnimation("Scale", animation);
        animation.Dispose();
    }

    public void Apply(GlassDockSettings value, DockAppearanceSettings appearance)
    {
        var wasSplit = Split;
        var oldLayout = settings.UtilitiesLayout;
        var previousWidth = pillWidth;
        settings = value;
        if (wasSplit && !Split)
        {
            ClearSplitWindowRegion();
        }
        layoutMenu.Hide();
        button.ContextFlyout = Split ? null : regularMenu;
        FocusGroup(null);
        CancelHold();
        backdrop.SurfaceRightInset = Split ? SurfaceGutter : double.NaN;
        glass.Margin = button.Margin = hoverHighlight.Margin = new(SurfaceGutter);
        var mode = value.DockAppearanceMode;
        theme.Apply(mode);
        tray.Visibility = Split ? Visibility.Visible : Visibility.Collapsed;
        root.RequestedTheme = mode == DockAppearanceMode.Light ? ElementTheme.Light : ElementTheme.Dark;
        UtilityPopupStyle.ApplyDockMatched(glass, backdrop, appearance, mode, Diameter / 2);
        if (Split)
        {
            // Fix the clock to the right side of the pill while the hidden
            // system controls are revealed on its left.
            status.HorizontalAlignment = HorizontalAlignment.Right;
            button.HorizontalContentAlignment = HorizontalAlignment.Right;
            button.HorizontalAlignment = HorizontalAlignment.Right;
            glass.HorizontalAlignment = HorizontalAlignment.Right;
            hoverHighlight.HorizontalAlignment = HorizontalAlignment.Right;
            clock.Margin = new(0, 0, SidePadding + 4, 0);
            clock.MinWidth = SplitClockWidth;
            expandTimer.Stop();
            holdTimer.Stop();
            hoverWatchTimer.Stop();
            SetHoverHighlight(false);
            // Do not replay hover transitions when settings refreshes.
            expanded = value.UtilitiesLayout == UtilitiesLayout.Expanded;
            // Reserve fixed icon slots so the time never slides when icons appear.
            systemIcons.Width = controlWidth;
            systemIcons.Opacity = expanded ? 1 : 0;
            var bleed = UtilitiesFocusGroup.Bleed;
            iconRevealClip.Rect = new global::Windows.Foundation.Rect(
                expanded ? -bleed : 0, -bleed, expanded ? controlWidth + bleed * 2 : 0, Diameter + bleed * 2);
            systemIcons.Clip = iconRevealClip;
            systemIcons.IsHitTestVisible = expanded;
            pillWidth = expanded ? ExpandedWidth : CompactWidth;
            button.Width = pillWidth;
            glass.Width = pillWidth;
            hoverHighlight.Width = pillWidth;
            clock.Text = DateTime.Now.ToString("h:mm tt");
        }
        else
        {
            expandTimer.Stop();
            holdTimer.Stop();
            hoverWatchTimer.Stop();
            glass.Width = double.NaN;
            glass.HorizontalAlignment = HorizontalAlignment.Stretch;
            hoverHighlight.HorizontalAlignment = HorizontalAlignment.Center;
            button.HorizontalAlignment = HorizontalAlignment.Center;
            status.HorizontalAlignment = HorizontalAlignment.Center;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            clock.Margin = new(0);
            clock.MinWidth = ClockWidth;
            systemIcons.Width = double.NaN;
            systemIcons.Opacity = 1;
            systemIcons.Clip = null;
            systemIcons.IsHitTestVisible = true;
            button.Width = pillWidth;
            hoverHighlight.Width = pillWidth;
            FitPill();
        }
        ShortcutAvailable = native.ConfigureShortcut(value.UtilitiesShortcutEnabled, value.UtilitiesShortcutKey);
        if (!positioned || savedX != value.UtilitiesX || savedY != value.UtilitiesY)
        {
            savedX = value.UtilitiesX; savedY = value.UtilitiesY;
            var area = DisplayArea.Primary.OuterBounds;
            var defaultX = area.X + area.Width - (pillWidth + Gutter * 2 + EdgeInset) * Scale;
            var defaultY = area.Y + VerticalInset * Scale;
            var bounds = new RectInt32((int)(value.UtilitiesRememberPosition ? value.UtilitiesX ?? defaultX : defaultX),
                (int)(value.UtilitiesRememberPosition ? value.UtilitiesY ?? defaultY : defaultY),
                (int)Math.Ceiling((pillWidth + Gutter * 2) * Scale),
                (int)Math.Ceiling((Diameter + SurfaceGutter * 2) * Scale));
            // Size and position are one operation: the default WinUI window is
            // much wider than the pill and must not influence monitor selection.
            initialBounds = root.IsLoaded ? null : bounds;
            AppWindow.MoveAndResize(bounds);
            positioned = true;
        }
        // Reapply for appearance changes even when the dimensions are unchanged.
        appliedGeometryWidth = double.NaN;
        Reconcile();
        UpdateBounds();
        if (Split && wasSplit && oldLayout != value.UtilitiesLayout &&
            oldLayout != UtilitiesLayout.Hidden && value.UtilitiesLayout != UtilitiesLayout.Hidden)
        {
            var target = expanded;
            expanded = !target;
            pillWidth = button.Width = glass.Width = previousWidth;
            SetExpanded(target);
        }
        if (Split && expanded && shown) hoverWatchTimer.Start();
    }

    public void Reconcile()
    {
        if (disposed || !positioned) return;
        var visible = settings.UtilitiesPlacement is UtilitiesPlacement.FloatingButton or UtilitiesPlacement.SplitDock;
        visible &= !Split || settings.UtilitiesLayout != UtilitiesLayout.Hidden;
        if (!visible)
        {
            // Always release the reserved work area when moving Utilities back
            // into the dock or switching to keyboard-shortcut-only mode.
            native.ClearTopBar();
            ClearSplitWindowRegion();
            var wasShown = shown;
            shown = false;
            if (wasShown || AppWindow.IsVisible)
            {
                AppWindow.Hide();
                if (wasShown) Suppressed?.Invoke();
            }
            return;
        }

        if (Split)
        {
            native.ClearTopBar();
            if (!shown) { AppWindow.Show(false); shown = true; }
            PlaceNearDock();
            return;
        }

        ClearSplitWindowRegion();
        // Reserve a thin strip across the physical monitor. There is NO
        // full-width visual host or hit-test surface behind the actual pill.
        var barHeight = (int)Math.Ceiling((Diameter + SurfaceGutter * 2 + VerticalInset * 2) * Scale);
        native.ReserveTopBar(barHeight);
        if (!down && !snapTimer.IsRunning) Clamp(false);
        if (!shown) { AppWindow.Show(false); shown = true; }
    }

    // Returns true when the content width changed (for example when the focus icon appears).
    private bool FitPill()
    {
        status.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var content = status.DesiredSize.Width;
        var width = Math.Ceiling((content > 40 ? content : FallbackContentWidth) + SidePadding * 2);
        if (Math.Abs(width - pillWidth) < .5) return false;
        pillWidth = width;
        button.Width = width;
        hoverHighlight.Width = width;
        return true;
    }

    private void UpdateBounds()
    {
        var scale = Scale;
        if (Math.Abs(appliedGeometryWidth - pillWidth) < .01 &&
            Math.Abs(appliedGeometryScale - scale) < .0001) return;
        appliedGeometryWidth = pillWidth;
        appliedGeometryScale = scale;
        backdrop.SetBounds(Split ? SplitHostWidth : pillWidth + Gutter * 2,
            Diameter + SurfaceGutter * 2, pillWidth, Diameter, SurfaceGutter, scale);
    }

    private WindowsSystemSnapshot? lastAppliedSnapshot;
    private string? lastFocusTip;

    public void RefreshClock()
    {
        if (disposed || !shown) return;
        var text = Split ? DateTime.Now.ToString("h:mm tt") : DateTime.Now.ToString("H:mm");
        if (!string.Equals(clock.Text, text, StringComparison.Ordinal))
            clock.Text = text;
    }

    public void RefreshStatus(WindowsSystemSnapshot snapshot)
    {
        if (disposed) return;
        var text = Split ? DateTime.Now.ToString("h:mm tt") : DateTime.Now.ToString("H:mm");
        if (!string.Equals(clock.Text, text, StringComparison.Ordinal))
            clock.Text = text;

        if (lastAppliedSnapshot != snapshot)
        {
            lastAppliedSnapshot = snapshot;
            if (Math.Abs(network.Opacity - 1) > 0.001) network.Opacity = 1;
            var glyph = snapshot.Muted ? "\uE74F" : "\uE767";
            if (!string.Equals(sound.Glyph, glyph, StringComparison.Ordinal))
                sound.Glyph = glyph;
            ToolTipService.SetToolTip(network, snapshot.NetworkName);
            ToolTipService.SetToolTip(sound, snapshot.Muted ? "Muted" : $"Volume {snapshot.VolumePercent}%");
            ToolTipService.SetToolTip(battery, snapshot.HasBattery ? $"Battery {snapshot.BatteryPercent}%" : "AC power");
        }
        if (lastFocusTip is null)
        {
            lastFocusTip = "Focus session active";
            ToolTipService.SetToolTip(focus, lastFocusTip);
        }

        if (!shown) return;

        if (Split)
        {
            // The focus icon may appear/disappear. Preserve the fixed HWND
            // and the clock position regardless of the visible controls.
            if (focus.Visibility != Visibility.Collapsed)
                focus.Visibility = Visibility.Collapsed;
            if (positioned && !down && !expandTimer.IsRunning) PlaceNearDock();
            return;
        }

        // Focus-session state is supported; do not invent a DND state from it.
        var nextFocusVisibility = SystemRadioControls.FocusStatus() == "On" ? Visibility.Visible : Visibility.Collapsed;
        if (focus.Visibility != nextFocusVisibility)
            focus.Visibility = nextFocusVisibility;

        if (!FitPill() || !positioned) return;
        UpdateBounds();
        if (!down && !snapTimer.IsRunning) Clamp(false);
    }

    private void Clamp(bool snap)
    {
        if (disposed) return;
        if (Split) { PlaceNearDock(); return; }
        var size = (int)Math.Ceiling((Diameter + SurfaceGutter * 2) * Scale);
        var width = (int)Math.Ceiling((pillWidth + Gutter * 2) * Scale);
        // Use the monitor / reserved AppBar strip, NEVER DisplayArea.WorkArea:
        // WorkArea now begins below the new strip and would push the pill down
        // over the foreground application's titlebar.
        var (left, top, right, bottom) = native.TopBarReserved
            ? native.TopBarBounds
            : native.MonitorBounds();
        if (right <= left || bottom <= top)
            (left, top, right, bottom) = native.MonitorBounds();
        if (right <= left || bottom <= top) return;
        if (!native.TopBarReserved)
            bottom = Math.Min(bottom, top + (int)Math.Ceiling((Diameter + SurfaceGutter * 2 + VerticalInset * 2) * Scale));
        var point = FloatingUtilityPlacement.ClampToTopStrip(
            AppWindow.Position.X, left, top, right, bottom, width, size, EdgeInset * Scale, snap);
        var rect = new RectInt32((int)Math.Round(point.X), (int)Math.Round(point.Y), width, size);
        if (snap && new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        {
            snapFrom = AppWindow.Position;
            snapTo = new(rect.X, rect.Y);
            snapClock.Restart(); snapTimer.Start();
            return;
        }
        if (AppWindow.Position.X != rect.X || AppWindow.Position.Y != rect.Y || AppWindow.Size.Width != width || AppWindow.Size.Height != size)
        { AppWindow.MoveAndResize(rect); UpdateBounds(); Moved?.Invoke(); }
        if (snap && settings.UtilitiesRememberPosition) PositionSaved?.Invoke(rect.X, rect.Y);
    }

    private void Pressed(object sender, PointerRoutedEventArgs e)
    {
        // Split Dock is a fixed far-right utility. Dragging is still
        // supported in the separate top-strip Floating Button mode.
        if (e.GetCurrentPoint(button).Properties.PointerUpdateKind != Microsoft.UI.Input.PointerUpdateKind.LeftButtonPressed)
        { CancelHold(); return; }
        suppressClick = false;
        if (Split)
        {
            var mouse = native.Cursor();
            var point = new global::Windows.Foundation.Point((mouse.X - AppWindow.Position.X) / Scale, (mouse.Y - AppWindow.Position.Y) / Scale);
            var empty = GroupAt(point) is null;
            hold.Begin(empty, point.X, point.Y, Environment.TickCount64);
            if (empty) { suppressClick = true; button.CapturePointer(e.Pointer); AnimatePress(true); holdTimer.Start(); e.Handled = true; }
            return;
        }
        down = true; dragged = false;
        snapTimer.Stop();
        start = native.Cursor(); origin = (AppWindow.Position.X, AppWindow.Position.Y);
        button.CapturePointer(e.Pointer);
    }
    private void Move(object sender, PointerRoutedEventArgs e)
    {
        if (Split)
        {
            var mouse = native.Cursor();
            var point = new global::Windows.Foundation.Point((mouse.X - AppWindow.Position.X) / Scale, (mouse.Y - AppWindow.Position.Y) / Scale);
            var group = GroupAt(point);
            FocusGroup(group);
            hold.Move(point.X, point.Y, group is null);
            if (!hold.Pending) CancelHold();
            return;
        }
        if (!down) return;
        var cursor = native.Cursor();
        var dx = cursor.X - start.X; var dy = cursor.Y - start.Y;
        if (!dragged && !FloatingUtilityPlacement.IsDrag(dx, dy, Scale)) return;
        dragged = true;
        AppWindow.Move(new(origin.X + dx, origin.Y + dy));
        Moved?.Invoke();
        e.Handled = true;
    }
    private void Released(object sender, PointerRoutedEventArgs e)
    {
        if (Split) { CancelHold(); button.ReleasePointerCapture(e.Pointer); if (suppressClick) e.Handled = true; return; }
        if (!down || e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return;
        down = false;
        button.ReleasePointerCapture(e.Pointer);
        if (!dragged) return;
        e.Handled = true;
        Clamp(settings.UtilitiesSnapEdges);
        if (!settings.UtilitiesSnapEdges && settings.UtilitiesRememberPosition) PositionSaved?.Invoke(AppWindow.Position.X, AppWindow.Position.Y);
    }
}
