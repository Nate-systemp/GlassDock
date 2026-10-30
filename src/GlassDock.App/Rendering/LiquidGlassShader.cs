using System.Numerics;
using GlassDock.Core.Materials;
using Microsoft.Graphics.Canvas.Effects;

namespace GlassDock.App.Rendering;

/// <summary>The dock and settings preview use identical optical constants and shader bytecode.</summary>
internal static class LiquidGlassShader
{
    public static void Configure(PixelShaderEffect effect, LiquidGlassMaterial m, float scale,
        Vector4 bounds, Vector4 shape, Vector2 frame, Vector4 lens, ref LiquidGlassShaderState? previous)
    {
        lens.Y = m.ChromaticDispersion * scale;
        var next = new LiquidGlassShaderState(bounds, shape,
            new(m.RefractionStrength * scale, m.EdgeThickness * scale, m.RefractionFalloff, m.DiffusionAmount * scale),
            new(m.TintOpacity, m.SpecularIntensity, m.SpecularFalloff, m.SpecularAngleDegrees * MathF.PI / 180), frame, lens)
        {
            Surface = new(m.Saturation, m.Brightness, m.Contrast, m.MaterialBlend),
            Profile = new(m.Splay, m.Magnification, m.EdgeFocus, m.RefractionSoftness),
            Reflections = new(m.ReflectionIntensity, m.ReflectionSoftness, m.Bevel, m.SurfaceOpacity)
        };
        var changes = next.ChangesFrom(previous);
        GlassDock.Core.Desktop.DockPerformanceDiagnostics.RecordShaderChanges(changes);
        // Write exact changed values, including subpixel motion, without per-frame boxing of unchanged constants.
        if ((changes & 1) != 0) effect.Properties["Dock"] = next.Dock;
        if ((changes & 2) != 0) effect.Properties["Wave"] = next.Wave;
        if ((changes & 4) != 0) effect.Properties["Optics"] = next.Optics;
        if ((changes & 8) != 0) effect.Properties["Lighting"] = next.Lighting;
        if ((changes & 16) != 0) effect.Properties["FrameSize"] = next.FrameSize;
        if ((changes & 32) != 0) effect.Properties["Lens"] = next.Lens;
        if ((changes & 64) != 0) effect.Properties["Surface"] = next.Surface;
        if ((changes & 128) != 0) effect.Properties["Profile"] = next.Profile;
        if ((changes & 256) != 0) effect.Properties["Reflections"] = next.Reflections;
        previous = next;
    }
}
