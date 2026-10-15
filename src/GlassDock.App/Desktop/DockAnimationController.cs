using System.Numerics;
using GlassDock.App.Controls;
using GlassDock.App.Rendering;
using GlassDock.Core.Settings;
using GlassDock.Core.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Animation;
using global::Windows.UI.ViewManagement;

namespace GlassDock.App.Desktop;

internal sealed class DockAnimationController : IDisposable
{
    private readonly DockHoverMagnification<FrameworkElement> hover = new();
    private readonly UISettings hoverSettings = new();

    private readonly GlassSurface surface;
    private readonly Panel icons;
    private readonly DockIconTransition iconTransition;
    private readonly FrameworkElement indicator;
    private readonly Canvas hoverArtworkForeground;
    // One cached mirror per real artwork image, including stack layers. Source
    // images still belong to their Buttons (for badges, drag and interactions),
    // but the mirrored pixels render above the glass in the unclipped root.
    private readonly Dictionary<Image, Image> hoverArtworkMirrors = new(ReferenceEqualityComparer.Instance);
    // The bitmap is mirrored at root level to avoid button clipping, so its
    // notification badge must be mirrored in that same coordinate space.
    // Otherwise the red number remains attached to the *unscaled* icon slot.
    private readonly Dictionary<NotificationBadge, ForegroundBadge> hoverBadgeMirrors = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<AdaptiveAppIcon> observedBadgeIcons = new(ReferenceEqualityComparer.Instance);
    private bool badgeRefreshQueued;

    private sealed class ForegroundBadge
    {
        public NotificationBadge Visual { get; } = new()
        {
            IsHitTestVisible = false,
            UseLayoutRounding = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        public Microsoft.UI.Xaml.Media.ScaleTransform Scale { get; } = new();
        public long LastRevision { get; set; } = -1;
    }

    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer magnificationTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer placementTimer;
    private double placementFrom, placementTarget, placementStarted, placementDuration;
    public bool IsPlacementAnimating { get; private set; }
    public event EventHandler? PlacementChanged;
    public event EventHandler? HoverPresentationChanged;

    private Storyboard? active;
    private Storyboard? indicatorFade;
    private TaskCompletionSource<bool>? completion;
    private bool disposed;
    private double expandedWidth = 560, expandedHeight = 68;
    private double pathStartProgress, pathStartBottom, pathTargetBottom;
    private double collapsedBottom;
    private bool pathExpanded;
    // A built-in XAML dependency property provides the one storyboard clock.
    // It is never inserted into the visual tree and cannot affect hit testing.
    private readonly Slider progressClock = new() { Minimum = 0, Maximum = 1 };
    private double Progress { get => progressClock.Value; set => progressClock.Value = value; }
    private void ProgressChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs args) => ApplyProgress();

    private void ApplyProgress()
    {
        if (disposed) return;
        var p = Math.Clamp(Progress, 0, 1);
        var frame = DockTransitionGeometry.At(p, expandedWidth, expandedHeight);
        surface.Width = indicator.Width = frame.Width;
        surface.Height = indicator.Height = frame.Height;
        if (indicator is Border pill) pill.CornerRadius = new CornerRadius(frame.CornerRadius);
        var remaining = (pathExpanded ? 1 : 0) - pathStartProgress;
        var fraction = Math.Abs(remaining) < .000001 ? 1 : Math.Clamp((p - pathStartProgress) / remaining, 0, 1);
        var bottom = pathStartBottom + (pathTargetBottom - pathStartBottom) * fraction;
        icons.Margin = new Thickness(0, 0, 0, bottom);
        ApplyBottom(bottom);
        // The retained pill and material share a silhouette, including during blending.
        indicator.Opacity = 1 - frame.MaterialBlend;
        surface.Opacity = frame.MaterialBlend;
    }

    private bool interactionHeld;
    private bool magnificationTimerRunning;

