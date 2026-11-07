using GlassDock.App.Rendering;
using System.Numerics;
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
    private const double MinimumExpandedWidth = 130;
    private const double MaximumExpandedWidth = 180;
    private const double CaptionRightPadding = 16;
    private static readonly TimeSpan TransitionDuration = TimeSpan.FromMilliseconds(180);
    public UtilityGlassSurface Glass { get; }
    private readonly Border tint = new() { IsHitTestVisible = false };
    private readonly UtilityIcon icon = new();
    private readonly UtilityIcon settingsIcon = new();
    private readonly UtilityIcon settingsBadge = new() { HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom };
    private readonly Grid settingsSymbols = new() { Opacity = 0, IsHitTestVisible = false };
    private readonly Grid symbols = new();
    private readonly Grid iconSlot = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    private readonly ScaleTransform scale = new();
    private readonly ScaleTransform morphScale = new();
    private readonly Grid face = new();
    private readonly Grid captionArea = new() { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid captionHost = new();
    private readonly StackPanel caption = new();
    private readonly TextBlock captionTitle = new() { Text = "Wi-Fi", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock captionNetwork = new() { Opacity = .65, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly WifiNetworkContentEffect captionEffect;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer hold;
    private readonly bool animations = new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
    private Storyboard? motion;
    private Storyboard? morph;
    private Storyboard? expansion;
    private Storyboard? toggleMotion;
    private TaskCompletionSource? toggleAnimationCompletion;
    private Task toggleLead = Task.CompletedTask;
    private Task? toggleCompletion;
    private bool? toggleVisualState;
    private bool nativeToggleUnderlay;
    public bool IsToggling { get; private set; }
    internal bool NeedsNativeToggleUnderlay => nativeToggleUnderlay && !Glass.UsesNativeDockBackdrop && !Glass.HasFrame;
    private bool DisplayOn => toggleVisualState ?? active;
    private double diameter, maximumWidth, expansionTarget;
    private double captionTitleWidth, captionWidth;
    public bool IsExpanded { get; private set; }
    private int morphRevision;
    private bool detailsOpen, disposed;
    private bool trackingPresentation;
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
    public event EventHandler? ExpansionChanged;
    public event EventHandler? GeometryChanged;
    public UtilityDefinition Definition { get; }
    internal global::Windows.Foundation.Rect GlassPresentationBounds()
    {
        // Use the assigned slot, not an arrange pass that can lag a moving dock.
        var bounds = Glass.Bounds;
        var pivot = face.RenderTransformOrigin;
        return new(bounds.X + bounds.Z * pivot.X * (1 - morphScale.ScaleX),
            bounds.Y + bounds.W * pivot.Y * (1 - morphScale.ScaleY),
            bounds.Z * morphScale.ScaleX, bounds.W * morphScale.ScaleY);
    }

    private void TrackPresentation(bool track)
    {
        if (trackingPresentation == track) return;
        trackingPresentation = track;
        if (track) CompositionTarget.Rendering += PresentationChanged;
        else CompositionTarget.Rendering -= PresentationChanged;
    }
    private void PresentationChanged(object? sender, object e) => GeometryChanged?.Invoke(this, EventArgs.Empty);
    private bool RadioUtility => Definition.Kind is UtilityKind.Wifi or UtilityKind.Bluetooth;
    private bool active, available = true, hover, pressed;
    private UtilityTheme theme;
    public UtilityButton(UtilityDefinition definition)
    {
        Definition = definition;
        Glass = new() { Circular = definition.Kind != UtilityKind.Wifi };
        Glass.RenderingChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
        { if (!disposed) GeometryChanged?.Invoke(this, EventArgs.Empty); });
        caption.Children.Add(captionTitle); caption.Children.Add(captionNetwork); captionHost.Children.Add(caption);
        captionEffect = new(captionHost, caption, Vector2.Zero, 0);
        captionArea.Children.Add(captionHost); captionArea.Children.Add(captionEffect.Element);
        symbols.Children.Add(icon);
        if (RadioUtility)
        {
            settingsSymbols.Children.Add(settingsIcon); settingsSymbols.Children.Add(settingsBadge);
            symbols.Children.Add(settingsSymbols);
        }
        face.Children.Add(Glass.Element); face.Children.Add(tint);
        if (definition.Kind == UtilityKind.Wifi)
        {
            iconSlot.Children.Add(symbols); face.Children.Add(iconSlot); face.Children.Add(captionArea);
        }
        else face.Children.Add(symbols);
        Content = face; Padding = new(0); MinWidth = MinHeight = 0; BorderThickness = new(0);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        symbols.RenderTransform = scale; symbols.RenderTransformOrigin = new(.5, .5);
        symbols.HorizontalAlignment = HorizontalAlignment.Center; symbols.VerticalAlignment = VerticalAlignment.Center;
        face.RenderTransform = morphScale; face.RenderTransformOrigin = new(.5, .5);
        icon.HorizontalAlignment = HorizontalAlignment.Center; icon.VerticalAlignment = VerticalAlignment.Center;
        UseLayoutRounding = true;
        Loaded += (_, _) => GeometryChanged?.Invoke(this, EventArgs.Empty);
        SizeChanged += (_, _) => { UpdateExpansionContent(); GeometryChanged?.Invoke(this, EventArgs.Empty); };
        // Updating clips from LayoutUpdated can continuously invalidate the same layout pass.
        captionArea.SizeChanged += (_, _) => UpdateCaptionEffect();
        caption.SizeChanged += (_, _) => UpdateCaptionEffect();
        IsHoldingEnabled = false; // The bounded hold timer owns touch and mouse holds alike.
        hold = DispatcherQueue.CreateTimer(); hold.Interval = TimeSpan.FromMilliseconds(550); hold.IsRepeating = false;
        hold.Tick += (_, _) =>
        {
            if (!gesture.Hold()) return;
            pressed = false; Render();
            if (Definition.Kind == UtilityKind.Wifi)
            {
                IsExpanded = !IsExpanded; UpdateExpansionTarget(); ExpansionChanged?.Invoke(this, EventArgs.Empty);
            }
            else DetailsRequested?.Invoke(this, EventArgs.Empty);
        };
        HorizontalAlignment = HorizontalAlignment.Left; VerticalAlignment = VerticalAlignment.Top;
        foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed" })
            Resources[key] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        if (animations)
            tint.BackgroundTransition = new BrushTransition { Duration = TransitionDuration };
        PointerEntered += (_, _) => { hover = true; Render(); };
        PointerExited += (_, _) => { hover = pressed = false; Render(); };
        AddHandler(PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            var properties = e.GetCurrentPoint(this).Properties;
            if (IsToggling && properties.IsLeftButtonPressed)
            { hold.Stop(); gesture.Cancel(); pressed = false; e.Handled = true; return; }
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
            {
                if (RadioUtility)
                {
                    hold.Stop(); gesture.Cancel(); pressed = false; Render(); e.Handled = true; return;
                }
                gesture.BeginDrag(); hold.Stop(); CapturePointer(e.Pointer);
            }
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
        Click += (_, _) => { if (!IsToggling && !gesture.SuppressPrimary) PrimaryRequested?.Invoke(this, EventArgs.Empty); };
        AutomationProperties.SetName(this, definition.Name);
    }

    public void Configure(double diameter, double iconSize, UtilityTheme theme, DockAppearanceMode dockMode, double availableWidth)
    {
        if (RadioUtility) iconSize *= .95;
        var changed = this.diameter != diameter || icon.Width != iconSize || this.theme != theme || this.dockMode != dockMode;
        var nextMaximum = Math.Max(diameter, Definition.Kind == UtilityKind.Wifi
            ? Math.Min(MaximumExpandedWidth, availableWidth) : availableWidth);
        var limitChanged = maximumWidth != nextMaximum;
        if (!changed && !limitChanged) return;
        this.diameter = diameter; maximumWidth = nextMaximum;
        this.dockMode = dockMode;
        this.theme = theme; Height = face.Height = diameter;
        if (!double.IsFinite(Width)) Width = diameter;
        face.Width = double.NaN;
        CornerRadius = tint.CornerRadius = new(diameter / 2);
        Glass.CornerRadius = (float)(diameter / 2);
        symbols.Width = symbols.Height = icon.Width = icon.Height = settingsSymbols.Width = settingsSymbols.Height = iconSize;
        settingsBadge.Width = settingsBadge.Height = iconSize * .56;
        if (Definition.Kind == UtilityKind.Wifi)
        {
            iconSlot.Width = iconSlot.Height = diameter;
            captionTitle.FontSize = diameter * .22; captionNetwork.FontSize = diameter * .17;
            captionTitle.MaxWidth = captionNetwork.MaxWidth = Math.Max(0, MaximumExpandedWidth - diameter - CaptionRightPadding);
            caption.Spacing = diameter * .025;
            MeasureCaption();
        }
        else Width = diameter;
        RequestedTheme = theme == UtilityTheme.Light ? ElementTheme.Light : ElementTheme.Dark;
        Render();
        UpdateExpansionTarget(changed: changed);
    }

    public void SetConnectionText(string text)
    {
        var display = WifiNetworkCaption.Truncate(text);
        AutomationProperties.SetName(captionNetwork, text);
        if (captionNetwork.Text == display) return;
        captionNetwork.Text = display;
        MeasureCaption();
        UpdateExpansionTarget();
    }

    private void MeasureCaption()
    {
        var available = new global::Windows.Foundation.Size(Math.Max(0, MaximumExpandedWidth - diameter - CaptionRightPadding), diameter);
        captionTitle.Measure(available);
        captionNetwork.Measure(available);
        // Collapsed layout can zero DesiredSize, so retain the independently measured reveal threshold.
        captionTitleWidth = captionTitle.DesiredSize.Width;
        captionWidth = Math.Max(captionTitleWidth, captionNetwork.DesiredSize.Width);
    }

    private void UpdateExpansionTarget(bool changed = false)
    {
        if (Definition.Kind != UtilityKind.Wifi || diameter <= 0) return;
        var preferred = Math.Clamp(diameter + captionWidth + CaptionRightPadding, MinimumExpandedWidth, MaximumExpandedWidth);
        var target = IsExpanded ? Math.Min(maximumWidth, preferred) : diameter;
        if (!changed && Math.Abs(target - expansionTarget) < .01) return;
        expansionTarget = target;
        var from = Math.Clamp(Width, diameter, maximumWidth); expansion?.Stop(); Width = target;
        if (animations && !changed && Math.Abs(from - target) > .01)
        {
            var story = new Storyboard(); expansion = story;
            var animation = new DoubleAnimation { From = from, To = target, Duration = new(TimeSpan.FromMilliseconds(220)),
                EnableDependentAnimation = true, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            Storyboard.SetTarget(animation, this); Storyboard.SetTargetProperty(animation, "Width"); story.Children.Add(animation);
            story.Completed += (_, _) => { if (ReferenceEquals(expansion, story)) { story.Stop(); Width = target; UpdateExpansionContent(); } };
            story.Begin();
        }
        UpdateExpansionContent();
    }

    private void UpdateExpansionContent()
    {
        if (disposed || Definition.Kind != UtilityKind.Wifi || diameter <= 0) return;
        captionArea.Margin = new(diameter, 0, CaptionRightPadding, 0);
        captionArea.Width = Math.Max(0, Width - diameter - CaptionRightPadding);
        face.RenderTransformOrigin = new(diameter / (2 * Math.Max(diameter, Width)), .5);
        UpdateCaptionEffect();
    }

    private void UpdateCaptionEffect()
    {
        if (disposed || diameter <= 0) return;
        var wanted = Math.Max(0, maximumWidth - diameter - CaptionRightPadding);
        var room = Math.Max(0, Width - diameter - CaptionRightPadding);
        var readable = captionTitleWidth;
        var progress = readable <= 0 || wanted + .01 < readable ? 0 : Math.Clamp((room / readable - .65) / .35, 0, 1);
        if (progress > .9999) progress = 1;
        var visibility = progress > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (captionArea.Visibility != visibility) captionArea.Visibility = visibility;
        if (progress > 0 && (captionArea.ActualWidth <= 0 || captionArea.ActualHeight <= 0)) return;
        captionEffect.Update(progress, captionArea.ActualWidth, captionArea.ActualHeight);
    }

    public void SetState(bool on, bool available, string message)
    {
        if (disposed) return;
        active = on; this.available = available; this.message = message;
        AutomationProperties.SetItemStatus(this, IsToggling ? "Changing..." : !available ? "Unavailable" : on ? "On" : "Off");
        Render();
    }

    public bool TryBeginToggle()
    {
        if (disposed || IsToggling || IsMorphing || !RadioUtility) return false;
        IsToggling = true; toggleVisualState = active; toggleCompletion = null;
        nativeToggleUnderlay = Glass.UsesNativeDockBackdrop;
        hold.Stop(); gesture.Cancel(); pressed = false;
        AutomationProperties.SetItemStatus(this, "Changing...");
        Render();
        toggleLead = PlayToggleAnimationAsync(settle: false);
        return true;
    }

    public Task CompleteToggleAsync(Action synchronizeActualState)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async void Finish()
        {
            try
            {
                await (toggleCompletion ??= CompleteToggleOnUiAsync(synchronizeActualState));
                completion.TrySetResult();
            }
            catch (Exception error) { completion.TrySetException(error); }
        }
        if (DispatcherQueue.HasThreadAccess) Finish();
        else if (!DispatcherQueue.TryEnqueue(Finish)) completion.TrySetResult();
        return completion.Task;
    }

    private async Task CompleteToggleOnUiAsync(Action synchronizeActualState)
    {
        try
        {
            await toggleLead;
            if (disposed || !IsToggling) return;
            synchronizeActualState();
            toggleVisualState = null; Render();
            GeometryChanged?.Invoke(this, EventArgs.Empty);
            await PlayToggleAnimationAsync(settle: true);
        }
        finally
        {
            CancelToggle();
            if (!disposed) synchronizeActualState();
        }
    }

    public void CancelToggle()
    {
        toggleMotion?.Stop(); toggleMotion = null;
        toggleAnimationCompletion?.TrySetResult(); toggleAnimationCompletion = null;
        IsToggling = false; toggleVisualState = null;
        if (disposed) return;
        scale.ScaleX = scale.ScaleY = symbols.Opacity = 1;
        if (!disposed) { Render(); GeometryChanged?.Invoke(this, EventArgs.Empty); }
    }

    private Task PlayToggleAnimationAsync(bool settle)
    {
        if (disposed || !animations) return Task.CompletedTask;
        var story = new Storyboard(); toggleMotion = story;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        toggleAnimationCompletion = completion;
        if (settle) AnimateTransition(story, symbols, "Opacity", 1, .65, 1, true, TransitionDuration);
        else
        {
            AnimateTransition(story, scale, "ScaleX", 1, .84, 1, true, TransitionDuration);
            AnimateTransition(story, scale, "ScaleY", 1, .84, 1, true, TransitionDuration);
        }
        story.Completed += (_, _) =>
        {
            story.Stop();
            if (!disposed) scale.ScaleX = scale.ScaleY = symbols.Opacity = 1;
            completion.TrySetResult();
        };
        try { story.Begin(); }
        catch (Exception error) { completion.TrySetException(error); }
        return completion.Task;
    }

    private static void AnimateTransition(Storyboard story, DependencyObject target, string property,
        double from, double middle, double to, bool shrink, TimeSpan duration)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = new Duration(duration), EnableDependentAnimation = true };
        void Frame(double at, double value) => animation.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromTicks((long)(duration.Ticks * at))), Value = value,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        });
        Frame(0, from); Frame(.3, shrink ? middle : from); Frame(.7, middle); Frame(1, to);
        Storyboard.SetTarget(animation, target); Storyboard.SetTargetProperty(animation, property); story.Children.Add(animation);
    }

    public async Task SetDetailsOpenAsync(bool open)
    {
        if (disposed || !RadioUtility || detailsOpen == open) return;
        detailsOpen = open;
        IsMorphing = true;
        var revision = ++morphRevision;
        Render();
        // Decode all vectors before crossfading, including the first opening.
        await Task.WhenAll(icon.SetAsync(Definition.Icon, DisplayOn && !RadioUtility, IconColor()),
            settingsIcon.SetAsync("gear-six", DisplayOn, IconColor()), settingsBadge.SetAsync(Definition.Icon, false, IconColor()));
        if (disposed || revision != morphRevision) return;
        var fromScale = morphScale.ScaleX;
        var fromWifi = icon.Opacity;
        var fromSettings = settingsSymbols.Opacity;
        morph?.Stop();
        morphScale.ScaleX = morphScale.ScaleY = 1;
        icon.Opacity = open ? 0 : 1; settingsSymbols.Opacity = open ? 1 : 0;
        if (!animations) { IsMorphing = false; TrackPresentation(false); return; }
        var duration = open ? TransitionDuration : TimeSpan.FromMilliseconds(120);
        morph = new();
        AnimateTransition(morph, morphScale, "ScaleX", fromScale, .84, 1, true, duration);
        AnimateTransition(morph, morphScale, "ScaleY", fromScale, .84, 1, true, duration);
        AnimateTransition(morph, icon, "Opacity", fromWifi, open ? 0 : 1, open ? 0 : 1, false, duration);
        AnimateTransition(morph, settingsSymbols, "Opacity", fromSettings, open ? 1 : 0, open ? 1 : 0, false, duration);
        morph.Completed += (_, _) =>
        {
            if (revision != morphRevision) return;
            IsMorphing = false; TrackPresentation(false);
            GeometryChanged?.Invoke(this, EventArgs.Empty);
        };
        morph.Begin();
        TrackPresentation(dockMode is (DockAppearanceMode.Frosted or DockAppearanceMode.Acrylic) && !DisplayOn);
    }

    private UtilityTheme IconTheme => dockMode.GlassStyle() is null
        ? dockMode == DockAppearanceMode.Light ? UtilityTheme.Light : UtilityTheme.Dark : theme;
    private uint IconColor()
    {
        if (RadioUtility) return DisplayOn ? Definition.Accent : 0xFFFFFFFF;
        return UtilityColors.Icon(IconTheme, DisplayOn, !available);
    }

    private void Render()
    {
        var sharedGlass = dockMode is DockAppearanceMode.Frosted or DockAppearanceMode.Acrylic;
        var displayOn = DisplayOn;
        Glass.UseDockMaterial = !displayOn && (sharedGlass || RadioUtility);
        if (!IsMorphing || !sharedGlass || displayOn) TrackPresentation(false);
        var items = Definition.Kind == UtilityKind.Bluetooth ? "paired devices" : "networks";
        AutomationProperties.SetName(this, detailsOpen ? Definition.Name + " Settings" : Definition.Name);
        AutomationProperties.SetHelpText(this, detailsOpen ? $"Open Windows {Definition.Name} Settings. Right-click shows {items}."
            : message + $". Left-click toggles {Definition.Name}; right-click shows {items}.");
        if (Definition.Kind == UtilityKind.Wifi)
            AutomationProperties.SetHelpText(this, AutomationProperties.GetHelpText(this) + " Hold to expand or collapse.");
        if (Definition.Kind == UtilityKind.Bluetooth)
            AutomationProperties.SetHelpText(this, AutomationProperties.GetHelpText(this) + " Hold to show paired devices.");
        ToolTipService.SetToolTip(this, IsToggling ? "Changing..." : detailsOpen ? Definition.Name + " Settings" : message);
        var color = displayOn ? Definition.Accent : UtilityColors.Surface(theme);
        var surfaceColor = global::Windows.UI.Color.FromArgb(
            displayOn ? (byte)(pressed ? 110 : 85) : dockMode.GlassStyle() is null ? (byte)255 : hover ? (byte)36 : (byte)12,
            (byte)(color >> 16), (byte)(color >> 8), (byte)color);
        if (dockMode.GlassStyle() is null)
        {
            var basis = Desktop.DockControlPalette.SolidSurface(dockMode);
            var blend = displayOn ? surfaceColor.A / 255d : 0;
            surfaceColor = global::Windows.UI.Color.FromArgb(255,
                (byte)(basis.R + (surfaceColor.R - basis.R) * blend),
                (byte)(basis.G + (surfaceColor.G - basis.G) * blend),
                (byte)(basis.B + (surfaceColor.B - basis.B) * blend));
        }
        if (sharedGlass && !displayOn)
            surfaceColor = Microsoft.UI.Colors.Transparent;
        else if (RadioUtility)
        {
            if (displayOn)
                surfaceColor = global::Windows.UI.Color.FromArgb((byte)(pressed ? 220 : hover ? 245 : 235), 255, 255, 255);
            else if (dockMode.GlassStyle() is null)
                surfaceColor = Desktop.DockControlPalette.SolidSurface(dockMode);
            else
                surfaceColor = Desktop.DockControlPalette.Surface(dockMode, (byte)(pressed ? 30 : hover ? 20 : 0));
        }
        tint.Background = new SolidColorBrush(surfaceColor);
        var targetScale = RadioUtility ? 1 : pressed ? .96 : hover ? 1.03 : 1;
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
        _ = icon.SetAsync(Definition.Icon, displayOn && !RadioUtility, IconColor());
        var captionTheme = Definition.Kind == UtilityKind.Wifi && displayOn ? UtilityTheme.Light : IconTheme;
        var textColor = UtilityColors.Icon(captionTheme, false, !available);
        captionTitle.Foreground = captionNetwork.Foreground = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255,
            (byte)(textColor >> 16), (byte)(textColor >> 8), (byte)textColor));
        if (RadioUtility)
        {
            settingsIcon.Width = settingsIcon.Height = symbols.Width * (displayOn ? .9 : 1);
            settingsIcon.HorizontalAlignment = displayOn ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            settingsIcon.VerticalAlignment = displayOn ? VerticalAlignment.Top : VerticalAlignment.Center;
            settingsBadge.Visibility = displayOn ? Visibility.Visible : Visibility.Collapsed;
            _ = settingsIcon.SetAsync("gear-six", displayOn, IconColor());
            _ = settingsBadge.SetAsync(Definition.Icon, false, IconColor());
        }
    }
    public void Dispose() { disposed = true; CancelToggle(); IsMorphing = false; TrackPresentation(false); morphRevision++; hold.Stop(); motion?.Stop(); morph?.Stop(); expansion?.Stop(); captionEffect.Dispose(); Glass.Dispose(); }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (IsToggling && e.Key is (global::Windows.System.VirtualKey.Space or global::Windows.System.VirtualKey.Enter))
        { e.Handled = true; return; }
        if (e.Key is global::Windows.System.VirtualKey.Space or global::Windows.System.VirtualKey.Enter) gesture.Keyboard();
        base.OnKeyDown(e);
    }
}
