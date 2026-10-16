using System.Numerics;
using GlassDock.Core.Materials;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class LiquidGlassShaderStateTests
{
    private static readonly LiquidGlassShaderState Original = new(Vector4.One, Vector4.One,
        Vector4.One, Vector4.One, Vector2.One, Vector4.One);

    [Fact]
    public void New_or_recreated_effect_requires_all_parameters()
        => Assert.Equal(63, Original.ChangesFrom(null));

    [Fact]
    public void Identical_frames_need_no_property_writes()
    {
        for (var i = 0; i < 10000; i++) Assert.Equal(0, Original.ChangesFrom(Original));
    }

    [Fact]
    public void Every_parameter_invalidates_independently_including_subpixel_geometry()
    {
        var tiny = new Vector4(MathF.BitIncrement(1f), 1, 1, 1);
        Assert.Equal(1, (Original with { Dock = tiny }).ChangesFrom(Original));
        Assert.Equal(2, (Original with { Wave = tiny }).ChangesFrom(Original));
        Assert.Equal(4, (Original with { Optics = tiny }).ChangesFrom(Original));
        Assert.Equal(8, (Original with { Lighting = tiny }).ChangesFrom(Original));
        Assert.Equal(16, (Original with { FrameSize = Vector2.Zero }).ChangesFrom(Original));
        Assert.Equal(32, (Original with { Lens = tiny }).ChangesFrom(Original));
    }

    [Fact]
    public void Changed_then_restored_state_is_not_suppressed()
    {
        var moved = Original with { Dock = Vector4.Zero, Wave = Vector4.Zero };
        Assert.Equal(3, moved.ChangesFrom(Original));
        Assert.Equal(3, Original.ChangesFrom(moved));
    }
}
