using GlassDock.Core.Applications;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class DockApplicationTests
{
    private static PinnedApplication Pin(string id, string path = "C:\\Apps\\app.exe") =>
        new(new(id, path), id, id + ".lnk", null);
    private static ApplicationWindow Window(string? id, long handle = 1, string path = "C:\\Apps\\app.exe", bool active = false) =>
        new(new(id, path), id ?? path, handle, 42, 100, active, null);

    [Fact]
    public void PinsKeepTheirOrderAndStayWhenStopped()
    {
        var pins = new[] { Pin("Zulu"), Pin("Alpha"), Pin("Middle") };
        var apps = DockApplicationCollection.Combine(pins, [Window("Alpha")]);
        Assert.Equal(new[] { "Zulu", "Alpha", "Middle" }, apps.Select(app => app.Name));
        Assert.All(apps, app => Assert.True(app.IsPinned));
        Assert.False(apps[0].IsRunning);
        Assert.True(apps[1].IsRunning);
        Assert.Equal(3, DockApplicationCollection.Combine(pins, []).Count);
    }

    [Fact]
    public void MultipleWindowsAndDuplicateEventsProduceOnePinnedItem()
    {
        var apps = DockApplicationCollection.Combine([Pin("Browser")],
            [Window("browser", 2), Window("BROWSER", 1, active: true), Window("browser", 2)]);
        var app = Assert.Single(apps);
        Assert.Equal(2, app.Windows.Count);
        Assert.True(app.IsRunning);
        Assert.True(app.IsActive);
        Assert.Equal(1, app.Windows[0].Handle);
    }

    [Fact]
    public void ExecutableFallbackMatchesOnePinWithoutExplicitWindowId()
    {
        var app = Assert.Single(DockApplicationCollection.Combine([Pin("Editor")],
            [Window(null, path: "c:/apps/APP.exe")]));
        Assert.True(app.IsPinned && app.IsRunning);
    }

    [Fact]
    public void DifferentExplicitIdsUsingOneExecutableRemainSeparate()
    {
        var apps = DockApplicationCollection.Combine([Pin("Profile.One")], [Window("Profile.Two")]);
        Assert.Equal(2, apps.Count);
        Assert.False(apps[0].IsRunning);
        Assert.False(apps[1].IsPinned);
    }

    [Fact]
    public void AmbiguousExecutableDoesNotGuessBetweenProfiles()
    {
        var apps = DockApplicationCollection.Combine([Pin("Profile.One"), Pin("Profile.Two")], [Window(null)]);
        Assert.Equal(3, apps.Count);
        Assert.All(apps.Take(2), app => Assert.False(app.IsRunning));
    }

    [Fact]
    public void UnpinnedOrderIsDeterministicAndClosedAppsDisappear()
    {
        var first = DockApplicationCollection.Combine([Pin("Pinned")], [Window("Zulu", 1), Window("Alpha", 2)]);
        var second = DockApplicationCollection.Combine([Pin("Pinned")], [Window("Alpha", 2), Window("Zulu", 1)]);
        Assert.Equal(first.Select(app => app.Id), second.Select(app => app.Id));
        Assert.Equal(new[] { "Pinned", "Alpha", "Zulu" }, first.Select(app => app.Name));
        var closed = DockApplicationCollection.Combine([Pin("Pinned")], []);
        Assert.True(Assert.Single(closed).IsPinned);
    }

    [Fact]
    public void ObservableCollectionRetainsPinnedItemAndNotifiesRunningAndActiveChanges()
    {
        var model = new DockApplications();
        model.Apply(new(DockApplicationCollection.Combine([Pin("Editor")], [])));
        var original = Assert.Single(model.VisibleDockApplications);
        var propertyChanges = 0;
        var collectionChanges = 0;
        original.PropertyChanged += (_, _) => propertyChanges++;
        model.VisibleDockApplications.CollectionChanged += (_, _) => collectionChanges++;
        model.Apply(new(DockApplicationCollection.Combine([Pin("Editor")], [Window("Editor", active: true)])));
        Assert.Same(original, Assert.Single(model.VisibleDockApplications));
        Assert.True(original.IsRunning && original.IsActive);
        Assert.Equal(1, propertyChanges);
        Assert.Equal(0, collectionChanges);
        model.Apply(new(DockApplicationCollection.Combine([Pin("Editor")], [])));
        Assert.Same(original, Assert.Single(model.VisibleDockApplications));
        Assert.False(original.IsRunning || original.IsActive);
    }

    [Fact]
    public void ObservableCollectionHandlesPinReorderAndRunningRemovalWithoutReplacingSurvivors()
    {
        var model = new DockApplications();
        model.Apply(new(DockApplicationCollection.Combine([Pin("One"), Pin("Two")], [Window("Other")])));
        var original = model.VisibleDockApplications[0];
        model.Apply(new(DockApplicationCollection.Combine([Pin("Two"), Pin("One")], [])));
        Assert.Equal(2, model.VisibleDockApplications.Count);
        Assert.Same(original, model.VisibleDockApplications[1]);
        Assert.Equal("Two", model.VisibleDockApplications[0].Name);
    }

    [Fact]
    public void RunningIdentityFallbackGroupsOnlyOneUnambiguousAppId()
    {
        Assert.Single(DockApplicationCollection.Combine([], [Window("Editor", 1), Window(null, 2)]));
        var genericPin = new PinnedApplication(new(null, "C:\\Apps\\app.exe"), "Generic", "generic.lnk", null);
        var apps = DockApplicationCollection.Combine([genericPin], [Window("Profile.One", 1), Window("Profile.Two", 2)]);
        Assert.Equal(3, apps.Count);
        Assert.False(apps[0].IsRunning);
    }

    [Fact]
    public void LaunchArgumentsRemainPartOfFallbackPinIdentity()
    {
        var identity = new ApplicationIdentity(null, "C:\\Apps\\app.exe", "--profile one");
        Assert.NotEqual(identity.Key, (identity with { Arguments = "--profile two" }).Key);
        var pins = new[] { new PinnedApplication(identity, "One", "one.lnk", null),
            new PinnedApplication(identity with { Arguments = "--profile two" }, "Two", "two.lnk", null) };
        Assert.Equal(3, DockApplicationCollection.Combine(pins, [Window(null)]).Count);
    }
}
