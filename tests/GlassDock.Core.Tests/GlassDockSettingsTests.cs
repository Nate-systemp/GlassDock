using GlassDock.Core.Settings;
using GlassDock.Core.Materials;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class GlassDockSettingsTests
{
    [Theory]
    [InlineData(DockAppearanceMode.Dark, null)]
    [InlineData(DockAppearanceMode.Light, null)]
    [InlineData(DockAppearanceMode.Frosted, GlassMaterialMode.Frosted)]
    [InlineData(DockAppearanceMode.Acrylic, GlassMaterialMode.Acrylic)]
    [InlineData(DockAppearanceMode.Clear, GlassMaterialMode.Clear)]
    public void Dock_surface_choices_select_existing_glass_styles(DockAppearanceMode mode, GlassMaterialMode? expected)
    {
        var session = new GlassDockSettingsSession(new());
        session.Replace(session.CreateDockAppearanceUpdate(mode));
        Assert.Equal(mode, session.Current.DockAppearanceMode);
        Assert.Equal(expected, session.Current.DockAppearanceMode.GlassStyle());
    }

    [Fact]
    public void Plain_appearance_normalizes_and_live_update_preserves_other_preferences()
    {
        Assert.Equal(DockAppearanceMode.Dark, new GlassDockSettings().DockAppearanceMode);
        Assert.Equal(DockAppearanceMode.Dark, GlassDockSettings.Normalize(new()
        {
            DockAppearanceMode = (DockAppearanceMode)99
        }).DockAppearanceMode);
        var original = new GlassDockSettings { IconSize = 36, LaunchAtStartup = true, PinDock = true,
            GlassMaterialMode = GlassMaterialMode.Clear, DockOpacity = .4 };
        var session = new GlassDockSettingsSession(original);
        var changes = 0;
        session.Changed += (_, _) => changes++;
        var edited = session.CreateDockAppearanceUpdate(DockAppearanceMode.Light);
        Assert.Equal(original, session.Current);
        Assert.True(session.Replace(edited));
        Assert.Equal(original with { DockAppearanceMode = DockAppearanceMode.Light }, session.Current);
        Assert.Equal(1, changes);
        Assert.False(session.Replace(edited));
        var reset = session.CreateDefaultEditableSettings();
        Assert.Equal(DockAppearanceMode.Dark, reset.DockAppearanceMode);
        Assert.False(reset.PinDock);
        Assert.Equal(DockAppearanceMode.Light, session.Current.DockAppearanceMode);
        Assert.True(reset.LaunchAtStartup);
    }

    [Fact]
    public void Defaults_match_current_dock_behavior()
    {
        var settings = new GlassDockSettings();

        Assert.Equal(1, settings.SchemaVersion);
        Assert.False(settings.LaunchAtStartup);
        Assert.False(settings.PinDock);
        Assert.True(settings.SuppressWindowsTaskbar);
        Assert.Equal(24, settings.BottomMargin);
        Assert.Equal(1000, settings.AutoHideDelayMilliseconds);
        Assert.Equal(2000, settings.PeekDelayMilliseconds);
        Assert.Equal(28, settings.IconSize);
        Assert.Equal(1.24, settings.MagnificationScale);
        Assert.Equal(6, settings.IconSpacing);
        Assert.Equal(20, settings.GlassBlurAmount);
        Assert.Equal(0.78, settings.DockOpacity);
        Assert.Equal(1.05, settings.BorderThickness);
        Assert.Equal(0.78, settings.BorderOpacity);
        Assert.Equal(GlassMaterialMode.Frosted, settings.GlassMaterialMode);
    }

    [Fact]
    public void Normalize_clamps_numeric_values_and_uses_current_schema()
    {
        var low = GlassDockSettings.Normalize(new()
        {
            SchemaVersion = 0,
            BottomMargin = -500,
            AutoHideDelayMilliseconds = -1,
            PeekDelayMilliseconds = -1,
            IconSize = -1,
            MagnificationScale = -1,
            IconSpacing = -1,
            GlassBlurAmount = -1,
            DockOpacity = -1,
            BorderThickness = -1,
            BorderOpacity = -1,
            GlassMaterialMode = (GlassMaterialMode)(-1)
        });
        var high = GlassDockSettings.Normalize(new()
        {
            SchemaVersion = int.MaxValue,
            BottomMargin = 100_000,
            AutoHideDelayMilliseconds = int.MaxValue,
            PeekDelayMilliseconds = int.MaxValue,
            IconSize = 1000,
            MagnificationScale = 1000,
            IconSpacing = 1000,
            GlassBlurAmount = 1000,
            DockOpacity = 1000,
            BorderThickness = 1000,
            BorderOpacity = 1000,
            GlassMaterialMode = (GlassMaterialMode)99
        });
        var nonFinite = GlassDockSettings.Normalize(new()
        {
            BottomMargin = double.NaN,
            IconSize = double.NaN,
            MagnificationScale = double.PositiveInfinity,
            IconSpacing = double.NegativeInfinity,
            GlassBlurAmount = double.NaN,
            DockOpacity = double.NaN,
            BorderThickness = double.NaN,
            BorderOpacity = double.NaN
        });

        Assert.Equal(GlassDockSettings.CurrentSchemaVersion, low.SchemaVersion);
        Assert.Equal(GlassDockSettings.MinimumBottomMargin, low.BottomMargin);
        Assert.Equal(GlassDockSettings.MinimumAutoHideDelayMilliseconds, low.AutoHideDelayMilliseconds);
        Assert.Equal(GlassDockSettings.MinimumPeekDelayMilliseconds, low.PeekDelayMilliseconds);
        Assert.Equal(GlassDockSettings.MinimumIconSize, low.IconSize);
        Assert.Equal(GlassDockSettings.MinimumMagnificationScale, low.MagnificationScale);
        Assert.Equal(GlassDockSettings.MinimumIconSpacing, low.IconSpacing);
        Assert.Equal(GlassDockSettings.MinimumGlassBlurAmount, low.GlassBlurAmount);
        Assert.Equal(GlassDockSettings.MinimumDockOpacity, low.DockOpacity);
        Assert.Equal(GlassDockSettings.MinimumBorderThickness, low.BorderThickness);
        Assert.Equal(GlassDockSettings.MinimumBorderOpacity, low.BorderOpacity);
        Assert.Equal(GlassMaterialMode.Frosted, low.GlassMaterialMode);
        Assert.Equal(GlassDockSettings.CurrentSchemaVersion, high.SchemaVersion);
        Assert.Equal(GlassDockSettings.MaximumBottomMargin, high.BottomMargin);
        Assert.Equal(GlassDockSettings.MaximumAutoHideDelayMilliseconds, high.AutoHideDelayMilliseconds);
        Assert.Equal(GlassDockSettings.MaximumPeekDelayMilliseconds, high.PeekDelayMilliseconds);
        Assert.Equal(GlassDockSettings.MaximumIconSize, high.IconSize);
        Assert.Equal(GlassDockSettings.MaximumMagnificationScale, high.MagnificationScale);
        Assert.Equal(GlassDockSettings.MaximumIconSpacing, high.IconSpacing);
        Assert.Equal(GlassDockSettings.MaximumGlassBlurAmount, high.GlassBlurAmount);
        Assert.Equal(GlassDockSettings.MaximumDockOpacity, high.DockOpacity);
        Assert.Equal(GlassDockSettings.MaximumBorderThickness, high.BorderThickness);
        Assert.Equal(GlassDockSettings.MaximumBorderOpacity, high.BorderOpacity);
        Assert.Equal(GlassMaterialMode.Frosted, high.GlassMaterialMode);
        Assert.Equal(GlassDockSettings.DefaultBottomMargin, nonFinite.BottomMargin);
        Assert.Equal(GlassDockSettings.DefaultIconSize, nonFinite.IconSize);
        Assert.Equal(GlassDockSettings.DefaultMagnificationScale, nonFinite.MagnificationScale);
        Assert.Equal(GlassDockSettings.DefaultIconSpacing, nonFinite.IconSpacing);
        Assert.Equal(GlassDockSettings.DefaultGlassBlurAmount, nonFinite.GlassBlurAmount);
        Assert.Equal(GlassDockSettings.DefaultDockOpacity, nonFinite.DockOpacity);
        Assert.Equal(GlassDockSettings.DefaultBorderThickness, nonFinite.BorderThickness);
        Assert.Equal(GlassDockSettings.DefaultBorderOpacity, nonFinite.BorderOpacity);
    }

    [Fact]
    public void Runtime_session_exposes_normalized_dock_behavior()
    {
        var session = new GlassDockSettingsSession(new()
        {
            BottomMargin = -20,
            AutoHideDelayMilliseconds = 50_000,
            PeekDelayMilliseconds = 3500
        });

        Assert.Equal(GlassDockSettings.MinimumBottomMargin, session.DockBehavior.BottomMargin);
        Assert.Equal(
            TimeSpan.FromMilliseconds(GlassDockSettings.MaximumAutoHideDelayMilliseconds),
            session.DockBehavior.AutoHideDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(3500), session.DockBehavior.PeekDelay);
    }

    [Fact]
    public void Safe_dock_update_propagates_and_preserves_unexposed_settings()
    {
        var session = new GlassDockSettingsSession(new()
        {
            LaunchAtStartup = true,
            SuppressWindowsTaskbar = false,
            PinDock = true
        });
        var changes = 0;
        session.Changed += (_, _) => changes++;

        var edited = session.CreateDockBehaviorUpdate(48, 3200, 6400);
        Assert.True(session.Replace(edited));

        Assert.Equal(48, session.DockBehavior.BottomMargin);
        Assert.Equal(TimeSpan.FromMilliseconds(3200), session.DockBehavior.AutoHideDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(6400), session.DockBehavior.PeekDelay);
        Assert.True(session.Current.LaunchAtStartup);
        Assert.False(session.Current.SuppressWindowsTaskbar);
        Assert.True(session.Current.PinDock);
        Assert.False(session.Current.HoverToExpandOnly);
        Assert.Equal(1, changes);
        Assert.False(session.Replace(edited));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Reset_dock_behavior_preserves_unexposed_settings()
    {
        var session = new GlassDockSettingsSession(new()
        {
            LaunchAtStartup = true,
            SuppressWindowsTaskbar = false,
            BottomMargin = 80,
            AutoHideDelayMilliseconds = 9000,
            PeekDelayMilliseconds = 12_000
        });

        var reset = session.CreateDefaultEditableSettings();

        Assert.Equal(GlassDockSettings.DefaultBottomMargin, reset.BottomMargin);
        Assert.Equal(GlassDockSettings.DefaultAutoHideDelayMilliseconds, reset.AutoHideDelayMilliseconds);
        Assert.Equal(GlassDockSettings.DefaultPeekDelayMilliseconds, reset.PeekDelayMilliseconds);
        Assert.Equal(GlassDockSettings.DefaultIconSize, reset.IconSize);
        Assert.Equal(GlassDockSettings.DefaultMagnificationScale, reset.MagnificationScale);
        Assert.Equal(GlassDockSettings.DefaultIconSpacing, reset.IconSpacing);
        Assert.Equal(GlassDockSettings.DefaultGlassBlurAmount, reset.GlassBlurAmount);
        Assert.Equal(GlassDockSettings.DefaultDockOpacity, reset.DockOpacity);
        Assert.Equal(GlassDockSettings.DefaultBorderThickness, reset.BorderThickness);
        Assert.Equal(GlassDockSettings.DefaultBorderOpacity, reset.BorderOpacity);
        Assert.Equal(GlassMaterialMode.Frosted, reset.GlassMaterialMode);
        Assert.False(reset.PinDock);
        Assert.False(reset.HoverToExpandOnly);
        Assert.True(reset.LaunchAtStartup);
        Assert.False(reset.SuppressWindowsTaskbar);
    }

    [Fact]
    public void Appearance_update_propagates_and_preserves_other_settings()
    {
        var session = new GlassDockSettingsSession(new()
        {
            LaunchAtStartup = true,
            SuppressWindowsTaskbar = false,
            BottomMargin = 44,
            AutoHideDelayMilliseconds = 3100,
            PeekDelayMilliseconds = 4200
        });
        GlassDockSettings? observed = null;
        session.Changed += (_, args) => observed = args.Settings;

        var edited = session.CreateAppearanceUpdate(GlassMaterialMode.Acrylic, 36, 1.5, 14, 48, 0.62, 1.6, 0.4);
        Assert.True(session.Replace(edited));

        Assert.Equal(new DockAppearanceSettings(GlassMaterialMode.Acrylic, 36, 1.5, 14, 48, 0.62, 1.6, 0.4), session.Appearance);
        Assert.Equal(session.Current, observed);
        Assert.True(session.Current.LaunchAtStartup);
        Assert.False(session.Current.SuppressWindowsTaskbar);
        Assert.Equal(44, session.Current.BottomMargin);
        Assert.Equal(3100, session.Current.AutoHideDelayMilliseconds);
        Assert.Equal(4200, session.Current.PeekDelayMilliseconds);
    }

    [Fact]
    public void Appearance_drives_layout_magnification_and_material_values()
    {
        var appearance = new DockAppearanceSettings(GlassMaterialMode.Clear, 32, 1.5, 10, 42, 0.64, 1.8, 0.35);

        Assert.Equal(44, appearance.ButtonWidth);
        Assert.Equal(48, appearance.ButtonHeight);
        Assert.Equal(242, appearance.TargetDockWidth(4));
        Assert.Equal(1.5, appearance.ScaleAtDistance(0), 6);
        Assert.True(appearance.ScaleAtDistance(52) < 1.5);
        Assert.Equal(1.8, appearance.BorderThickness);
        Assert.Equal(0.35, appearance.BorderOpacity);

        var expanded = appearance.ApplyTo(new GlassMaterial { BlurAmount = 3, Opacity = 0.78 }, expanded: true);
        var collapsed = appearance.ApplyTo(new GlassMaterial { BlurAmount = 3, Opacity = 0.78 }, expanded: false);
        Assert.Equal(42, expanded.BlurAmount);
        Assert.Equal(0.64, expanded.Opacity);
        Assert.Equal(42, collapsed.BlurAmount);
        Assert.Equal(0.78, collapsed.Opacity);
    }

    [Fact]
    public void Material_mode_presets_keep_frosted_and_define_acrylic_and_clear()
    {
        var frosted = DockMaterialStylePresets.Create(GlassMaterialMode.Frosted);
        var acrylic = DockMaterialStylePresets.Create(GlassMaterialMode.Acrylic);
        var clear = DockMaterialStylePresets.Create(GlassMaterialMode.Clear);

        Assert.Equal(20, frosted.BlurAmount);
        Assert.Equal(0.78, frosted.Opacity);
        Assert.Equal(1.15, frosted.Saturation);
        Assert.Equal(0xDCEAFFu, frosted.Tint);
        Assert.Equal(1.05, frosted.BorderThickness);
        Assert.Equal(0.78, frosted.BorderOpacity);

        Assert.Equal(10, acrylic.BlurAmount);
        Assert.Equal(0.58, acrylic.Opacity);
        Assert.Equal(1.22, acrylic.Saturation);
        Assert.Equal(0xD8E9FFu, acrylic.Tint);
        Assert.Equal(0.8, acrylic.BorderThickness);
        Assert.Equal(0.48, acrylic.BorderOpacity);

        Assert.Equal(4, clear.BlurAmount);
        Assert.Equal(0.42, clear.Opacity);
        Assert.Equal(1.06, clear.Saturation);
        Assert.Equal(0xE8F3FFu, clear.Tint);
        Assert.Equal(0.5, clear.BorderThickness);
        Assert.Equal(0.25, clear.BorderOpacity);
    }
}
