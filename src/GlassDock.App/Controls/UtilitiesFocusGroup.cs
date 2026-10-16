using System.Numerics;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace GlassDock.App.Controls;

/// <summary>Captures only this group's glyphs/text on the compositor, never the glass.</summary>
internal sealed class UtilitiesFocusGroup : Grid, IDisposable
{
    internal const float Bleed = 6f;
    private const float FrostedBlurDip = 1.2f;
    private const float FrostedOpacity = 0.70f;
    private static readonly TimeSpan TransitionDuration = TimeSpan.FromMilliseconds(180);

    private readonly FrameworkElement content;
    private readonly Grid sourceHost = new();
    private readonly Grid captureRoot = new();
    private CompositionVisualSurface? surface;
    private CompositionSurfaceBrush? source;
    private CompositionEffectFactory? factory;
    private CompositionEffectBrush? effect;
    private SpriteVisual? output;
    private XamlRoot? observedRoot;
    private float currentDpi = 1f;
    private GlassDock.Core.Desktop.UtilitiesGlyphGeometry? appliedGeometry;
    private bool frosted, disposed;

    public UtilitiesFocusGroup(FrameworkElement content)
    {
        this.content = content;
        VerticalAlignment = VerticalAlignment.Center;
        captureRoot.Children.Add(content);
        sourceHost.Children.Add(captureRoot);
        Children.Add(sourceHost);
        Loaded += (_, _) => { AttachRoot(); Connect(); Resize(); };
        SizeChanged += (_, _) => { Connect(); Resize(); };
        content.SizeChanged += (_, _) => { Connect(); Resize(); };
        Unloaded += (_, _) => { DetachRoot(); Disconnect(); };
    }

    private void AttachRoot()
    {
        if (disposed || XamlRoot is null || ReferenceEquals(observedRoot, XamlRoot)) return;
        DetachRoot();
        observedRoot = XamlRoot;
        observedRoot.Changed += OnXamlRootChanged;
    }

    private void DetachRoot()
    {
        if (observedRoot is null) return;
        observedRoot.Changed -= OnXamlRootChanged;
        observedRoot = null;
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (disposed) return;
        Connect();
        Resize();
    }

    private void Connect()
    {
        if (disposed || effect is not null || ActualWidth <= 0 || ActualHeight <= 0) return;
        try
        {
            AttachRoot();
            var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
            surface = compositor.CreateVisualSurface();
            surface.SourceVisual = ElementCompositionPreview.GetElementVisual(captureRoot);
            source = compositor.CreateSurfaceBrush(surface);
            source.Stretch = CompositionStretch.Fill;
            source.BitmapInterpolationMode = CompositionBitmapInterpolationMode.Linear;
            factory = compositor.CreateEffectFactory(new GaussianBlurEffect
            {
                Name = "Frost",
                BlurAmount = 0f,
                BorderMode = EffectBorderMode.Soft,
                Optimization = EffectOptimization.Quality,
                Source = new CompositionEffectSourceParameter("Glyphs")
            }, new[] { "Frost.BlurAmount" });
            effect = factory.CreateBrush();
            effect.SetSourceParameter("Glyphs", source);
            output = compositor.CreateSpriteVisual();
            output.BorderMode = CompositionBorderMode.Soft;
            output.Brush = effect;
            ElementCompositionPreview.SetElementChildVisual(this, output);
            // The capture starts at captureRoot, so it excludes this ancestor's opacity.
            ElementCompositionPreview.GetElementVisual(sourceHost).Opacity = 0f;
            Resize();
            Apply(false);
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            Disconnect();
            System.Diagnostics.Debug.WriteLine($"Utilities glyph frost unavailable: {error.Message}");
        }
    }

