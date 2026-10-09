using GlassDock.Core.Applications;
using GlassDock.Windows.Applications;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class DockStackStoreTests
{
    [Theory]
    [InlineData(60, DockPointerMode.Stack)]
    [InlineData(89, DockPointerMode.Reorder)]
    [InlineData(200, DockPointerMode.Outside)]
    public void Pointer_release_routes_to_one_persisted_operation(double x, DockPointerMode expected)
    {
        WithStore(path =>
        {
            var apps = new[] { App("a"), App("b"), App("c") };
            var store = new DockPinStore(path);
            DockDragBounds[] bounds = [new(0, 0, 40, 40), new(46, 0, 86, 40), new(92, 0, 132, 40)];
            var target = DockPointerTarget.Resolve(x, 20, bounds, 0, _ => true);
            Assert.Equal(expected, target.Mode);
            Assert.False(File.Exists(path)); // Preview cannot persist anything.
            if (target.Mode == DockPointerMode.Stack)
            {
                Assert.True(store.MergeStack(apps[0].Id, apps[target.Index].Id, apps));
                Assert.Single(new DockPinStore(path).ApplyStacks(apps), a => a.Stack is not null);
            }
            else if (target.Mode == DockPointerMode.Reorder)
            {
                var ids = apps.Select(a => a.Id).ToList(); var moved = ids[0]; ids.RemoveAt(0); ids.Insert(target.Index, moved);
                Assert.True(store.Reorder(apps.Select(a => a.Id).ToArray(), ids));
                var loaded = new DockPinStore(path).ApplyStacks(apps);
                Assert.Equal(ids, loaded.Select(a => a.Id)); Assert.All(loaded, a => Assert.Null(a.Stack));
            }
            else Assert.False(File.Exists(path));
        });
    }
    private static DockApplication App(string id, bool pinned = true) => new("app:" + id.ToUpperInvariant(),
        new(id,@"C:\Apps\" + id + ".exe", "--profile test"), id,@"C:\Links\" + id + ".lnk",pinned,[],null);
    private static void WithStore(Action<string> run)
    {
        var path = Path.Combine(Path.GetTempPath(),"doky-stacks-"+Guid.NewGuid()+".json");
        try { run(path); } finally { File.Delete(path); }
    }
    [Fact]
    public void Legacy_profile_is_unchanged_until_stack_creation()
    {
        WithStore(path =>
        {
            const string json = "{\"Pins\":[],\"Excluded\":[],\"Order\":[\"app:B\",\"app:A\"]}";
            File.WriteAllText(path,json); var apps = new[]{App("a"),App("b")};
            Assert.Equal(new[]{apps[1],apps[0]},new DockPinStore(path).ApplyStacks(apps));
            Assert.Equal(json,File.ReadAllText(path));
        });
    }
    [Fact]
    public void Create_rename_reorder_and_reload_preserve_identity_and_launch_target()
    {
        WithStore(path =>
        {
            var apps = new[]{App("a"),App("b"),App("c")}; var store = new DockPinStore(path);
            Assert.True(store.MergeStack(apps[0].Id,apps[1].Id,apps));
            var root = store.ApplyStacks(apps); Assert.Equal(2,root.Count);
            var stack = Assert.Single(root, app => app.Stack is not null);
            Assert.Equal(new[]{apps[1],apps[0]},stack.StackApps);
            Assert.DoesNotContain(root,app => app.Id == apps[0].Id || app.Id == apps[1].Id);
            Assert.True(store.RenameStack(stack.Id,"Work"));
            Assert.True(store.ReorderStack(stack.Id,[apps[0].Id,apps[1].Id]));
            var reloaded = new DockPinStore(path).ApplyStacks(apps);
            var loaded = Assert.Single(reloaded, app => app.Stack is not null);
            Assert.Equal("Work",loaded.Name); Assert.Equal(new[]{apps[0],apps[1]},loaded.StackApps);
            Assert.Contains("--profile test",File.ReadAllText(path)); Assert.Contains("a.lnk",File.ReadAllText(path));
        });
    }
    [Fact]
    public void Adding_member_then_extracting_last_pair_dissolves_without_losing_apps()
    {
        WithStore(path =>
        {
            var apps = new[]{App("a"),App("b"),App("c")}; var store = new DockPinStore(path);
            Assert.True(store.MergeStack(apps[0].Id,apps[1].Id,apps));
            var id = store.ApplyStacks(apps).Single(app => app.Stack is not null).Id;
            Assert.True(store.MergeStack(apps[2].Id,id,apps));
            Assert.False(store.MergeStack(apps[2].Id,id,apps));
            Assert.True(store.ExtractStack(id,apps[2].Id));
            Assert.Equal(2,store.ApplyStacks(apps).Single(app => app.Stack is not null).StackApps.Count);
            Assert.True(store.ExtractStack(id,apps[0].Id));
            var result = new DockPinStore(path).ApplyStacks(apps);
            Assert.Equal(3,result.Count); Assert.All(result,app => Assert.Null(app.Stack));
            Assert.Equal(3,result.Select(app => app.Id).Distinct().Count());
        });
    }
    [Fact]
    public void Ungroup_restores_member_order_at_stack_slot_and_root_order_persists()
    {
        WithStore(path =>
        {
            var apps = new[]{App("a"),App("b"),App("c")}; var store = new DockPinStore(path);
            Assert.True(store.MergeStack(apps[0].Id,apps[1].Id,apps));
            var stack = store.ApplyStacks(apps).Single(app => app.Stack is not null);
            var current = store.ApplyStacks(apps).Select(app=>app.Id).ToArray();
            Assert.True(store.Reorder(current,[apps[2].Id,stack.Id]));
            Assert.True(store.ExtractStack(stack.Id));
            Assert.Equal(new[]{apps[2].Id,apps[1].Id,apps[0].Id}, new DockPinStore(path).ApplyStacks(apps).Select(app=>app.Id));
        });
    }
    [Fact]
    public void Invalid_merge_reorder_or_rename_never_changes_persisted_state()
    {
        WithStore(path =>
        {
            var apps = new[]{App("a"),App("b"),App("running",false)}; var store=new DockPinStore(path);
            Assert.False(store.MergeStack(apps[0].Id,apps[2].Id,apps)); Assert.False(File.Exists(path));
            Assert.True(store.MergeStack(apps[0].Id,apps[1].Id,apps));
            var id=store.ApplyStacks(apps).Single(app=>app.Stack is not null).Id; var saved=File.ReadAllText(path);
            Assert.False(store.RenameStack(id," ")); Assert.False(store.ReorderStack(id,[apps[0].Id,apps[0].Id]));
            Assert.False(store.ExtractStack(id,"unknown")); Assert.Equal(saved,File.ReadAllText(path));
        });
    }
    [Fact]
    public void Secondary_monitor_edits_are_visible_in_both_views_and_limit_is_nine()
    {
        WithStore(path =>
        {
            var apps=Enumerable.Range(0,10).Select(i=>App("app"+i)).ToArray(); var store=new DockPinStore(path);
            Assert.True(store.MergeStack(apps[0].Id,apps[1].Id,apps));
            var id=store.ApplyStacks(apps).Single(app=>app.Stack is not null).Id;
            for(var i=2;i<9;i++) Assert.True(store.MergeStack(apps[i].Id,id,apps));
            Assert.False(store.MergeStack(apps[9].Id,id,apps));
            var snapshot=new ApplicationSnapshot(store.ApplyStacks(apps));
            Assert.Equal(id,DockApplicationMonitorFilter.ForMonitor(snapshot,_=>false).Applications.Single(app=>app.Stack is not null).Id);
            Assert.True(store.RenameStack(id,"Secondary edit"));
            Assert.Equal("Secondary edit",new DockPinStore(path).ApplyStacks(apps).Single(app=>app.Stack is not null).Name);
        });
    }
}
