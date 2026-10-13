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
using Windows.Graphics;

namespace GlassDock.App.Desktop;

internal sealed class FloatingUtilitiesWindow : Window
{
    // The pill is sized from its content. Fixed side padding replaces the old fixed
    // 124 px width, which left roughly 19 px of empty space on each side.
    private const double Diameter = 34, Gutter = 4, EdgeInset = 12, VerticalInset = 4, SidePadding = 12, ClockWidth = 37, FallbackContentWidth = 92;
    // The floating pill is always Clear liquid glass, whatever the dock uses.
    private const DockAppearanceMode PillMode = DockAppearanceMode.Clear;
    private double pillWidth = FallbackContentWidth + SidePadding * 2;
    private readonly StackPanel status = new() { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly FontIcon network = new() { Glyph = "\uE701", FontSize = 14, FontFamily = new FontFamily("Segoe Fluent Icons") };
    private readonly FontIcon sound = new() { Glyph = "\uE767", FontSize = 14, FontFamily = new FontFamily("Segoe Fluent Icons") };
    private readonly FontIcon battery = new() { Glyph = "\uE83F", FontSize = 16, FontFamily = new FontFamily("Segoe Fluent Icons") };
    private readonly TextBlock clock = new() { FontSize = 12, FontFamily = new FontFamily("Segoe UI Variable Text"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, MinWidth = ClockWidth, TextAlignment = TextAlignment.Center };
    private readonly Border focus = new() { Width = 21, Height = 21, CornerRadius = new(11), Visibility = Visibility.Collapsed };
    private readonly Grid root = new();
    private readonly GlassSurface glass = new() { UseDesktopBackdrop = true, IsHitTestVisible = false, Margin = new(Gutter) };
    private readonly DesktopGlassBackdrop backdrop = new();
    private readonly PopupLiquidGlassSurface liquid;
    private readonly UtilityPopupTheme theme = new();
    private readonly InteractiveGlassWindowHost host;
    private readonly WindowsUtilitiesLauncherHost native;
    private readonly Button button = new() { Padding = new(0), Margin = new(Gutter), CornerRadius = new(24), BorderThickness = new(.6), MinWidth = 0, MinHeight = 0,
        HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    private GlassDockSettings settings = new();
    private bool down, dragged, disposed, shown;
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
        if (!shown) return false;
        var p = native.Cursor(); var origin = AppWindow.Position; var size = AppWindow.Size;
        return p.X >= origin.X && p.Y >= origin.Y && p.X < origin.X + size.Width && p.Y < origin.Y + size.Height;
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
        status.Children.Add(focus);
        foreach (var icon in new[] { network, sound, battery }) { icon.Foreground = theme.Primary; status.Children.Add(icon); }
        clock.Foreground = theme.Primary;
        status.Children.Add(clock);
        button.Content = status;
        button.CornerRadius = new(Diameter / 2);
        button.Background = theme.Overlay;
        button.BorderBrush = theme.TileBorder;
        theme.StyleButton(button);
        button.Width = pillWidth;
        button.Height = Diameter;
        button.HorizontalAlignment = HorizontalAlignment.Center;
        button.VerticalAlignment = VerticalAlignment.Center;
        button.CornerRadius = new(Diameter / 2);
        button.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        button.BorderThickness = new(0);
        root.Children.Add(button);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, "Open Control Center");
        Content = root;
        FitPill();
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
        button.Click += (_, _) => { if (!dragged) ToggleRequested?.Invoke(); };
        button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Pressed), true);
        button.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(Move), true);
        button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Released), true);
        button.PointerCanceled += (_, _) => down = false;
        button.PointerCaptureLost += (_, _) => down = false;
        var menu = new MenuFlyout();
        void Add(string text, Action action) { var item = new MenuFlyoutItem { Text = text }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("Open Utilities", () => ToggleRequested?.Invoke());
        Add("Hidden Tray", () => TrayRequested?.Invoke());
        Add("Calendar", () => CalendarRequested?.Invoke());
        Add("Utilities Settings", () => SettingsRequested?.Invoke());
        Add("Return Utilities to Dock", () => ReturnRequested?.Invoke());
        button.ContextFlyout = menu;
        button.PointerEntered += (_, _) => AnimateHover(1.04f);
        button.PointerExited += (_, _) => AnimateHover(1);
        Closed += (_, _) => { disposed = true; snapTimer.Stop(); native.Dispose(); host.Dispose(); SystemBackdrop = null; };
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
        settings = value;
        theme.Apply(PillMode);
        root.RequestedTheme = ElementTheme.Dark;
        UtilityPopupStyle.ApplyDockMatched(glass, backdrop, appearance, PillMode, Diameter / 2);
        FitPill();
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
                (int)Math.Ceiling((Diameter + Gutter * 2) * Scale));
            // Size and position are one operation: the default WinUI window is
            // much wider than the pill and must not influence monitor selection.
            initialBounds = root.IsLoaded ? null : bounds;
            AppWindow.MoveAndResize(bounds);
            positioned = true;
        }
        Reconcile();
    }

    public void Reconcile()
    {
        if (disposed || !positioned) return;
        var visible = settings.UtilitiesPlacement == UtilitiesPlacement.FloatingButton;
        if (!visible)
        {
            // Always release the reserved work area when moving Utilities back
            // into the dock or switching to keyboard-shortcut-only mode.
            native.ClearTopBar();
            if (shown) { AppWindow.Hide(); shown = false; Suppressed?.Invoke(); }
            return;
        }

        // Reserve a thin strip across the physical monitor. There is NO
        // full-width visual host or hit-test surface behind the actual pill.
        var barHeight = (int)Math.Ceiling((Diameter + Gutter * 2 + VerticalInset * 2) * Scale);
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
        return true;
    }

    private void UpdateBounds()
    {
        backdrop.SetBounds(pillWidth + Gutter * 2, Diameter + Gutter * 2, pillWidth, Diameter, Gutter, Scale);
    }

    public void RefreshStatus(WindowsSystemSnapshot snapshot)
    {
        clock.Text = DateTime.Now.ToString("H:mm");
        network.Opacity = snapshot.NetworkAvailable ? 1 : .45;
        sound.Glyph = snapshot.Muted ? "\uE74F" : "\uE767";
        ToolTipService.SetToolTip(network, snapshot.NetworkName);
        ToolTipService.SetToolTip(sound, snapshot.Muted ? "Muted" : $"Volume {snapshot.VolumePercent}%");
        ToolTipService.SetToolTip(battery, snapshot.HasBattery ? $"Battery {snapshot.BatteryPercent}%" : "AC power");
        // Focus-session state is supported; do not invent a DND state from it.
        focus.Visibility = SystemRadioControls.FocusStatus() == "On" ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(focus, "Focus session active");
        if (!FitPill() || disposed || !positioned) return;
        UpdateBounds();
        if (!down && !snapTimer.IsRunning) Clamp(false);
    }

    private void Clamp(bool snap)
    {
        if (disposed) return;
        var size = (int)Math.Ceiling((Diameter + Gutter * 2) * Scale);
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
            bottom = Math.Min(bottom, top + (int)Math.Ceiling((Diameter + Gutter * 2 + VerticalInset * 2) * Scale));
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
        if (!e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return;
        down = true; dragged = false;
        snapTimer.Stop();
        start = native.Cursor(); origin = (AppWindow.Position.X, AppWindow.Position.Y);
        button.CapturePointer(e.Pointer);
    }
    private void Move(object sender, PointerRoutedEventArgs e)
    {
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
        if (!down || e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return;
        down = false;
        button.ReleasePointerCapture(e.Pointer);
        if (!dragged) return;
        e.Handled = true;
        Clamp(settings.UtilitiesSnapEdges);
        if (!settings.UtilitiesSnapEdges && settings.UtilitiesRememberPosition) PositionSaved?.Invoke(AppWindow.Position.X, AppWindow.Position.Y);
    }
}
