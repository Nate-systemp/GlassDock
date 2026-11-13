using GlassDock.Core.Materials;

namespace GlassDock.App.Rendering;

internal static class RefractionLayer
{
    public const bool IsSupported = false;
    public const string Description = "Optical refraction unavailable · Refractive uses edge lighting only";
    public static GlassMaterial ForNativeBackend(GlassMaterial material) =>
        material.Normalize() with { RefractionAmount = 0 };
}
