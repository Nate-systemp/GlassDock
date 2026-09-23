using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Effects;
using GlassDock.Core.Materials;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using W = global::Windows.UI.Composition;

namespace GlassDock.App.Rendering;

/// <summary>The OS compositor supplies desktop pixels; the laboratory's graph supplies the material.</summary>
internal sealed class DesktopGlassBackdrop : SystemBackdrop
{
    public bool UseInnerEdge { get; set; }
    private double presentationScale = 1, presentationOffset, presentationX, presentationY, presentationOpacity = 1;
    public void SetPresentation(double scale, double offsetY, double originX, double originY, double opacity)
    {
        presentationScale = scale;
        presentationOffset = offsetY;
        presentationX = originX;
        presentationY = originY;
        presentationOpacity = opacity;
        if (visual is null) return;
        var units = (float)(lastScale * (UseInnerEdge ? 2 : 1));
        var origin = new Vector3((float)originX * units, (float)originY * units, 0);
        var matrix = Matrix4x4.CreateTranslation(-origin) *
            Matrix4x4.CreateScale((float)scale, (float)scale, 1) *
            Matrix4x4.CreateTranslation(origin + new Vector3(0, (float)offsetY * units, 0));
        visual.TransformMatrix = matrix;
        visual.Opacity = (float)(opacity * lastOpacity);
        if (edgeVisual is not null) edgeVisual.TransformMatrix = matrix;
    }
    private W.CompositionEffectFactory? edgeFactory;
    private W.CompositionEffectBrush? edgeEffect;
    private W.CompositionSpriteShape? edgeShape;
    private W.ShapeVisual? edgeVisual;
    private W.CompositionVisualSurface? edgeSurface;
    private W.CompositionSurfaceBrush? edgeMask;
    private W.CompositionColorBrush? edgeFill;
    private W.Compositor? compositor;
    private W.CompositionEffectFactory? factory;
    private W.CompositionEffectBrush? effect;
    private W.CompositionBackdropBrush? source;
    private W.CompositionPathGeometry? geometry;
    private W.CompositionSpriteShape? shape;
    private CanvasDevice? canvasDevice;
    private CanvasGeometry? canvasGeometry;
    private readonly record struct MaskState(double WindowWidth, double WindowHeight, double Width,
        double Height, double Bottom, double Scale, double Radius, bool Wave, double Center,
        double HalfWidth, double Rise, double Strength);
    private MaskState? renderedMask;
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

    // Continuous dock-wave state. This deforms the TOP EDGE of the same
    // dock silhouette; it is not a second pill/shape layered above it.
    private bool waveEnabled;
    private double waveCenterX;
    private double waveHalfWidth = 68;
    private double waveRise = 18;
    private double waveStrength;

    public string RenderingMode { get; private set; } = "Desktop backdrop connecting";
    public event EventHandler? RenderingModeChanged;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        compositor = new W.Compositor();
        canvasDevice = CanvasDevice.GetSharedDevice();
        geometry = compositor.CreatePathGeometry();
        shape = compositor.CreateSpriteShape(geometry);
        fill = compositor.CreateColorBrush(global::Windows.UI.Color.FromArgb(255, 255, 255, 255));
        shape.FillBrush = fill;
        visual = compositor.CreateShapeVisual();
        // These off-tree mask visuals have no XAML parent to supply soft edge
        // composition. Explicitly request antialiased bitmap/clip boundaries.
        visual.BorderMode = W.CompositionBorderMode.Soft;
        visual.Shapes.Add(shape);
        maskSurface = compositor.CreateVisualSurface();
        maskSurface.SourceVisual = visual;
        mask = compositor.CreateSurfaceBrush(maskSurface);
        mask.Stretch = W.CompositionStretch.Fill;
        mask.BitmapInterpolationMode = W.CompositionBitmapInterpolationMode.Linear;
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
            if (UseInnerEdge)
            {
                // One vector path owns both the body mask and the inner perimeter.
                // The final outer mask clips the centered stroke to its inner half.
                edgeShape = compositor.CreateSpriteShape(geometry);
                edgeFill = compositor.CreateColorBrush(global::Windows.UI.Color.FromArgb(30, 255, 255, 255));
                edgeShape.StrokeBrush = edgeFill;
                edgeVisual = compositor.CreateShapeVisual();
                edgeVisual.BorderMode = W.CompositionBorderMode.Soft;
                edgeVisual.Shapes.Add(edgeShape);
                edgeSurface = compositor.CreateVisualSurface();
                edgeSurface.SourceVisual = edgeVisual;
                edgeMask = compositor.CreateSurfaceBrush(edgeSurface);
                edgeMask.Stretch = W.CompositionStretch.Fill;
                edgeMask.BitmapInterpolationMode = W.CompositionBitmapInterpolationMode.Linear;
                edgeFactory = compositor.CreateEffectFactory(new CompositeEffect
                {
                    Mode = CanvasComposite.SourceOver,
                    Sources =
                    {
                        new W.CompositionEffectSourceParameter("Body"),
                        new AlphaMaskEffect
                        {
                            Source = new ExposureEffect { Exposure = .35f,
                                Source = new SaturationEffect { Saturation = 1.12f,
                                    Source = new W.CompositionEffectSourceParameter("BodyEdge") } },
                            AlphaMask = new W.CompositionEffectSourceParameter("EdgeMask")
                        }
                    }
                });
                edgeEffect = edgeFactory.CreateBrush();
                edgeEffect.SetSourceParameter("Body", effect);
                edgeEffect.SetSourceParameter("BodyEdge", effect);
                edgeEffect.SetSourceParameter("EdgeMask", edgeMask);
                output.Source = edgeEffect;
            }
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
        if (edgeShape is not null)
        {
            edgeShape.StrokeThickness = (float)(material.BorderThickness * 6 * lastScale);
            edgeFill!.Color = global::Windows.UI.Color.FromArgb((byte)(material.BorderOpacity * 100), 255, 255, 255);
        }

