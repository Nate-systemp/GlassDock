using GlassDock.Core.Applications;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class MissingPinArtworkTests
{
    [Fact]
    public void Unavailable_target_overrides_cached_original_and_running_artwork()
    {
        var icon = new ApplicationIcon(1, 1, [0, 0, 255, 255]);
        var pin = new PinnedApplication(new(null, "missing.exe"), "Missing", "missing.exe", icon)
            { Availability = PinTargetAvailability.Unavailable };
        var window = new ApplicationWindow(pin.Identity, pin.Name, 42, 1, 1, false, icon);
        var app = Assert.Single(DockApplicationCollection.Combine([pin], [window]));
        Assert.NotSame(icon, app.Icon);
        Assert.Same(ApplicationIconFallback.Visible(null), app.Icon);
        Assert.True(app.IsRunning);
    }

    [Fact]
    public void Missing_or_transparent_artwork_is_visible_without_removing_pins()
    {
        ApplicationIcon?[] missing = [null, new(0, 0, []), new(4, 4, new byte[64]), new(4, 4, [1])];
        foreach (var icon in missing)
        {
            var pin = new PinnedApplication(new(null, "missing.exe"), "Recoverable app", "missing.exe", icon)
                { Availability = PinTargetAvailability.Unavailable };
            var app = Assert.Single(DockApplicationCollection.Combine([pin], []));
            Assert.Equal(pin.Identity.Key, app.Id);
            Assert.Equal(pin.LaunchTarget, app.LaunchTarget);
            Assert.Equal(PinTargetAvailability.Unavailable, app.PinAvailability);
            Assert.True(ApplicationIconFallback.HasArtwork(app.Icon));
            Assert.Same(app.Icon, Assert.Single(DockApplicationCollection.Combine([pin], [])).Icon);
        }
    }

    [Fact]
    public void Real_artwork_returns_after_recovery_and_explicit_removal_closes_collection_gap()
    {
        var first = new PinnedApplication(new(null, "a"), "A", "a", null);
        var second = new PinnedApplication(new(null, "b"), "B", "b", null);
        var recovered = new ApplicationIcon(1, 1, [255, 255, 255, 255]);
        var apps = DockApplicationCollection.Combine([first, second with { Icon = recovered }], []);
        Assert.Equal(new[] { first.Identity.Key, second.Identity.Key }, apps.Select(app => app.Id));
        Assert.Same(recovered, apps[1].Icon);
        Assert.Equal(second.Identity.Key, Assert.Single(DockApplicationCollection.Combine([second], [])).Id);
    }

    [Fact]
    public void Availability_refresh_notifies_existing_item_and_removal_compacts_observable_collection()
    {
        var pins = new[] { "a", "b", "c" }.Select(id => new PinnedApplication(new(null, id), id, id, null)).ToArray();
        var apps = DockApplicationCollection.Combine(pins, []);
        var dock = new DockApplications();
        dock.Apply(new(apps));
        var first = dock.VisibleDockApplications[0];
        var notifications = 0;
        first.PropertyChanged += (_, _) => notifications++;
        dock.Apply(new([apps[0] with { PinAvailability = PinTargetAvailability.Unavailable }, apps[2]]));
        Assert.Same(first, dock.VisibleDockApplications[0]);
        Assert.Equal(1, notifications);
        Assert.Equal(new[] { apps[0].Id, apps[2].Id }, dock.VisibleDockApplications.Select(item => item.Id));
    }
}
