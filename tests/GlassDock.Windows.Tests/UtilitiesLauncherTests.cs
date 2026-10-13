using System.Runtime.InteropServices;
using GlassDock.Windows.Desktop;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class UtilitiesLauncherTests
{
    [Fact]
    public void Conflicting_shortcut_is_rejected_and_disposal_releases_registration()
    {
        var first = CreateWindowExW(0, "STATIC", "Doky shortcut test A", 0, 0, 0, 1, 1, 0, 0, 0, 0);
        var second = CreateWindowExW(0, "STATIC", "Doky shortcut test B", 0, 0, 0, 1, 1, 0, 0, 0, 0);
        Assert.NotEqual(0, first); Assert.NotEqual(0, second);
        try
        {
            using var a = new WindowsUtilitiesLauncherHost(first);
            using var b = new WindowsUtilitiesLauncherHost(second);
            // Reserve a test-only chord. Never generate input or take over a shortcut.
            Assert.True(a.ConfigureShortcut(true, 0x87)); // Ctrl+Alt+F24
            Assert.False(b.ConfigureShortcut(true, 0x87));
            Assert.True(a.ConfigureShortcut(false, 0));
            Assert.True(b.ConfigureShortcut(true, 0x87));
            b.Dispose();
            Assert.True(a.ConfigureShortcut(true, 0x87));
        }
        finally { DestroyWindow(second); DestroyWindow(first); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(uint ex, string cls, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
}
