using GlassDock.Core.Materials;

namespace GlassDock.Core.Settings;

public sealed record GlassVisualControl(string Id, string Section, string Label, string Description,
    double Minimum, double Maximum, bool Basic, string Capability,
    Func<LiquidGlassMaterial, float> Read, Func<LiquidGlassMaterial, float, LiquidGlassMaterial> Write)
{
    public bool Supported(DockAppearanceMode mode) => Capability switch
    {
        "clear" => mode == DockAppearanceMode.Clear,
        "glass" => mode.GlassStyle() is not null,
        _ => true
    };

    public static IReadOnlyList<GlassVisualControl> All { get; } = new GlassVisualControl[]
    {
        new("SpecularAngleDegrees", "Figma Glass", "Light Angle", "Direction of the specular highlights.", 0, 360, true, "all", m => m.SpecularAngleDegrees, (m, v) => m with { SpecularAngleDegrees = v }),
        new("SpecularIntensity", "Figma Glass", "Light Intensity", "Brightness of highlights and edge lighting; also controls highlight opacity.", 0, 0.4, true, "all", m => m.SpecularIntensity, (m, v) => m with { SpecularIntensity = v }),
        new("RefractionStrength", "Figma Glass", "Refraction", "Edge distortion strength in DIP.", 0, 20, true, "clear", m => m.RefractionStrength, (m, v) => m with { RefractionStrength = v }),
        new("EdgeThickness", "Figma Glass", "Depth", "Inward extent of the curved edge profile in DIP.", 2, 32, true, "clear", m => m.EdgeThickness, (m, v) => m with { EdgeThickness = v }),
        new("ChromaticDispersion", "Figma Glass", "Dispersion / radius", "RGB separation distance at refracted edges.", 0, 1, true, "clear", m => m.ChromaticDispersion, (m, v) => m with { ChromaticDispersion = v }),
        new("DiffusionAmount", "Figma Glass", "Frost / diffusion", "Diffusion sampling radius; also controls perceived roughness.", 0, 3, true, "clear", m => m.DiffusionAmount, (m, v) => m with { DiffusionAmount = v }),
        new("Splay", "Figma Glass", "Splay", "Spread of the edge optical field.", 0.25, 3, true, "clear", m => m.Splay, (m, v) => m with { Splay = v }),
        new("RefractionFalloff", "Optics", "Distortion falloff / concentration", "Concentration of refraction near the edge.", 1, 5, false, "clear", m => m.RefractionFalloff, (m, v) => m with { RefractionFalloff = v }),
        new("SpecularFalloff", "Highlights", "Edge-glint falloff", "Concentration of directional light.", 0.5, 8, false, "all", m => m.SpecularFalloff, (m, v) => m with { SpecularFalloff = v }),
        new("Magnification", "Optics", "Refraction scale", "Interior lens magnification; independent of edge distortion.", 0, 0.25, false, "clear", m => m.Magnification, (m, v) => m with { Magnification = v }),
        new("EdgeFocus", "Optics", "Edge-focused refraction", "Concentrates distortion at the perimeter.", 0, 1, false, "clear", m => m.EdgeFocus, (m, v) => m with { EdgeFocus = v }),
        new("RefractionSoftness", "Optics", "Refraction softness", "Smooths the curved optical profile.", 0, 1, false, "clear", m => m.RefractionSoftness, (m, v) => m with { RefractionSoftness = v }),
        new("HighlightWidth", "Highlights", "Highlight thickness", "Width of the specular rim in DIP.", 0, 6, false, "all", m => m.HighlightWidth, (m, v) => m with { HighlightWidth = v }),
        new("HighlightSoftness", "Highlights", "Highlight softness", "Softness of the opposing light catches.", 0.02, 0.5, false, "all", m => m.HighlightSoftness, (m, v) => m with { HighlightSoftness = v }),
        new("HighlightSpread", "Highlights", "Highlight spread", "Separation of the opposing light catches.", 0.1, 1, false, "all", m => m.HighlightSpread, (m, v) => m with { HighlightSpread = v }),
        new("HighlightPosition", "Highlights", "Highlight position", "Moves the catches along the lighting axis.", 0, 1, false, "all", m => m.HighlightPosition, (m, v) => m with { HighlightPosition = v }),
        new("InnerGlint", "Highlights", "Inner edge glint", "Intensity of the broader inner catch.", 0, 2, false, "clear", m => m.InnerGlint, (m, v) => m with { InnerGlint = v }),
        new("OuterGlint", "Highlights", "Outer edge glint", "Intensity of the fine perimeter catch.", 0, 2, false, "all", m => m.OuterGlint, (m, v) => m with { OuterGlint = v }),
        new("TopLight", "Edges", "Top edge intensity", "Scales lighting at the top edge.", 0, 2, false, "all", m => m.TopLight, (m, v) => m with { TopLight = v }),
        new("BottomLight", "Edges", "Bottom edge intensity", "Scales lighting at the bottom edge.", 0, 2, false, "all", m => m.BottomLight, (m, v) => m with { BottomLight = v }),
        new("ReflectionIntensity", "Highlights", "Reflection intensity", "A soft reflection band within the surface.", 0, 0.4, false, "clear", m => m.ReflectionIntensity, (m, v) => m with { ReflectionIntensity = v }),
        new("ReflectionSoftness", "Highlights", "Reflection softness", "Width of the interior reflection band.", 0.03, 1, false, "clear", m => m.ReflectionSoftness, (m, v) => m with { ReflectionSoftness = v }),
        new("InnerShine", "Highlights", "Inner shine / glow", "Soft light inside the glass; one control for the same effect.", 0, 2, false, "clear", m => m.InnerShine, (m, v) => m with { InnerShine = v }),
        new("EdgeOpacity", "Edges", "Edge opacity", "Continuous edge definition, separate from glints.", 0, 1, false, "glass", m => m.EdgeOpacity, (m, v) => m with { EdgeOpacity = v }),
        new("EdgeWidth", "Edges", "Edge thickness", "Thickness of the continuous edge in DIP.", 0, 6, false, "glass", m => m.EdgeWidth, (m, v) => m with { EdgeWidth = v }),
        new("Bevel", "Edges", "Bevel definition", "Curved edge shading; depth controls its extent.", 0, 1, false, "clear", m => m.Bevel, (m, v) => m with { Bevel = v }),
        new("SurfaceOpacity", "Surface", "Surface opacity / transparency", "Opacity of the material only; app icons stay visible.", 0, 1, true, "all", m => m.SurfaceOpacity, (m, v) => m with { SurfaceOpacity = v }),
        new("MaterialBlend", "Surface", "Material opacity", "Blends processed glass with the original background.", 0, 1, false, "glass", m => m.MaterialBlend, (m, v) => m with { MaterialBlend = v }),
        new("BlurAmount", "Surface", "Blur strength", "Gaussian background blur in DIP.", 0, 60, true, "glass", m => m.BlurAmount, (m, v) => m with { BlurAmount = v }),
        new("BlurQuality", "Surface", "Blur quality", "0: speed, 1: balanced, 2: quality.", 0, 2, false, "glass", m => m.BlurQuality, (m, v) => m with { BlurQuality = v }),
        new("Saturation", "Surface", "Saturation", "Background color saturation; preset colors remain fixed.", 0, 2, false, "glass", m => m.Saturation, (m, v) => m with { Saturation = v }),
        new("Brightness", "Surface", "Brightness", "Background luminance.", 0.25, 2, false, "glass", m => m.Brightness, (m, v) => m with { Brightness = v }),
        new("Contrast", "Surface", "Contrast", "Background contrast around middle gray.", 0.25, 2, false, "glass", m => m.Contrast, (m, v) => m with { Contrast = v }),
        new("CornerRadius", "Edges", "Corner radius", "Visual contour only; dock placement and input regions are unchanged.", 0, 34, false, "all", m => m.CornerRadius, (m, v) => m with { CornerRadius = v }),
    };
}

