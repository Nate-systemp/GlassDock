using GlassDock.App.Rendering;
using GlassDock.Core.Materials;
using GlassDock.Core.Desktop;
using GlassDock.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace GlassDock.App.Controls;

internal sealed class UtilityButton : Button, IDisposable
{
    public UtilityGlassSurface Glass { get; } = new();
    private readonly Border tint = new() { IsHitTestVisible = false };
    private readonly UtilityIcon icon = new();
    private readonly UtilityIcon settingsIcon = new() { Opacity = 0 };
    private readonly Grid symbols = new();
    private readonly ScaleTransform scale = new();
    private readonly ScaleTransform morphScale = new();
    private readonly Grid face = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer hold;
    private readonly bool animations = new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
    private Storyboard? motion;
    private Storyboard? morph;
    private int morphRevision;
    private bool detailsOpen, disposed;
    private string message = "";
    private double scaleTarget = 1;
    private global::Windows.Foundation.Point pointerStart, pointerLast;
    private readonly UtilityPointerGesture gesture = new();
    private DockAppearanceMode dockMode;
    public bool IsDragging => gesture.IsDragging;
    public bool IsMorphing { get; private set; }
    public event EventHandler? DetailsRequested;
    public event EventHandler? PrimaryRequested;
    public event Action<UtilityButton, double>? Dragged;
    public UtilityDefinition Definition { get; }
    private bool active, available = true, hover, pressed;
    private UtilityTheme theme;
    public UtilityButton(UtilityDefinition definition)
    {
        Definition = definition;
        symbols.Children.Add(icon);
        if (definition.Kind == UtilityKind.Wifi) symbols.Children.Add(settingsIcon);
        face.Children.Add(Glass.Element); face.Children.Add(tint); face.Children.Add(symbols);
        Content = face; Padding = new(0); MinWidth = MinHeight = 0; BorderThickness = new(0);
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        symbols.RenderTransform = scale; symbols.RenderTransformOrigin = new(.5, .5);
        symbols.HorizontalAlignment = HorizontalAlignment.Center; symbols.VerticalAlignment = VerticalAlignment.Center;
        face.RenderTransform = morphScale; face.RenderTransformOrigin = new(.5, .5);
        icon.HorizontalAlignment = HorizontalAlignment.Center; icon.VerticalAlignment = VerticalAlignment.Center;
        UseLayoutRounding = true;
        IsHoldingEnabled = false; // The bounded hold timer owns touch and mouse holds alike.
        hold = DispatcherQueue.CreateTimer(); hold.Interval = TimeSpan.FromMilliseconds(550); hold.IsRepeating = false;
        hold.Tick += (_, _) =>
        {
            if (!gesture.Hold()) return;
            pressed = false; Render(); DetailsRequested?.Invoke(this, EventArgs.Empty);
        };
        HorizontalAlignment = HorizontalAlignment.Left; VerticalAlignment = VerticalAlignment.Top;
        foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed" })
            Resources[key] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        if (animations)
            tint.BackgroundTransition = new BrushTransition { Duration = TimeSpan.FromMilliseconds(180) };
        PointerEntered += (_, _) => { hover = true; Render(); };
        PointerExited += (_, _) => { hover = pressed = false; Render(); };
        AddHandler(PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            var properties = e.GetCurrentPoint(this).Properties;
            gesture.Press(properties.IsLeftButtonPressed, properties.IsRightButtonPressed);
            pressed = gesture.PrimaryDown; hold.Stop();
            pointerStart = pointerLast = e.GetCurrentPoint(null).Position;
            if (gesture.PrimaryDown) hold.Start();
            Render();
        }), true);
        AddHandler(PointerMovedEvent, new PointerEventHandler((_, e) =>
        {
            if (!gesture.PrimaryDown || (gesture.SuppressPrimary && !IsDragging)) return;
            var point = e.GetCurrentPoint(null).Position;
            if (!IsDragging && Math.Abs(point.Y - pointerStart.Y) > 5 && Math.Abs(point.X - pointerStart.X) <= 5)
            { hold.Stop(); gesture.Cancel(); return; }
            if (!IsDragging && Math.Abs(point.X - pointerStart.X) > 5)
            { gesture.BeginDrag(); hold.Stop(); CapturePointer(e.Pointer); }
            if (IsDragging) { Dragged?.Invoke(this, point.X - pointerLast.X); e.Handled = true; }
            pointerLast = point;
        }), true);
        void Release()
        {
            hold.Stop(); pressed = false; gesture.Release(); Render();
            // Keep suppression through the current release's Click, then restore keyboard activation.
            // Gesture suppression is reset by the next press or an explicit keyboard action.
        }
        AddHandler(PointerReleasedEvent, new PointerEventHandler((_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == Microsoft.UI.Input.PointerUpdateKind.RightButtonReleased)
            {
                e.Handled = true;
                if (gesture.ReleaseSecondary()) DetailsRequested?.Invoke(this, EventArgs.Empty);
            }
            Release();
        }), true);
        AddHandler(PointerCaptureLostEvent, new PointerEventHandler((_, e) =>
        {
            if (IsDragging || e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) gesture.Cancel();
            Release();
        }), true);
        AddHandler(PointerCanceledEvent, new PointerEventHandler((_, _) => { gesture.Cancel(); Release(); }), true);
        AddHandler(RightTappedEvent, new RightTappedEventHandler((_, e) =>
        { e.Handled = true; if (gesture.RequestDetails()) DetailsRequested?.Invoke(this, EventArgs.Empty); }), true);
        ContextRequested += (_, e) =>
        {
            e.Handled = true;
            if (!e.TryGetPosition(this, out var position)) gesture.Keyboard();
            hold.Stop(); if (gesture.RequestDetails()) DetailsRequested?.Invoke(this, EventArgs.Empty);
        };
        Click += (_, _) => { if (!gesture.SuppressPrimary) PrimaryRequested?.Invoke(this, EventArgs.Empty); };
        AutomationProperties.SetName(this, definition.Name);
    }

    public void Configure(double diameter, double iconSize, UtilityTheme theme, DockAppearanceMode dockMode)
    {
        if (Width == diameter && icon.Width == iconSize && this.theme == theme && this.dockMode == dockMode) return;
        this.dockMode = dockMode;
        this.theme = theme; Width = Height = face.Width = face.Height = diameter;
        CornerRadius = tint.CornerRadius = new(diameter / 2);
        symbols.Width = symbols.Height = icon.Width = icon.Height = settingsIcon.Width = settingsIcon.Height = iconSize;
        RequestedTheme = theme == UtilityTheme.Light ? ElementTheme.Light : ElementTheme.Dark;
        Render();
    }

    public void SetState(bool on, bool available, string message)
    {
        active = on; this.available = available; this.message = message;
        AutomationProperties.SetItemStatus(this, !available ? "Unavailable" : on ? "On" : "Off");
        Render();
    }

    public async Task SetDetailsOpenAsync(bool open)
    {
        if (disposed || Definition.Kind != UtilityKind.Wifi || detailsOpen == open) return;
        detailsOpen = open;
        IsMorphing = true;
        var revision = ++morphRevision;
        Render();
        // Decode both vectors before crossfading, including the first opening.
        await Task.WhenAll(icon.SetAsync(Definition.Icon, active, IconColor()), settingsIcon.SetAsync("gear-six", active, IconColor()));
        if (disposed || revision != morphRevision) return;
        var fromScale = morphScale.ScaleX;
        var fromWifi = icon.Opacity;
        var fromSettings = settingsIcon.Opacity;
        morph?.Stop();
        morphScale.ScaleX = morphScale.ScaleY = 1;
        icon.Opacity = open ? 0 : 1; settingsIcon.Opacity = open ? 1 : 0;
        if (!animations) { IsMorphing = false; return; }
        var duration = TimeSpan.FromMilliseconds(open ? 180 : 120);
        morph = new();
        void Animate(DependencyObject target, string property, double from, double middle, double to, bool shrink)
        {
            var animation = new DoubleAnimationUsingKeyFrames { Duration = new Duration(duration), EnableDependentAnimation = true };
            void Frame(double at, double value) => animation.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromTicks((long)(duration.Ticks * at))), Value = value,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            });
            Frame(0, from); Frame(.3, shrink ? middle : from); Frame(.7, middle); Frame(1, to);
            Storyboard.SetTarget(animation, target); Storyboard.SetTargetProperty(animation, property); morph.Children.Add(animation);
        }
        Animate(morphScale, "ScaleX", fromScale, .84, 1, true);
        Animate(morphScale, "ScaleY", fromScale, .84, 1, true);
        Animate(icon, "Opacity", fromWifi, open ? 0 : 1, open ? 0 : 1, false);
        Animate(settingsIcon, "Opacity", fromSettings, open ? 1 : 0, open ? 1 : 0, false);
        morph.Completed += (_, _) => { if (revision == morphRevision) IsMorphing = false; };
        morph.Begin();
    }

    private uint IconColor()
    {
        var iconTheme = dockMode.GlassStyle() is null
            ? dockMode == DockAppearanceMode.Light ? UtilityTheme.Light : UtilityTheme.Dark : theme;
        return UtilityColors.Icon(iconTheme, active, !available);
    }

    private void Render()
    {
        AutomationProperties.SetName(this, detailsOpen ? "Wi-Fi Settings" : Definition.Name);
        AutomationProperties.SetHelpText(this, detailsOpen ? "Open Windows Wi-Fi Settings. Right-click shows networks."
            : message + ". Left-click toggles Wi-Fi; right-click shows networks.");
        ToolTipService.SetToolTip(this, detailsOpen ? "Wi-Fi Settings" : message);
        var color = active ? Definition.Accent : UtilityColors.Surface(theme);
        var surfaceColor = global::Windows.UI.Color.FromArgb(
            active ? (byte)(pressed ? 110 : 85) : dockMode.GlassStyle() is null ? (byte)255 : hover ? (byte)36 : (byte)12,
            (byte)(color >> 16), (byte)(color >> 8), (byte)color);
        if (dockMode.GlassStyle() is null)
        {
            var basis = Desktop.DockControlPalette.SolidSurface(dockMode);
            var blend = active ? surfaceColor.A / 255d : 0;
            surfaceColor = global::Windows.UI.Color.FromArgb(255,
                (byte)(basis.R + (surfaceColor.R - basis.R) * blend),
                (byte)(basis.G + (surfaceColor.G - basis.G) * blend),
                (byte)(basis.B + (surfaceColor.B - basis.B) * blend));
        }
        tint.Background = new SolidColorBrush(surfaceColor);
        var targetScale = pressed ? .96 : hover ? 1.03 : 1;
        if (scaleTarget != targetScale)
        {
            scaleTarget = targetScale;
            var from = scale.ScaleX; motion?.Stop();
            scale.ScaleX = scale.ScaleY = targetScale;
            if (animations)
            {
                motion = new();
                foreach (var property in new[] { "ScaleX", "ScaleY" })
                {
                    var animation = new DoubleAnimation { From = from, To = targetScale,
                        Duration = new Duration(TimeSpan.FromMilliseconds(140)), EnableDependentAnimation = true,
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    Storyboard.SetTarget(animation, scale); Storyboard.SetTargetProperty(animation, property); motion.Children.Add(animation);
                }
                motion.Begin();
            }
        }
        _ = icon.SetAsync(Definition.Icon, active, IconColor());
        if (Definition.Kind == UtilityKind.Wifi) _ = settingsIcon.SetAsync("gear-six", active, IconColor());
    }
    public void Dispose() { disposed = true; IsMorphing = false; morphRevision++; hold.Stop(); motion?.Stop(); morph?.Stop(); Glass.Dispose(); }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (e.Key is global::Windows.System.VirtualKey.Space or global::Windows.System.VirtualKey.Enter) gesture.Keyboard();
        base.OnKeyDown(e);
    }
}
