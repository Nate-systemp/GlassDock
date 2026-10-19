using GlassDock.Core.Materials;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class ControlCenterGlassMaterialTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(20)]
    public void Clear_dashboard_limits_optics_without_changing_dock(float strength)
    {
        var dock = new LiquidGlassMaterial { RefractionStrength = strength, SpecularAngleDegrees = 135 };
        var panel = ControlCenterGlassMaterial.Panel(dock);
        var card = ControlCenterGlassMaterial.Control(dock);
        Assert.Equal(strength, dock.RefractionStrength);
        Assert.Equal(.16f, dock.SpecularIntensity);
        Assert.Equal(135, panel.SpecularAngleDegrees);
        Assert.Equal(135, card.SpecularAngleDegrees);
        Assert.Equal(Math.Min(strength * .35f, 5), panel.RefractionStrength);
        Assert.Equal(Math.Min(strength * .2f, 3), card.RefractionStrength);
        Assert.True(card.RefractionStrength <= panel.RefractionStrength);
        Assert.Equal(6, panel.EdgeThickness);
        Assert.Equal(4, card.EdgeThickness);
        Assert.Equal(.18f, panel.SpecularIntensity);
        Assert.Equal(.11f, card.SpecularIntensity);
        Assert.Equal(.8f, panel.SpecularFalloff);
        Assert.Equal(.8f, card.SpecularFalloff);
        Assert.Equal(0, panel.TintOpacity);
        Assert.Equal(0, card.TintOpacity);
        if (strength == 0) Assert.Equal(0, panel.RefractionStrength + card.RefractionStrength);
    }

    [Fact]
    public void Invalid_values_are_normalized_before_deriving_dashboard_optics()
    {
        var invalid = new LiquidGlassMaterial { RefractionStrength = float.NaN, SpecularAngleDegrees = float.PositiveInfinity };
        Assert.Equal(ControlCenterGlassMaterial.Panel(new()), ControlCenterGlassMaterial.Panel(invalid));
        Assert.Equal(ControlCenterGlassMaterial.Control(new()), ControlCenterGlassMaterial.Control(invalid));
    }
}
