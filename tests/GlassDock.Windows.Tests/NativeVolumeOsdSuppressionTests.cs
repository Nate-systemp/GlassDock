using GlassDock.Windows.Desktop;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class NativeVolumeOsdSuppressionTests
{
    [Theory]
    [InlineData(0xAD)]
    [InlineData(0xAE)]
    [InlineData(0xAF)]
    public void First_press_and_release_are_consumed_only_after_audio_accepts(uint key)
    {
        var capture = new VolumeKeyCapture();
        Assert.False(capture.Process(key, true, true, _ => false));
        Assert.False(capture.Process(key, false, true, _ => true));
        Assert.True(capture.Process(key, true, true, _ => true));
        Assert.True(capture.Process(key, false, true, _ => false));
    }

    [Fact]
    public void Held_volume_steps_repeat_but_held_mute_does_not_toggle_repeatedly()
    {
        var capture = new VolumeKeyCapture();
        var calls = 0;
        bool Handle(uint _) { calls++; return true; }
        for (var i = 0; i < 100; i++) Assert.True(capture.Process(0xAF, true, true, Handle));
        Assert.Equal(100, calls);
        Assert.True(capture.Process(0xAF, false, true, Handle));
        for (var i = 0; i < 100; i++) Assert.True(capture.Process(0xAD, true, true, Handle));
        Assert.Equal(101, calls);
    }

    [Fact]
    public void Disable_keeps_captured_release_paired_and_next_press_passes_through()
    {
        var capture = new VolumeKeyCapture();
        Assert.True(capture.Process(0xAE, true, true, _ => true));
        Assert.True(capture.Process(0xAE, true, false, _ => throw new Exception()));
        Assert.True(capture.Process(0xAE, false, false, _ => throw new Exception()));
        Assert.False(capture.Process(0xAE, true, false, _ => throw new Exception()));
        Assert.False(capture.Process(0xAE, false, false, _ => throw new Exception()));
    }

    [Theory]
    [InlineData(0x41)]
    [InlineData(0x5B)]
    [InlineData(0x20)]
    [InlineData(0xFF)]
    public void All_other_keys_pass_through_without_audio_work(uint key)
    {
        var capture = new VolumeKeyCapture();
        Assert.False(capture.Process(key, true, true, _ => throw new Exception()));
        Assert.False(capture.Process(key, false, true, _ => throw new Exception()));
    }

    [Fact]
    public void Unavailable_audio_does_not_consume_keys()
    {
        using var audio = new MasterVolumeService();
        Assert.False(audio.TryHandleVolumeKey(0xAF));
        Assert.False(audio.TryHandleVolumeKey(0xAD));
        Assert.False(audio.TryHandleVolumeKey(0x41));
    }

    [Fact]
    public void Lifecycle_reset_releases_a_stale_mute_capture()
    {
        var capture = new VolumeKeyCapture();
        var calls = 0;
        bool Handle(uint _) { calls++; return true; }
        Assert.True(capture.Process(0xAD, true, true, Handle));
        capture.Reset();
        Assert.True(capture.Process(0xAD, true, true, Handle));
        Assert.Equal(2, calls);
    }
}
