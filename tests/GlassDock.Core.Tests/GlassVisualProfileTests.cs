using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class GlassVisualProfileTests
{
    [Fact]
    public void Legacy_settings_keep_their_values_and_default_clear_transparency()
    {
        var settings = new GlassDockSettings { DockAppearanceMode = DockAppearanceMode.Clear,
            ClearRefractionStrength = 17, SpecularHighlightAngle = 135, BottomMargin = 42, PinDock = true };
        var profile = GlassVisualProfile.FromSettings(settings);
        Assert.Equal(17, profile.Material.RefractionStrength);
        Assert.Equal(135, profile.Material.SpecularAngleDegrees);
        Assert.Equal(0, profile.Material.BlurAmount);
        Assert.Equal(1, profile.Material.SurfaceOpacity);
        Assert.Equal(1, profile.Material.MaterialBlend);
        Assert.Null(settings.GlassVisuals);
        Assert.Equal(42, settings.BottomMargin);
        Assert.True(settings.PinDock);
    }

    [Fact]
    public void Inheritance_and_overrides_are_immutable_and_independent()
    {
        var original = GlassVisualProfile.BuiltIn(DockAppearanceMode.Clear);
        var changed = original with { Material = original.Material with { Splay = 2 } };
        Assert.Equal(2, changed.DockMaterial.Splay);
        var overridden = changed with { DockOverride = changed.Material with { Splay = .5f } };
        overridden = overridden.WithComponent("app:test", changed.Material with { SurfaceOpacity = .4f });
        Assert.Equal(.5f, overridden.DockMaterial.Splay);
        Assert.Equal(.4f, overridden.Component("app:test")!.SurfaceOpacity);
        Assert.Empty(original.Components);
        Assert.Equal(1, original.Material.Splay);
        Assert.Null(overridden.WithComponent("app:test", null).Component("app:test"));
        Assert.Equal(2, (overridden with { DockOverride = null }).DockMaterial.Splay);
    }

    [Fact]
    public void Every_exposed_property_is_independently_editable_and_normalized()
    {
        var original = new LiquidGlassMaterial();
        Assert.Equal(GlassVisualControl.All.Count, GlassVisualControl.All.Select(c => c.Id).Distinct().Count());
        foreach (var control in GlassVisualControl.All)
        {
            var value = control.Read(original) == control.Maximum ? control.Minimum : control.Maximum;
            var changed = control.Write(original, (float)value).Normalize();
            Assert.Equal((float)value, control.Read(changed));
            foreach (var other in GlassVisualControl.All.Where(c => c.Id != control.Id))
                Assert.Equal(other.Read(original), other.Read(changed));
            var invalid = control.Write(original, float.NaN).Normalize();
            Assert.True(float.IsFinite(control.Read(invalid)), control.Id);
            Assert.InRange(control.Read(invalid), (float)control.Minimum, (float)control.Maximum);
        }
    }

    [Fact]
    public void Profiles_cannot_change_builtin_colors_or_interactions()
    {
        foreach (var mode in Enum.GetValues<DockAppearanceMode>())
        {
            var first = GlassVisualProfile.BuiltIn(mode);
            var edited = first with { Material = first.Material with { Brightness = 2, CornerRadius = 4 } };
            Assert.NotEqual(first.Material, edited.Material);
            Assert.Equal(first, GlassVisualProfile.BuiltIn(mode));
        }
        Assert.DoesNotContain(typeof(GlassVisualProfile).GetProperties(), p =>
            p.Name.Contains("Shadow") || p.Name.Contains("Color") || p.Name.Contains("Animation"));
    }

    [Fact]
    public void Normalization_is_idempotent_and_bounds_untrusted_profiles()
    {
        var id = Guid.NewGuid();
        var value = new GlassDockSettings
        {
            GlassVisuals = new() { Material = new() { BlurAmount = float.PositiveInfinity, SurfaceOpacity = -10 },
                Components = new[] { new GlassComponentVisual("app:x", new()), new GlassComponentVisual("app:x", new()) } },
            CustomGlassPresets = new[] { new CustomGlassPreset(id, new string('x', 100), new()), new CustomGlassPreset(id, "duplicate", new()) }
        };
        var normalized = GlassDockSettings.Normalize(value);
        Assert.Equal(normalized, GlassDockSettings.Normalize(normalized));
        Assert.Single(normalized.GlassVisuals!.Components);
        Assert.Single(normalized.CustomGlassPresets);
        Assert.Equal(60, normalized.CustomGlassPresets[0].Name.Length);
        Assert.Equal(0, normalized.GlassVisuals.Material.SurfaceOpacity);
        Assert.True(float.IsFinite(normalized.GlassVisuals.Material.BlurAmount));
    }
}
