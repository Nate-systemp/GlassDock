using GlassDock.Windows.Desktop;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class NightLightUtilityTests
{
    private static byte[] Off() => Convert.FromHexString(
        "434201000A0201002A068995FCBE062A2B0E1343420100D00A02C614A9F6E2D3EFEAE6ED0100000000");
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1800000000);

    [Fact]
    public void On_and_off_updates_preserve_unknown_fields_at_every_envelope_level()
    {
        var bytes = Convert.FromHexString(
            "434201000A0201002A068995FCBE062A2B0E1543420100A201D00A02C614A9F6E2D3EFEAE6ED0100A20100A20100A20100");
        var original = (byte[])bytes.Clone();
        var before = new NightLightCloudStore(bytes);
        Assert.False(before.On);
        var onBytes = before.SetActive(true, Now, 0);
        var on = new NightLightCloudStore(onBytes);
        Assert.True(on.On); Assert.True(on.Usable);
        Assert.Equal((ulong)Now.ToUnixTimeSeconds(), on.Modified);
        Assert.Equal((ulong)Now.UtcDateTime.ToFileTimeUtc(), on.LastTransition);
        Assert.Equal(4, Markers(onBytes));
        var offBytes = on.SetActive(false, Now, on.Modified + 50);
        var off = new NightLightCloudStore(offBytes);
        Assert.False(off.On);
        Assert.Equal(on.Modified + 52, off.Modified);
        Assert.Equal(4, Markers(offBytes));
        Assert.Equal(original, bytes);
        Assert.Equal(bytes[..8], onBytes[..8]);
    }

    private static int Markers(byte[] bytes) => Enumerable.Range(0, bytes.Length - 1)
        .Count(index => bytes[index] == 0xA2 && bytes[index + 1] == 1);

    [Fact]
    public void Malformed_future_and_duplicate_known_fields_are_rejected()
    {
        var version = Off(); version[2] = 2;
        Assert.Throws<InvalidDataException>(() => new NightLightCloudStore(version));
        Assert.Throws<InvalidDataException>(() => new NightLightCloudStore(Off()[..^1]));
        Assert.Throws<InvalidDataException>(() => new NightLightCloudStore([.. Off(), 0]));
        var uninitialized = Convert.FromHexString("434201000A0201002A068995FCBE062A2B0E054342010000000000");
        Assert.Throws<InvalidDataException>(() => new NightLightCloudStore(uninitialized));
        var duplicate = Convert.FromHexString(
            "434201000A0201002A068995FCBE062A2B0E174342010010001000D00A02C614A9F6E2D3EFEAE6ED0100000000");
        Assert.Throws<InvalidDataException>(() => new NightLightCloudStore(duplicate));
        var futureStatus = Convert.FromHexString(
            "434201000A0201002A068995FCBE062A2B0E15434201001002D00A02C614A9F6E2D3EFEAE6ED0100000000");
        Assert.Throws<InvalidDataException>(() => new NightLightCloudStore(futureStatus));
    }

    [Fact]
    public async Task Unknown_format_quarantines_without_any_write()
    {
        var writes = 0;
        using var service = new NightLightUtilityService(new(() => [1, 2, 3], (_, _, _) => writes++));
        await service.ToggleAsync(); await service.ToggleAsync(); await service.RefreshAsync();
        Assert.Equal(0, writes); Assert.Null(service.State.On);
        Assert.False(service.State.Available); Assert.False(service.State.Busy);
    }

    [Fact]
    public async Task Native_confirmation_and_animation_share_one_nonqueued_gate()
    {
        var bytes = Off(); var writes = 0;
        var settling = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = new NightLightUtilityService(new(() => bytes, (expected, updated, cancel) =>
        { Assert.Equal(expected, bytes); cancel.ThrowIfCancellationRequested(); bytes = updated; writes++; }));
        var toggle = service.ToggleAsync(async () => { settling.TrySetResult(); await finish.Task; });
        try
        {
            await settling.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(service.State.On); Assert.True(service.State.Busy);
            await service.ToggleAsync(); await service.RefreshAsync();
            Assert.Equal(1, writes);
        }
        finally { finish.TrySetResult(); await toggle; }
        Assert.False(service.State.Busy);
        bytes = new NightLightCloudStore(bytes).SetActive(false, Now, 0);
        await service.RefreshAsync();
        Assert.False(service.State.On); Assert.Equal(1, writes);
    }

    [Fact]
    public async Task Failed_write_recovers_latest_state_without_retry_or_rollback()
    {
        var writes = 0;
        using var service = new NightLightUtilityService(new(Off, (_, _, _) =>
        { writes++; throw new IOException("Concurrent Windows update"); }));
        await service.ToggleAsync();
        Assert.Equal(1, writes);
        Assert.False(service.State.On); Assert.False(service.State.Available);
        Assert.Contains("Concurrent", service.State.Message);
    }
}
