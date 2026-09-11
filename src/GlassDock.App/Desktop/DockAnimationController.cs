using GlassDock.App.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using global::Windows.UI.ViewManagement;

namespace GlassDock.App.Desktop;

internal sealed class DockAnimationController(GlassSurface surface, FrameworkElement icons, FrameworkElement indicator)
{
    private Storyboard? active;
    private TaskCompletionSource<bool>? completion;

    public Task<bool> AnimateAsync(bool expanded)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        Trace($"AnimateAsync({expanded}), previousPending={completion is { Task.IsCompleted: false }}");
        var width = surface.ActualWidth;
        var height = surface.ActualHeight;
        var opacity = icons.Opacity;
        var surfaceOpacity = surface.Opacity;
        var indicatorOpacity = indicator.Opacity;
        var indicatorWidth = indicator.ActualWidth;
        active?.Stop();
        completion?.TrySetResult(false);
        surface.Width = width;
        surface.Height = height;
        icons.Opacity = opacity;
        surface.Opacity = surfaceOpacity;
        indicator.Opacity = indicatorOpacity;
        indicator.Width = indicatorWidth;
        var animationsEnabled = new UISettings().AnimationsEnabled;
        Trace($"AnimationsEnabled={animationsEnabled}");
        active = new Storyboard();
        // Keep this explicitly requested transformation visible even when Windows
        // disables optional animations. Other UI animations retain their OS policy.
        if (expanded)
        {
            // Diretso na papunta sa final size, walang medium stop
            Add(indicator, "Opacity", indicatorOpacity, (150, indicatorOpacity), (300, 0));
            Add(surface, "Width", width, (420, 560));
            Add(surface, "Height", height, (420, 84));
            Add(surface, "Opacity", surfaceOpacity, (140, surfaceOpacity), (440, 1));
            Add(icons, "Opacity", opacity, (400, opacity), (720, 1));
        }
        else
        {
            // Start contracting before the icons fade; finish as the plain indicator.
            Add(surface, "Width", width, (620, 120));
            Add(surface, "Height", height, (500, 5));
            Add(icons, "Opacity", opacity, (60, opacity), (260, 0));
            Add(surface, "Opacity", surfaceOpacity, (340, surfaceOpacity), (580, 0));
            Add(indicator, "Width", indicatorWidth, (340, indicatorWidth), (620, 120));
            Add(indicator, "Opacity", indicatorOpacity, (400, indicatorOpacity), (620, 1));
        }
        var pending = new TaskCompletionSource<bool>();
        completion = pending;
        active.Completed += (_, _) => { Trace($"Completed({expanded}) elapsed={started.ElapsedMilliseconds}ms width={surface.ActualWidth} height={surface.ActualHeight} icons={icons.Opacity}"); pending.TrySetResult(true); };
        Trace($"Begin({expanded})");
        active.Begin();
        return pending.Task;
    }

    internal static void Trace(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[DockAnimation] {message}");
    }

    private void Add(DependencyObject target, string property, double from, params (int Milliseconds, double Value)[] frames)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            EnableDependentAnimation = true
        };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = from });
        foreach (var (milliseconds, value) in frames)
        {
            animation.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds)), Value = value,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            });
        }
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
