using GlassDock.Core.Settings;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class SettingsCatalogTests
{
    [Theory]
    [InlineData(DockAppearanceMode.Dark, DockAppearanceMode.Dark)]
    [InlineData(DockAppearanceMode.Light, DockAppearanceMode.Light)]
    [InlineData(DockAppearanceMode.Frosted, DockAppearanceMode.Frosted)]
    [InlineData(DockAppearanceMode.Acrylic, DockAppearanceMode.Acrylic)]
    [InlineData(DockAppearanceMode.Clear, DockAppearanceMode.Frosted)]
    public void Settings_material_policy_preserves_dock_choice(DockAppearanceMode selected, DockAppearanceMode expected)
    {
        var session = new GlassDockSettingsSession(new() { DockAppearanceMode = selected, GlassBlurAmount = 7, DockOpacity = .6 });
        var before = session.Current;
        var result = SettingsShellAppearance.Resolve(selected, session.Appearance);
        Assert.Equal(expected, result.Mode);
        Assert.Equal(before, session.Current);
        if (selected == DockAppearanceMode.Clear)
        {
            var preset = DockMaterialStylePresets.Create(GlassMaterialMode.Frosted);
            Assert.Equal(preset.BlurAmount, result.Appearance.GlassBlurAmount);
            Assert.Equal(preset.Opacity, result.Appearance.DockOpacity);
        }
        else Assert.Equal(session.Appearance, result.Appearance);
    }

    [Fact]
    public void Navigation_has_exactly_the_supported_nine_pages()
    {
        Assert.Equal(new[] { "General", "Appearance", "Dock", "Apps", "Behavior", "Displays", "System", "Advanced", "About" },
            SettingsCatalog.Pages.Select(page => page.Id));
        Assert.All(SettingsCatalog.Options, entry => Assert.Contains(SettingsCatalog.Pages, page => page.Id == entry.Page));
    }

    [Theory]
    [InlineData("magnification", "Dock", "MagnificationScaleBox")]
    [InlineData("stack", "Apps", "StackHelp")]
    [InlineData("monitor", "Displays", "DockDisplayModeBox")]
    [InlineData("startup", "General", "LaunchAtStartupToggle")]
    [InlineData("liquid", "Appearance", "AppearanceTiles")]
    [InlineData("NOTIFICATION access", "Apps", "NotificationBadgesToggle")]
    public void Search_resolves_real_setting_destinations(string query, string page, string control)
    {
        Assert.Contains(SettingsCatalog.Search(query), entry => entry.Page == page && entry.Control == control);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("not-a-real-setting")]
    public void Search_does_not_fabricate_results(string? query) => Assert.Empty(SettingsCatalog.Search(query));

    [Theory]
    [InlineData(DockAppearanceMode.Dark)]
    [InlineData(DockAppearanceMode.Light)]
    [InlineData(DockAppearanceMode.Frosted)]
    [InlineData(DockAppearanceMode.Acrylic)]
    [InlineData(DockAppearanceMode.Clear)]
    public void Appearance_change_notifies_the_same_session_and_preserves_other_preferences(DockAppearanceMode mode)
    {
        var session = new GlassDockSettingsSession(new() { PinDock = true, DockDisplayMode = DockDisplayMode.AllDisplays,
            NotificationBadgesEnabled = true, IconSpacing = 13,
            DockAppearanceMode = mode == DockAppearanceMode.Dark ? DockAppearanceMode.Light : DockAppearanceMode.Dark });
        GlassDockSettings? received = null;
        session.Changed += (_, e) => received = e.Settings;
        session.Replace(session.Current with { DockAppearanceMode = mode });
        Assert.NotNull(received);
        Assert.Equal(mode, received.DockAppearanceMode);
        Assert.True(received.PinDock);
        Assert.True(received.NotificationBadgesEnabled);
        Assert.Equal(DockDisplayMode.AllDisplays, received.DockDisplayMode);
        Assert.Equal(13, received.IconSpacing);
    }
}