    public DockAnimationController(
        GlassSurface surface,
        Panel icons,
        FrameworkElement indicator,
        Canvas hoverArtworkForeground)
    {
        this.hoverArtworkForeground = hoverArtworkForeground;
        progressClock.ValueChanged += ProgressChanged;
        this.surface = surface;
        this.icons = icons;
        iconTransition = new(icons, surface);
        this.indicator = indicator;

        magnificationTimer =
            icons.DispatcherQueue.CreateTimer();

        magnificationTimer.Interval =
            TimeSpan.FromMilliseconds(16);

        magnificationTimer.Tick +=
            (_, _) => TickMagnification();
        icons.SizeChanged += HoverLayoutChanged;
        placementTimer = icons.DispatcherQueue.CreateTimer();
        placementTimer.Interval = TimeSpan.FromMilliseconds(16);
        placementTimer.Tick += (_, _) =>
        {
            if (disposed) return;
            var t = Math.Clamp((Environment.TickCount64 - placementStarted) / placementDuration, 0, 1);
            if (t == 1) { placementTimer.Stop(); IsPlacementAnimating = false; }

            // Directional easing, deliberately not ease-in-out:
            // raising/opening settles with sine ease-out;
            // lowering/closing folds away with sine ease-in.
            var eased = placementTarget >= placementFrom
                ? Math.Sin(t * Math.PI / 2)
                : 1 - Math.Cos(t * Math.PI / 2);

            ApplyBottom(placementFrom + (placementTarget - placementFrom) * eased);
        };
    }

    public void SetBottom(double bottom)
    {
        placementTimer.Stop(); IsPlacementAnimating = false;
        ApplyBottom(bottom);
    }

    public void AnimateBottom(double bottom, double milliseconds)
    {
        if (disposed) return;
        if (!new UISettings().AnimationsEnabled || milliseconds <= 0)
        {
            SetBottom(bottom);
            return;
        }
        placementFrom = surface.Margin.Bottom;
        placementTarget = bottom;
        placementStarted = Environment.TickCount64;
        placementDuration = milliseconds;
        IsPlacementAnimating = true;
        placementTimer.Start();
    }

