using GlassDock.Core.Applications;
using GlassDock.Windows.Applications;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class DockOrderTests
{
    [Fact]
    public void Secondary_monitor_reorder_persists_then_primary_reorder_preserves_hidden_slots()
    {
        var path = Path.Combine(Path.GetTempPath(), $"doky-monitor-order-{Guid.NewGuid():N}.json");
        try
        {
            DockApplication[] apps = [App("a", true), App("primary", false), App("b", true), App("secondary", false)];
            var ids = apps.Select(app => app.Id).ToArray();
            var order = WindowsApplicationService.MergeRequestedOrder(ids, [ids[3], ids[2], ids[0]])!;
            Assert.True(new DockPinStore(path).Reorder(ids, order));
            var restored = new DockPinStore(path).ApplyOrder(apps).Select(app => app.Id).ToArray();
            Assert.Equal(new[] { ids[3], ids[1], ids[2], ids[0] }, restored);
            var primaryOrder = WindowsApplicationService.MergeRequestedOrder(restored, [ids[0], ids[1], ids[2]])!;
            Assert.Equal(new[] { ids[3], ids[0], ids[1], ids[2] }, primaryOrder);
            Assert.True(new DockPinStore(path).Reorder(restored, primaryOrder));
            Assert.Equal(primaryOrder, new DockPinStore(path).ApplyOrder(apps).Select(app => app.Id));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Running_app_can_move_before_pins_and_order_survives_reload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"glassdock-order-{Guid.NewGuid():N}.json");
        try
        {
            var first = App("first", true);
            var middle = App("middle", true);
            var last = App("last", false);
            DockApplication[] apps = [first, middle, last];
            var store = new DockPinStore(path);
            Assert.Equal(apps, store.ApplyOrder(apps));
            Assert.True(store.Reorder(apps.Select(app => app.Id).ToArray(), [last.Id, first.Id, middle.Id]));
            var loaded = new DockPinStore(path);
            var ordered = loaded.ApplyOrder(apps);
            Assert.Equal(new[] { last, first, middle }, ordered);
            Assert.Same(last, ordered[0]);
            Assert.False(ordered[0].IsPinned);
            Assert.Equal(new[] { first, middle }, loaded.ApplyOrder([first, middle]));
            Assert.Equal(new[] { last, first, middle }, loaded.ApplyOrder(apps));
            Assert.False(loaded.Reorder(apps.Select(app => app.Id).ToArray(), [first.Id, first.Id, middle.Id]));
            Assert.Equal(new[] { last, first, middle }, new DockPinStore(path).ApplyOrder(apps));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Per_monitor_reorder_preserves_hidden_global_app_slots()
    {
        string[] current = ["pinned-a", "hidden-monitor-2", "pinned-b", "hidden-monitor-3", "pinned-c"];

        var merged = WindowsApplicationService.MergeRequestedOrder(
            current,
            ["pinned-c", "pinned-a", "pinned-b"]);

        Assert.NotNull(merged);
        Assert.Equal(
            ["pinned-c", "hidden-monitor-2", "pinned-a", "hidden-monitor-3", "pinned-b"],
            merged);
    }

    [Fact]
    public void Per_monitor_reorder_rejects_unknown_or_duplicate_ids()
    {
        string[] current = ["a", "hidden", "b"];

        Assert.Null(WindowsApplicationService.MergeRequestedOrder(current, ["a", "missing"]));
        Assert.Null(WindowsApplicationService.MergeRequestedOrder(current, ["a", "a"]));
    }

    [Fact]
    public void Legacy_pins_without_order_keep_the_existing_application_order()
    {
        var path = Path.Combine(Path.GetTempPath(), $"glassdock-order-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"Pins\":[],\"Excluded\":[]}");
            DockApplication[] apps = [App("pinned", true), App("running", false)];
            Assert.Equal(apps, new DockPinStore(path).ApplyOrder(apps));
        }
        finally { File.Delete(path); }
    }

    private static DockApplication App(string name, bool pinned)
    {
        var identity = new ApplicationIdentity(name, null);
        return new(identity.Key, identity, name, null, pinned, [], null);
    }
}
