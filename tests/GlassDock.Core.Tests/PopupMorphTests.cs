using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class PopupMorphTests
{
    [Theory]
    [InlineData(40)]
    [InlineData(370)]
    public void Funnel_keeps_bottom_at_clicked_source_during_strong_phase(double sourceX)
    {
        var matrix = PopupMorph.Funnel(PopupMorph.Progress(.25 * .3, false, 0), 420, 464, sourceX, 490, 16);
        var bottom = System.Numerics.Vector4.Transform(new System.Numerics.Vector4(210, 448, 0, 1), matrix);
        Assert.InRange(Math.Abs(bottom.X / bottom.W - sourceX), 0, .002);
        Assert.InRange(Math.Abs(bottom.Y / bottom.W - 490), 0, .002);
        var final = PopupMorph.Funnel(1, 420, 464, sourceX, 490, 16);
        Assert.Equal(System.Numerics.Matrix4x4.Identity, final);
    }

    [Fact]
    public void Forward_tilt_projects_upper_rows_more_and_keeps_bottom_hinged()
    {
        const double progress = .8; // Initial V is resolved; forward tilt remains.
        var frame = PopupMorph.Frame(progress);
        var matrix = PopupMorph.Transform(progress, 200, 400, 30, 40, 384);
        double RelativeProjection(float fractionAboveBottom)
        {
            var point = System.Numerics.Vector4.Transform(
                new System.Numerics.Vector4(300, 400 - 384 * fractionAboveBottom, 0, 1), matrix);
            var distance = point.X / point.W - 230;
            return distance / (100 * frame.ScaleX) - 1;
        }
        Assert.InRange(Math.Abs(RelativeProjection(0)), 0, .000001);
        Assert.InRange(RelativeProjection(.2f), 0, .02);
        Assert.True(RelativeProjection(.5f) > RelativeProjection(.2f));
        Assert.True(RelativeProjection(.8f) > RelativeProjection(.5f));
        Assert.InRange(RelativeProjection(1), .07, .10);
        var hinge = System.Numerics.Vector4.Transform(new System.Numerics.Vector4(200, 400, 0, 1), matrix);
        Assert.InRange(Math.Abs(hinge.X / hinge.W - 230), 0, .001);
        Assert.InRange(Math.Abs(hinge.Y / hinge.W - 440), 0, .001);
    }

    [Fact]
    public void Fold_fans_out_then_resolves_to_identity_without_layer_offsets()
    {
        var matrix = PopupMorph.Transform(.25, 200, 400, 30, 40, 384);
        static System.Numerics.Vector2 Project(System.Numerics.Matrix4x4 m, float x, float y)
        {
            var point = System.Numerics.Vector4.Transform(new System.Numerics.Vector4(x, y, 0, 1), m);
            Assert.True(point.W > 0);
            return new(point.X / point.W, point.Y / point.W);
        }
        var topLeft = Project(matrix, 16, 16);
        var topRight = Project(matrix, 384, 16);
        var bottomLeft = Project(matrix, 16, 400);
        var bottomRight = Project(matrix, 384, 400);
        Assert.True(topRight.X - topLeft.X > 4 * (bottomRight.X - bottomLeft.X));
        Assert.Equal(System.Numerics.Matrix4x4.Identity, PopupMorph.Transform(1, 200, 400, 0, 0, 384));
        var collapsed = PopupMorph.Transform(0, 200, 400, 70, 30, 384);
        Assert.InRange(System.Numerics.Vector2.Distance(Project(collapsed, 16, 16), new(270, 430)), 0, .001f);
        // Mask coordinates use exactly the same projection at every DPI.
        foreach (var units in new[] { 2f, 2.5f, 3f, 3.5f, 4f })
        {
            var physical = System.Numerics.Matrix4x4.CreateScale(1 / units, 1 / units, 1) * matrix *
                System.Numerics.Matrix4x4.CreateScale(units, units, 1);
            Assert.InRange(System.Numerics.Vector2.Distance(Project(physical, 16 * units, 16 * units) / units, topLeft), 0, .001f);
        }
    }

    [Fact]
    public void Endpoints_are_exact_and_opacity_stays_until_object_is_small()
    {
        Assert.Equal(new PopupMorphFrame(0, 0, 1, 0), PopupMorph.Frame(0));
        Assert.Equal(new PopupMorphFrame(1, 1, 0, 1), PopupMorph.Frame(1));
        for (var t = 0d; t <= .18; t += .01)
        {
            var frame = PopupMorph.Frame(PopupMorph.Progress(t, true, 1));
            Assert.Equal(1, frame.Opacity);
        }
        var small = PopupMorph.Frame(.04);
        Assert.True(small.ScaleX < .2 && small.ScaleY < .05);
        Assert.Equal(.5, small.Opacity);
    }

    [Fact]
    public void Interrupting_open_preserves_the_current_shape_and_reverses()
    {
        var current = PopupMorph.Progress(.07, false, 0);
        Assert.Equal(current, PopupMorph.Progress(0, true, current));
        Assert.True(PopupMorph.Progress(.1, true, current) < current);
        Assert.Equal(0, PopupMorph.Progress(.21, true, current));
        Assert.Equal(1, PopupMorph.Progress(.25, false, 0));
    }

    [Theory]
    [InlineData(-500, 620)]
    [InlineData(50, 440)]
    [InlineData(720, 530)]
    public void Collapsed_transform_ends_at_source_even_when_clamped(double x, double y)
    {
        var frame = PopupMorph.Frame(0);
        var originX = 210d;
        var originY = 430d;
        double Transform(double point, double origin, double source, double scale) =>
            (point - origin) * scale + origin + (source - origin) * frame.Travel;
        Assert.Equal(x, Transform(16, originX, x, frame.ScaleX));
        Assert.Equal(y, Transform(16, originY, y, frame.ScaleY));
    }
}