    private void ApplyBottom(double bottom)
    {
        surface.Margin = indicator.Margin = new Thickness(0, 0, 0, bottom);
        PlacementChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Fades the collapsed indicator without interrupting dock animation.</summary>
    public void AnimateIndicatorOpacity(double target, double milliseconds)
    {
        if (disposed) return;
        var from = indicator.Opacity;
        indicatorFade?.Stop();
        indicatorFade = null;
        indicator.Opacity = from;
        if (!new UISettings().AnimationsEnabled)
        {
            indicator.Opacity = target;
            return;
        }
        if (Math.Abs(from - target) < 0.001)
        {
            indicator.Opacity = target;
            return;
        }

        var storyboard = new Storyboard();
        var fade = new DoubleAnimation
        {
            From = from,
            To = target,
            Duration = TimeSpan.FromMilliseconds(Math.Max(1, milliseconds)),
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(fade, indicator);
        Storyboard.SetTargetProperty(fade, "Opacity");
        storyboard.Children.Add(fade);
        indicatorFade = storyboard;
        storyboard.Completed += (_, _) =>
        {
            if (ReferenceEquals(indicatorFade, storyboard)) indicatorFade = null;
        };
        storyboard.Begin();
    }

    public void StopIndicatorOpacityAnimation()
    {
        var opacity = indicator.Opacity;
        indicatorFade?.Stop();
        indicatorFade = null;
        indicator.Opacity = opacity;
    }

    public void HoldMagnification(bool held)
    {
        if (interactionHeld == held) return;
        interactionHeld = held;
        hover.Pause(held, Environment.TickCount64);
        if (held) StopMagnificationTimer();
        else if (hover.IsAnimating) StartMagnificationTimer();
    }

    /// <summary>Resize a settled open dock without interpreting removed slots as collapse progress.</summary>
    public void ResizeExpanded(double width, double height)
    {
        if (disposed) return;
        expandedWidth = width;
        expandedHeight = height;
        iconTransition.SetExpandedSize(width, height);
        surface.Width = indicator.Width = width;
        surface.Height = indicator.Height = height;
    }

    // Pin placement changes the expanded surface depth, not collapse progress.
    public void SetPlacementHeight(double height)
    {
        expandedHeight = height;
        iconTransition.SetExpandedSize(expandedWidth, height);
    }

    public Task<bool> AnimateAsync(
        bool expanded,
        double targetWidth = 560,
        double targetHeight = 68,
        double? bottom = null)
    {
        if (!icons.DispatcherQueue.HasThreadAccess)
            throw new InvalidOperationException("Dock transitions must run on the UI thread.");
        ObjectDisposedException.ThrowIf(disposed, this);
        var started =
            System.Diagnostics.Stopwatch.StartNew();

        Trace(
            $"AnimateAsync({expanded}, targetWidth={targetWidth}), previousPending={completion is { Task.IsCompleted: false }}");

        // Sample the rendered geometry before releasing its single animation clock.
        var progress = DockTransitionTiming.Progress(surface.Width, surface.Height, targetWidth, targetHeight);
        var currentBottom = surface.Margin.Bottom;
        if (expanded && progress <= .000001) collapsedBottom = currentBottom;
        var previous = active;
        active = null;
        previous?.Stop();
        completion?.TrySetResult(false);
        completion = null;
        StopIndicatorOpacityAnimation();
        placementTimer.Stop();
        IsPlacementAnimating = false;

        expandedWidth = targetWidth;
        expandedHeight = targetHeight;
        pathStartProgress = progress;
        pathStartBottom = currentBottom;
        pathTargetBottom = expanded ? bottom ?? currentBottom : collapsedBottom;
        pathExpanded = expanded;
        iconTransition.SetExpandedSize(targetWidth, targetHeight);
        Progress = progress;
        ApplyProgress();

        var animationsEnabled = new UISettings().AnimationsEnabled;
        Trace($"AnimationsEnabled={animationsEnabled}");
        if (!animationsEnabled)
        {
            ResetMagnification(immediate: true);
            Progress = expanded ? 1 : 0;
            ApplyProgress();
            return Task.FromResult(true);
        }

        if (!expanded) ResetMagnification();
        active = new Storyboard();
        var duration = (int)Math.Round(DockTransitionTiming.Duration(expanded, progress));
        Add(progressClock, "Value", progress, EasingMode.EaseInOut, (duration, expanded ? 1 : 0));
        var pending =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        completion = pending;

        var storyboard = active;
        storyboard.Completed +=
            (_, _) =>
            {
                if (!ReferenceEquals(active, storyboard) || !ReferenceEquals(completion, pending)) return;
                Trace(
                    $"Completed({expanded}) elapsed={started.ElapsedMilliseconds}ms width={surface.ActualWidth} height={surface.ActualHeight} icons={icons.Opacity}");

                pending.TrySetResult(true);
            };

        Trace(
            $"Begin({expanded})");

        storyboard.Begin();

        return pending.Task;
    }

    /// <summary>Retargets one continuous wave without restarting its spring clock.</summary>
    private void HoverLayoutChanged(object sender, SizeChangedEventArgs args)
    {
        if (disposed || interactionHeld || !hover.HasPointer) return;
        TickMagnification();
        if (hover.IsAnimating) StartMagnificationTimer();
    }

    public void UpdateMagnification(double pointerX, double pointerY)
    {
        if (disposed || interactionHeld || !hover.SetPointer(pointerX, pointerY)) return;
        TickMagnification();
        if (hover.IsAnimating) StartMagnificationTimer();
    }

    /// <summary>Releases both scale and translation before drag/reorder takes ownership.</summary>
    public void ResetMagnification(bool immediate = false)
    {
        if (immediate) { hover.Reset(); StopMagnificationTimer(); }
        else if (!hover.Leave()) return;
        TickMagnification();
        if (!immediate && hover.IsAnimating) StartMagnificationTimer();
    }

    private void TickMagnification()
    {
        if (disposed) return;
        var elements = icons.Children.OfType<FrameworkElement>().Where(element => element.IsLoaded).ToArray();
        var items = elements.Select(element => new DockHoverMagnification<FrameworkElement>.Item(
            element, element.ActualOffset.X, element.ActualOffset.Y, element.ActualWidth, element.ActualHeight,
            !string.Equals(element.Tag as string, "NoMagnify", StringComparison.Ordinal),
            Artwork(element)?.HoverArtworkWidth ?? element.ActualWidth)).ToArray();
        var frames = hover.Step(items, Environment.TickCount64, hoverSettings.AnimationsEnabled);
        var liveImages = new HashSet<Image>(ReferenceEqualityComparer.Instance);
        var liveBadges = new HashSet<NotificationBadge>(ReferenceEqualityComparer.Instance);
        var liveBadgeIcons = new HashSet<AdaptiveAppIcon>(ReferenceEqualityComparer.Instance);
        foreach (var frame in frames)
        {
            var element = frame.Id;
            ElementCompositionPreview.GetElementVisual(element).Scale = Vector3.One;
            if (Artwork(element) is { } artwork)
            {
                var artworkVisual = ElementCompositionPreview.GetElementVisual(artwork);
                // The actual icon never scales inside its Button: the Button's
                // native ContentPresenter can clip it even with ZIndex raised.
                // Magnification is drawn in the root-level foreground instead.
                artworkVisual.Scale = Vector3.One;
                DrawHoverArtworkInForeground(artwork, frame.Scale, liveImages);
                liveBadgeIcons.Add(artwork);
                if (observedBadgeIcons.Add(artwork)) artwork.HoverBadgeChanged += OnHoverBadgeChanged;
                DrawHoverBadgeInForeground(artwork, frame.Scale, liveBadges);
            }
            element.Translation = Vector3.Zero;
            Canvas.SetZIndex(element, (int)Math.Round((frame.Scale - 1) * 1000));
        }
        // Restore the originals on pointer exit, reorder, source replacement or
        // removal; never leave a real icon hidden behind a stale mirror.
        foreach (var pair in hoverArtworkMirrors.ToArray())
        {
            if (liveImages.Contains(pair.Key)) continue;
            pair.Key.Opacity = 1;
            pair.Value.Visibility = Visibility.Collapsed;
            if (pair.Key.IsLoaded) continue;
            hoverArtworkForeground.Children.Remove(pair.Value);
            hoverArtworkMirrors.Remove(pair.Key);
        }
        foreach (var pair in hoverBadgeMirrors.ToArray())
        {
            if (liveBadges.Contains(pair.Key)) continue;
            pair.Key.Opacity = 1;
            pair.Value.Visual.Visibility = Visibility.Collapsed;
            if (pair.Key.IsLoaded) continue;
            hoverArtworkForeground.Children.Remove(pair.Value.Visual);
            hoverBadgeMirrors.Remove(pair.Key);
        }
        foreach (var icon in observedBadgeIcons.ToArray())
        {
            if (liveBadgeIcons.Contains(icon)) continue;
            icon.HoverBadgeChanged -= OnHoverBadgeChanged;
            observedBadgeIcons.Remove(icon);
        }
        HoverPresentationChanged?.Invoke(this, EventArgs.Empty);
        if (!hover.IsAnimating) StopMagnificationTimer();
    }

    // Notification counts can change after the magnification spring settles.
    // Refresh the foreground badge once on the UI dispatcher without waking the
    // 60fps animation timer or disturbing the icon's current target scale.
    private void OnHoverBadgeChanged(object? sender, EventArgs args)
    {
        if (disposed || !hover.HasPointer || badgeRefreshQueued) return;
        badgeRefreshQueued = true;
        if (!icons.DispatcherQueue.TryEnqueue(() =>
        {
            badgeRefreshQueued = false;
            if (!disposed && hover.HasPointer) TickMagnification();
        })) badgeRefreshQueued = false;
    }

    private void DrawHoverBadgeInForeground(
        AdaptiveAppIcon icon,
        double scale,
        HashSet<NotificationBadge> liveBadges)
    {
        var badge = icon.LensBadge;
        if (badge is null || !badge.IsLoaded ||
            icon.HoverBadgeState is not { } display || !display.IsVisible)
            return;

        liveBadges.Add(badge);
        if (scale <= 1.001)
        {
            badge.Opacity = 1;
            if (hoverBadgeMirrors.TryGetValue(badge, out var inactive))
                inactive.Visual.Visibility = Visibility.Collapsed;
            return;
        }

        var width = badge.ActualWidth;
        var height = badge.ActualHeight;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            return;

        // The badge follows the exact same optical anchor and spring progress
        // as the mirrored bitmap, rather than the fixed-size Button/ContentPresenter.
        var bounds = badge.TransformToVisual(hoverArtworkForeground).TransformBounds(
            new global::Windows.Foundation.Rect(0, 0, width, height));
        var anchor = icon.TransformToVisual(hoverArtworkForeground).TransformPoint(
            new global::Windows.Foundation.Point(icon.ActualWidth / 2, icon.HoverArtworkBaseline));
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        if (!hoverBadgeMirrors.TryGetValue(badge, out var mirror))
        {
            mirror = new ForegroundBadge();
            mirror.Visual.RenderTransform = mirror.Scale;
            hoverBadgeMirrors.Add(badge, mirror);
            hoverArtworkForeground.Children.Add(mirror.Visual);
        }

        if (mirror.LastRevision != icon.HoverBadgeRevision)
        {
            mirror.Visual.SetBadge(display);
            mirror.LastRevision = icon.HoverBadgeRevision;
        }

        // Scale the entire badge (including its number/text), not just its
        // outline. Anchor both components to the same baseline as the icon.
        mirror.Visual.Width = bounds.Width;
        mirror.Visual.Height = bounds.Height;
        Canvas.SetLeft(mirror.Visual, anchor.X + (bounds.Left - anchor.X) * scale);
        Canvas.SetTop(mirror.Visual, anchor.Y + (bounds.Top - anchor.Y) * scale);
        mirror.Scale.ScaleX = mirror.Scale.ScaleY = scale;
        Canvas.SetZIndex(mirror.Visual, 10_000 + (int)Math.Round(scale * 1000));
        mirror.Visual.Visibility = Visibility.Visible;
        badge.Opacity = 0;
    }

    private void DrawHoverArtworkInForeground(
        AdaptiveAppIcon icon,
        double scale,
        HashSet<Image> liveImages)
    {
        var magnified = scale > 1.001;
        icon.VisitLensArtwork((_, source, _) =>
        {
            if (source is not Image image || !image.IsLoaded || image.Source is null)
                return;

            liveImages.Add(image);
            if (!magnified)
            {
                image.Opacity = 1;
                if (hoverArtworkMirrors.TryGetValue(image, out var existing))
                    existing.Visibility = Visibility.Collapsed;
                return;
            }

            var width = image.ActualWidth > 0 ? image.ActualWidth : image.Width;
            var height = image.ActualHeight > 0 ? image.ActualHeight : image.Height;
            if (!double.IsFinite(width) || !double.IsFinite(height) ||
                width <= 0 || height <= 0)
                return;

            // Positions are sampled from the unscaled image in root coordinates.
            // The final rectangle expands about the dock's existing optical
            // baseline; no button moves or gives up its original hit target.
            var bounds = image.TransformToVisual(hoverArtworkForeground).TransformBounds(
                new global::Windows.Foundation.Rect(0, 0, width, height));
            var anchor = icon.TransformToVisual(hoverArtworkForeground).TransformPoint(
                new global::Windows.Foundation.Point(icon.ActualWidth / 2, icon.HoverArtworkBaseline));
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            if (!hoverArtworkMirrors.TryGetValue(image, out var mirror))
            {
                mirror = new Image
                {
                    IsHitTestVisible = false,
                    UseLayoutRounding = false,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
                };
                hoverArtworkMirrors.Add(image, mirror);
                hoverArtworkForeground.Children.Add(mirror);
            }

            if (!ReferenceEquals(mirror.Source, image.Source)) mirror.Source = image.Source;
            mirror.Width = bounds.Width * scale;
            mirror.Height = bounds.Height * scale;
            Canvas.SetLeft(mirror, anchor.X + (bounds.Left - anchor.X) * scale);
            Canvas.SetTop(mirror, anchor.Y + (bounds.Top - anchor.Y) * scale);
            Canvas.SetZIndex(mirror, (int)Math.Round(scale * 1000));
            mirror.Visibility = Visibility.Visible;
            // The artwork and badge are both drawn above the glass in sync;
            // running indicators, focus visuals and tiles stay in their slots.
            image.Opacity = 0;
        });
    }

    private static AdaptiveAppIcon? Artwork(FrameworkElement element) =>
        element is Button { Content: Grid content } ? content.Children.OfType<AdaptiveAppIcon>().FirstOrDefault() : null;
    private void StartMagnificationTimer()
    {
        if (disposed || magnificationTimerRunning || interactionHeld)
            return;

        magnificationTimerRunning = true;
        magnificationTimer.Start();
    }

    private void StopMagnificationTimer()
    {
        if (!magnificationTimerRunning)
            return;

        magnificationTimerRunning = false;
        magnificationTimer.Stop();
    }

    internal static void Trace(
        string message)
    {
        System.Diagnostics.Debug.WriteLine(
            $"[DockAnimation] {message}");
    }

    private void Add(
        DependencyObject target,
        string property,
        double from,
        EasingMode easingMode,
        params (int Milliseconds, double Value)[] frames)
    {
        var animation =
            new DoubleAnimationUsingKeyFrames
            {
                EnableDependentAnimation = true
            };

        animation.KeyFrames.Add(
            new DiscreteDoubleKeyFrame
            {
                KeyTime =
                    KeyTime.FromTimeSpan(
                        TimeSpan.Zero),
                Value = from
            });

        foreach (var (milliseconds, value) in frames)
        {
            animation.KeyFrames.Add(
                new EasingDoubleKeyFrame
                {
                    KeyTime =
                        KeyTime.FromTimeSpan(
                            TimeSpan.FromMilliseconds(
                                milliseconds)),
                    Value = value,
                    EasingFunction =
                        new SineEase
                        {
                            EasingMode =
                                easingMode
                        }
                });
        }

        Storyboard.SetTarget(
            animation,
            target);

        Storyboard.SetTargetProperty(
            animation,
            property);

        active!.Children.Add(
            animation);
    }

    /// <summary>
    /// Stops an in-flight placement/shape transition while retaining the
    /// values currently rendered on screen. This is used only when a context
    /// menu takes ownership of the dock interaction; unlike Stop(), it does
    /// not reset icon magnification or otherwise rebuild the visual state.
    /// </summary>
    public bool FreezeCurrentTransitions()
    {
        var progress = Progress;
        var hadTransition = IsPlacementAnimating || active is not null || indicatorFade is not null;
        var bottom = surface.Margin.Bottom;
        var width = double.IsFinite(surface.Width) ? surface.Width : surface.ActualWidth;
        var height = double.IsFinite(surface.Height) ? surface.Height : surface.ActualHeight;
        var surfaceOpacity = surface.Opacity;
        var indicatorWidth = double.IsFinite(indicator.Width) ? indicator.Width : indicator.ActualWidth;
        var indicatorOpacity = indicator.Opacity;

        placementTimer.Stop();
        IsPlacementAnimating = false;
        var previous = active;
        active = null;
        previous?.Stop();
        completion?.TrySetResult(false);
        completion = null;
        indicatorFade?.Stop();
        indicatorFade = null;

        Progress = progress;
        ApplyProgress();
        surface.Width = width;
        surface.Height = height;
        surface.Opacity = surfaceOpacity;
        indicator.Width = indicatorWidth;
        indicator.Opacity = indicatorOpacity;
        ApplyBottom(bottom);
        return hadTransition;
    }

    public void Stop()
    {
        placementTimer.Stop(); IsPlacementAnimating = false;
        var previous = active;
        active = null;
        previous?.Stop();
        StopIndicatorOpacityAnimation();
        completion?.TrySetResult(false);
        completion = null;

        ResetMagnification(
            immediate: true);
        foreach (var pair in hoverArtworkMirrors)
        {
            pair.Key.Opacity = 1;
            hoverArtworkForeground.Children.Remove(pair.Value);
        }
        hoverArtworkMirrors.Clear();
        foreach (var pair in hoverBadgeMirrors)
        {
            pair.Key.Opacity = 1;
            hoverArtworkForeground.Children.Remove(pair.Value.Visual);
        }
        hoverBadgeMirrors.Clear();
        foreach (var icon in observedBadgeIcons) icon.HoverBadgeChanged -= OnHoverBadgeChanged;
        observedBadgeIcons.Clear();
        badgeRefreshQueued = false;

        active = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        Stop();
        disposed = true;
        progressClock.ValueChanged -= ProgressChanged;
        icons.SizeChanged -= HoverLayoutChanged;
        iconTransition.Dispose();
        PlacementChanged = null;
        HoverPresentationChanged = null;
    }
}
