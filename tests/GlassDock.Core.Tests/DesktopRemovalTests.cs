using System.Collections.Concurrent;
using GlassDock.Core.Applications;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class DesktopRemovalTests
{
    private static readonly ApplicationIdentity Identity = new("test.app", "test.exe");
    private static ApplicationWindow Window(int handle, bool current) =>
        new(Identity, "Test", handle, 1, 1, false, null) { IsOnCurrentDesktop = current };
    private static ApplicationSnapshot Snapshot(params ApplicationWindow[] windows) => new(
        DockApplicationCollection.Combine([new(Identity, "Pinned", "test.exe", null)],
            VirtualDesktopPolicy.Filter(windows, false)));

    [Fact]
    public void Removing_desktop_moves_windows_to_survivor_without_replacing_pin_or_losing_preview_geometry()
    {
        var ui = new Queue<Action>();
        var apps = new DockApplications();
        using var pump = new ApplicationSnapshotPump(action => { ui.Enqueue(action); return true; }, apps.Apply);
        pump.Publish(Snapshot(Window(1, true), Window(2, false)));
        ui.Dequeue()();
        var pin = Assert.Single(apps.VisibleDockApplications);
        for (var i = 0; i < 40; i++)
        {
            pump.Publish(Snapshot(Window(1, true), Window(2, false)));
            pump.Publish(Snapshot()); // Transitional enumeration while Shell moves windows.
            pump.Publish(Snapshot(Window(1, true), Window(2, true)));
            Assert.Single(ui);
            ui.Dequeue()();
            Assert.Same(pin, Assert.Single(apps.VisibleDockApplications));
            Assert.True(pin.IsRunning);
            Assert.Equal(2, pin.Application.Windows.Count);
            var frame = WindowPreviewFrame.Create(pin.Application.Windows, 20, 800, 500);
            Assert.Equal(0, frame.Page);
            Assert.Equal(2, frame.Compact.Cards.Count);
            Assert.Equal(2, frame.Expanded.Cards.Count);
        }
    }

    [Fact]
    public void Refresh_during_collection_change_cannot_reenter_apply_even_when_UI_pumps_messages()
    {
        var ui = new Queue<Action>();
        var apps = new DockApplications();
        var depth = 0;
        var applied = new List<ApplicationSnapshot>();
        using var pump = new ApplicationSnapshotPump(action => { ui.Enqueue(action); return true; }, snapshot =>
        {
            Assert.Equal(1, ++depth);
            apps.Apply(snapshot);
            applied.Add(snapshot);
            depth--;
        });
        var moved = Snapshot(Window(1, true), Window(2, true));
        apps.VisibleDockApplications.CollectionChanged += (_, _) =>
        {
            pump.Publish(moved);
            pump.Reapply();
            Assert.Empty(ui); // No callback available to run inside this mutation.
        };
        pump.Publish(Snapshot(Window(1, true)));
        ui.Dequeue()();
        Assert.Single(ui);
        ui.Dequeue()();
        Assert.Same(moved, applied[^1]);
        Assert.Equal(2, apps.VisibleDockApplications[0].Application.Windows.Count);
    }

    [Fact]
    public void Concurrent_refresh_burst_keeps_latest_and_disposal_discards_queued_callback()
    {
        var ui = new ConcurrentQueue<Action>();
        var applied = new List<ApplicationSnapshot>();
        using var pump = new ApplicationSnapshotPump(action => { ui.Enqueue(action); return true; }, applied.Add);
        Parallel.For(0, 200, i => pump.Publish(Snapshot(Window(i + 1, true))));
        var survivor = Snapshot(Window(500, true));
        pump.Publish(survivor);
        Assert.Single(ui);
        Assert.True(ui.TryDequeue(out var callback));
        callback();
        Assert.Same(survivor, Assert.Single(applied));
        pump.Publish(Snapshot());
        pump.Dispose();
        Assert.True(ui.TryDequeue(out callback));
        callback();
        Assert.Single(applied);
    }

    [Fact]
    public void Rejected_dispatch_can_retry_without_replaying_old_desktop_state()
    {
        var ui = new Queue<Action>();
        var accept = false;
        ApplicationSnapshot? applied = null;
        using var pump = new ApplicationSnapshotPump(action =>
        {
            if (!accept) return false;
            ui.Enqueue(action); return true;
        }, snapshot => applied = snapshot);
        pump.Publish(Snapshot());
        accept = true;
        var moved = Snapshot(Window(1, true));
        pump.Publish(moved);
        ui.Dequeue()();
        Assert.Same(moved, applied);
    }
}
