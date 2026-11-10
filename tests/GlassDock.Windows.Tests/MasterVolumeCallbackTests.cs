using System.Runtime.InteropServices;
using GlassDock.Windows.Desktop;
using Xunit;
using Xunit.Abstractions;

namespace GlassDock.Windows.Tests;

public sealed class MasterVolumeCallbackTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Passive_monitor_reports_an_endpoint_or_explicit_unavailability_without_writing_volume()
    {
        var initial = new TaskCompletionSource<MasterVolumeState>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = new MasterVolumeService();
        service.Changed += (_, args) => initial.TrySetResult(args.State);
        service.Start();
        var state = await initial.Task.WaitAsync(TimeSpan.FromSeconds(5));
        output.WriteLine(state.Available ? "Read-only Core Audio endpoint monitoring initialized." : state.Message);
        Assert.False(string.IsNullOrWhiteSpace(state.Message));
        if (state.Available)
        {
            Assert.InRange(state.Volume, 0, 1);
            Assert.False(string.IsNullOrWhiteSpace(state.EndpointId));
        }
        else Assert.Null(state.EndpointId);
    }

    [Fact]
    public void Endpoint_callback_exposes_the_actual_Core_Audio_COM_interface()
    {
        var seen = nint.Zero;
        var callback = new SystemControlAudio.VolumeCallback(value => seen = value);
        var pointer = Marshal.GetComInterfaceForObject(callback, typeof(IVolumeNotificationCallback));
        try
        {
            var iid = new Guid("657804FA-D6AD-4496-8A60-352752AF4F89");
            Assert.Equal(0, Marshal.QueryInterface(pointer, in iid, out var queried));
            Marshal.Release(queried);
            Assert.Equal(0, callback.OnNotify(123)); Assert.Equal((nint)123, seen);
        }
        finally { Marshal.Release(pointer); GC.KeepAlive(callback); }
    }
    [Fact]
    public void Device_callback_is_COM_visible_and_filters_default_roles()
    {
        var changes = 0;
        var callback = new SystemControlAudio.DeviceCallback(() => changes++);
        var pointer = Marshal.GetComInterfaceForObject(callback, typeof(IDefaultAudioDeviceCallback));
        try
        {
            var iid = new Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0");
            Assert.Equal(0, Marshal.QueryInterface(pointer, in iid, out var queried)); Marshal.Release(queried);
            callback.OnDefaultDeviceChanged(1, 1, "microphone");
            callback.OnDefaultDeviceChanged(0, 2, "communications");
            Assert.Equal(0, changes);
            callback.OnDefaultDeviceChanged(0, 1, "output"); Assert.Equal(1, changes);
        }
        finally { Marshal.Release(pointer); GC.KeepAlive(callback); }
    }
    [Fact]
    public void Managed_failure_does_not_escape_into_the_audio_callback()
    {
        var callback = new SystemControlAudio.VolumeCallback(_ => throw new InvalidOperationException("Test failure"));
        Assert.Equal(0, callback.OnNotify(0));
    }
}
