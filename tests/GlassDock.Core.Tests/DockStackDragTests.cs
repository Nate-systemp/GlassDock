using GlassDock.Core.Applications;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class DockStackDragTests
{
    [Fact]
    public void Crossing_icons_keeps_reorder_until_intentional_centered_dwell()
    {
        var drag = new DockStackDrag(); drag.Begin();
        Assert.Equal(DockDragMode.Reorder, drag.Mode);
        drag.Hover("a", 0); drag.Hover("b", 200);
        Assert.Equal(DockDragMode.StackCandidate, drag.Mode);
        drag.Hover("b", 200 + DockStackDrag.DwellMilliseconds - 1);
        Assert.Equal(DockDragMode.StackCandidate, drag.Mode);
        drag.Hover("b", 200 + DockStackDrag.DwellMilliseconds);
        Assert.Equal(DockDragMode.StackMerge, drag.Mode);
        drag.Hover(null, 201 + DockStackDrag.DwellMilliseconds);
        Assert.Equal(DockDragMode.Reorder, drag.Mode);
        Assert.Null(drag.TargetId);
    }
    [Fact]
    public void External_files_never_enter_internal_modes()
    {
        var drag = new DockStackDrag(); drag.Begin(external: true);
        drag.Hover("pinned-app", 0); drag.Hover("pinned-app", 10000);
        Assert.Equal(DockDragMode.ExternalFiles, drag.Mode); Assert.Null(drag.TargetId);
    }
    [Fact]
    public void Cancelled_candidate_and_ready_merge_clear_all_state()
    {
        var drag = new DockStackDrag(); drag.Begin(); drag.Hover("a", 0); drag.Hover("a", DockStackDrag.DwellMilliseconds);
        drag.Reset(); Assert.Equal(DockDragMode.None, drag.Mode); Assert.Null(drag.TargetId);
        drag.Begin(); drag.Hover("a", 700);
        Assert.Equal(DockDragMode.StackCandidate, drag.Mode);
    }
    [Theory]
    [InlineData(1,1)] [InlineData(2,2)] [InlineData(4,2)] [InlineData(5,3)] [InlineData(9,3)]
    public void Grid_columns_are_bounded(int count, int columns) => Assert.Equal(columns, DockStack.Columns(count));

    [Fact]
    public void Monitor_filter_retains_global_stack_but_filters_member_windows()
    {
        var identity = new ApplicationIdentity("Vendor.App", "C:\\App.exe");
        var app = new DockApplication(identity.Key, identity, "App", "C:\\App.exe", true,
            [new(identity,"App",1,1,1,false,null),new(identity,"App",2,1,1,false,null)], null);
        var stack = new DockApplication("stack:one",new(null,null),"Stack",null,true,app.Windows,null)
            { Stack = new("stack:one","Stack",[app.Id]), StackApps = [app] };
        var filtered = DockApplicationMonitorFilter.ForMonitor(new([stack]), window => window.Handle == 2);
        var local = Assert.Single(filtered.Applications);
        Assert.Equal(stack.Id, local.Id); Assert.Same(stack.Stack,local.Stack);
        Assert.Equal(2,Assert.Single(Assert.Single(local.StackApps).Windows).Handle);
        Assert.Equal(2,app.Windows.Count);
    }
}
