using GlassDock.Core.Materials;

namespace GlassDock.Core.Settings;

public sealed record GlassVisualProfile
{
    public DockAppearanceMode Mode { get; init; } = DockAppearanceMode.Clear;
    public LiquidGlassMaterial Material { get; init; } = new() { RefractionStrength = 12 };
    public LiquidGlassMaterial? DockOverride { get; init; }
    public bool NativeHighlights { get; init; } = true;
    public bool ClearHighlights { get; init; } = true;
    public GlassComponentVisual[] Components { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public LiquidGlassMaterial DockMaterial => DockOverride ?? Material;

    public GlassVisualProfile Normalize()
    {
        var components = (Components ?? []).Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Id))
            .DistinctBy(c => c.Id, StringComparer.Ordinal).Take(256)
            .Select(c => c with { Material = (c.Material ?? new()).Normalize() }).ToArray();
        return this with
        {
            Mode = Enum.IsDefined(Mode) ? Mode : DockAppearanceMode.Clear,
            Material = (Material ?? new()).Normalize(),
            DockOverride = DockOverride?.Normalize(),
            Components = components.Length == 0 ? Array.Empty<GlassComponentVisual>() :
                Components is not null && components.SequenceEqual(Components) ? Components : components
        };
    }

    public LiquidGlassMaterial? Component(string id) => Components.FirstOrDefault(c => c.Id == id)?.Material;

    public GlassVisualProfile WithComponent(string id, LiquidGlassMaterial? material) => this with
    {
        Components = Components.Where(c => c.Id != id)
            .Concat(material is null ? Array.Empty<GlassComponentVisual>() : new[] { new GlassComponentVisual(id, material) }).ToArray()
    };

    public static GlassVisualProfile FromSettings(GlassDockSettings settings)
    {
        if (settings.GlassVisuals is { } saved) return saved.Normalize();
        var mode = settings.DockAppearanceMode;
        var style = DockMaterialStylePresets.Create(mode.GlassStyle() ?? settings.GlassMaterialMode);
        var clear = mode == DockAppearanceMode.Clear;
        return new()
        {
            Mode = mode,
            NativeHighlights = settings.CircularHighlightsEnabled,
            ClearHighlights = settings.ClearCircularHighlightsEnabled,
            Material = new LiquidGlassMaterial
            {
                RefractionStrength = (float)settings.ClearRefractionStrength,
                SpecularAngleDegrees = (float)settings.SpecularHighlightAngle,
                BlurAmount = clear ? 0 : (float)settings.GlassBlurAmount,
                MaterialBlend = clear ? 1 : (float)settings.DockOpacity,
                Saturation = clear ? 1 : (float)style.Saturation,
                Brightness = clear ? 1 : (float)style.Brightness,
                EdgeOpacity = 0,
                EdgeWidth = clear ? 1 : (float)settings.BorderThickness
            }
        };
    }

    public static GlassVisualProfile BuiltIn(DockAppearanceMode mode)
    {
        var style = DockMaterialStylePresets.Create(mode.GlassStyle() ?? GlassMaterialMode.Frosted);
        return FromSettings(new GlassDockSettings
        {
            DockAppearanceMode = mode, GlassBlurAmount = style.BlurAmount,
            DockOpacity = style.Opacity, BorderThickness = style.BorderThickness, BorderOpacity = style.BorderOpacity
        });
    }
}

public sealed record GlassComponentVisual(string Id, LiquidGlassMaterial Material);
public sealed record CustomGlassPreset(Guid Id, string Name, GlassVisualProfile Profile)
{
    public CustomGlassPreset Normalize() => this with
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "Custom glass" : Name.Trim()[..Math.Min(Name.Trim().Length, 60)],
        Profile = (Profile ?? new()).Normalize()
    };
}

