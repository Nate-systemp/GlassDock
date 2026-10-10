using GlassDock.Core.Applications;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class WindowPreviewFrameTests
{
    private static ApplicationWindow Window(int handle) => new(new("test", "test.exe"), "Test", handle, 1, 1, false, null);

    [Fact]
    public void Membership_change_after_capture_cannot_change_layout_or_rendered_cards()
    {
        var source = new List<ApplicationWindow> { Window(1), Window(2), Window(3) };
        var frame = WindowPreviewFrame.Create(source, 0, 1000, 600);
        source.Clear();
        source.Add(Window(9));
        Assert.Equal(3, frame.Windows.Count);
        Assert.Equal(3, frame.Compact.Cards.Count);
        Assert.Equal(3, frame.Expanded.Cards.Count);
        Assert.Equal(1, frame.Windows[0].Handle);
    }

    [Fact]
    public void Rapid_switches_empty_desktops_and_stale_pages_keep_both_endpoints_coherent()
    {
        var session = new WindowPreviewSession();
        for (var i = 0; i < 200; i++)
        {
            var windows = Enumerable.Range(1, i % 17).Select(Window).ToArray();
            session.Refresh(windows);
            var frame = WindowPreviewFrame.Create(session.Windows, i % 20, i % 2 == 0 ? 500 : 1600, 500);
            session.Refresh([]); // Simulate another switch before SizeChanged draws this frame.
            Assert.Equal(frame.Windows.Count, frame.Compact.Cards.Count);
            Assert.Equal(frame.Windows.Count, frame.Expanded.Cards.Count);
            for (var card = frame.Windows.Count - 1; card >= 0; card--)
            {
                Assert.True(frame.Compact.Cards[card].Width > 0);
                Assert.True(frame.Expanded.Cards[card].Width > 0);
            }
        }
    }
}
