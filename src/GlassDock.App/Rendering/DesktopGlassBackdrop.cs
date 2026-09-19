using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
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

        visual.Opacity = (float)opacity;

        var size =
            new Vector2(
                (float)(windowWidth * scale),
                (float)(windowHeight * scale));

        visual.Size = size;
        maskSurface.SourceSize = size;

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

        var scale =
            Math.Max(
                0.01,
                lastScale);

        var left =
            (float)(
                (lastWindowWidth - lastWidth) /
                2 *
                scale);

        var top =
            (float)(
                (lastWindowHeight -
                 lastBottom -
                 lastHeight) *
                scale);

        var right =
            left +
            (float)(
                lastWidth *
                scale);

        var bottom =
            top +
            (float)(
                lastHeight *
                scale);

        var radius =
            (float)(
                Math.Min(
                    Math.Min(
                        material.CornerRadius,
                        lastHeight / 2),
                    lastWidth / 2) *
                scale);

        radius =
            Math.Max(
                0,
                Math.Min(
                    radius,
                    Math.Min(
                        (right - left) / 2,
                        (bottom - top) / 2)));

        var strength =
            waveEnabled
                ? Math.Clamp(
                    waveStrength,
                    0,
                    1)
                : 0;

        var eased =
            strength *
            strength *
            (3 - 2 * strength);

        var rise =
            (float)(
                waveRise *
                eased *
                scale);

        var availableTop =
            Math.Max(
                0,
                right -
                left -
                radius * 2);

        var requestedHalfWidth =
            (float)Math.Min(
                waveHalfWidth *
                scale,
                Math.Max(
                    24 * scale,
                    availableTop * 0.46));

        var topStart =
            left +
            radius;

        var topEnd =
            right -
            radius;

        var crestInset =
            (float)(
                4 *
                scale);

        var center =
            (float)(
                waveCenterX *
                scale);

        var centerMin = Math.Min(topStart + crestInset, topEnd - crestInset);
        var centerMax = Math.Max(topStart + crestInset, topEnd - crestInset);
        center = Math.Clamp(center, centerMin, centerMax);

        var leftRoom =
            Math.Max(
                0,
                center -
                topStart);

        var rightRoom =
            Math.Max(
                0,
                topEnd -
                center);

        var edgeMergeThreshold =
            Math.Min(
                radius +
                (float)(12 * scale),
                requestedHalfWidth * 0.68f);

        var leftEdge =
            rise > 0.01f &&
            leftRoom <
            edgeMergeThreshold;

        var rightEdge =
            rise > 0.01f &&
            rightRoom <
            edgeMergeThreshold;

        if (leftEdge &&
            rightEdge)
        {
            leftEdge = false;
            rightEdge = false;
        }

        var leftSpan =
            Math.Min(
                requestedHalfWidth,
                leftRoom);

        var rightSpan =
            Math.Min(
                requestedHalfWidth,
                rightRoom);

        var waveStart =
            center -
            leftSpan;

        var waveEnd =
            center +
            rightSpan;

        const float kappa =
            0.55228475f;

        var cornerMergeY =
            top +
            radius;

        using var builder =
            new CanvasPathBuilder(
                canvasDevice);

        if (leftEdge)
        {
            builder.BeginFigure(
                new Vector2(
                    left,
                    cornerMergeY));

            var outerDistance =
                Math.Max(
                    (float)(18 * scale),
                    center - left);

            builder.AddCubicBezier(
                new Vector2(
                    left,
                    top +
                    radius * 0.18f),

                new Vector2(
                    center -
                    outerDistance * 0.48f,
                    top - rise),

                new Vector2(
                    center,
                    top - rise));
        }
        else
        {
            builder.BeginFigure(
                new Vector2(
                    left + radius,
                    top));

            if (rise > 0.01f)
            {
                builder.AddLine(
                    new Vector2(
                        waveStart,
                        top));

                builder.AddCubicBezier(
                    new Vector2(
                        waveStart +
                        leftSpan * 0.38f,
                        top),

                    new Vector2(
                        center -
                        leftSpan * 0.46f,
                        top - rise),

                    new Vector2(
                        center,
                        top - rise));
            }
        }

        //
        // CREST -> RIGHT SIDE
        //
        if (rise > 0.01f)
        {
            if (rightEdge)
            {
                var outerDistance =
                    Math.Max(
                        (float)(18 * scale),
                        right - center);

                builder.AddCubicBezier(
                    new Vector2(
                        center +
                        outerDistance * 0.48f,
                        top - rise),

                    new Vector2(
                        right,
                        top +
                        radius * 0.18f),

                    new Vector2(
                        right,
                        cornerMergeY));
            }
            else
            {
                builder.AddCubicBezier(
                    new Vector2(
                        center +
                        rightSpan * 0.46f,
                        top - rise),

                    new Vector2(
                        waveEnd -
                        rightSpan * 0.38f,
                        top),

                    new Vector2(
                        waveEnd,
                        top));

                builder.AddLine(
                    new Vector2(
                        right - radius,
                        top));

                builder.AddCubicBezier(
                    new Vector2(
                        right -
                        radius +
                        radius * kappa,
                        top),

                    new Vector2(
                        right,
                        top +
                        radius -
                        radius * kappa),

                    new Vector2(
                        right,
                        top + radius));
            }
        }
        else
        {
            builder.AddLine(
                new Vector2(
                    right - radius,
                    top));

            builder.AddCubicBezier(
                new Vector2(
                    right -
                    radius +
                    radius * kappa,
                    top),

                new Vector2(
                    right,
                    top +
                    radius -
                    radius * kappa),

                new Vector2(
                    right,
                    top + radius));
        }

        //
        // RIGHT SIDE + BOTTOM-RIGHT
        //
        builder.AddLine(
            new Vector2(
                right,
                bottom - radius));

        builder.AddCubicBezier(
            new Vector2(
                right,
                bottom -
                radius +
                radius * kappa),

            new Vector2(
                right -
                radius +
                radius * kappa,
                bottom),

            new Vector2(
                right - radius,
                bottom));

        //
        // BOTTOM + BOTTOM-LEFT
        //
        builder.AddLine(
            new Vector2(
                left + radius,
                bottom));

        builder.AddCubicBezier(
            new Vector2(
                left +
                radius -
                radius * kappa,
                bottom),

            new Vector2(
                left,
                bottom -
                radius +
                radius * kappa),

            new Vector2(
                left,
                bottom - radius));

        //
        // LEFT SIDE + TOP-LEFT
        //
        if (leftEdge)
        {
            builder.AddLine(
                new Vector2(
                    left,
                    cornerMergeY));
        }
        else
        {
            builder.AddLine(
                new Vector2(
                    left,
                    top + radius));

            builder.AddCubicBezier(
                new Vector2(
                    left,
                    top +
                    radius -
                    radius * kappa),

                new Vector2(
                    left +
                    radius -
                    radius * kappa,
                    top),

                new Vector2(
                    left + radius,
                    top));
        }

        builder.EndFigure(
            CanvasFigureLoop.Closed);

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
