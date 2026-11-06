using System.Numerics;
using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Xaml;

namespace GlassDock.App.Rendering;

/// <summary>One circular output fed by the owning dock's existing capture, never its own session.</summary>
internal sealed class UtilityGlassSurface : IDisposable
{
    private readonly LiquidGlassDrawingTarget target = new();
    private readonly LiquidGlassRim rim = new();
    private readonly Action<CanvasDrawingSession, CanvasGeometry> drawRim;
    private LiquidGlassMaterial material = new();
    private GlassMaterial backdrop = new();
    private GlassMaterial? appliedBackdrop;
    private DockAppearanceMode mode;
    private ArithmeticCompositeEffect? materialEffect;
    private GaussianBlurEffect? baseBlur, materialBlur;
    private GaussianBlurEffect? blur;
    private bool highlights;
    private PixelShaderEffect? shader;
    private LiquidGlassShaderState? previous;
    private CanvasGeometry? geometry;
    private float diameter, dpi, layoutScale;
    private float height, radius;
    public bool Circular { get; init; } = true;
    public float CornerRadius { get; set; } = 24;
    public bool HasFrame { get; private set; }
    public event EventHandler? RenderingChanged;
    public FrameworkElement Element => target.Element;
    public Vector4 Bounds { get; set; }
    public bool Active { get; set; }
    public bool UseDockMaterial { get; set; }
    public UtilityGlassSurface() => drawRim = (drawing, shape) =>
    {
        // Frosted/Acrylic dock material already owns its texture; omit the utility's extra Clear-style sheen.
        if (UseDockMaterial && mode is DockAppearanceMode.Frosted or DockAppearanceMode.Acrylic) return;
        rim.Draw(drawing, shape, new(0, 0, diameter / dpi, height / dpi), dpi,
            highlights ? material : material with { SpecularIntensity = 0 }, dockSurface: true, strokeScale: layoutScale);
    };

    public void Configure(DockAppearanceMode mode, GlassMaterial backdrop, LiquidGlassMaterial optics, bool highlights, float layoutScale)
    {
        if (this.mode != mode) appliedBackdrop = null;
        this.mode = mode; this.backdrop = backdrop; material = optics;
        this.highlights = highlights; this.layoutScale = layoutScale;
    }

    public void Draw(CanvasDevice device, CanvasBitmap bitmap, Vector2 origin, Vector2 frame, float scale)
    {
        if (!Active) return;
        shader ??= new PixelShaderEffect(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Rendering", "Shaders", "LiquidGlass.bin")));
        var size = Bounds.Z * scale;
        var nextHeight = Bounds.W * scale;
        var nextRadius = Circular ? size / 2 : Math.Min(CornerRadius * scale, Math.Min(size, nextHeight) / 2);
        if (geometry is null || size != diameter || nextHeight != height || radius != nextRadius)
        {
            geometry?.Dispose(); diameter = size; height = nextHeight; radius = nextRadius;
            geometry = Circular ? CanvasGeometry.CreateEllipse(device, size / 2, size / 2, size / 2, size / 2)
                : CanvasGeometry.CreateRoundedRectangle(device, 0, 0, size, height, radius, radius);
        }
        dpi = scale;
        var bounds = Bounds * scale + new Vector4(origin, 0, 0);
        blur ??= new() { BorderMode = EffectBorderMode.Hard };
        blur.Source = bitmap; blur.BlurAmount = material.BlurAmount * scale;
        blur.Optimization = GlassEffectGraph.Quality((int)material.BlurQuality);
        shader.Source1 = blur.BlurAmount > 0 ? blur : bitmap;
        LiquidGlassShader.Configure(shader, material, scale, bounds,
            new(bounds.X + size / 2, size / 2, 0, radius), frame, Vector4.Zero, ref previous);
        try
        {
            if (mode == DockAppearanceMode.Clear)
                target.Draw(device, shader, geometry, new(bounds.X, bounds.Y, size, height), scale, drawRim);
            else
            {
                var physical = backdrop with { BlurAmount = backdrop.BlurAmount * scale };
                if (materialEffect is null || physical != appliedBackdrop)
                {
                    materialEffect?.Dispose();
                    materialEffect = (ArithmeticCompositeEffect)GlassEffectGraph.Create(bitmap, bitmap,
                        physical.BlurQuality, physical, DockMaterialRendering.BaseBlur(mode.GlassStyle(), physical.BlurAmount));
                    baseBlur = (GaussianBlurEffect)materialEffect.Source1;
                    var composite = (CompositeEffect)materialEffect.Source2;
                    materialBlur = (GaussianBlurEffect)((SaturationEffect)((ExposureEffect)((ContrastEffect)composite.Sources[0]).Source).Source).Source;
                    appliedBackdrop = physical;
                }
                baseBlur!.Source = bitmap; materialBlur!.Source = bitmap;
                target.Draw(device, materialEffect, geometry, new(bounds.X, bounds.Y, size, height), scale, drawRim);
            }
            Element.Visibility = Visibility.Visible;
            if (!HasFrame) { HasFrame = true; RenderingChanged?.Invoke(this, EventArgs.Empty); }
        }
        finally
        {
            shader.Source1 = null; blur.Source = null;
            if (baseBlur is not null) baseBlur.Source = null;
            if (materialBlur is not null) materialBlur.Source = null;
        }
    }

    public void Dispose()
    {
        shader?.Dispose(); shader = null; previous = null;
        blur?.Dispose(); blur = null;
        materialEffect?.Dispose(); materialEffect = null; appliedBackdrop = null;
        baseBlur = materialBlur = null;
        Element.Visibility = Visibility.Collapsed;
        if (HasFrame) { HasFrame = false; RenderingChanged?.Invoke(this, EventArgs.Empty); }
        geometry?.Dispose(); geometry = null; target.Dispose(); rim.Dispose();
    }
}
