using System.Numerics;
using System.Runtime.InteropServices;
using GlassDock.Core.Materials;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using W = global::Windows.UI.Composition;

namespace GlassDock.App.Rendering;

/// <summary>The OS compositor supplies desktop pixels; the laboratory's graph supplies the material.</summary>
internal sealed class DesktopGlassBackdrop : SystemBackdrop
{
    private W.Compositor? compositor;
    private W.CompositionEffectFactory? factory;
    private W.CompositionEffectBrush? effect;
    private W.CompositionBackdropBrush? source;
    private W.CompositionRoundedRectangleGeometry? geometry;
    private W.CompositionSpriteShape? shape;
    private W.CompositionColorBrush? fill;
    private W.ShapeVisual? visual;
    private W.CompositionVisualSurface? maskSurface;
    private W.CompositionSurfaceBrush? mask;
    private W.CompositionMaskBrush? output;
    private W.CompositionBrush? fallback;
    private GlassMaterial material = new();

    // Retain the latest mask geometry because AppWindow.Hide()/Show()
    // can disconnect and reconnect the SystemBackdrop.
    private bool hasBounds;
    private double lastWindowWidth;
    private double lastWindowHeight;
    private double lastWidth;
    private double lastHeight;
    private double lastBottom;
    private double lastScale = 1;
    private double lastOpacity = 1;

    public string RenderingMode { get; private set; } = "Desktop backdrop connecting";
    public event EventHandler? RenderingModeChanged;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        compositor = new W.Compositor();
        geometry = compositor.CreateRoundedRectangleGeometry();
        shape = compositor.CreateSpriteShape(geometry);
        fill = compositor.CreateColorBrush(global::Windows.UI.Color.FromArgb(255, 255, 255, 255));
        shape.FillBrush = fill;
        visual = compositor.CreateShapeVisual();
        visual.Shapes.Add(shape);
        maskSurface = compositor.CreateVisualSurface();
        maskSurface.SourceVisual = visual;
        mask = compositor.CreateSurfaceBrush(maskSurface);
        output = compositor.CreateMaskBrush();
        output.Mask = mask;
        var stage = "backdrop source";
        try
        {
            source = compositor.CreateBackdropBrush();
            stage = "effect factory";
            // Separate named leaves keep the graph tree-shaped. Both sample the same
            // live desktop brush; neither material branch reintroduces sharp pixels.
            factory = compositor.CreateEffectFactory(
                GlassEffectGraph.Create(new W.CompositionEffectSourceParameter("Backdrop"), new W.CompositionEffectSourceParameter("BaseBackdrop")),
                GlassEffectGraph.Properties.Concat(["BaseBlur.BlurAmount"]));
            stage = "effect brush";
            effect = factory.CreateBrush();
            effect.SetSourceParameter("Backdrop", source);
            effect.SetSourceParameter("BaseBackdrop", source);
            output.Source = effect;
            RenderingMode = "Native system backdrop · shared glass graph";
            stage = "material parameters";
            Apply(material);

            if (hasBounds)
                ApplyBounds(
                    lastWindowWidth,
                    lastWindowHeight,
                    lastWidth,
                    lastHeight,
                    lastBottom,
                    lastScale,
                    lastOpacity);
        }
        catch (Exception exception) when (exception is COMException or ArgumentException)
        {
            fallback = compositor.CreateColorBrush(global::Windows.UI.Color.FromArgb(230, 35, 45, 62));
            output.Source = fallback;
            RenderingMode = $"Desktop solid fallback: {stage} (0x{exception.HResult:X8})";
            System.Diagnostics.Debug.WriteLine(exception);
        }
        target.SystemBackdrop = output;
        RenderingModeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Apply(GlassMaterial value)
    {
        material = RefractionLayer.ForNativeBackend(value);
        if (effect is null) return;
        foreach (var (name, scalar) in GlassEffectGraph.Scalars(material)) effect.Properties.InsertScalar(name, scalar);
        effect.Properties.InsertScalar("BaseBlur.BlurAmount", (float)material.BlurAmount);
        effect.Properties.InsertColor("Tint.Color", GlassEffectGraph.Tint(material));
    }

    public void SetBounds(
        double windowWidth,
        double windowHeight,
        double width,
        double height,
        double bottom,
        double scale,
        double opacity = 1)
    {
        hasBounds = true;
        lastWindowWidth = windowWidth;
        lastWindowHeight = windowHeight;
        lastWidth = width;
        lastHeight = height;
        lastBottom = bottom;
        lastScale = scale;
        lastOpacity = opacity;

        ApplyBounds(
            windowWidth,
            windowHeight,
            width,
            height,
            bottom,
            scale,
            opacity);
    }

    private void ApplyBounds(
        double windowWidth,
        double windowHeight,
        double width,
        double height,
        double bottom,
        double scale,
        double opacity)
    {
        if (geometry is null ||
            visual is null ||
            maskSurface is null)
        {
            return;
        }

        visual.Opacity = (float)opacity;

        var size =
            new Vector2(
                (float)(windowWidth * scale),
                (float)(windowHeight * scale));

        visual.Size = size;
        maskSurface.SourceSize = size;

        geometry.Offset =
            new Vector2(
                (float)((windowWidth - width) / 2 * scale),
                (float)((windowHeight - bottom - height) * scale));

        geometry.Size =
            new Vector2(
                (float)(width * scale),
                (float)(height * scale));

        var radius =
            (float)(
                Math.Min(
                    material.CornerRadius,
                    height / 2) *
                scale);

        geometry.CornerRadius =
            new Vector2(radius);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        target.SystemBackdrop = null;
        output?.Dispose();
        mask?.Dispose();
        maskSurface?.Dispose();
        visual?.Dispose();
        shape?.Dispose();
        fill?.Dispose();
        geometry?.Dispose();
        fallback?.Dispose();
        effect?.Dispose();
        factory?.Dispose();
        source?.Dispose();
        compositor?.Dispose();
        output = null; mask = null; maskSurface = null; visual = null; shape = null; fill = null;
        geometry = null; fallback = null; effect = null; factory = null; source = null; compositor = null;
        base.OnTargetDisconnected(target);
    }
}
