using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class WindowsKeyGestureTests
{
    [Theory]
    [InlineData(0x5B)]
    [InlineData(0x5C)]
    public void Bare_windows_key_activates_once_on_release(int key)
    {
        var gesture = new WindowsKeyGesture();
        Assert.False(gesture.Process(key, true));
        Assert.False(gesture.Process(key, true));
        Assert.True(gesture.Process(key, false));
        Assert.False(gesture.Process(key, false));
    }

    [Theory]
    [InlineData(0x52)] // R
    [InlineData(0x45)] // E
    [InlineData(0x4C)] // L
    [InlineData(0x44)] // D
    [InlineData(0x09)] // Tab
    [InlineData(0xA2)] // Ctrl
    public void Windows_shortcuts_never_request_dock(int key)
    {
        var gesture = new WindowsKeyGesture();
        Assert.False(gesture.Process(0x5B, true));
        Assert.False(gesture.Process(key, true));
        Assert.False(gesture.Process(key, false));
        Assert.False(gesture.Process(0x5B, false));
        Assert.False(gesture.Process(0x5B, true));
        Assert.True(gesture.Process(0x5B, false));
    }

    [Fact]
    public void Existing_modifier_and_two_windows_keys_are_not_bare()
    {
        var gesture = new WindowsKeyGesture();
        gesture.Process(0xA0, true);
        gesture.Process(0x5B, true);
        gesture.Process(0xA0, false);
        Assert.False(gesture.Process(0x5B, false));
        gesture.Process(0x5B, true);
        gesture.Process(0x5C, true);
        Assert.False(gesture.Process(0x5B, false));
        Assert.False(gesture.Process(0x5C, false));
    }
}