        if (effect is not null)
        {
            foreach (var (name, scalar) in GlassEffectGraph.Scalars(material))
                effect.Properties.InsertScalar(name, scalar);

            effect.Properties.InsertScalar(
                "BaseBlur.BlurAmount",
                (float)material.BlurAmount);

            effect.Properties.InsertColor(
                "Tint.Color",
                GlassEffectGraph.Tint(material));
        }

        UpdateMaskPath();
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

        SetPresentation(presentationScale, presentationOffset, presentationX, presentationY, presentationOpacity);

        var size =
            new Vector2(
                (float)(windowWidth * scale),
                (float)(windowHeight * scale));

        // Supersample the vector coverage mask (not the desktop pixels). The
        // compositor then linearly downsamples the mask, preserving subpixel
        // coverage on rounded corners and the animated wave crest. The legacy
        // XAML rim is now geometry-only, so this is the sole visible edge path.
        var sampling = UseInnerEdge ? 2f : 1f;
        shape!.Scale = new Vector2(sampling);
        visual.Size = size * sampling;
        maskSurface.SourceSize = size * sampling;
        if (edgeVisual is not null)
        {
            edgeShape!.Scale = new Vector2(sampling);
            edgeShape.StrokeThickness = (float)(material.BorderThickness * 6 * scale);
            edgeVisual.Size = size * sampling;
            edgeSurface!.SourceSize = size * sampling;
        }

        UpdateMaskPath();
    }

    /// <summary>
    /// Deforms the dock's existing top edge into one smooth wave.
    /// All values are in root/window DIPs.
    /// </summary>
    public void SetDockWave(
        double centerX,
        double halfWidth,
        double rise,
        double strength)
    {
        if (!double.IsFinite(centerX) ||
            !double.IsFinite(halfWidth) ||
            !double.IsFinite(rise) ||
            !double.IsFinite(strength))
        {
            return;
        }

        waveEnabled = true;
        waveCenterX = centerX;
        waveHalfWidth = Math.Max(24, halfWidth);
        waveRise = Math.Max(0, rise);
        waveStrength = Math.Clamp(strength, 0, 1);

        UpdateMaskPath();
    }

    public void ClearDockWave()
    {
        waveEnabled = false;
        waveStrength = 0;
        UpdateMaskPath();
    }

    private void UpdateMaskPath()
    {
        if (geometry is null ||
            canvasDevice is null ||
            visual is null ||
            maskSurface is null ||
            !hasBounds)
        {
            return;
        }

        var state = new MaskState(lastWindowWidth, lastWindowHeight, lastWidth, lastHeight,
            lastBottom, lastScale, material.CornerRadius, waveEnabled, waveCenterX,
            waveHalfWidth, waveRise, waveStrength);
        if (renderedMask == state) return;

        var outline = GlassDock.Core.Desktop.DockWaveGeometry.Create(
            (lastWindowWidth - lastWidth) / 2,
            lastWindowHeight - lastBottom - lastHeight, lastWidth, lastHeight,
            material.CornerRadius, waveCenterX, waveHalfWidth, waveRise,
            waveEnabled ? waveStrength : 0);
        Vector2 Pixel(GlassDock.Core.Desktop.DockWaveGeometry.Point point) =>
            new((float)(point.X * lastScale), (float)(point.Y * lastScale));
        using var builder = new CanvasPathBuilder(canvasDevice);
        builder.BeginFigure(Pixel(outline.Start));
        foreach (var segment in outline.Segments)
        {
            if (segment.IsLine) builder.AddLine(Pixel(segment.End));
            else builder.AddCubicBezier(Pixel(segment.Control1), Pixel(segment.Control2), Pixel(segment.End));
        }
        builder.EndFigure(CanvasFigureLoop.Closed);
        var nextGeometry =
            CanvasGeometry.CreatePath(
                builder);

        geometry.Path = new W.CompositionPath(nextGeometry);
        renderedMask = state;

        var previousGeometry =
            canvasGeometry;

        canvasGeometry =
            nextGeometry;

        previousGeometry?.Dispose();
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        target.SystemBackdrop = null;
        edgeEffect?.Dispose(); edgeFactory?.Dispose(); edgeMask?.Dispose();
        edgeSurface?.Dispose(); edgeVisual?.Dispose(); edgeShape?.Dispose(); edgeFill?.Dispose();
        edgeEffect = null; edgeFactory = null; edgeMask = null; edgeSurface = null;
        edgeVisual = null; edgeShape = null; edgeFill = null;
        output?.Dispose();
        mask?.Dispose();
        maskSurface?.Dispose();
        visual?.Dispose();
        shape?.Dispose();
        fill?.Dispose();
        geometry?.Dispose();
        canvasGeometry?.Dispose();
        fallback?.Dispose();
        effect?.Dispose();
        factory?.Dispose();
        source?.Dispose();
        compositor?.Dispose();
        output = null;
        mask = null;
        maskSurface = null;
        visual = null;
        shape = null;
        fill = null;
        geometry = null;
        renderedMask = null;
        canvasGeometry = null;
        canvasDevice = null;
        fallback = null;
        effect = null;
        factory = null;
        source = null;
        compositor = null;
        base.OnTargetDisconnected(target);
    }
}
