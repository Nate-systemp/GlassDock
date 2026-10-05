using GlassDock.Core.Applications;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class AppActionPanelTests
{
    [Theory]
    [InlineData(0, true, false, "Pinned", "Unpin from Doky")]
    [InlineData(0, false, false, "Not running", "Pin to Doky")]
    [InlineData(1, true, false, "Running · 1 window", "Unpin from Doky")]
    [InlineData(3, false, false, "Running · 3 windows", "Pin to Doky")]
    [InlineData(2, true, true, "Running · 2 windows", "Move out of stack")]
    public void ContextUsesActualWindowsAndDistinctStackAction(int count, bool pinned, bool member, string status, string action)
    {
        var identity = new ApplicationIdentity(null, @"C:\Apps\Editor.exe");
        var windows = Enumerable.Range(1, count).Select(i => new ApplicationWindow(identity, "Editor", i, 10, 20, false, null, $"Document {i}")).ToArray();
        var app = new DockApplication(identity.Key, identity, "Editor", identity.ExecutablePath, pinned, windows, null);
        var model = new AppActionPanelModel(app, true, member);
        Assert.Equal(status, model.Status);
        Assert.Equal(action, model.PinAction);
        Assert.Same(windows, model.RunningWindows);
        Assert.Empty(model.RecentItems);
        Assert.True(model.CanOpenFile);
    }
}
