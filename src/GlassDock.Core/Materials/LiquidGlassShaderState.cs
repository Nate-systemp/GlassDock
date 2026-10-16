using System.Numerics;

namespace GlassDock.Core.Materials;

/// <summary>Exact shader inputs; equality never quantizes geometry or optics.</summary>
public readonly record struct LiquidGlassShaderState(Vector4 Dock, Vector4 Wave, Vector4 Optics,
    Vector4 Lighting, Vector2 FrameSize, Vector4 Lens)
{
    public const int All = 63;
    public int ChangesFrom(LiquidGlassShaderState? previous)
    {
        if (previous is not { } p) return All;
        return (Dock != p.Dock ? 1 : 0) | (Wave != p.Wave ? 2 : 0) |
            (Optics != p.Optics ? 4 : 0) | (Lighting != p.Lighting ? 8 : 0) |
            (FrameSize != p.FrameSize ? 16 : 0) | (Lens != p.Lens ? 32 : 0);
    }
}
