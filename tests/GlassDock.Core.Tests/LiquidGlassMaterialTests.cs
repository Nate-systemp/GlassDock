using GlassDock.Core.Materials;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class LiquidGlassMaterialTests
{
    [Fact]
    public void DispersionDefaultIsRestrainedAndNormalizationPreservesDefaults()
    {
        var material = new LiquidGlassMaterial();
        Assert.Equal(.65f, material.ChromaticDispersion);
        Assert.Equal(material, material.Normalize());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(.85f, .85f)]
    [InlineData(1, 1)]
    [InlineData(float.MaxValue, 1)]
    [InlineData(float.NaN, .65f)]
    [InlineData(float.PositiveInfinity, .65f)]
    [InlineData(float.NegativeInfinity, .65f)]
    public void DispersionIsFiniteBoundedAndCanBeDisabled(float input, float expected)
    {
        var baseline = new LiquidGlassMaterial();
        var normalized = (baseline with { ChromaticDispersion = input }).Normalize();
        Assert.Equal(expected, normalized.ChromaticDispersion);
        Assert.Equal(baseline, normalized with { ChromaticDispersion = baseline.ChromaticDispersion });
    }

    [Fact]
    public void DefaultsKeepRecognizableBackdropAndBoundedLensRadius()
    {
        var m = new LiquidGlassMaterial().Normalize();
        Assert.InRange(m.RefractionStrength, 1, m.EdgeThickness);
        Assert.InRange(m.DiffusionAmount, 0, 1);
        Assert.InRange(m.TintOpacity, 0, .1f);
        Assert.InRange(m.SpecularIntensity, 0, .2f);
    }
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void InvalidOpticsCannotReachGpuConstants(float value)
    {
        var m = new LiquidGlassMaterial { RefractionStrength = value, RefractionFalloff = value,
            EdgeThickness = value, DiffusionAmount = value, TintOpacity = value,
            SpecularIntensity = value, SpecularFalloff = value }.Normalize();
        Assert.Equal(new LiquidGlassMaterial(), m);
    }
    [Fact]
    public void ZeroRefractionCanBeUsedAsAnAlignmentControl()
    {
        var m = new LiquidGlassMaterial { RefractionStrength = 0, DiffusionAmount = 0, TintOpacity = 0, SpecularIntensity = 0 }.Normalize();
        Assert.Equal(0, m.RefractionStrength);
        Assert.Equal(0, m.DiffusionAmount);
        Assert.Equal(0, m.TintOpacity);
        Assert.Equal(0, m.SpecularIntensity);
    }
}
