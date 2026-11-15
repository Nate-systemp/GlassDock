using GlassDock.Core.Settings;
using GlassDock.Windows.Desktop;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class UtilityKeyStateTests
{
    [Fact]
    public void Keyboard_make_repeat_and_break_form_one_gesture()
    {
        var events = new List<(UtilityKind, bool)>();
        var keys = new UtilityKeyState((kind, down) => events.Add((kind, down)));
        keys.Keyboard(1, 0xAE, true);
        keys.Keyboard(1, 0xAE, true);
        keys.Keyboard(1, 0x41, true);
        keys.Keyboard(1, 0xAE, false);
        keys.Keyboard(1, 0xAE, true);
        keys.Reset();
        Assert.Equal(new[] { (UtilityKind.Volume, true), (UtilityKind.Volume, false),
            (UtilityKind.Volume, true), (UtilityKind.Volume, false) }, events);
    }
    [Fact]
    public void Repeats_do_not_repeat_down_and_empty_report_releases()
    {
        var events = new List<(UtilityKind, bool)>();
        var keys = new UtilityKeyState((kind, down) => events.Add((kind, down)));
        keys.Update(1, 2, [0x6F]);
        keys.Update(1, 2, [0x6F]);
        keys.Update(1, 3, [0xE9]);
        keys.Update(1, 3, []);
        Assert.Equal(new[] { (UtilityKind.Brightness, true), (UtilityKind.Volume, true), (UtilityKind.Volume, false) }, events);
        keys.Update(1, 2, []);
        Assert.Equal((UtilityKind.Brightness, false), events[^1]);
    }

    [Fact]
    public void Independent_devices_and_reports_preserve_other_held_keys()
    {
        var events = new List<(UtilityKind, bool)>();
        var keys = new UtilityKeyState((kind, down) => events.Add((kind, down)));
        keys.Update(1, 0, [0xEA]);
        keys.Update(2, 0, [0xE9]);
        keys.Update(1, 0, []);
        Assert.Equal(2, events.Count);
        keys.Remove(2);
        Assert.Equal((UtilityKind.Volume, false), events[^1]);
        keys.Update(1, 1, [0x70]);
        keys.Reset();
        Assert.Equal((UtilityKind.Brightness, false), events[^1]);
    }

    [Theory]
    [InlineData(0xAD, 0xE2)]
    [InlineData(0xAE, 0xEA)]
    [InlineData(0xAF, 0xE9)]
    [InlineData(0x41, 0)]
    public void Only_volume_virtual_keys_are_mapped(int key, int usage)
        => Assert.Equal(usage, UtilityKeyState.KeyboardUsage((ushort)key));
}
