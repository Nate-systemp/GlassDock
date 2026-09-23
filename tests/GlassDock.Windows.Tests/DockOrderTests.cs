using GlassDock.Core.Applications;
using GlassDock.Windows.Applications;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class DockOrderTests
{
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
