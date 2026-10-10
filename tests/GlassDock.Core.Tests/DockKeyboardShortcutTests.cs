using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class DockKeyboardShortcutTests
{
    [Fact]
    public void Win_number_and_T_are_the_only_owned_dock_commands()
    {
        Assert.Equal(0, DockKeyboardShortcut.FromVirtualKey(0x54));
        for (var digit = 1; digit <= 9; digit++)
            Assert.Equal(digit, DockKeyboardShortcut.FromVirtualKey(0x30 + digit));
        foreach (var key in new[] { 0x30, 0x4C, 0x53, 0x45, 0x52, 0x20, 0x1B, 0x5B, 0x5C })
            Assert.Equal(-1, DockKeyboardShortcut.FromVirtualKey(key));
    }

    [Fact]
    public void Shortcut_transport_rejects_duplicates_bad_indices_and_expired_signals()
    {
        long sequence = 0;
        var first = DockKeyboardShortcut.Read("SHORTCUT|1|1000|7|2", ref sequence, 1001);
        Assert.NotNull(first);
        Assert.Equal(2, first.Value.Index);
        Assert.Equal((uint)7, first.Value.Revision);
        Assert.Equal(1, sequence);
        Assert.Null(DockKeyboardShortcut.Read("SHORTCUT|1|1000|7|2", ref sequence, 1001));
        Assert.Null(DockKeyboardShortcut.Read("SHORTCUT|2|1000|7|9", ref sequence, 1501));
        Assert.Null(DockKeyboardShortcut.Read("SHORTCUT|2|1002|7|0", ref sequence, 1001));
        Assert.Null(DockKeyboardShortcut.Read("SHORTCUT|2|1001|7|10", ref sequence, 1001));
        Assert.Null(DockKeyboardShortcut.Read("SHORTCUT|2|1001|7|-1", ref sequence, 1001));
        Assert.Null(DockKeyboardShortcut.Read("SHORTCUT|2|1001|7|1|extra", ref sequence, 1001));
        Assert.Null(DockKeyboardShortcut.Read("SHORTCUT|nonsense|1001|7|1", ref sequence, 1001));
        Assert.Equal(1, sequence);
        Assert.Equal(0, DockKeyboardShortcut.Read("SHORTCUT|2|1003|7|0", ref sequence, 1004)?.Index);
        Assert.Equal(2, sequence);
    }

    [Fact]
    public void Modifiers_are_exposed_to_preserve_unrelated_Windows_shortcuts()
    {
        var gesture = new WindowsKeyGesture();
        gesture.Process(0xA0, true);
        gesture.Process(0xA2, true);
        gesture.Process(0xA4, true);
        Assert.True(gesture.IsShiftHeld);
        Assert.True(gesture.IsControlHeld);
        Assert.True(gesture.IsAltHeld);
        gesture.Reset();
        Assert.False(gesture.IsShiftHeld);
        Assert.False(gesture.IsControlHeld);
        Assert.False(gesture.IsAltHeld);
    }
}