    private void Resize()
    {
        if (output is null || surface is null || ActualWidth <= 0 || ActualHeight <= 0) return;
        var next = GlassDock.Core.Desktop.UtilitiesGlyphGeometry.Create(
            ActualWidth, ActualHeight, XamlRoot?.RasterizationScale ?? 1.0, Bleed);
        if (appliedGeometry == next) return;
        var dpi = next.Dpi;
        var bleedPx = next.BleedPixels;
        var bleedDip = bleedPx / dpi;
        var totalWidthPx = next.PixelSize.X;
        var totalHeightPx = next.PixelSize.Y;

        // Render content at physical pixel scale inside captureRoot so CompositionVisualSurface
        // preserves native high-DPI glyph sharpness, while captureRoot's inverse scale keeps
        // outer XAML layout and hit-testing coordinates unchanged.
        ElementCompositionPreview.GetElementVisual(content).Scale = new Vector3(dpi, dpi, 1f);
        ElementCompositionPreview.GetElementVisual(captureRoot).Scale = new Vector3(1f / dpi, 1f / dpi, 1f);

        surface.SourceOffset = new Vector2(-bleedPx, -bleedPx);
        surface.SourceSize = new Vector2(totalWidthPx, totalHeightPx);
        output.Offset = new Vector3(-bleedDip, -bleedDip, 0f);
        output.Size = new Vector2(totalWidthPx / dpi, totalHeightPx / dpi);
        appliedGeometry = next;

        if (MathF.Abs(dpi - currentDpi) > 0.001f)
        {
            currentDpi = dpi;
            if (frosted) Apply(false);
        }
    }

    public void SetFrosted(bool value)
    {
        if (frosted == value) return;
        frosted = value;
        Apply(new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled);
    }

    private void Apply(bool animate)
    {
        var targetOpacity = frosted ? FrostedOpacity : 1f;
        if (effect is null || output is null)
        {
            // Fallback when GPU effect is unavailable: smoothly attenuate opacity on the host visual.
            var hostVisual = ElementCompositionPreview.GetElementVisual(sourceHost);
            if (!animate)
            {
                hostVisual.StopAnimation("Opacity");
                hostVisual.Opacity = targetOpacity;
                return;
            }
            using var fallbackEasing = hostVisual.Compositor.CreateCubicBezierEasingFunction(new(.2f, 0f), new(.2f, 1f));
            using var fallbackOpacity = hostVisual.Compositor.CreateScalarKeyFrameAnimation();
            fallbackOpacity.InsertKeyFrame(1f, targetOpacity, fallbackEasing);
            fallbackOpacity.Duration = TransitionDuration;
            hostVisual.StartAnimation("Opacity", fallbackOpacity);
            return;
        }

        var targetBlur = frosted ? FrostedBlurDip * currentDpi : 0f;
        if (!animate)
        {
            effect.StopAnimation("Frost.BlurAmount");
            effect.Properties.InsertScalar("Frost.BlurAmount", targetBlur);
            output.StopAnimation("Opacity");
            output.Opacity = targetOpacity;
            return;
        }

        using var easing = effect.Compositor.CreateCubicBezierEasingFunction(new(.2f, 0f), new(.2f, 1f));
        using var blurAnimation = effect.Compositor.CreateScalarKeyFrameAnimation();
        blurAnimation.InsertKeyFrame(1f, targetBlur, easing);
        blurAnimation.Duration = TransitionDuration;
        effect.StartAnimation("Frost.BlurAmount", blurAnimation);

        using var opacityAnimation = output.Compositor.CreateScalarKeyFrameAnimation();
        opacityAnimation.InsertKeyFrame(1f, targetOpacity, easing);
        opacityAnimation.Duration = TransitionDuration;
        output.StartAnimation("Opacity", opacityAnimation);
    }

    private void Disconnect()
    {
        appliedGeometry = null;
        ElementCompositionPreview.SetElementChildVisual(this, null);
        ElementCompositionPreview.GetElementVisual(content).Scale = Vector3.One;
        ElementCompositionPreview.GetElementVisual(captureRoot).Scale = Vector3.One;
        ElementCompositionPreview.GetElementVisual(sourceHost).Opacity = 1f;
        output?.Dispose(); effect?.Dispose(); factory?.Dispose(); source?.Dispose(); surface?.Dispose();
        output = null; effect = null; factory = null; source = null; surface = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        DetachRoot();
        Disconnect();
    }
}
