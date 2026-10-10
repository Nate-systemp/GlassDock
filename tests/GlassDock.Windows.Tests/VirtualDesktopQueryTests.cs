using System.Runtime.InteropServices;
using GlassDock.Windows.Applications;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class VirtualDesktopQueryTests
{
    [Fact]
    public void Public_api_recognizes_own_window_and_can_be_recreated()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            nint window = 0;
            try
            {
                window = CreateWindowExW(0x08000080, "STATIC", "Doky virtual desktop query test",
                    0x90000000, 0, 0, 1, 1, 0, 0, 0, 0);
                Assert.NotEqual(0, window);
                for (var i = 0; i < 3; i++)
                {
                    using var query = new WindowsVirtualDesktopQuery();
                    Assert.True(query.IsCurrent(window));
                    Assert.Null(query.IsCurrent(new nint(-1)));
                }
                DestroyWindow(window);
                using var afterDestroy = new WindowsVirtualDesktopQuery();
                Assert.Null(afterDestroy.IsCurrent(window));
                window = 0;
            }
            catch (Exception error) { failure = error; }
            finally { if (window != 0) DestroyWindow(window); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Theory]
    [InlineData(unchecked((int)0x80010108))] // RPC_E_DISCONNECTED
    [InlineData(unchecked((int)0x80004005))] // E_FAIL
    public void Com_failure_falls_back_and_next_query_can_recover(int hr)
    {
        Assert.Null(WindowsVirtualDesktopQuery.QuerySafely(() => throw new COMException("test", hr)));
        Assert.False(WindowsVirtualDesktopQuery.QuerySafely(() => false));
        Assert.True(WindowsVirtualDesktopQuery.QuerySafely(() => true));
        Assert.Null(WindowsVirtualDesktopQuery.QuerySafely(() => throw new InvalidComObjectException()));
    }

    [Fact]
    public void Refresh_burst_and_shutdown_do_not_race_disposed_signal()
    {
        using var service = new WindowsApplicationService();
        Parallel.Invoke(
            () => { for (var i = 0; i < 10000; i++) service.RequestRefresh(); },
            () => { for (var i = 0; i < 10000; i++) service.RequestRefresh(); },
            service.Dispose);
        service.RequestRefresh();
    }

    [Fact]
    public void Concurrent_refresh_requests_publish_a_complete_real_enumeration()
    {
        using var service = new WindowsApplicationService();
        GlassDock.Core.Applications.ApplicationSnapshot? received = null;
        service.SnapshotChanged += (_, snapshot) => Volatile.Write(ref received, snapshot);
        service.Start();
        Parallel.For(0, 200, _ => service.RequestRefresh());
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref received) is not null, TimeSpan.FromSeconds(30)));
        var result = Volatile.Read(ref received)!;
        Assert.Equal(result.Applications.Count, result.Applications.Select(app => app.Id).Distinct().Count());
        Assert.All(result.Applications, app =>
            Assert.Equal(app.Windows.Count, app.Windows.Select(window => window.Handle).Distinct().Count()));
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(uint exStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
}
