using System.Numerics;
using GlassDock.Core.Applications;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace GlassDock.App.Controls;

/// <summary>Non-interactive overlay; its parent's hover/reorder transform moves the complete icon.</summary>
internal sealed class NotificationBadge : Grid
{
    private readonly NotificationBadgeState state = new();
    private readonly Grid body = new()
    {
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top
    };
    private readonly Border fill = new()
    {
        Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 207, 38, 55))
    };
    private readonly Border pulse = new()
    {
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Background = new SolidColorBrush(Microsoft.UI.Colors.Red),
        Opacity = 0
    };
    private readonly TextBlock number = new()
    {
        FontSize = 10,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsTextScaleFactorEnabled = false
    };
    private readonly global::Windows.UI.ViewManagement.UISettings ui = new();
    private CompositionScopedBatch? batch;
    private int revision;
    private bool awaitingLoad;

    internal FrameworkElement LensBody => body;
    internal string LensText => number.Visibility == Visibility.Visible ? number.Text : string.Empty;

    public NotificationBadge()
    {
        Width = Height = 20;
        IsHitTestVisible = false;
        Children.Add(pulse);
        body.Children.Add(fill);
        body.Children.Add(number);
        Children.Add(body);
        Visibility = Visibility.Collapsed;
        ApplyPresentationGeometry();

        Loaded += (_, _) =>
        {
            if (!awaitingLoad) return;
            awaitingLoad = false;
            var display = state.Display;
            state.SetDisplay(BadgeDisplayState.None);
            SetBadge(display);
        };
        Unloaded += (_, _) =>
        {
            revision++;
            Stop();
            SetStable();
        };
    }

    public void SetCount(int count) => SetBadge(BadgeDisplayState.Counted(count, "legacy-count"));

    public void SetBadge(BadgeDisplayState display)
    {
        var previous = state.Display;
        var transition = state.SetDisplay(display);
        if (transition == BadgeTransition.None)
            return; // Source/timestamp metadata changed, but the visible badge did not.

        var version = ++revision;
        Stop();
        ApplyPresentationGeometry(transition == BadgeTransition.Remove ? previous : state.Display);
        Visibility = Visibility.Visible;

        if (!IsLoaded)
        {
            awaitingLoad = state.Display.IsVisible;
            SetStable();
            return;
        }
        if (!ui.AnimationsEnabled)
        {
            SetStable();
            return;
        }

        var shell = ElementCompositionPreview.GetElementVisual(body);
        var text = ElementCompositionPreview.GetElementVisual(number);
        var glow = ElementCompositionPreview.GetElementVisual(pulse);
        var animatedDisplay = transition == BadgeTransition.Remove ? previous : state.Display;
        var diameter = animatedDisplay.Kind == BadgeKind.Count ? 20f : 8f;
        shell.CenterPoint = glow.CenterPoint = new(diameter / 2, diameter / 2, 0);
        text.CenterPoint = new((float)Math.Max(1, number.ActualWidth) / 2, (float)Math.Max(1, number.ActualHeight) / 2, 0);

        var duration = transition == BadgeTransition.Appear ? 440 : transition == BadgeTransition.Remove ? 220 : 180;
        batch = shell.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        if (transition == BadgeTransition.Appear)
        {
            AnimateScale(shell, duration, (0, .18f), (.22f, .25f), (.62f, 1.04f), (1, 1));
            Fade(shell, duration, (0, 0), (.20f, 1), (1, 1));

            if (state.Display.Kind == BadgeKind.Count)
            {
                Fade(text, duration, (0, 0), (.52f, 0), (.78f, 1), (1, 1));
                AnimateScale(text, duration, (0, .75f), (.52f, .75f), (.78f, 1), (1, 1));
            }

            Fade(glow, duration, (0, 0), (.62f, 0), (.72f, .20f), (1, 0));
            AnimateScale(glow, duration, (0, 1), (.62f, 1), (1, 1.55f));
        }
        else if (transition == BadgeTransition.Remove)
        {
            if (previous.Kind == BadgeKind.Count)
                Fade(text, duration, (0, 1), (.4f, 0), (1, 0));
            AnimateScale(shell, duration, (0, 1), (.7f, .18f), (1, .18f));
            Fade(shell, duration, (0, 1), (.7f, 1), (1, 0));
        }
        else if (previous.Kind != state.Display.Kind)
        {
            shell.Opacity = 1;
            var startScale = previous.Kind == BadgeKind.Activity && state.Display.Kind == BadgeKind.Count
                ? .4f
                : previous.Kind == BadgeKind.Count && state.Display.Kind == BadgeKind.Activity ? 2.5f : 1.45f;
            AnimateScale(shell, duration, (0, startScale), (.65f, 1.04f), (1, 1));
            if (state.Display.Kind == BadgeKind.Count)
            {
                Fade(text, duration, (0, 0), (.45f, 0), (1, 1));
                AnimateScale(text, duration, (0, .78f), (1, 1));
            }
        }
        else
        {
            shell.Opacity = 1;
            AnimateScale(shell, duration, (0, 1), (.4f, transition == BadgeTransition.Increase ? 1.08f : 1), (1, 1));
            if (state.Display.Kind == BadgeKind.Count)
                Fade(text, duration, (0, .35f), (1, 1));
        }

        batch.Completed += (_, _) =>
        {
            if (revision == version)
            {
                Stop();
                SetStable();
            }
        };
        batch.End();
    }

    private void ApplyPresentationGeometry(BadgeDisplayState? display = null)
    {
        var presentation = display ?? state.Display;
        var count = presentation.Kind == BadgeKind.Count;
        var diameter = count ? 20d : 8d;
        body.Width = body.Height = diameter;
        pulse.Width = pulse.Height = diameter;
        fill.CornerRadius = pulse.CornerRadius = new CornerRadius(diameter / 2);
        number.Visibility = count ? Visibility.Visible : Visibility.Collapsed;
        if (count) number.Text = presentation.Count > 99 ? "99+" : presentation.Count.ToString();
    }

    private static void Fade(Visual visual, int milliseconds, params (float At, float Value)[] keys)
    {
        using var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        using var easing = visual.Compositor.CreateCubicBezierEasingFunction(new(.2f, 0), new(.2f, 1));
        animation.Duration = TimeSpan.FromMilliseconds(milliseconds);
        foreach (var key in keys) animation.InsertKeyFrame(key.At, key.Value, easing);
        visual.StartAnimation("Opacity", animation);
    }

    private static void AnimateScale(Visual visual, int milliseconds, params (float At, float Value)[] keys)
    {
        using var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        using var easing = visual.Compositor.CreateCubicBezierEasingFunction(new(.2f, 0), new(.2f, 1));
        animation.Duration = TimeSpan.FromMilliseconds(milliseconds);
        foreach (var key in keys) animation.InsertKeyFrame(key.At, new(key.Value, key.Value, 1), easing);
        visual.StartAnimation("Scale", animation);
    }

    private void Stop()
    {
        batch?.Dispose();
        batch = null;
        foreach (var element in new UIElement[] { body, number, pulse })
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            visual.StopAnimation("Scale");
            visual.StopAnimation("Opacity");
            visual.Scale = Vector3.One;
            visual.Opacity = ReferenceEquals(element, pulse) ? 0 : 1;
        }
    }

    private void SetStable()
    {
        ApplyPresentationGeometry();
        Visibility = state.Display.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        number.Text = state.Display.Kind == BadgeKind.Count ? state.Text : string.Empty;
    }
}
