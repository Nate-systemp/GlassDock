using GlassDock.App.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using global::Windows.UI.ViewManagement;

namespace GlassDock.App.Desktop;

internal sealed class DockAnimationController(GlassSurface surface, FrameworkElement icons)
{
    private Storyboard? active;
    private TaskCompletionSource<bool>? completion;

    public Task<bool> AnimateAsync(bool expanded)
    {
        var width = surface.ActualWidth;
        var height = surface.ActualHeight;
        var opacity = icons.Opacity;
        active?.Stop();
        completion?.TrySetResult(false);
        surface.Width = width;
        surface.Height = height;
        icons.Opacity = opacity;
        var animationsEnabled = new UISettings().AnimationsEnabled;
        var duration = animationsEnabled ? 200 : 1;
        active = new Storyboard();
        Add(surface, "Width", width, expanded ? 560 : 120, duration);
        Add(surface, "Height", height, expanded ? 84 : 5, duration);
        Add(icons, "Opacity", opacity, expanded ? 1 : 0, animationsEnabled ? (expanded ? 150 : 90) : 1);
        var pending = new TaskCompletionSource<bool>();
        completion = pending;
        active.Completed += (_, _) => pending.TrySetResult(true);
        active.Begin();
        return pending.Task;
    }

    private void Add(DependencyObject target, string property, double from, double to, int milliseconds)
    {
        var animation = new DoubleAnimation
        {
            From = from, To = to, Duration = TimeSpan.FromMilliseconds(milliseconds),
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        active!.Children.Add(animation);
    }

    public void Stop()
    {
        active?.Stop();
        completion?.TrySetResult(false);
        active = null;
    }
}
