using GlassDock.Core.Desktop;
using GlassDock.Core.Settings;
using System.Text.Json;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class UtilitiesHoldGestureTests
{
    [Fact]
    public void Background_hold_fires_once_at_threshold()
    {
        var gesture = new UtilitiesHoldGesture();
        gesture.Begin(true, 10, 10, 100);
        Assert.False(gesture.Take(699));
        Assert.True(gesture.Take(700));
        Assert.False(gesture.Take(701));
    }

    [Theory]
    [InlineData(false, 10, 10, true)]
    [InlineData(true, 15, 10, true)]
    [InlineData(true, 10, 15, true)]
    [InlineData(true, 10, 10, false)]
    public void Content_or_movement_cancels(bool start, double x, double y, bool end)
    {
        var gesture = new UtilitiesHoldGesture();
        gesture.Begin(start, 10, 10, 0);
        gesture.Move(x, y, end);
        Assert.False(gesture.Take(600));
    }

    [Fact]
    public void Cancellation_and_next_hold_are_independent()
    {
        var gesture = new UtilitiesHoldGesture();
        gesture.Begin(true, 0, 0, 0);
        gesture.Cancel();
        Assert.False(gesture.Take(600));
        gesture.Begin(true, 0, 0, 700);
        Assert.False(gesture.Take(800));
        Assert.True(gesture.Take(1300));
    }

    [Theory]
    [InlineData(UtilitiesLayout.Expanded)]
    [InlineData(UtilitiesLayout.Compact)]
    [InlineData(UtilitiesLayout.Hidden)]
    public void Layout_round_trips(UtilitiesLayout layout)
    {
        var settings = new GlassDockSettings { UtilitiesLayout = layout, UtilitiesPlacement = UtilitiesPlacement.SplitDock };
        var restored = JsonSerializer.Deserialize<GlassDockSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(layout, restored.UtilitiesLayout);
        Assert.Equal(UtilitiesPlacement.SplitDock, restored.UtilitiesPlacement);
        Assert.Equal(UtilitiesLayout.Expanded, new GlassDockSettings().UtilitiesLayout);
    }
}
