using System.Numerics;
using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class DockSpecularLightingTests
{
    [Theory]
    [InlineData(900,68,45)]
    [InlineData(900,68,135)]
    [InlineData(1800,136,270)]
    [InlineData(400,68,0)]
    [InlineData(400,68,360)]
    public void Native_gradient_matches_shader_projection(double width, double height, double angle)
    {
        var (start,end) = DockSpecularLighting.Gradient(width,height,angle);
        var direction = end-start;
        foreach (var local in new[] { Vector2.Zero, Vector2.One, new Vector2(.5f), new Vector2(.2f,.8f) })
        {
            var p = local * new Vector2((float)width,(float)height);
            var actual = Vector2.Dot(p-start,direction)/direction.LengthSquared();
            var x = Math.Cos(angle*Math.PI/180); var y = Math.Sin(angle*Math.PI/180);
            var expected = ((local.X-.5)*x+(local.Y-.5)*y)/(Math.Abs(x)+Math.Abs(y))+.5;
            Assert.InRange(Math.Abs(actual-expected),0,.00001);
        }
    }

    [Fact]
    public void Optical_settings_are_bounded_and_old_defaults_preserve_the_approved_finish()
    {
        var defaults = new GlassDockSettings();
        Assert.Equal(12,defaults.ClearRefractionStrength);
        Assert.Equal(45,defaults.SpecularHighlightAngle);
        var bounded = GlassDockSettings.Normalize(defaults with { ClearRefractionStrength=500,SpecularHighlightAngle=-90 });
        Assert.Equal(20,bounded.ClearRefractionStrength); Assert.Equal(0,bounded.SpecularHighlightAngle);
        var nonfinite = GlassDockSettings.Normalize(defaults with { ClearRefractionStrength=double.NaN,SpecularHighlightAngle=double.PositiveInfinity });
        Assert.Equal(defaults,nonfinite);
        Assert.Equal(45,new LiquidGlassMaterial { SpecularAngleDegrees=float.NaN }.Normalize().SpecularAngleDegrees);
    }
}
