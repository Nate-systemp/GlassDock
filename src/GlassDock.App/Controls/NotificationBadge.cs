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
    private readonly Grid body = new();
    private readonly Border pulse = new() { CornerRadius = new(10), Background = new SolidColorBrush(Microsoft.UI.Colors.Red), Opacity = 0 };
    private readonly TextBlock number = new() { FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center, IsTextScaleFactorEnabled = false };
    private readonly global::Windows.UI.ViewManagement.UISettings ui = new();
    private CompositionScopedBatch? batch;
    private int revision;
    private bool awaitingLoad;

    public NotificationBadge()
    {
        Width = Height = 20;
        IsHitTestVisible = false;
        Children.Add(pulse);
        body.Children.Add(new Border { CornerRadius = new(10), Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 207, 38, 55)) });
        body.Children.Add(number);
        Children.Add(body);
        Visibility = Visibility.Collapsed;
        Loaded += (_, _) =>
        {
            if (!awaitingLoad) return;
            awaitingLoad = false;
            var count = state.Count;
            state.SetCount(0);
            SetCount(count);
        };
        Unloaded += (_, _) => { revision++; Stop(); SetStable(); };
    }

    public void SetCount(int count)
    {
        var transition = state.SetCount(count);
        if (transition == BadgeTransition.None) return;
        var version = ++revision;
        Stop();
        if (state.Count > 0) number.Text = state.Text;
        Visibility = Visibility.Visible;
        if (!IsLoaded) { awaitingLoad = state.Count > 0; SetStable(); return; }
        if (!ui.AnimationsEnabled) { SetStable(); return; }
        var shell = ElementCompositionPreview.GetElementVisual(body);
        var text = ElementCompositionPreview.GetElementVisual(number);
        var glow = ElementCompositionPreview.GetElementVisual(pulse);
        shell.CenterPoint = glow.CenterPoint = new(10, 10, 0);
        text.CenterPoint = new((float)number.ActualWidth / 2, (float)number.ActualHeight / 2, 0);
        var duration = transition == BadgeTransition.Appear ? 440 : transition == BadgeTransition.Remove ? 220 : 180;
        batch = shell.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        if (transition == BadgeTransition.Appear)
        {
            AnimateScale(shell, duration, (0, .18f), (.22f, .25f), (.62f, 1.04f), (1, 1));
            Fade(shell, duration, (0, 0), (.20f, 1), (1, 1));
            Fade(text, duration, (0, 0), (.52f, 0), (.78f, 1), (1, 1));
            AnimateScale(text, duration, (0, .75f), (.52f, .75f), (.78f, 1), (1, 1));
            Fade(glow, duration, (0, 0), (.62f, 0), (.72f, .20f), (1, 0));
            AnimateScale(glow, duration, (0, 1), (.62f, 1), (1, 1.55f));
        }
        else if (transition == BadgeTransition.Remove)
        {
            Fade(text, duration, (0, 1), (.4f, 0), (1, 0));
            AnimateScale(shell, duration, (0, 1), (.7f, .18f), (1, .18f));
            Fade(shell, duration, (0, 1), (.7f, 1), (1, 0));
        }
        else
        {
            shell.Opacity = 1;
            AnimateScale(shell, duration, (0, 1), (.4f, transition == BadgeTransition.Increase ? 1.08f : 1), (1, 1));
            Fade(text, duration, (0, .35f), (1, 1));
        }
        batch.Completed += (_, _) => { if (revision == version) { Stop(); SetStable(); } };
        batch.End();
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
        batch?.Dispose(); batch = null;
        foreach (var element in new UIElement[] { body, number, pulse })
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            visual.StopAnimation("Scale"); visual.StopAnimation("Opacity");
            visual.Scale = Vector3.One; visual.Opacity = ReferenceEquals(element, pulse) ? 0 : 1;
        }
    }

    private void SetStable()
    {
        Visibility = state.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        number.Text = state.Count == 0 ? "" : state.Text;
    }
}
