using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using GlassDock.Windows.Settings;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class GlassVisualPersistenceTests
{
    [Fact]
    public async Task Custom_material_presets_and_component_overrides_survive_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), "Doky-glass-test-" + Guid.NewGuid(), "settings.json");
        try
        {
            var material = new LiquidGlassMaterial { SpecularAngleDegrees = 135, SpecularIntensity = .25f,
                RefractionStrength = 15, EdgeThickness = 24, ChromaticDispersion = .9f,
                DiffusionAmount = 2, Splay = 2, Contrast = 1.4f, BlurQuality = 2 };
            var profile = new GlassVisualProfile { Material = material, DockOverride = material with { SurfaceOpacity = .7f } }
                .WithComponent("app:test", material with { CornerRadius = 5 })
                .WithComponent("indicators", material with { SurfaceOpacity = .5f });
            var preset = new CustomGlassPreset(Guid.NewGuid(), "My glass", profile);
            var original = new GlassDockSettings { GlassVisuals = profile, CustomGlassPresets = new[] { preset },
                SelectedCustomGlassPreset = preset.Id, PinDock = true, DockSize = .8, BottomMargin = 42,
                LaunchAtStartup = true, ClearRefractionStrength = 17 };
            await new GlassDockSettingsStore(path).SaveAsync(original);
            var loaded = await new GlassDockSettingsStore(path).LoadAsync();
            Assert.Equal(material, loaded.GlassVisuals!.Material);
            Assert.Equal(profile.DockOverride, loaded.GlassVisuals.DockOverride);
            Assert.Equal(profile.Components, loaded.GlassVisuals.Components);
            Assert.Equal(preset.Name, Assert.Single(loaded.CustomGlassPresets).Name);
            Assert.Equal(material, loaded.CustomGlassPresets[0].Profile.Material);
            Assert.Equal(preset.Id, loaded.SelectedCustomGlassPreset);
            Assert.True(loaded.PinDock);
            Assert.True(loaded.LaunchAtStartup);
            Assert.Equal(.8, loaded.DockSize);
            Assert.Equal(42, loaded.BottomMargin);
            Assert.Equal(17, loaded.ClearRefractionStrength);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            var directory = Path.GetDirectoryName(path)!;
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }
}
