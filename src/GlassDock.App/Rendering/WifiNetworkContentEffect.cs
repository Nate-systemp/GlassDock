using System.Numerics;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace GlassDock.App.Rendering;

/// <summary>Redirects only the network header; glass and its highlights remain outside this effect.</summary>
internal sealed class WifiNetworkContentEffect(Grid sourceHost, FrameworkElement content,
    Vector2? contentOffset = null, float cornerRadius = 18) : IDisposable
{
    public Canvas Element { get; } = new() { IsHitTestVisible = false };
    private readonly RectangleGeometry hidden = new() { Rect = new(0, 0, 0, 0) };
    private CompositionVisualSurface? surface;
    private CompositionSurfaceBrush? source;
    private CompositionEffectFactory? factory;
    private CompositionEffectBrush? brush;
    private SpriteVisual? image;
    private ContainerVisual? output;
    private CompositionRoundedRectangleGeometry? shape;
    private CompositionGeometricClip? clip;

    public void Update(double progress, double width, double height)
    {
        if (progress >= 1 || progress <= 0 || content.ActualWidth <= 0)
        {
            sourceHost.Clip = progress <= 0 ? hidden : null;
            if (output is not null) output.IsVisible = false;
            return;
        }
        if (surface is null)
        {
            var compositor = ElementCompositionPreview.GetElementVisual(content).Compositor;
            surface = compositor.CreateVisualSurface();
            surface.SourceVisual = ElementCompositionPreview.GetElementVisual(content);
            source = compositor.CreateSurfaceBrush(surface); source.Stretch = CompositionStretch.Fill;
            factory = compositor.CreateEffectFactory(new GaussianBlurEffect
            {
                Name = "Blur", BlurAmount = 0, BorderMode = EffectBorderMode.Soft,
                Source = new CompositionEffectSourceParameter("Content")
            }, new[] { "Blur.BlurAmount" });
            brush = factory.CreateBrush(); brush.SetSourceParameter("Content", source);
            image = compositor.CreateSpriteVisual(); image.Brush = brush;
            image.Offset = new(contentOffset ?? new Vector2(10, 5), 0);
            shape = compositor.CreateRoundedRectangleGeometry();
            clip = compositor.CreateGeometricClip(shape);
            output = compositor.CreateContainerVisual(); output.Clip = clip; output.Children.InsertAtTop(image);
            ElementCompositionPreview.SetElementChildVisual(Element, output);
        }
        surface.SourceSize = new((float)content.ActualWidth, (float)content.ActualHeight);
        image!.Size = surface.SourceSize;
        output!.Size = shape!.Size = new((float)width, (float)height);
        shape.CornerRadius = new((float)Math.Min(cornerRadius, height / 2));
        brush!.Properties.InsertScalar("Blur.BlurAmount", (float)((1 - progress) * 4));
        output.Opacity = (float)(progress * progress); output.IsVisible = true;
        // Hide through an ancestor clip: the visual surface still captures the live header subtree.
        sourceHost.Clip = hidden;
    }

    public void Dispose()
    {
        sourceHost.Clip = null; ElementCompositionPreview.SetElementChildVisual(Element, null);
        output?.Dispose(); image?.Dispose(); clip?.Dispose(); shape?.Dispose(); brush?.Dispose(); factory?.Dispose();
        source?.Dispose(); surface?.Dispose();
    }
}
