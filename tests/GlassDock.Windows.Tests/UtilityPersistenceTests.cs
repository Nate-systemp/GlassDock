using GlassDock.Core.Settings;
using GlassDock.Windows.Settings;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class UtilityPersistenceTests
{
    [Fact]
    public async Task Selected_utilities_and_theme_survive_restart_without_affecting_glass()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Doky-utilities-" + Guid.NewGuid());
        var path = Path.Combine(folder, "settings.json");
        try
        {
            var store = new GlassDockSettingsStore(path);
            await store.SaveAsync(new() { SelectedUtilities = new[] { UtilityKind.Bluetooth, UtilityKind.Wifi }, UtilityTheme = UtilityTheme.Light,
                DockAppearanceMode = DockAppearanceMode.Clear, ClearRefractionStrength = 17, PinDock = true });
            var loaded = await new GlassDockSettingsStore(path).LoadAsync();
            Assert.Equal(new[] { UtilityKind.Bluetooth, UtilityKind.Wifi }, loaded.SelectedUtilities);
            Assert.Equal(UtilityTheme.Light, loaded.UtilityTheme);
            Assert.Equal(DockAppearanceMode.Clear, loaded.DockAppearanceMode);
            Assert.Equal(17, loaded.ClearRefractionStrength); Assert.True(loaded.PinDock);
            await store.SaveAsync(loaded with { SelectedUtilities = [] });
            Assert.Empty((await store.LoadAsync()).SelectedUtilities);
        }
        finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(folder)) Directory.Delete(folder); }
    }
}
