using System.Runtime.InteropServices;
using GlassDock.Windows.Desktop;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class DockInteractionRegionTests
{
    [Fact]
    public void Region_excludes_transparent_host_and_updates_without_recreating_window()
    {
        var window = CreateWindowExW(0x08000080, "STATIC", "Dock region test", 0x80000000,
            -640, -144, 640, 144, 0, 0, 0, 0);
        Assert.NotEqual(0, window);
        var region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            using var manager = new WindowsOverlayManager(window);
            manager.Configure();
            var scale = manager.Scale;
            bool Contains(double x, double y) => PtInRegion(region,
                (int)Math.Round(x * scale), (int)Math.Round(y * scale));
            void Hit(double x, double y, bool inside)
            {
                var sx = -640 + (int)Math.Round(x * scale);
                var sy = -144 + (int)Math.Round(y * scale);
                var packed = (nint)((ushort)sx | ((long)(ushort)sy << 16));
                var result = SendMessageW(window, 0x84, 0, packed);
                Assert.Equal(!inside, result == -1);
                Assert.Equal(!inside, (GetWindowLongPtrW(window, -20).ToInt64() & 0x20) != 0);
            }
            for (var i = 0; i < 20; i++)
            {
                // A wave rising above the base, with transparent space beside it.
                manager.SetInteractionPolygon([(100, 70), (270, 70), (320, 58),
                    (370, 70), (540, 70), (540, 120), (100, 120)]);
                Assert.Equal(0, GetWindowRgn(window, region)); // No visual clipping region.
                Hit(320, 65, true);
                Hit(320, 100, true);
                Hit(150, 65, false);
                Hit(320, 20, false);
                Hit(50, 100, false);
                Hit(320, 135, false);

                manager.SetInteractionRegion(false);
                Assert.NotEqual(0, GetWindowRgn(window, region));
                Assert.True(Contains(320, 120));
                Assert.False(Contains(150, 100));
            }
        }
        finally { DeleteObject(region); DestroyWindow(window); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(uint ex, string cls, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint window, int index);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(nint window, nint region);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(nint region, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
}
